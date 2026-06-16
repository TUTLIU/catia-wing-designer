using System;
using System.IO;
using CatiaWingDesigner.Core.Model;
using CatiaWingDesigner.Core.Services;

var solver = new DriverSolver();
var airfoilLibrary = new AirfoilLibrary();
var geometryBuilder = new WingGeometryBuilder();
var serializer = new DesignJsonSerializer();

TestDriverGroups();
TestInvalidParameters();
TestAirfoils();
TestGeometryAndJson();

Console.WriteLine("All tests passed.");

void TestDriverGroups()
{
    foreach (DriverGroupPreset group in Enum.GetValues(typeof(DriverGroupPreset)))
    {
        var design = WingDesign.CreateDefault();
        design.RootChord = 1000.0;
        design.Segments.Clear();
        design.Segments.Add(new WingSegment
        {
            Name = group.ToString(),
            DriverGroup = group,
            Span = 1000.0,
            TipChord = 700.0,
            Taper = 0.7,
            Area = 850000.0,
            AspectRatio = 1000.0 * 1000.0 / 850000.0,
            AverageChord = 850.0,
            SweepDeg = 15.0,
            SweepLocation = 0.25,
            TipAirfoil = AirfoilRef.Naca("0012")
        });

        var result = solver.Solve(design)[0];
        AssertPositive(result.Span, "Span");
        AssertPositive(result.TipChord, "TipChord");
        AssertPositive(result.Area, "Area");
        AssertPositive(result.AspectRatio, "AspectRatio");
    }
}

void TestInvalidParameters()
{
    var design = WingDesign.CreateDefault();
    design.RootChord = -1.0;
    ExpectFailure(() => solver.Solve(design), "负根弦必须失败");

    design = WingDesign.CreateDefault();
    design.Segments[0].SweepLocation = 1.2;
    ExpectFailure(() => solver.Solve(design), "非法 SweepLoc 必须失败");
}

void TestAirfoils()
{
    var naca = airfoilLibrary.CreateNaca4("2412");
    Assert(naca.SampleUpper(50).Count == 50, "NACA 上表面采样失败");
    Assert(naca.SampleLower(50).Count == 50, "NACA 下表面采样失败");
    AssertAirfoilXRange(naca, "NACA 2412");
    AssertClose(naca.UpperPoints[0].X, 0.0, "NACA 2412 上表面前缘 x 应为 0");
    AssertClose(naca.LowerPoints[0].X, 0.0, "NACA 2412 下表面前缘 x 应为 0");
    AssertClose(naca.UpperPoints[^1].X, 1.0, "NACA 2412 上表面尾缘 x 应为 1");
    AssertClose(naca.LowerPoints[^1].X, 1.0, "NACA 2412 下表面尾缘 x 应为 1");
    Assert(Math.Abs(naca.UpperPoints[^1].Y - naca.LowerPoints[^1].Y) > 1e-6, "NACA 2412 尾缘必须保留有限厚度");

    var datPath = Path.Combine(Path.GetTempPath(), "catia_wing_test_airfoil.dat");
    File.WriteAllLines(datPath, new[]
    {
        "TestFoil",
        "1.0 0.0",
        "0.75 0.06",
        "0.25 0.08",
        "0.0 0.0",
        "0.25 -0.05",
        "0.75 -0.04",
        "1.0 0.0"
    });

    var dat = airfoilLibrary.Load(AirfoilRef.DatFile(datPath));
    Assert(dat.UpperPoints.Count == 4, "DAT 上表面分段失败");
    Assert(dat.LowerPoints.Count == 4, "DAT 下表面分段失败");

    TestUiucAirfoilFiles();
}

void TestUiucAirfoilFiles()
{
    var root = FindWorkspaceRoot();
    var files = new[]
    {
        Path.Combine(root, "testdata", "airfoils", "uiuc", "naca2412.dat"),
        Path.Combine(root, "testdata", "airfoils", "uiuc", "clarky.dat"),
        Path.Combine(root, "testdata", "airfoils", "uiuc", "e387.dat")
    };

    foreach (var file in files)
    {
        var curve = airfoilLibrary.Load(AirfoilRef.DatFile(file));
        Assert(curve.UpperPoints.Count > 3, $"{Path.GetFileName(file)} 上表面点数不足");
        Assert(curve.LowerPoints.Count > 3, $"{Path.GetFileName(file)} 下表面点数不足");
        Assert(curve.SampleUpper(50).Count == 50, $"{Path.GetFileName(file)} 上表面采样失败");
        Assert(curve.SampleLower(50).Count == 50, $"{Path.GetFileName(file)} 下表面采样失败");
        AssertAirfoilXRange(curve, Path.GetFileName(file));

        var design = WingDesign.CreateDefault();
        design.RootAirfoil = AirfoilRef.DatFile(file);
        design.Segments.Clear();
        design.Segments.Add(new WingSegment
        {
            Name = "DAT_Test",
            DriverGroup = DriverGroupPreset.SpanTaperSweep,
            Span = 1000.0,
            Taper = 0.7,
            SweepDeg = 12.0,
            SweepLocation = 0.25,
            TipAirfoil = AirfoilRef.DatFile(file)
        });
        var geometry = geometryBuilder.Build(design);
        Assert(geometry.Sections.Count == 2, $"{Path.GetFileName(file)} 几何生成截面数错误");
    }
}

void TestGeometryAndJson()
{
    var design = WingDesign.CreateDefault();
    var geometry = geometryBuilder.Build(design);
    Assert(geometry.Sections.Count == 3, "默认设计应生成 3 个截面");
    Assert(geometry.UpperPreviewGrid.Count == 3, "上翼面预览网格截面数错误");
    AssertPositive(geometry.HalfSpan, "HalfSpan");
    AssertPositive(geometry.HalfArea, "HalfArea");

    var path = Path.Combine(Path.GetTempPath(), "catia_wing_design.json");
    serializer.Save(design, path);
    var loaded = serializer.Load(path);
    Assert(loaded.Segments.Count == design.Segments.Count, "JSON 载入翼段数不一致");
}

void AssertPositive(double value, string name)
{
    Assert(value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value), $"{name} 必须为正数");
}

void AssertAirfoilXRange(AirfoilCurve curve, string label)
{
    foreach (var point in curve.UpperPoints)
    {
        Assert(point.X >= 0.0 && point.X <= 1.0, $"{label} 上表面 x 超出 [0,1]");
    }

    foreach (var point in curve.LowerPoints)
    {
        Assert(point.X >= 0.0 && point.X <= 1.0, $"{label} 下表面 x 超出 [0,1]");
    }
}

void AssertClose(double actual, double expected, string message)
{
    Assert(Math.Abs(actual - expected) < 1e-8, message);
}

void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}

void ExpectFailure(Action action, string message)
{
    try
    {
        action();
    }
    catch
    {
        return;
    }

    throw new Exception(message);
}

string FindWorkspaceRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "testdata", "airfoils", "uiuc")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("找不到 testdata/airfoils/uiuc 测试数据目录。");
}
