using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using CatiaWingDesigner.Core.Geometry;
using CatiaWingDesigner.Core.Model;
using HybridShapeTypeLib;
using INFITF;
using KnowledgewareTypeLib;
using MECMOD;

namespace CatiaWingDesigner.App.Catia
{
    public sealed class CatiaWingBuilder
    {
        private const string CatiaProgId = "CATIA.Application";
        private const int MkEUnavailable = unchecked((int)0x800401E3);
        private const int RpcECallRejected = unchecked((int)0x80010001);
        private const double CatiaPointCoincidenceTolerance = 1.0e-6;
        private static readonly TimeSpan StartupComCallRetryTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private string _currentStep = "未开始";

        public void Build(WingDesign design, GeneratedWingGeometry geometry, string catPartPath)
        {
            Build(design, geometry, catPartPath, new CatiaWingBuildOptions
            {
                Mode = CatiaWingBuildMode.SurfaceOnly,
                ThickSurfaceThickness = design.ThickSurfaceThickness
            });
        }

        public void Build(WingDesign design, GeneratedWingGeometry geometry, string catPartPath, CatiaWingBuildOptions options)
        {
            var messageFilterRegistered = false;
            try
            {
                CatiaComMessageFilter.Register();
                messageFilterRegistered = true;

                if (geometry == null)
                {
                    throw new ArgumentNullException(nameof(geometry));
                }

                ValidateBuildRequest(design, geometry, options);

                if (string.IsNullOrWhiteSpace(catPartPath))
                {
                    throw new InvalidOperationException("CATPart 保存路径不能为空。");
                }

                var directory = Path.GetDirectoryName(catPartPath);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    throw new DirectoryNotFoundException("CATPart 输出目录不存在。");
                }

                _currentStep = "连接已打开的 CATIA";
                Application catia = (Application)GetOpenedCatiaApplication();

                var (partDocument, part) = CreateNewPartDocument(catia);

                _currentStep = "创建特征树分组";
                HybridShapeFactory factory = GetComObject(() => (HybridShapeFactory)part.HybridShapeFactory, "获取 Part.HybridShapeFactory");
                PARTITF.ShapeFactory? shapeFactory = options.Mode == CatiaWingBuildMode.SurfaceOnly
                    ? null
                    : GetComObject(() => (PARTITF.ShapeFactory)part.ShapeFactory, "获取 Part.ShapeFactory");
                HybridBodies hybridBodies = GetComObject(() => part.HybridBodies, "获取 Part.HybridBodies");
                HybridBody wingBody = AddHybridBody(hybridBodies, "WingSurface");
                UpdatePart(part, "更新 WingSurface 几何图形集");

                var sectionRefs = new object[geometry.Sections.Count];

                for (var i = 0; i < geometry.Sections.Count; i++)
                {
                    var section = geometry.Sections[i];
                    _currentStep = $"创建闭合截面 {section.Name}";

                    var sectionCurve = CreateClosedSectionCurve(part, factory, wingBody, section);
                    _currentStep = $"创建截面 {section.Name} 闭合轮廓引用";
                    sectionRefs[i] = part.CreateReferenceFromObject(sectionCurve);
                }

                _currentStep = "创建前缘和后缘导线";
                var leadingEdge = CreateGuideCurve(part, factory, wingBody, GetLeadingEdgeGuidePoints(geometry), "LeadingEdge_Guide");
                var trailingEdge = CreateGuideCurve(part, factory, wingBody, GetTrailingEdgeMidPoints(geometry), "TrailingEdge_Guide");
                _currentStep = "创建前缘导线引用";
                var leadingRef = part.CreateReferenceFromObject(leadingEdge);
                _currentStep = "创建后缘导线引用";
                var trailingRef = part.CreateReferenceFromObject(trailingEdge);

                UpdatePart(part, "更新截面曲线和导线");

                _currentStep = "创建机翼闭合截面 Loft";
                var loft = CreateLoft(part, factory, wingBody, sectionRefs, leadingRef, trailingRef, "WingSurfaceLoft");

                HybridShapeFill? rootCap = null;
                HybridShapeFill? tipCap = null;
                if (design.GenerateRootCap)
                {
                    _currentStep = "创建根部端面";
                    rootCap = CreateEndCap(factory, wingBody, sectionRefs[0], "RootCap");
                }

                if (design.GenerateTipCap)
                {
                    _currentStep = "创建翼尖端面";
                    var last = geometry.Sections.Count - 1;
                    tipCap = CreateEndCap(factory, wingBody, sectionRefs[last], "TipCap");
                }

                UpdatePart(part, "更新 Loft 和端面曲面");

                if (options.Mode == CatiaWingBuildMode.ClosedSurfaceSolid)
                {
                    _currentStep = "创建封闭曲面 Join";
                    var closedSurface = CreateClosedSurfaceJoin(part, factory, wingBody, loft, rootCap!, tipCap!, "WingClosedSurfaceJoin");

                    _currentStep = "创建封闭曲面 Join 引用";
                    var closedSurfaceRef = part.CreateReferenceFromObject(closedSurface);

                    Body solidBody = GetComObject(() => part.MainBody, "获取 Part.MainBody");
                    CreateSolidFromClosedSurface(part, shapeFactory!, solidBody, closedSurfaceRef, "WingSolid_CloseSurface");
                }
                else if (options.Mode == CatiaWingBuildMode.ThickSurfaceSolid)
                {
                    _currentStep = "创建加厚曲面引用";
                    var thickSurfaceRef = part.CreateReferenceFromObject(loft);

                    Body solidBody = GetComObject(() => part.MainBody, "获取 Part.MainBody");
                    CreateSolidFromThickSurface(part, shapeFactory!, solidBody, thickSurfaceRef, options.ThickSurfaceThickness, "WingSolid_ThickSurface");
                }

                _currentStep = "记录设计参数";
                CreateParameters(part, design, geometry);

                UpdatePart(part, "最终更新 CATPart");

                _currentStep = $"保存 CATPart 到 {catPartPath}";
                partDocument.SaveAs(catPartPath);
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException($"CATIA COM 调用失败，步骤：{_currentStep}。HRESULT: 0x{ex.ErrorCode:X8}。{ex.Message}", ex);
            }
            catch (Exception ex) when (!(ex is InvalidOperationException) && _currentStep != "未开始")
            {
                throw new InvalidOperationException($"CATIA 建模失败，步骤：{_currentStep}。异常类型：{ex.GetType().Name}。{ex.Message}", ex);
            }
            finally
            {
                if (messageFilterRegistered)
                {
                    CatiaComMessageFilter.Revoke();
                }
            }
        }

