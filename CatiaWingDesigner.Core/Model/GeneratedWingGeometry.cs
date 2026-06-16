using System.Collections.Generic;
using System.Linq;

namespace CatiaWingDesigner.Core.Model
{
    public sealed class GeneratedWingGeometry
    {
        public List<WingSection> Sections { get; } = new List<WingSection>();

        public List<WingSegmentSolution> SegmentSolutions { get; } = new List<WingSegmentSolution>();

        public List<List<Geometry.Point3DValue>> UpperPreviewGrid { get; } = new List<List<Geometry.Point3DValue>>();

        public List<List<Geometry.Point3DValue>> LowerPreviewGrid { get; } = new List<List<Geometry.Point3DValue>>();

        public double HalfSpan { get; set; }

        public double HalfArea { get; set; }

        public double FullArea => HalfArea * 2.0;

        public double FullAspectRatio => FullArea <= 0.0 ? 0.0 : (HalfSpan * 2.0) * (HalfSpan * 2.0) / FullArea;

        public double AverageChord => HalfSpan <= 0.0 ? 0.0 : HalfArea / HalfSpan;

        public double TipChord => Sections.Count == 0 ? 0.0 : Sections.Last().Chord;
    }
}
