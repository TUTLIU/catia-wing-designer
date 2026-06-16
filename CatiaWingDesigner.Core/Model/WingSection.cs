using System.Collections.Generic;
using CatiaWingDesigner.Core.Geometry;

namespace CatiaWingDesigner.Core.Model
{
    public sealed class WingSection
    {
        public string Name { get; set; } = string.Empty;

        public Point3DValue LeadingEdge { get; set; }

        public double Chord { get; set; }

        public double TwistDeg { get; set; }

        public AirfoilRef Airfoil { get; set; } = AirfoilRef.Naca("2412");

        public List<Point3DValue> UpperRawPoints { get; set; } = new List<Point3DValue>();

        public List<Point3DValue> LowerRawPoints { get; set; } = new List<Point3DValue>();

        public List<Point3DValue> UpperPreviewPoints { get; set; } = new List<Point3DValue>();

        public List<Point3DValue> LowerPreviewPoints { get; set; } = new List<Point3DValue>();
    }
}
