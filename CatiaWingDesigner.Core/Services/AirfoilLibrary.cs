using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CatiaWingDesigner.Core.Geometry;
using CatiaWingDesigner.Core.Model;

namespace CatiaWingDesigner.Core.Services
{
    public sealed class AirfoilLibrary
    {
        public AirfoilCurve Load(AirfoilRef airfoilRef)
        {
            if (airfoilRef == null)
            {
                throw new ArgumentNullException(nameof(airfoilRef));
            }

            switch (airfoilRef.Kind)
            {
                case AirfoilKind.Naca4Digit:
                    return CreateNaca4(airfoilRef.Value);
                case AirfoilKind.DatFile:
                    return LoadSeligDat(airfoilRef.Value);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public AirfoilCurve CreateNaca4(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 4 || code.Trim().Any(c => !char.IsDigit(c)))
            {
                throw new InvalidOperationException("NACA 翼型必须是 4 位数字，例如 2412。");
            }

            code = code.Trim();
            var m = (code[0] - '0') / 100.0;
            var p = (code[1] - '0') / 10.0;
            var t = int.Parse(code.Substring(2, 2), CultureInfo.InvariantCulture) / 100.0;

            if (t <= 0.0)
            {
                throw new InvalidOperationException("NACA 翼型厚度必须大于 0。");
            }

            if (m > 0.0 && p <= 0.0)
            {
                throw new InvalidOperationException("有弯度 NACA 翼型的最大弯度位置不能为 0。");
            }

            const int pointCount = 81;
            var upper = new List<Point2DValue>(pointCount);
            var lower = new List<Point2DValue>(pointCount);

            for (var i = 0; i < pointCount; i++)
            {
                var thetaCos = Math.PI * i / (pointCount - 1);
                var x = 0.5 * (1.0 - Math.Cos(thetaCos));
                var yt = 5.0 * t * (0.2969 * Math.Sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x * x * x - 0.1015 * x * x * x * x);

                double yc;
                double dyc;
                if (m <= 0.0 || p <= 0.0)
                {
                    yc = 0.0;
                    dyc = 0.0;
                }
                else if (x < p)
                {
                    yc = m / (p * p) * (2.0 * p * x - x * x);
                    dyc = 2.0 * m / (p * p) * (p - x);
                }
                else
                {
                    yc = m / ((1.0 - p) * (1.0 - p)) * ((1.0 - 2.0 * p) + 2.0 * p * x - x * x);
                    dyc = 2.0 * m / ((1.0 - p) * (1.0 - p)) * (p - x);
                }

                var theta = Math.Atan(dyc);
                var xu = x - yt * Math.Sin(theta);
                var yu = yc + yt * Math.Cos(theta);
                var xl = x + yt * Math.Sin(theta);
                var yl = yc - yt * Math.Cos(theta);

                if (i == pointCount - 1)
                {
                    xu = 1.0;
                    xl = 1.0;
                }

                upper.Add(new Point2DValue(Clamp01(xu), yu));
                lower.Add(new Point2DValue(Clamp01(xl), yl));
            }

            upper = DeduplicateAndSort(upper, "NACA 上表面");
            lower = DeduplicateAndSort(lower, "NACA 下表面");
            return new AirfoilCurve("NACA " + code, upper, lower);
        }

        public AirfoilCurve LoadSeligDat(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("DAT 文件路径不能为空。");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("找不到 DAT 翼型文件。", path);
            }

            var lines = File.ReadAllLines(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var titleLines = new List<string>();
            var tokens = new List<string>();

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var normalized = line.Replace(",", " ");
                var parts = normalized.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                tokens.AddRange(parts);
            }

            var points = ParseCoordinateTokens(tokens, titleLines);
            if (titleLines.Count > 0)
            {
                name = string.Join(" ", titleLines);
            }

            if (points.Count < 7)
            {
                throw new InvalidOperationException("DAT 文件点数不足，无法分出上下表面。");
            }

            var leadingIndex = FindUniqueLeadingEdge(points);
            if (leadingIndex <= 0 || leadingIndex >= points.Count - 1)
            {
                throw new InvalidOperationException("DAT 文件无法按最小 x 识别前缘分段。");
            }

            var upper = points.Take(leadingIndex + 1).Reverse().ToList();
            var lower = points.Skip(leadingIndex).ToList();

            return new AirfoilCurve(name, upper, lower);
        }

        private static List<Point2DValue> ParseCoordinateTokens(IReadOnlyList<string> tokens, List<string> titleTokens)
        {
            var points = new List<Point2DValue>();
            var start = -1;

            for (var i = 0; i < tokens.Count - 1; i++)
            {
                if (TryParseDouble(tokens[i], out var possibleX) &&
                    possibleX >= -1e-8 &&
                    possibleX <= 1.0 + 1e-8 &&
                    TryParseDouble(tokens[i + 1], out _))
                {
                    start = i;
                    break;
                }

                titleTokens.Add(tokens[i]);
            }

            if (start < 0)
            {
                throw new InvalidOperationException("DAT 文件未找到有效坐标。");
            }

            if ((tokens.Count - start) % 2 != 0)
            {
                throw new InvalidOperationException("DAT 文件坐标 token 数量不是偶数，无法按 x y 成对解析。");
            }

            for (var i = start; i < tokens.Count; i += 2)
            {
                if (!TryParseDouble(tokens[i], out var x) || !TryParseDouble(tokens[i + 1], out var y))
                {
                    throw new InvalidOperationException($"DAT 文件存在非数字坐标 token：{tokens[i]} {tokens[i + 1]}");
                }

                points.Add(new Point2DValue(x, y));
            }

            return points;
        }

        private static int FindUniqueLeadingEdge(IReadOnlyList<Point2DValue> points)
        {
            var minX = points.Min(p => p.X);
            var matches = points.Select((p, i) => new { p.X, Index = i })
                .Where(p => Math.Abs(p.X - minX) < 1e-8)
                .ToList();

            if (matches.Count != 1)
            {
                throw new InvalidOperationException("DAT 文件存在多个最小 x 点，无法唯一识别前缘。");
            }

            return matches[0].Index;
        }

        private static List<Point2DValue> DeduplicateAndSort(IEnumerable<Point2DValue> points, string label)
        {
            var sorted = points.OrderBy(p => p.X).ToList();
            var result = new List<Point2DValue>();

            foreach (var p in sorted)
            {
                if (result.Count == 0 || Math.Abs(result[result.Count - 1].X - p.X) > 1e-7)
                {
                    result.Add(p);
                }
            }

            if (result.Count < 3)
            {
                throw new InvalidOperationException($"{label}有效点数不足。");
            }

            return result;
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0 && value > -1e-7)
            {
                return 0.0;
            }

            if (value > 1.0 && value < 1.0 + 1e-7)
            {
                return 1.0;
            }

            return value;
        }
    }
}
