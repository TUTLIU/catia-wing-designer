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

        public WingPlanformMode PlanformMode { get; set; } = WingPlanformMode.SegmentDriven;

        public List<WingSegment> Segments { get; set; } = new List<WingSegment>();

        public List<WingPlanformStation> PlanformStations { get; set; } = new List<WingPlanformStation>();

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
                },
                PlanformStations =
                {
                    new WingPlanformStation
                    {
                        Name = "Root",
                        SpanY = 0.0,
                        LeadingEdgeX = 0.0,
                        TrailingEdgeX = 1200.0,
                        TwistDeg = 0.0,
                        DihedralDegFromPrevious = 0.0,
                        Airfoil = AirfoilRef.Naca("2412")
                    },
                    new WingPlanformStation
                    {
                        Name = "InnerTip",
                        SpanY = 1400.0,
                        LeadingEdgeX = 529.866,
                        TrailingEdgeX = 1429.866,
                        TwistDeg = -1.5,
                        DihedralDegFromPrevious = 4.0,
                        Airfoil = AirfoilRef.Naca("2412")
                    },
                    new WingPlanformStation
                    {
                        Name = "Tip",
                        SpanY = 2600.0,
                        LeadingEdgeX = 1064.098,
                        TrailingEdgeX = 1559.098,
                        TwistDeg = -4.0,
                        DihedralDegFromPrevious = 6.0,
                        Airfoil = AirfoilRef.Naca("0010")
                    }
                }
            };
        }
    }
}
