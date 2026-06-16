namespace CatiaWingDesigner.Core.Model
{
    public sealed class WingSegmentSolution
    {
        public string Name { get; set; } = string.Empty;

        public double RootChord { get; set; }

        public double TipChord { get; set; }

        public double Span { get; set; }

        public double Area { get; set; }

        public double AspectRatio { get; set; }

        public double Taper { get; set; }

        public double AverageChord { get; set; }

        public double SweepDeg { get; set; }

        public double SweepLocation { get; set; }

        public double LeadingEdgeOffsetX { get; set; }
    }
}