        private static void ValidateBuildRequest(WingDesign design, GeneratedWingGeometry geometry, CatiaWingBuildOptions options)
        {
            if (design == null)
            {
                throw new ArgumentNullException(nameof(design));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (geometry.Sections.Count < 2)
            {
                throw new InvalidOperationException("CATIA 建模至少需要 2 个机翼截面。");
            }

            switch (options.Mode)
            {
                case CatiaWingBuildMode.SurfaceOnly:
                    return;
                case CatiaWingBuildMode.ClosedSurfaceSolid:
                    ValidateClosedSurfaceSolidRequest(design);
                    return;
                case CatiaWingBuildMode.ThickSurfaceSolid:
                    ValidateThickSurfaceSolidRequest(options.ThickSurfaceThickness);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options.Mode), options.Mode, "未知的 CATIA 生成模式。");
            }
        }

        private static void ValidateClosedSurfaceSolidRequest(WingDesign design)
        {
            if (!IsWingSurfaceClosed(design))
            {
                throw new InvalidOperationException("通过封闭曲面生成实体要求同时生成根部端面和翼尖端面。");
            }
        }

        private static void ValidateThickSurfaceSolidRequest(double thickness)
        {
            if (thickness <= 0.0 || double.IsNaN(thickness) || double.IsInfinity(thickness))
            {
                throw new InvalidOperationException("加厚曲面实体的厚度必须为正数。");
            }
        }

        private static bool IsWingSurfaceClosed(WingDesign design)
        {
            return design.GenerateRootCap && design.GenerateTipCap;
        }

        private static dynamic GetOpenedCatiaApplication()
        {
            try
            {
                return GetRunningComObject(CatiaProgId);
            }
            catch (COMException ex) when (ex.ErrorCode == MkEUnavailable)
            {
                throw new InvalidOperationException(
                    "未找到已打开且可自动化连接的 CATIA。请先手动启动 CATIA V5 R20，确认进入主界面且没有许可证/环境/确认弹窗，" +
                    "再回到本程序生成 CATPart。程序现在不会自动启动 CATIA。", ex);
            }
        }

        private static object GetRunningComObject(string progId)
        {
            var result = CLSIDFromProgIDEx(progId, out var clsid);
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            NativeGetActiveObject(ref clsid, IntPtr.Zero, out var activeObject);
            return activeObject;
        }

        private (PartDocument Document, Part Part) CreateNewPartDocument(Application catia)
        {
            _currentStep = "获取 CATIA Documents 集合";
            Documents documents = RetryStartupComCall<Documents>(() => catia.Documents);

            _currentStep = "执行 Documents.Add(\"Part\")";
            PartDocument partDocument = RetryStartupComCall<PartDocument>(() => (PartDocument)documents.Add("Part"));

            _currentStep = "获取新建 CATPart 的 Part 对象";
            Part part = RetryStartupComCall<Part>(() => partDocument.Part);

            return (partDocument, part);
        }

        private T GetComObject<T>(Func<T> action, string step)
        {
            _currentStep = step;
            return RetryStartupComCall(action);
        }

        private static void RetryStartupComCall(Action action)
        {
            RetryStartupComCall<object>(() =>
            {
                action();
                return new object();
            });
        }

        private static T RetryStartupComCall<T>(Func<T> action)
        {
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    return action();
                }
                catch (COMException ex) when (IsStartupRetryableComError(ex) && stopwatch.Elapsed < StartupComCallRetryTimeout)
                {
                    Thread.Sleep(PollInterval);
                }
            }
        }

        private static bool IsStartupRetryableComError(COMException ex)
        {
            return ex.ErrorCode == RpcECallRejected;
        }

        private HybridBody AddHybridBody(HybridBodies hybridBodies, string name)
        {
            _currentStep = $"创建几何图形集 {name}";
            HybridBody body = hybridBodies.Add();

            _currentStep = $"命名几何图形集 {name}";
            body.set_Name(name);
            return body;
        }

        private void UpdatePart(Part part, string step)
        {
            _currentStep = step;
            part.Update();
        }

        private void UpdateObject(Part part, AnyObject target, string step)
        {
            _currentStep = step;
            part.UpdateObject(target);
        }

        private void CreateParameters(Part part, WingDesign design, GeneratedWingGeometry geometry)
        {
            Parameters parameters = GetComObject(() => part.Parameters, "获取 Part.Parameters");

            _currentStep = "记录参数 ProjectName";
            parameters.CreateString("ProjectName", design.ProjectName);

            _currentStep = "记录参数 RootChord";
            parameters.CreateDimension("RootChord", "LENGTH", design.RootChord);

            _currentStep = "记录参数 HalfSpan";
            parameters.CreateDimension("HalfSpan", "LENGTH", geometry.HalfSpan);

            _currentStep = "记录参数 HalfArea_mm2";
            parameters.CreateReal("HalfArea_mm2", geometry.HalfArea);

            _currentStep = "记录参数 FullArea_mm2";
            parameters.CreateReal("FullArea_mm2", geometry.FullArea);

            _currentStep = "记录参数 FullAspectRatio";
            parameters.CreateReal("FullAspectRatio", geometry.FullAspectRatio);

            _currentStep = "记录参数 CoordinateRule";
            parameters.CreateString("CoordinateRule", "Root leading edge origin; X chord, Y span, Z height; mm/deg");
        }

        private AnyObject CreateClosedSectionCurve(Part part, HybridShapeFactory factory, HybridBody body, WingSection section)
        {
            var points = BuildClosedSectionPointOrder(section, out var hasFiniteTrailingEdge);
            if (points.Count < 3)
            {
                throw new InvalidOperationException($"Section {section.Name} does not have enough points to create a closed section.");
            }

            if (!hasFiniteTrailingEdge)
            {
                throw new InvalidOperationException($"Section {section.Name} has a closed trailing edge. CATIA wing surface generation requires finite trailing-edge thickness.");
            }

            var splineName = $"{section.Name}_AirfoilSpline";

            _currentStep = $"创建截面样条 {splineName}";
            HybridShapeSpline spline = factory.AddNewSpline();

            _currentStep = $"设置截面样条 {splineName} 类型";
            spline.SetSplineType(0);
            spline.SetClosing(0);

            Reference? firstPointRef = null;
            Reference? lastPointRef = null;
            for (var i = 0; i < points.Count; i++)
            {
                var point = CreatePoint(factory, body, points[i], $"{section.Name}_P{i:000}");
                _currentStep = $"创建 {splineName} 第 {i + 1} 个控制点引用";
                Reference pointRef = part.CreateReferenceFromObject(point);

                _currentStep = $"向截面样条 {splineName} 添加第 {i + 1} 个控制点";
                spline.AddPointWithConstraintExplicit(pointRef, null, -1, 1, null, 0);

                if (i == 0)
                {
                    firstPointRef = pointRef;
                }

                if (i == points.Count - 1)
                {
                    lastPointRef = pointRef;
                }
            }

            if (firstPointRef == null || lastPointRef == null)
            {
                throw new InvalidOperationException($"截面 {section.Name} 没有足够的点创建后缘闭合直线。");
            }

            _currentStep = $"命名截面样条 {splineName}";
            spline.set_Name(splineName);

            _currentStep = $"追加截面样条 {splineName} 到特征树";
            body.AppendHybridShape(spline);
            UpdateObject(part, spline, $"更新截面样条 {splineName}");

            var trailingLineName = $"{section.Name}_TrailingEdgeLine";
            _currentStep = $"创建截面后缘闭合直线 {trailingLineName}";
            HybridShapeLinePtPt trailingLine = factory.AddNewLinePtPt(lastPointRef, firstPointRef);

            _currentStep = $"命名截面后缘闭合直线 {trailingLineName}";
            trailingLine.set_Name(trailingLineName);

            _currentStep = $"追加截面后缘闭合直线 {trailingLineName} 到特征树";
            body.AppendHybridShape(trailingLine);
            UpdateObject(part, trailingLine, $"更新截面后缘闭合直线 {trailingLineName}");

            _currentStep = $"创建截面闭合 Join {section.Name}";
            Reference splineRef = part.CreateReferenceFromObject(spline);
            Reference trailingLineRef = part.CreateReferenceFromObject(trailingLine);
            HybridShapeAssemble join = factory.AddNewJoin(splineRef, trailingLineRef);

            _currentStep = $"命名截面闭合 Join {section.Name}";
            join.set_Name($"{section.Name}_ClosedSection");

            _currentStep = $"追加截面闭合 Join {section.Name} 到特征树";
            body.AppendHybridShape(join);
            UpdateObject(part, join, $"更新截面闭合 Join {section.Name}");
            return join;
        }

        private static System.Collections.Generic.List<Point3DValue> BuildClosedSectionPointOrder(WingSection section, out bool hasFiniteTrailingEdge)
        {
            if (section.UpperRawPoints.Count < 2 || section.LowerRawPoints.Count < 2)
            {
                throw new InvalidOperationException($"Section {section.Name} does not have enough upper/lower points to create a closed section.");
            }

            var upperTrailing = section.UpperRawPoints[section.UpperRawPoints.Count - 1];
            var lowerTrailing = section.LowerRawPoints[section.LowerRawPoints.Count - 1];
            hasFiniteTrailingEdge = !AreCoincident(upperTrailing, lowerTrailing);

            var points = new System.Collections.Generic.List<Point3DValue>(section.UpperRawPoints.Count + section.LowerRawPoints.Count - 1);
            for (var i = section.UpperRawPoints.Count - 1; i >= 0; i--)
            {
                points.Add(section.UpperRawPoints[i]);
            }

            for (var i = 1; i < section.LowerRawPoints.Count; i++)
            {
                points.Add(section.LowerRawPoints[i]);
            }

            return points;
        }

        private static bool AreCoincident(Point3DValue first, Point3DValue second)
        {
            return DistanceSquared(first, second) <= CatiaPointCoincidenceTolerance * CatiaPointCoincidenceTolerance;
        }

        private static double DistanceSquared(Point3DValue first, Point3DValue second)
        {
            var dx = first.X - second.X;
            var dy = first.Y - second.Y;
            var dz = first.Z - second.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static System.Collections.Generic.List<Point3DValue> GetLeadingEdgeGuidePoints(GeneratedWingGeometry geometry)
        {
            var points = new System.Collections.Generic.List<Point3DValue>(geometry.Sections.Count);
            foreach (var section in geometry.Sections)
            {
                points.Add(section.UpperRawPoints[0]);
            }

            return points;
        }

        private static System.Collections.Generic.List<Point3DValue> GetTrailingEdgeMidPoints(GeneratedWingGeometry geometry)
        {
            var points = new System.Collections.Generic.List<Point3DValue>(geometry.Sections.Count);
            foreach (var section in geometry.Sections)
            {
                var upperTrailing = section.UpperRawPoints[section.UpperRawPoints.Count - 1];
                var lowerTrailing = section.LowerRawPoints[section.LowerRawPoints.Count - 1];
                points.Add(new Point3DValue(
                    (upperTrailing.X + lowerTrailing.X) * 0.5,
                    (upperTrailing.Y + lowerTrailing.Y) * 0.5,
                    (upperTrailing.Z + lowerTrailing.Z) * 0.5));
            }

            return points;
        }

        private HybridShapeSpline CreateSpline(Part part, HybridShapeFactory factory, HybridBody body, System.Collections.Generic.IReadOnlyList<Point3DValue> points, string name)
        {
            _currentStep = $"创建样条 {name}";
            HybridShapeSpline spline = factory.AddNewSpline();

            _currentStep = $"设置样条 {name} 类型";
            spline.SetSplineType(0);
            spline.SetClosing(0);

            for (var i = 0; i < points.Count; i++)
            {
                var point = CreatePoint(factory, body, points[i], $"{name}_P{i:000}");
                _currentStep = $"创建 {name} 第 {i + 1} 个控制点引用";
                Reference pointRef = part.CreateReferenceFromObject(point);

                _currentStep = $"向样条 {name} 添加第 {i + 1} 个控制点";
                spline.AddPointWithConstraintExplicit(pointRef, null, -1, 1, null, 0);
            }

            _currentStep = $"命名样条 {name}";
            spline.set_Name(name);

            _currentStep = $"追加样条 {name} 到特征树";
            body.AppendHybridShape(spline);
            UpdateObject(part, spline, $"更新样条 {name}");
            return spline;
        }

        private AnyObject CreateGuideCurve(Part part, HybridShapeFactory factory, HybridBody body, System.Collections.Generic.IReadOnlyList<Point3DValue> guidePoints, string name)
        {
            if (guidePoints.Count < 2)
            {
                throw new InvalidOperationException("导线至少需要 2 个点。");
            }

            var pointRefs = new System.Collections.Generic.List<Reference>(guidePoints.Count);
            for (var i = 0; i < guidePoints.Count; i++)
            {
                var point = CreatePoint(factory, body, guidePoints[i], $"{name}_P{i:000}");
                _currentStep = $"创建导线 {name} 第 {i + 1} 个点引用";
                pointRefs.Add(part.CreateReferenceFromObject(point));
            }

            if (pointRefs.Count == 2)
            {
                _currentStep = $"创建导线直线 {name}";
                HybridShapeLinePtPt line = factory.AddNewLinePtPt(pointRefs[0], pointRefs[1]);

                _currentStep = $"命名导线直线 {name}";
                line.set_Name(name);

                _currentStep = $"追加导线直线 {name} 到特征树";
                body.AppendHybridShape(line);
                UpdateObject(part, line, $"更新导线直线 {name}");
                return line;
            }

            var lineRefs = new System.Collections.Generic.List<Reference>(pointRefs.Count - 1);
            for (var i = 0; i < pointRefs.Count - 1; i++)
            {
                _currentStep = $"创建导线 {name} 第 {i + 1} 段直线";
                HybridShapeLinePtPt line = factory.AddNewLinePtPt(pointRefs[i], pointRefs[i + 1]);

                _currentStep = $"命名导线 {name} 第 {i + 1} 段直线";
                line.set_Name($"{name}_Segment{i:000}");

                _currentStep = $"追加导线 {name} 第 {i + 1} 段直线到特征树";
                body.AppendHybridShape(line);
                UpdateObject(part, line, $"更新导线 {name} 第 {i + 1} 段直线");
                lineRefs.Add(part.CreateReferenceFromObject(line));
            }

            _currentStep = $"创建导线 Join {name}";
            HybridShapeAssemble join = factory.AddNewJoin(lineRefs[0], lineRefs[1]);
            for (var i = 2; i < lineRefs.Count; i++)
            {
                join.AddElement(lineRefs[i]);
            }

            _currentStep = $"命名导线 Join {name}";
            join.set_Name(name);

            _currentStep = $"追加导线 Join {name} 到特征树";
            body.AppendHybridShape(join);
            UpdateObject(part, join, $"更新导线 Join {name}");
            return join;
        }

        private HybridShapePointCoord CreatePoint(HybridShapeFactory factory, HybridBody body, Point3DValue point, string name)
        {
            _currentStep = $"创建点 {name}";
            HybridShapePointCoord catPoint = factory.AddNewPointCoord(point.X, point.Y, point.Z);

            _currentStep = $"命名点 {name}";
            catPoint.set_Name(name);

            _currentStep = $"追加点 {name} 到特征树";
            body.AppendHybridShape(catPoint);
            return catPoint;
        }

        private HybridShapeLoft CreateLoft(Part part, HybridShapeFactory factory, HybridBody body, object[] sectionRefs, Reference leadingRef, Reference trailingRef, string name)
        {
            _currentStep = $"创建 Loft {name}";
            HybridShapeLoft loft = factory.AddNewLoft();

            _currentStep = $"命名 Loft {name}";
            loft.set_Name(name);

            for (var i = 0; i < sectionRefs.Length; i++)
            {
                _currentStep = $"向 Loft {name} 添加第 {i + 1} 个截面";
                loft.AddSectionToLoft((Reference)sectionRefs[i], 1, null);
            }

            _currentStep = $"向 Loft {name} 添加前缘导线";
            loft.AddGuide(leadingRef);

            _currentStep = $"向 Loft {name} 添加后缘导线";
            loft.AddGuide(trailingRef);

            _currentStep = $"追加 Loft {name} 到特征树";
            body.AppendHybridShape(loft);
            UpdatePart(part, $"更新 Loft {name}");
            return loft;
        }

        private HybridShapeFill CreateEndCap(HybridShapeFactory factory, HybridBody body, object boundaryRef, string name)
        {
            _currentStep = $"创建端面 Fill {name}";
            HybridShapeFill fill = factory.AddNewFill();

            _currentStep = $"命名端面 Fill {name}";
            fill.set_Name(name);

            _currentStep = $"向端面 Fill {name} 添加闭合边界";
            fill.AddBound((Reference)boundaryRef);

            _currentStep = $"追加端面 Fill {name} 到特征树";
            body.AppendHybridShape(fill);
            return fill;
        }

        private HybridShapeAssemble CreateClosedSurfaceJoin(
            Part part,
            HybridShapeFactory factory,
            HybridBody body,
            AnyObject loft,
            AnyObject rootCap,
            AnyObject tipCap,
            string name)
        {
            _currentStep = $"创建 {name} 的 Loft 引用";
            Reference loftRef = part.CreateReferenceFromObject(loft);

            _currentStep = $"创建 {name} 的根部端面引用";
            Reference rootCapRef = part.CreateReferenceFromObject(rootCap);

            _currentStep = $"创建 {name} 的翼尖端面引用";
            Reference tipCapRef = part.CreateReferenceFromObject(tipCap);

            _currentStep = $"创建封闭曲面 Join {name}";
            HybridShapeAssemble join = factory.AddNewJoin(loftRef, rootCapRef);

            _currentStep = $"向封闭曲面 Join {name} 添加翼尖端面";
            join.AddElement(tipCapRef);

            _currentStep = $"命名封闭曲面 Join {name}";
            join.set_Name(name);

            _currentStep = $"追加封闭曲面 Join {name} 到特征树";
            body.AppendHybridShape(join);
            UpdateObject(part, join, $"更新封闭曲面 Join {name}");
            return join;
        }

        private void CreateSolidFromClosedSurface(
            Part part,
            PARTITF.ShapeFactory shapeFactory,
            Body solidBody,
            Reference closedSurfaceRef,
            string name)
        {
            if (solidBody == null)
            {
                throw new InvalidOperationException("当前 CATPart 没有可用于生成实体的零件几何体。");
            }

            _currentStep = $"设置零件几何体为当前工作对象 {name}";
            part.InWorkObject = solidBody;

            _currentStep = $"通过封闭曲面生成实体 {name}";
            PARTITF.CloseSurface closeSurface = shapeFactory.AddNewCloseSurface(closedSurfaceRef);

            _currentStep = $"命名封闭曲面实体 {name}";
            closeSurface.set_Name(name);

            UpdateObject(part, closeSurface, $"更新封闭曲面实体 {name}");
        }

        private void CreateSolidFromThickSurface(
            Part part,
            PARTITF.ShapeFactory shapeFactory,
            Body solidBody,
            Reference surfaceRef,
            double thickness,
            string name)
        {
            if (solidBody == null)
            {
                throw new InvalidOperationException("当前 CATPart 没有可用于生成实体的零件几何体。");
            }

            _currentStep = $"设置零件几何体为当前工作对象 {name}";
            part.InWorkObject = solidBody;

            _currentStep = $"通过加厚曲面生成实体 {name}";
            PARTITF.ThickSurface thickSurface = shapeFactory.AddNewThickSurface(surfaceRef, 1, thickness, 0.0);

            _currentStep = $"命名加厚曲面实体 {name}";
            thickSurface.set_Name(name);

            UpdateObject(part, thickSurface, $"更新加厚曲面实体 {name}");
        }

        [DllImport("oleaut32.dll", EntryPoint = "GetActiveObject", PreserveSig = false)]
        private static extern void NativeGetActiveObject(
            ref Guid rclsid,
            IntPtr pvReserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

        [DllImport("ole32.dll")]
        private static extern int CLSIDFromProgIDEx(
            [MarshalAs(UnmanagedType.LPWStr)] string lpszProgID,
            out Guid lpclsid);
    }
}
