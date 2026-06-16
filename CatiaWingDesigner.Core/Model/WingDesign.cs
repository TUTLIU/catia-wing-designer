using System.Collections.Generic;

namespace CatiaWingDesigner.Core.Model
{
    public sealed class WingDesign
    {
        public string ProjectName { get; set; } = "CATIA_Wing";

        public double RootChord { get; set; } = 1200.0;

        public AirfoilRef RootAirfoil { get; set; } = AirfoilRef.Naca("2412");

        public bool GenerateRootCap { get; set; } = true;

        public bool GenerateTipCap { get; set; } = true;

        public double ThickSurfaceThickness { get; set; } = 2.0;

        public string OutputDirectory { get; set; } = string.Empty;

        public List<WingSegment> Segments { get; set; } = new List<WingSegment>();

        public static WingDesign CreateDefault()
        {
            return new WingDesign
            {
                ProjectName = "Demo_Wing",
                RootChord = 1200.0,
                RootAirfoil = AirfoilRef.Naca("2412"),
                GenerateRootCap = true,
                GenerateTipCap = true,
                ThickSurfaceThickness = 2.0,
                Segments =
                {
                    new WingSegment
                    {
                        Name = "Inner",
                        DriverGroup = DriverGroupPreset.SpanTaperSweep,
                        Span = 1400.0,
                        Taper = 0.75,
                        SweepDeg = 18.0,
                        SweepLocation = 0.25,
                        TipTwistDeg = -1.5,
                        TipDihedralDeg = 4.0,
                        TipAirfoil = AirfoilRef.Naca("2412")
                    },
                    new WingSegment
                    {
                        Name = "Outer",
                        DriverGroup = DriverGroupPreset.SpanTaperSweep,
                        Span = 1200.0,
                        Taper = 0.55,
                        SweepDeg = 24.0,
                        SweepLocation = 0.25,
                        TipTwistDeg = -4.0,
                        TipDihedralDeg = 6.0,
                        TipAirfoil = AirfoilRef.Naca("0010")
                    }
                }
            };
        }
    }
}
