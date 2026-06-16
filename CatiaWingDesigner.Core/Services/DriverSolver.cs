using System;
using System.Collections.Generic;
using CatiaWingDesigner.Core.Model;

namespace CatiaWingDesigner.Core.Services
{
    public sealed class DriverSolver
    {
        private const double Epsilon = 1e-9;

        public IReadOnlyList<WingSegmentSolution> Solve(WingDesign design)
        {
            ValidateDesignRoot(design);

            var result = new List<WingSegmentSolution>();
            var rootChord = design.RootChord;
            var rootLeadingEdgeX = 0.0;

            for (var i = 0; i < design.Segments.Count; i++)
            {
                var segment = design.Segments[i];
                var solution = SolveSegment(segment, rootChord, rootLeadingEdgeX);
                segment.RootChord = solution.RootChord;
                segment.Span = solution.Span;
                segment.Area = solution.Area;
                segment.AspectRatio = solution.AspectRatio;
                segment.Taper = solution.Taper;
                segment.AverageChord = solution.AverageChord;
                segment.TipChord = solution.TipChord;
                segment.SweepDeg = solution.SweepDeg;

                result.Add(solution);
                rootChord = solution.TipChord;
                rootLeadingEdgeX += solution.LeadingEdgeOffsetX;
            }

            return result;
        }

        private static WingSegmentSolution SolveSegment(WingSegment segment, double rootChord, double rootLeadingEdgeX)
        {
            RequirePositive(rootChord, "RootChord");
            RequireRange(segment.SweepLocation, 0.0, 1.0, "SweepLocation");

            double span;
            double tipChord;
            double area;
            double aspectRatio;
            double taper;
            double averageChord;

            switch (segment.DriverGroup)
            {
                case DriverGroupPreset.SpanTipChordSweep:
                    span = segment.Span;
                    tipChord = segment.TipChord;
                    RequirePositive(span, "Span");
                    RequirePositive(tipChord, "TipChord");
                    averageChord = (rootChord + tipChord) / 2.0;
                    area = span * averageChord;
                    aspectRatio = span * span / area;
                    taper = tipChord / rootChord;
                    break;

                case DriverGroupPreset.SpanTaperSweep:
                    span = segment.Span;
                    taper = segment.Taper;
                    RequirePositive(span, "Span");
                    RequirePositive(taper, "Taper");
                    tipChord = rootChord * taper;
                    averageChord = (rootChord + tipChord) / 2.0;
                    area = span * averageChord;
                    aspectRatio = span * span / area;
                    break;

                case DriverGroupPreset.SpanAreaSweep:
                    span = segment.Span;
                    area = segment.Area;
                    RequirePositive(span, "Span");
                    RequirePositive(area, "Area");
                    averageChord = area / span;
                    tipChord = 2.0 * averageChord - rootChord;
                    RequirePositive(tipChord, "computed TipChord");
                    taper = tipChord / rootChord;
                    aspectRatio = span * span / area;
                    break;

                case DriverGroupPreset.SpanAspectRatioSweep:
                    span = segment.Span;
                    aspectRatio = segment.AspectRatio;
                    RequirePositive(span, "Span");
                    RequirePositive(aspectRatio, "AspectRatio");
                    area = span * span / aspectRatio;
                    averageChord = area / span;
                    tipChord = 2.0 * averageChord - rootChord;
                    RequirePositive(tipChord, "computed TipChord");
                    taper = tipChord / rootChord;
                    break;

                case DriverGroupPreset.AreaAspectRatioSweep:
                    area = segment.Area;
                    aspectRatio = segment.AspectRatio;
                    RequirePositive(area, "Area");
                    RequirePositive(aspectRatio, "AspectRatio");
                    span = Math.Sqrt(area * aspectRatio);
                    averageChord = area / span;
                    tipChord = 2.0 * averageChord - rootChord;
                    RequirePositive(tipChord, "computed TipChord");
                    taper = tipChord / rootChord;
                    break;

                case DriverGroupPreset.SpanAverageChordSweep:
                    span = segment.Span;
                    averageChord = segment.AverageChord;
                    RequirePositive(span, "Span");
                    RequirePositive(averageChord, "AverageChord");
                    tipChord = 2.0 * averageChord - rootChord;
                    RequirePositive(tipChord, "computed TipChord");
                    taper = tipChord / rootChord;
                    area = span * averageChord;
                    aspectRatio = span * span / area;
                    break;

                case DriverGroupPreset.AreaTaperSweep:
                    area = segment.Area;
                    taper = segment.Taper;
                    RequirePositive(area, "Area");
                    RequirePositive(taper, "Taper");
                    tipChord = rootChord * taper;
                    averageChord = (rootChord + tipChord) / 2.0;
                    span = area / averageChord;
                    aspectRatio = span * span / area;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            RequirePositive(averageChord, "AverageChord");
            RequirePositive(area, "Area");
            RequirePositive(aspectRatio, "AspectRatio");

            var sweepRad = DegreesToRadians(segment.SweepDeg);
            var loc = segment.SweepLocation;
            var dx = span * Math.Tan(sweepRad) + loc * rootChord - loc * tipChord;

            if (double.IsNaN(dx) || double.IsInfinity(dx))
            {
                throw new InvalidOperationException("Sweep 计算结果无效。");
            }

            return new WingSegmentSolution
            {
                Name = segment.Name,
                RootChord = rootChord,
                TipChord = tipChord,
                Span = span,
                Area = area,
                AspectRatio = aspectRatio,
                Taper = taper,
                AverageChord = averageChord,
                SweepDeg = segment.SweepDeg,
                SweepLocation = loc,
                LeadingEdgeOffsetX = dx
            };
        }

        private static void ValidateDesignRoot(WingDesign design)
        {
            if (design == null)
            {
                throw new ArgumentNullException(nameof(design));
            }

            RequirePositive(design.RootChord, "RootChord");
            if (design.Segments.Count == 0)
            {
                throw new InvalidOperationException("至少需要一个翼段。");
            }
        }

        private static void RequirePositive(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= Epsilon)
            {
                throw new InvalidOperationException($"{name} 必须为正数。");
            }
        }

        private static void RequireRange(double value, double min, double max, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
            {
                throw new InvalidOperationException($"{name} 必须位于 [{min}, {max}]。");
            }
        }

        private static double DegreesToRadians(double degree)
        {
            return degree * Math.PI / 180.0;
        }
    }
}
