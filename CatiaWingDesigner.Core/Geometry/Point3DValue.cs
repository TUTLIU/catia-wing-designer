namespace CatiaWingDesigner.Core.Geometry
{
    public readonly struct Point3DValue
    {
        public Point3DValue(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }
    }
}
