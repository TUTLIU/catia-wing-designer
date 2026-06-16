using System;
using System.Collections.Generic;
using System.Linq;
using CatiaWingDesigner.Core.Geometry;
using MathNet.Numerics.Interpolation;

namespace CatiaWingDesigner.Core.Services
{
    public sealed class AirfoilCurve
    {
        public AirfoilCurve(string name, IReadOnlyList<Point2DValue> upperPoints, IReadOnlyList<Point2DValue> lowerPoints)
        {
            if (upperPoints == null || lowerPoints == null)
            {
                throw new ArgumentNullException("翼型上下表面点不能为空。");
            }

            if (upperPoints.Count < 3 || lowerPoints.Count < 3)
            {
                throw new InvalidOperationException("翼型上下表面都至少需要 3 个点。");
            }

            Name = string.IsNullOrWhiteSpace(name) ? "Airfoil" : name.Trim();
            UpperPoints = ValidateMonotonic("上表面", upperPoints);
            LowerPoints = ValidateMonotonic("下表面", lowerPoints);
        }

        public string Name { get; }

        public IReadOnlyList<Point2DValue> UpperPoints { get; }

        public IReadOnlyList<Point2DValue> LowerPoints { get; }

        public IReadOnlyList<Point2DValue> SampleUpper(int count)
        {
            return Sample(UpperPoints, count);
        }

        public IReadOnlyList<Point2DValue> SampleLower(int count)
        {
            return Sample(LowerPoints, count);
        }

        private static IReadOnlyList<Point2DValue> Sample(IReadOnlyList<Point2DValue> points, int count)
        {
            if (count < 3)
            {
                throw new InvalidOperationException("采样点数至少为 3。");
            }

            var xs = points.Select(p => p.X).ToArray();
            var ys = points.Select(p => p.Y).ToArray();
            var spline = CubicSpline.InterpolatePchipSorted(xs, ys);
            var result = new List<Point2DValue>(count);

            for (var i = 0; i < count; i++)
            {
                var theta = Math.PI * i / (count - 1);
                var x = 0.5 * (1.0 - Math.Cos(theta));
                result.Add(new Point2DValue(x, spline.Interpolate(x)));
            }

            return result;
        }

        private static IReadOnlyList<Point2DValue> ValidateMonotonic(string label, IReadOnlyList<Point2DValue> points)
        {
            const double eps = 1e-8;
            var result = points.ToList();

            for (var i = 0; i < result.Count; i++)
            {
                var p = result[i];
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y))
                {
                    throw new InvalidOperationException($"{label}存在非法坐标。");
                }

                if (p.X < -eps || p.X > 1.0 + eps)
                {
                    throw new InvalidOperationException($"{label}的 x 坐标必须位于 [0, 1]。");
                }

                if (i > 0 && p.X <= result[i - 1].X + eps)
                {
                    throw new InvalidOperationException($"{label}必须按前缘到后缘方向排列，且 x 坐标严格递增。");
                }
            }

            return result;
        }
    }
}
