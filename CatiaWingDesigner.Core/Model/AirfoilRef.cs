namespace CatiaWingDesigner.Core.Model
{
    public sealed class AirfoilRef
    {
        public AirfoilKind Kind { get; set; } = AirfoilKind.Naca4Digit;

        public string Value { get; set; } = "2412";

        public static AirfoilRef Naca(string code)
        {
            return new AirfoilRef { Kind = AirfoilKind.Naca4Digit, Value = code };
        }

        public static AirfoilRef DatFile(string path)
        {
            return new AirfoilRef { Kind = AirfoilKind.DatFile, Value = path };
        }

        public AirfoilRef Clone()
        {
            return new AirfoilRef { Kind = Kind, Value = Value };
        }

        public override string ToString()
        {
            return $"{Kind}: {Value}";
        }
    }
}
