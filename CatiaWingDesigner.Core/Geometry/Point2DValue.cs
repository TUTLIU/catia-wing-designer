namespace CatiaWingDesigner.Core.Geometry
{
    public readonly struct Point2DValue
    {
        public Point2DValue(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }
    }
}
