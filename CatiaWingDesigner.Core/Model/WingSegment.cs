using System;

namespace CatiaWingDesigner.Core.Model
{
    public sealed class WingSegment
    {
        public string Name { get; set; } = "Segment";

        public DriverGroupPreset DriverGroup { get; set; } = DriverGroupPreset.SpanTaperSweep;

        public double Span { get; set; } = 1000.0;

        public double Area { get; set; }

        public double AspectRatio { get; set; }

        public double Taper { get; set; } = 0.8;

        public double AverageChord { get; set; }

        public double RootChord { get; set; }

        public double TipChord { get; set; }

        public double SweepDeg { get; set; } = 15.0;

        public double SweepLocation { get; set; } = 0.25;

        public double TipTwistDeg { get; set; }

        public double TipDihedralDeg { get; set; }

        public AirfoilRef TipAirfoil { get; set; } = AirfoilRef.Naca("2412");

        public bool IsActive(DriverParameter parameter)
        {
            switch (DriverGroup)
            {
                case DriverGroupPreset.SpanTipChordSweep:
                    return parameter == DriverParameter.Span ||
                           parameter == DriverParameter.TipChord ||
                           parameter == DriverParameter.Sweep;
                case DriverGroupPreset.SpanTaperSweep:
                    return parameter == DriverParameter.Span ||
                           parameter == DriverParameter.Taper ||
                           parameter == DriverParameter.Sweep;
                case DriverGroupPreset.SpanAreaSweep:
                    return parameter == DriverParameter.Span ||
                           parameter == DriverParameter.Area ||
                           parameter == DriverParameter.Sweep;
                case DriverGroupPreset.SpanAspectRatioSweep:
                    return parameter == DriverParameter.Span ||
                           parameter == DriverParameter.AspectRatio ||
                           parameter == DriverParameter.Sweep;
                case DriverGroupPreset.AreaAspectRatioSweep:
                    return parameter == DriverParameter.Area ||
                           parameter == DriverParameter.AspectRatio ||
                           parameter == DriverParameter.Sweep;
                case DriverGroupPreset.SpanAverageChordSweep:
                    return parameter == DriverParameter.Span ||
                           parameter == DriverParameter.AverageChord ||
                           parameter == DriverParameter.Sweep;
                case DriverGroupPreset.AreaTaperSweep:
                    return parameter == DriverParameter.Area ||
                           parameter == DriverParameter.Taper ||
                           parameter == DriverParameter.Sweep;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
