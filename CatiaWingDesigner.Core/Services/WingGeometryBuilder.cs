using System;
using System.Collections.Generic;
using System.Linq;
using CatiaWingDesigner.Core.Geometry;
using CatiaWingDesigner.Core.Model;

namespace CatiaWingDesigner.Core.Services
{
    public sealed class WingGeometryBuilder
    {
        private const int PreviewSurfacePointCount = 50;

        private readonly DriverSolver _driverSolver;

        private readonly AirfoilLibrary _airfoilLibrary;

        public WingGeometryBuilder()
            : this(new DriverSolver(), new AirfoilLibrary())
        {
        }

        public WingGeometryBuilder(DriverSolver driverSolver, AirfoilLibrary airfoilLibrary)
        {
            _driverSolver = driverSolver;
            _airfoilLibrary = airfoilLibrary;
        }

        public GeneratedWingGeometry Build(WingDesign design)
        {
            if (design == null)
            {
                throw new ArgumentNullException(nameof(design));
            }

            switch (design.PlanformMode)
            {
                case WingPlanformMode.SegmentDriven:
                    return BuildSegmentDriven(design);
                case WingPlanformMode.CustomEdgeSpline:
                    return BuildCustomEdgeSpline(design);
                default:
                    throw new ArgumentOutOfRangeException(nameof(design.PlanformMode), design.PlanformMode, "未知的平面外形模式。");
            }
        }

        private GeneratedWingGeometry BuildSegmentDriven(WingDesign design)
        {
            var solutions = _driverSolver.Solve(design);
            var geometry = new GeneratedWingGeometry { PlanformMode = WingPlanformMode.SegmentDriven };
            geometry.SegmentSolutions.AddRange(solutions);

            var y = 0.0;
            var z = 0.0;
            var leadingEdgeX = 0.0;
            var currentChord = design.RootChord;
            var currentTwist = 0.0;

            AddSection(geometry, "Section_00_Root", new Point3DValue(leadingEdgeX, y, z), currentChord, currentTwist, design.RootAirfoil);

            for (var i = 0; i < solutions.Count; i++)
            {
                var solution = solutions[i];
                var segment = design.Segments[i];
                y += solution.Span;
                z += solution.Span * Math.Tan(DegreesToRadians(segment.TipDihedralDeg));
                leadingEdgeX += solution.LeadingEdgeOffsetX;
                currentChord = solution.TipChord;
                currentTwist = segment.TipTwistDeg;

                AddSection(
                    geometry,
                    $"Section_{i + 1:00}_{segment.Name}",
                    new Point3DValue(leadingEdgeX, y, z),
                    currentChord,
                    currentTwist,
                    segment.TipAirfoil);
            }

            foreach (var section in geometry.Sections)
            {
                geometry.UpperPreviewGrid.Add(section.UpperPreviewPoints);
                geometry.LowerPreviewGrid.Add(section.LowerPreviewPoints);
            }

            geometry.HalfSpan = geometry.Sections.Last().LeadingEdge.Y;
            geometry.HalfArea = solutions.Sum(s => s.Area);
            return geometry;
        }

        private GeneratedWingGeometry BuildCustomEdgeSpline(WingDesign design)
        {
            ValidateCustomPlanformStations(design.PlanformStations);

            var geometry = new GeneratedWingGeometry { PlanformMode = WingPlanformMode.CustomEdgeSpline };
            var z = 0.0;
            WingPlanformStation? previous = null;

            for (var i = 0; i < design.PlanformStations.Count; i++)
            {
                var station = design.PlanformStations[i];
                if (previous != null)
                {
                    var dy = station.SpanY - previous.SpanY;
                    z += dy * Math.Tan(DegreesToRadians(station.DihedralDegFromPrevious));
                }

                var curve = _airfoilLibrary.Load(station.Airfoil);
                ValidateFiniteTrailingEdge(station, curve);
                var chord = ComputeScaledChordFromProjectedTrailingEdge(station, curve);
                var sectionName = string.IsNullOrWhiteSpace(station.Name)
                    ? $"Section_{i:00}_Custom"
                    : $"Section_{i:00}_{station.Name}";

                AddSection(
                    geometry,
                    sectionName,
                    new Point3DValue(station.LeadingEdgeX, station.SpanY, z),
                    chord,
                    station.TwistDeg,
                    station.Airfoil,
                    curve);

                previous = station;
            }

            foreach (var section in geometry.Sections)
            {
                geometry.UpperPreviewGrid.Add(section.UpperPreviewPoints);
                geometry.LowerPreviewGrid.Add(section.LowerPreviewPoints);
            }

            geometry.HalfSpan = geometry.Sections.Last().LeadingEdge.Y;
            geometry.HalfArea = ComputeProjectedPlanformArea(design.PlanformStations);
            return geometry;
        }

        private void AddSection(GeneratedWingGeometry geometry, string name, Point3DValue leadingEdge, double chord, double twistDeg, AirfoilRef airfoilRef)
        {
            var curve = _airfoilLibrary.Load(airfoilRef);
            AddSection(geometry, name, leadingEdge, chord, twistDeg, airfoilRef, curve);
        }

        private static void AddSection(GeneratedWingGeometry geometry, string name, Point3DValue leadingEdge, double chord, double twistDeg, AirfoilRef airfoilRef, AirfoilCurve curve)
        {
            var section = new WingSection
            {
                Name = name,
                LeadingEdge = leadingEdge,
                Chord = chord,
                TwistDeg = twistDeg,
                Airfoil = airfoilRef.Clone(),
                UpperRawPoints = curve.UpperPoints.Select(p => Transform(p, leadingEdge, chord, twistDeg)).ToList(),
                LowerRawPoints = curve.LowerPoints.Select(p => Transform(p, leadingEdge, chord, twistDeg)).ToList(),
                UpperPreviewPoints = curve.SampleUpper(PreviewSurfacePointCount).Select(p => Transform(p, leadingEdge, chord, twistDeg)).ToList(),
                LowerPreviewPoints = curve.SampleLower(PreviewSurfacePointCount).Select(p => Transform(p, leadingEdge, chord, twistDeg)).ToList()
            };

            geometry.Sections.Add(section);
        }

