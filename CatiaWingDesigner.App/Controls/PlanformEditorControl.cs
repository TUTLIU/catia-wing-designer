using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CatiaWingDesigner.Core.Model;

namespace CatiaWingDesigner.App.Controls
{
    public sealed class PlanformEditorControl : FrameworkElement
    {
        private const double MarginSize = 28.0;
        private const double PointHitRadius = 10.0;
        private const double MinimumChord = 10.0;
        private const double MinimumSpanGap = 1.0;

        public static readonly DependencyProperty StationsProperty =
            DependencyProperty.Register(
                nameof(Stations),
                typeof(ObservableCollection<WingPlanformStation>),
                typeof(PlanformEditorControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnStationsChanged));

        public static readonly DependencyProperty SelectedStationProperty =
            DependencyProperty.Register(
                nameof(SelectedStation),
                typeof(WingPlanformStation),
                typeof(PlanformEditorControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

        private DragTarget? _dragTarget;
        private string? _lastError;

        public PlanformEditorControl()
        {
            Focusable = true;
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseButtonUp;
            MouseLeave += OnMouseLeave;
        }

        public ObservableCollection<WingPlanformStation>? Stations
        {
            get => (ObservableCollection<WingPlanformStation>?)GetValue(StationsProperty);
            set => SetValue(StationsProperty, value);
        }

        public WingPlanformStation? SelectedStation
        {
            get => (WingPlanformStation?)GetValue(SelectedStationProperty);
            set => SetValue(SelectedStationProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(360.0, 240.0);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var rect = new Rect(0.0, 0.0, ActualWidth, ActualHeight);
            drawingContext.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(204, 209, 213)), 1.0), rect);

            if (Stations == null || Stations.Count == 0 || ActualWidth <= 2.0 * MarginSize || ActualHeight <= 2.0 * MarginSize)
            {
                return;
            }

            var transform = CreateTransform();
            DrawGrid(drawingContext, transform);
            DrawPlanform(drawingContext, transform);
            DrawControlPoints(drawingContext, transform);
            DrawError(drawingContext);
        }

        private static void OnStationsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        {
            var control = (PlanformEditorControl)dependencyObject;
            control.DetachStations(args.OldValue as ObservableCollection<WingPlanformStation>);
            control.AttachStations(args.NewValue as ObservableCollection<WingPlanformStation>);
            control.InvalidateVisual();
        }

        private void AttachStations(ObservableCollection<WingPlanformStation>? stations)
        {
            if (stations == null)
            {
                return;
            }

            stations.CollectionChanged += OnStationsCollectionChanged;
            foreach (var station in stations)
            {
                station.PropertyChanged += OnStationPropertyChanged;
            }
        }

        private void DetachStations(ObservableCollection<WingPlanformStation>? stations)
        {
            if (stations == null)
            {
                return;
            }

            stations.CollectionChanged -= OnStationsCollectionChanged;
            foreach (var station in stations)
            {
                station.PropertyChanged -= OnStationPropertyChanged;
            }
        }

        private void OnStationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.OldItems != null)
            {
                foreach (WingPlanformStation station in args.OldItems)
                {
                    station.PropertyChanged -= OnStationPropertyChanged;
                }
            }

            if (args.NewItems != null)
            {
                foreach (WingPlanformStation station in args.NewItems)
                {
                    station.PropertyChanged += OnStationPropertyChanged;
                }
            }

