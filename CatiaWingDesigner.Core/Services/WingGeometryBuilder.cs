using System;
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
            var solutions = _driverSolver.Solve(design);
            var geometry = new GeneratedWingGeometry();
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

        private void AddSection(GeneratedWingGeometry geometry, string name, Point3DValue leadingEdge, double chord, double twistDeg, AirfoilRef airfoilRef)
        {
            var curve = _airfoilLibrary.Load(airfoilRef);
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
