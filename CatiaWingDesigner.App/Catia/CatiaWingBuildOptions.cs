namespace CatiaWingDesigner.App.Catia
{
    public enum CatiaWingBuildMode
    {
        SurfaceOnly,
        ClosedSurfaceSolid,
        ThickSurfaceSolid
    }

    public sealed class CatiaWingBuildOptions
    {
        public CatiaWingBuildMode Mode { get; set; } = CatiaWingBuildMode.SurfaceOnly;

        public double ThickSurfaceThickness { get; set; } = 2.0;
    }
}