            InvalidateVisual();
        }

        private void OnStationPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            InvalidateVisual();
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs args)
        {
            Focus();
            var target = HitTestControlPoint(args.GetPosition(this));
            if (target == null)
            {
                return;
            }

            SelectedStation = Stations![target.StationIndex];
            _lastError = null;

            if (target.StationIndex == 0 && target.Edge == EdgeKind.Leading)
            {
                _lastError = "根站前缘固定在原点。";
                InvalidateVisual();
                return;
            }

            _dragTarget = target;
            CaptureMouse();
            args.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs args)
        {
            if (_dragTarget == null || args.LeftButton != MouseButtonState.Pressed || Stations == null)
            {
                return;
            }

            var world = CreateTransform().ScreenToWorld(args.GetPosition(this));
            TryApplyDrag(_dragTarget, world);
            args.Handled = true;
        }

        private void OnMouseButtonUp(object sender, MouseButtonEventArgs args)
        {
            FinishDrag(args);
        }

        private void OnMouseLeave(object sender, MouseEventArgs args)
        {
            FinishDrag(args);
        }

        private void FinishDrag(MouseEventArgs args)
        {
            if (_dragTarget == null)
            {
                return;
            }

            _dragTarget = null;
            ReleaseMouseCapture();
            args.Handled = true;
        }

        private void TryApplyDrag(DragTarget target, Point worldPoint)
        {
            var station = Stations![target.StationIndex];
            var newSpanY = target.StationIndex == 0 ? 0.0 : worldPoint.Y;
            var newLeadingEdgeX = station.LeadingEdgeX;
            var newTrailingEdgeX = station.TrailingEdgeX;

            if (target.Edge == EdgeKind.Leading)
            {
                newLeadingEdgeX = worldPoint.X;
            }
            else
            {
                newTrailingEdgeX = worldPoint.X;
            }

            if (!IsValidDrag(target.StationIndex, newSpanY, newLeadingEdgeX, newTrailingEdgeX, out var error))
            {
                _lastError = error;
                InvalidateVisual();
                return;
            }

            station.SpanY = newSpanY;
            station.LeadingEdgeX = newLeadingEdgeX;
            station.TrailingEdgeX = newTrailingEdgeX;
            _lastError = null;
            InvalidateVisual();
        }

        private bool IsValidDrag(int stationIndex, double spanY, double leadingEdgeX, double trailingEdgeX, out string error)
        {
            if (!IsFinite(spanY) || !IsFinite(leadingEdgeX) || !IsFinite(trailingEdgeX))
            {
                error = "控制点坐标必须是有限数值。";
                return false;
            }

            if (stationIndex == 0)
            {
                if (Math.Abs(spanY) > 1e-8 || Math.Abs(leadingEdgeX) > 1e-8)
                {
                    error = "根站前缘必须保持在原点。";
                    return false;
                }
            }
            else if (spanY <= Stations![stationIndex - 1].SpanY + MinimumSpanGap)
            {
                error = "站位展向坐标必须严格递增。";
                return false;
            }

            if (stationIndex < Stations!.Count - 1 && spanY >= Stations[stationIndex + 1].SpanY - MinimumSpanGap)
            {
                error = "站位展向坐标必须严格递增。";
                return false;
            }

            if (trailingEdgeX <= leadingEdgeX + MinimumChord)
            {
                error = "后缘 X 必须大于前缘 X。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private DragTarget? HitTestControlPoint(Point screenPoint)
        {
            if (Stations == null || Stations.Count == 0)
            {
                return null;
            }

            var transform = CreateTransform();
            DragTarget? bestTarget = null;
            var bestDistance = PointHitRadius * PointHitRadius;

            for (var i = 0; i < Stations.Count; i++)
            {
                var station = Stations[i];
                TryHit(new Point(station.LeadingEdgeX, station.SpanY), i, EdgeKind.Leading);
                TryHit(new Point(station.TrailingEdgeX, station.SpanY), i, EdgeKind.Trailing);
            }

            return bestTarget;

            void TryHit(Point worldPoint, int stationIndex, EdgeKind edge)
            {
                var point = transform.WorldToScreen(worldPoint);
                var distance = (point - screenPoint).LengthSquared;
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    bestTarget = new DragTarget(stationIndex, edge);
                }
            }
        }

        private void DrawGrid(DrawingContext drawingContext, CoordinateTransform transform)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(232, 235, 238)), 1.0);
            for (var i = 0; i <= 4; i++)
            {
                var tx = i / 4.0;
                var x = transform.ContentLeft + tx * transform.ContentWidth;
                drawingContext.DrawLine(gridPen, new Point(x, MarginSize), new Point(x, ActualHeight - MarginSize));

                var y = MarginSize + tx * transform.ContentHeight;
                drawingContext.DrawLine(gridPen, new Point(MarginSize, y), new Point(ActualWidth - MarginSize, y));
            }
        }

        private void DrawPlanform(DrawingContext drawingContext, CoordinateTransform transform)
        {
            var chordPen = new Pen(new SolidColorBrush(Color.FromRgb(156, 164, 171)), 1.0);
            var leadingPen = new Pen(new SolidColorBrush(Color.FromRgb(31, 113, 184)), 2.0);
            var trailingPen = new Pen(new SolidColorBrush(Color.FromRgb(195, 83, 70)), 2.0);

            for (var i = 0; i < Stations!.Count; i++)
            {
                var station = Stations[i];
                drawingContext.DrawLine(
                    chordPen,
                    transform.WorldToScreen(new Point(station.LeadingEdgeX, station.SpanY)),
                    transform.WorldToScreen(new Point(station.TrailingEdgeX, station.SpanY)));
            }

            DrawPolyline(drawingContext, leadingPen, Stations.Select(s => new Point(s.LeadingEdgeX, s.SpanY)).ToArray(), transform);
            DrawPolyline(drawingContext, trailingPen, Stations.Select(s => new Point(s.TrailingEdgeX, s.SpanY)).ToArray(), transform);
        }

        private static void DrawPolyline(DrawingContext drawingContext, Pen pen, Point[] points, CoordinateTransform transform)
        {
            for (var i = 0; i < points.Length - 1; i++)
            {
                drawingContext.DrawLine(pen, transform.WorldToScreen(points[i]), transform.WorldToScreen(points[i + 1]));
            }
        }

        private void DrawControlPoints(DrawingContext drawingContext, CoordinateTransform transform)
        {
            var leadingBrush = new SolidColorBrush(Color.FromRgb(31, 113, 184));
            var trailingBrush = new SolidColorBrush(Color.FromRgb(195, 83, 70));
            var selectedPen = new Pen(new SolidColorBrush(Color.FromRgb(37, 43, 48)), 2.0);

            for (var i = 0; i < Stations!.Count; i++)
            {
                var station = Stations[i];
                DrawPoint(new Point(station.LeadingEdgeX, station.SpanY), leadingBrush, station);
                DrawPoint(new Point(station.TrailingEdgeX, station.SpanY), trailingBrush, station);
            }

            void DrawPoint(Point worldPoint, Brush brush, WingPlanformStation station)
            {
                var point = transform.WorldToScreen(worldPoint);
                var radius = ReferenceEquals(station, SelectedStation) ? 5.8 : 4.8;
                drawingContext.DrawEllipse(brush, ReferenceEquals(station, SelectedStation) ? selectedPen : null, point, radius, radius);
            }
        }

        private void DrawError(DrawingContext drawingContext)
        {
            if (string.IsNullOrWhiteSpace(_lastError))
            {
                return;
            }

            var text = new FormattedText(
                _lastError,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"),
                12.0,
                new SolidColorBrush(Color.FromRgb(176, 55, 45)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            drawingContext.DrawText(text, new Point(MarginSize, 6.0));
        }

        private CoordinateTransform CreateTransform()
        {
            var minX = Stations!.Min(s => Math.Min(s.LeadingEdgeX, s.TrailingEdgeX));
            var maxX = Stations!.Max(s => Math.Max(s.LeadingEdgeX, s.TrailingEdgeX));
            var minY = Math.Min(0.0, Stations!.Min(s => s.SpanY));
            var maxY = Stations!.Max(s => s.SpanY);

            if (Math.Abs(maxX - minX) < 1.0)
            {
                maxX = minX + 1.0;
            }

            if (Math.Abs(maxY - minY) < 1.0)
            {
                maxY = minY + 1.0;
            }

            var contentWidth = Math.Max(1.0, ActualWidth - 2.0 * MarginSize);
            var contentHeight = Math.Max(1.0, ActualHeight - 2.0 * MarginSize);
            var scale = Math.Min(contentWidth / (maxX - minX), contentHeight / (maxY - minY));
            return new CoordinateTransform(minX, minY, scale, MarginSize, ActualHeight - MarginSize, contentWidth, contentHeight);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private enum EdgeKind
        {
            Leading,
            Trailing
        }

        private sealed class DragTarget
        {
            public DragTarget(int stationIndex, EdgeKind edge)
            {
                StationIndex = stationIndex;
                Edge = edge;
            }

            public int StationIndex { get; }

            public EdgeKind Edge { get; }
        }

        private readonly struct CoordinateTransform
        {
            public CoordinateTransform(double minX, double minY, double scale, double contentLeft, double contentBottom, double contentWidth, double contentHeight)
            {
                MinX = minX;
                MinY = minY;
                Scale = scale;
                ContentLeft = contentLeft;
                ContentBottom = contentBottom;
                ContentWidth = contentWidth;
                ContentHeight = contentHeight;
            }

            public double MinX { get; }

            public double MinY { get; }

            public double Scale { get; }

            public double ContentLeft { get; }

            public double ContentBottom { get; }

            public double ContentWidth { get; }

            public double ContentHeight { get; }

            public Point WorldToScreen(Point point)
            {
                return new Point(ContentLeft + (point.X - MinX) * Scale, ContentBottom - (point.Y - MinY) * Scale);
            }

            public Point ScreenToWorld(Point point)
            {
                return new Point((point.X - ContentLeft) / Scale + MinX, (ContentBottom - point.Y) / Scale + MinY);
            }
        }
    }
}