        private static void ValidateCustomPlanformStations(IReadOnlyList<WingPlanformStation> stations)
        {
            const double eps = 1e-8;
            if (stations == null || stations.Count < 3)
            {
                throw new InvalidOperationException("自定义前后缘模式至少需要 3 个站位。");
            }

            for (var i = 0; i < stations.Count; i++)
            {
                var station = stations[i];
                if (station == null)
                {
                    throw new InvalidOperationException($"自定义站位 {i + 1} 不能为空。");
                }

                RequireFinite(station.SpanY, $"站位 {i + 1} SpanY");
                RequireFinite(station.LeadingEdgeX, $"站位 {i + 1} 前缘 X");
                RequireFinite(station.TrailingEdgeX, $"站位 {i + 1} 后缘 X");
                RequireFinite(station.TwistDeg, $"站位 {i + 1} 扭转角");
                RequireFinite(station.DihedralDegFromPrevious, $"站位 {i + 1} 相对上反角");

                if (station.Airfoil == null)
                {
                    throw new InvalidOperationException($"站位 {i + 1} 翼型不能为空。");
                }

                if (station.TrailingEdgeX <= station.LeadingEdgeX + eps)
                {
                    throw new InvalidOperationException($"站位 {i + 1} 后缘 X 必须大于前缘 X。");
                }

                if (i == 0)
                {
                    if (Math.Abs(station.SpanY) > eps)
                    {
                        throw new InvalidOperationException("自定义前后缘根站 SpanY 必须为 0。");
                    }

                    if (Math.Abs(station.LeadingEdgeX) > eps)
                    {
                        throw new InvalidOperationException("自定义前后缘根站前缘 X 必须为 0。");
                    }
                }
                else if (station.SpanY <= stations[i - 1].SpanY + eps)
                {
                    throw new InvalidOperationException($"站位 {i + 1} SpanY 必须严格大于前一站位。");
                }
            }
        }

        private static void ValidateFiniteTrailingEdge(WingPlanformStation station, AirfoilCurve curve)
        {
            var upperTrailing = curve.UpperPoints[curve.UpperPoints.Count - 1];
            var lowerTrailing = curve.LowerPoints[curve.LowerPoints.Count - 1];
            var dx = upperTrailing.X - lowerTrailing.X;
            var dy = upperTrailing.Y - lowerTrailing.Y;
            if (dx * dx + dy * dy <= 1e-12)
            {
                throw new InvalidOperationException($"站位 {station.Name} 的翼型尾缘闭合；CATIA 自定义前后缘曲面要求有限尾缘厚度。");
            }
        }

        private static double ComputeScaledChordFromProjectedTrailingEdge(WingPlanformStation station, AirfoilCurve curve)
        {
            const double eps = 1e-8;
            var upperTrailing = curve.UpperPoints[curve.UpperPoints.Count - 1];
            var lowerTrailing = curve.LowerPoints[curve.LowerPoints.Count - 1];
            var trailingMidX = (upperTrailing.X + lowerTrailing.X) * 0.5;
            var trailingMidY = (upperTrailing.Y + lowerTrailing.Y) * 0.5;
            var twist = DegreesToRadians(station.TwistDeg);
            var projectedFactor = trailingMidX * Math.Cos(twist) + trailingMidY * Math.Sin(twist);
            if (double.IsNaN(projectedFactor) || double.IsInfinity(projectedFactor) || projectedFactor <= eps)
            {
                throw new InvalidOperationException($"站位 {station.Name} 的扭转角导致后缘投影弦长不可计算。");
            }

            var projectedChord = station.TrailingEdgeX - station.LeadingEdgeX;
            var chord = projectedChord / projectedFactor;
            if (double.IsNaN(chord) || double.IsInfinity(chord) || chord <= eps)
            {
                throw new InvalidOperationException($"站位 {station.Name} 计算弦长无效。");
            }

            return chord;
        }

        private static double ComputeProjectedPlanformArea(IReadOnlyList<WingPlanformStation> stations)
        {
            var area = 0.0;
            for (var i = 1; i < stations.Count; i++)
            {
                var rootChord = stations[i - 1].TrailingEdgeX - stations[i - 1].LeadingEdgeX;
                var tipChord = stations[i].TrailingEdgeX - stations[i].LeadingEdgeX;
                var span = stations[i].SpanY - stations[i - 1].SpanY;
                area += 0.5 * (rootChord + tipChord) * span;
            }

            return area;
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidOperationException($"{name} 必须是有限数值。");
            }
        }

        private static Point3DValue Transform(Point2DValue normalized, Point3DValue leadingEdge, double chord, double twistDeg)
        {
            var x = normalized.X * chord;
            var z = normalized.Y * chord;
            var twist = DegreesToRadians(twistDeg);
            var cos = Math.Cos(twist);
            var sin = Math.Sin(twist);
            var xRot = x * cos + z * sin;
            var zRot = -x * sin + z * cos;
            return new Point3DValue(leadingEdge.X + xRot, leadingEdge.Y, leadingEdge.Z + zRot);
        }

        private static double DegreesToRadians(double degree)
        {
            return degree * Math.PI / 180.0;
        }
    }
}
