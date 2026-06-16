using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CatiaWingDesigner.App.Catia;
using CatiaWingDesigner.App.ViewModels;
using CatiaWingDesigner.Core.Geometry;
using CatiaWingDesigner.Core.Model;
using HelixToolkit.Wpf;
using WinForms = System.Windows.Forms;

namespace CatiaWingDesigner.App
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            RenderPreview(_viewModel.Geometry);
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Geometry))
            {
                RenderPreview(_viewModel.Geometry);
            }
        }

        private void OnRebuildPreview(object sender, RoutedEventArgs e)
        {
            TryRun(() =>
            {
                _viewModel.RebuildPreview();
                RenderPreview(_viewModel.Geometry);
            });
        }

        private void OnSaveJson(object sender, RoutedEventArgs e)
        {
            TryRun(() =>
            {
                using (var dialog = new WinForms.SaveFileDialog())
                {
                    dialog.Filter = "Wing Design (*.json)|*.json";
                    dialog.FileName = _viewModel.ProjectName + ".json";
                    if (dialog.ShowDialog() != WinForms.DialogResult.OK)
                    {
                        return;
                    }

                    _viewModel.SaveDesign(dialog.FileName);
                }
            });
        }

        private void OnLoadJson(object sender, RoutedEventArgs e)
        {
            TryRun(() =>
            {
                using (var dialog = new WinForms.OpenFileDialog())
                {
                    dialog.Filter = "Wing Design (*.json)|*.json";
                    if (dialog.ShowDialog() != WinForms.DialogResult.OK)
                    {
                        return;
                    }

                    _viewModel.LoadDesign(dialog.FileName);
                    RenderPreview(_viewModel.Geometry);
                }
            });
        }

        private void OnBuildCatiaSurface(object sender, RoutedEventArgs e)
        {
            BuildCatia(CatiaWingBuildMode.SurfaceOnly);
        }

        private void OnBuildCatiaClosedSolid(object sender, RoutedEventArgs e)
        {
            BuildCatia(CatiaWingBuildMode.ClosedSurfaceSolid);
        }

        private void OnBuildCatiaThickSolid(object sender, RoutedEventArgs e)
        {
            BuildCatia(CatiaWingBuildMode.ThickSurfaceSolid);
        }

        private void BuildCatia(CatiaWingBuildMode mode)
        {
            TryRun(() =>
            {
                _viewModel.RebuildPreview();
                RenderPreview(_viewModel.Geometry);

                using (var dialog = new WinForms.SaveFileDialog())
                {
                    dialog.Filter = "CATIA Part (*.CATPart)|*.CATPart";
                    dialog.FileName = $"{_viewModel.ProjectName}_{GetCatiaModeFileSuffix(mode)}.CATPart";
                    if (dialog.ShowDialog() != WinForms.DialogResult.OK)
                    {
                        return;
                    }

                    var options = new CatiaWingBuildOptions
                    {
                        Mode = mode,
                        ThickSurfaceThickness = _viewModel.ThickSurfaceThickness
                    };

                    var builder = new CatiaWingBuilder();
                    builder.Build(_viewModel.GetCurrentDesign(), _viewModel.Geometry!, dialog.FileName, options);
                    MessageBox.Show($"{GetCatiaModeLabel(mode)}完成。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });
        }

        private static string GetCatiaModeFileSuffix(CatiaWingBuildMode mode)
        {
            switch (mode)
            {
                case CatiaWingBuildMode.SurfaceOnly:
                    return "Surface";
                case CatiaWingBuildMode.ClosedSurfaceSolid:
                    return "ClosedSolid";
                case CatiaWingBuildMode.ThickSurfaceSolid:
                    return "ThickSolid";
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        private static string GetCatiaModeLabel(CatiaWingBuildMode mode)
        {
            switch (mode)
            {
                case CatiaWingBuildMode.SurfaceOnly:
                    return "CATIA 曲面生成";
                case CatiaWingBuildMode.ClosedSurfaceSolid:
                    return "CATIA 封闭曲面实体生成";
                case CatiaWingBuildMode.ThickSurfaceSolid:
                    return "CATIA 加厚曲面实体生成";
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        private void TryRun(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RenderPreview(GeneratedWingGeometry? geometry)
        {
            Viewport.Children.Clear();
            Viewport.Children.Add(new SunLight());
            Viewport.Children.Add(new CoordinateSystemVisual3D { ArrowLengths = 180 });

            if (geometry == null || geometry.Sections.Count < 2)
            {
                return;
            }

            AddSurface(geometry.UpperPreviewGrid, Color.FromArgb(84, 53, 132, 205));
            AddSurface(geometry.LowerPreviewGrid, Color.FromArgb(84, 56, 150, 118));
            AddLines(geometry);
            Viewport.ZoomExtents(500);
        }

        private void AddSurface(IReadOnlyList<List<Point3DValue>> grid, Color color)
        {
            if (grid.Count < 2 || grid[0].Count < 2)
            {
                return;
            }

            var pointCount = grid[0].Count;
            if (grid.Any(row => row.Count != pointCount))
            {
                throw new InvalidOperationException("预览网格截面点数不一致。");
            }

            var mesh = new MeshGeometry3D();
            for (var i = 0; i < grid.Count; i++)
            {
                for (var j = 0; j < pointCount; j++)
                {
                    mesh.Positions.Add(ToPoint3D(grid[i][j]));
                }
            }

            for (var i = 0; i < grid.Count - 1; i++)
            {
                for (var j = 0; j < pointCount - 1; j++)
                {
                    var a = i * pointCount + j;
                    var b = (i + 1) * pointCount + j;
                    var c = (i + 1) * pointCount + j + 1;
                    var d = i * pointCount + j + 1;

                    mesh.TriangleIndices.Add(a);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(c);
                    mesh.TriangleIndices.Add(a);
                    mesh.TriangleIndices.Add(c);
                    mesh.TriangleIndices.Add(d);
                }
            }

            var material = new DiffuseMaterial(new SolidColorBrush(color));
            var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
            Viewport.Children.Add(new ModelVisual3D { Content = model });
        }

        private void AddLines(GeneratedWingGeometry geometry)
        {
            var lines = new LinesVisual3D
            {
                Color = Color.FromRgb(40, 48, 54),
                Thickness = 1.1
            };

            foreach (var section in geometry.Sections)
            {
                AddPolyline(lines, section.UpperPreviewPoints);
                AddPolyline(lines, section.LowerPreviewPoints);
            }

            AddPolyline(lines, geometry.Sections.Select(s => s.UpperPreviewPoints.First()).ToList());
            AddPolyline(lines, geometry.Sections.Select(s => s.UpperPreviewPoints.Last()).ToList());

            var pointCount = geometry.UpperPreviewGrid[0].Count;
            var step = Math.Max(1, pointCount / 8);
            for (var j = 0; j < pointCount; j += step)
            {
                AddPolyline(lines, geometry.UpperPreviewGrid.Select(row => row[j]).ToList());
                AddPolyline(lines, geometry.LowerPreviewGrid.Select(row => row[j]).ToList());
            }

            Viewport.Children.Add(lines);
        }

        private static void AddPolyline(LinesVisual3D visual, IReadOnlyList<Point3DValue> points)
        {
            for (var i = 0; i < points.Count - 1; i++)
            {
                visual.Points.Add(ToPoint3D(points[i]));
                visual.Points.Add(ToPoint3D(points[i + 1]));
            }
        }

        private static Point3D ToPoint3D(Point3DValue value)
        {
            return new Point3D(value.X, value.Y, value.Z);
        }
    }
}
