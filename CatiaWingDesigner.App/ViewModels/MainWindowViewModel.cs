using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CatiaWingDesigner.Core.Model;
using CatiaWingDesigner.Core.Services;

namespace CatiaWingDesigner.App.ViewModels
{
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly WingGeometryBuilder _geometryBuilder = new WingGeometryBuilder();
        private readonly DesignJsonSerializer _serializer = new DesignJsonSerializer();
        private WingDesign _design;
        private GeneratedWingGeometry? _geometry;
        private WingSegment? _selectedSegment;
        private string _statusText = "就绪";

        public MainWindowViewModel()
        {
            _design = WingDesign.CreateDefault();
            Segments = new ObservableCollection<WingSegment>(_design.Segments);
            RebuildCommand = new RelayCommand(RebuildPreview);
            AddSegmentCommand = new RelayCommand(AddSegment);
            RemoveSegmentCommand = new RelayCommand(RemoveSelectedSegment, () => SelectedSegment != null && Segments.Count > 1);
            InsertSegmentCommand = new RelayCommand(InsertSegment, () => SelectedSegment != null);
            FirstSectionCommand = new RelayCommand(SelectFirstSection, () => SelectedSegmentIndex > 0);
            PreviousSectionCommand = new RelayCommand(SelectPreviousSection, () => SelectedSegmentIndex > 0);
            NextSectionCommand = new RelayCommand(SelectNextSection, () => SelectedSegmentIndex >= 0 && SelectedSegmentIndex < Segments.Count - 1);
            LastSectionCommand = new RelayCommand(SelectLastSection, () => SelectedSegmentIndex >= 0 && SelectedSegmentIndex < Segments.Count - 1);
            SelectedSegment = Segments.Count > 0 ? Segments[0] : null;
            RebuildPreview();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public WingDesign Design
        {
            get => _design;
            private set
            {
                _design = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProjectName));
                OnPropertyChanged(nameof(RootChord));
                OnPropertyChanged(nameof(RootAirfoilCode));
                OnPropertyChanged(nameof(GenerateRootCap));
                OnPropertyChanged(nameof(GenerateTipCap));
                OnPropertyChanged(nameof(ThickSurfaceThickness));
                OnPropertyChanged(nameof(IsWingSurfaceClosed));
            }
        }

        public ObservableCollection<WingSegment> Segments { get; }

        public WingSegment? SelectedSegment
        {
            get => _selectedSegment;
            set
            {
                if (ReferenceEquals(_selectedSegment, value))
                {
                    return;
                }

                _selectedSegment = value;
                OnPropertyChanged();
                RefreshSelectedSegmentBindings();
                RaiseSectionCommandStates();
            }
        }

        public int SelectedSegmentIndex
        {
            get => SelectedSegment == null ? -1 : Segments.IndexOf(SelectedSegment);
            set
            {
                if (value < 0 || value >= Segments.Count)
                {
                    return;
                }

                SelectedSegment = Segments[value];
            }
        }

        public string SectionPositionText => SelectedSegment == null ? "0 / 0" : $"{SelectedSegmentIndex + 1} / {Segments.Count}";

        public GeneratedWingGeometry? Geometry
        {
            get => _geometry;
            private set
            {
                _geometry = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SummaryText));
            }
        }

        public string ProjectName
        {
            get => Design.ProjectName;
            set
            {
                Design.ProjectName = value;
                OnPropertyChanged();
            }
        }

        public double RootChord
        {
            get => Design.RootChord;
            set
            {
                Design.RootChord = value;
                OnPropertyChanged();
            }
        }

        public string RootAirfoilCode
        {
            get => Design.RootAirfoil.Value;
            set
            {
                Design.RootAirfoil = AirfoilRef.Naca(value);
                OnPropertyChanged();
            }
        }

        public bool GenerateRootCap
        {
            get => Design.GenerateRootCap;
            set
            {
                Design.GenerateRootCap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWingSurfaceClosed));
            }
        }

        public bool GenerateTipCap
        {
            get => Design.GenerateTipCap;
            set
            {
                Design.GenerateTipCap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWingSurfaceClosed));
            }
        }

        public double ThickSurfaceThickness
        {
            get => Design.ThickSurfaceThickness;
            set
            {
                Design.ThickSurfaceThickness = value;
                OnPropertyChanged();
            }
        }

        public bool IsWingSurfaceClosed => Design.GenerateRootCap && Design.GenerateTipCap;

        public string StatusText
        {
            get => _statusText;
            private set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public string SummaryText
        {
            get
            {
                if (Geometry == null)
                {
                    return "尚未生成预览";
                }

                return $"半展长 {Geometry.HalfSpan:F2} mm | 半翼面积 {Geometry.HalfArea:F2} mm² | 全翼面积 {Geometry.FullArea:F2} mm² | 全翼 AR {Geometry.FullAspectRatio:F3} | 平均弦长 {Geometry.AverageChord:F2} mm | 翼尖弦长 {Geometry.TipChord:F2} mm";
            }
        }

        public RelayCommand RebuildCommand { get; }

        public RelayCommand AddSegmentCommand { get; }

        public RelayCommand RemoveSegmentCommand { get; }

        public RelayCommand InsertSegmentCommand { get; }

        public RelayCommand FirstSectionCommand { get; }

        public RelayCommand PreviousSectionCommand { get; }

        public RelayCommand NextSectionCommand { get; }

        public RelayCommand LastSectionCommand { get; }

        public string SegmentName
        {
            get => RequireSelectedSegment().Name;
            set
            {
                RequireSelectedSegment().Name = value;
                OnPropertyChanged();
            }
        }

        public string SegmentTipAirfoilCode
        {
            get => RequireSelectedSegment().TipAirfoil.Value;
            set
            {
                RequireSelectedSegment().TipAirfoil = AirfoilRef.Naca(value);
                OnPropertyChanged();
            }
        }

        public double SegmentSpan
        {
            get => RequireSelectedSegment().Span;
            set
            {
                RequireSelectedSegment().Span = value;
                OnPropertyChanged();
            }
        }

        public double SegmentArea
        {
            get => RequireSelectedSegment().Area;
            set
            {
                RequireSelectedSegment().Area = value;
                OnPropertyChanged();
            }
        }

        public double SegmentAspectRatio
        {
            get => RequireSelectedSegment().AspectRatio;
            set
            {
                RequireSelectedSegment().AspectRatio = value;
                OnPropertyChanged();
            }
        }

        public double SegmentTaper
        {
            get => RequireSelectedSegment().Taper;
            set
            {
                RequireSelectedSegment().Taper = value;
                OnPropertyChanged();
            }
        }

        public double SegmentAverageChord
        {
            get => RequireSelectedSegment().AverageChord;
            set
            {
                RequireSelectedSegment().AverageChord = value;
                OnPropertyChanged();
            }
        }

        public double SegmentRootChord => RequireSelectedSegment().RootChord;

        public double SegmentTipChord
        {
            get => RequireSelectedSegment().TipChord;
            set
            {
                RequireSelectedSegment().TipChord = value;
                OnPropertyChanged();
            }
        }

        public double SegmentSweepDeg
        {
            get => RequireSelectedSegment().SweepDeg;
            set
            {
                RequireSelectedSegment().SweepDeg = value;
                OnPropertyChanged();
            }
        }

        public double SegmentSweepLocation
        {
            get => RequireSelectedSegment().SweepLocation;
            set
            {
                RequireSelectedSegment().SweepLocation = value;
                OnPropertyChanged();
            }
        }

        public double SegmentTwistDeg
        {
            get => RequireSelectedSegment().TipTwistDeg;
            set
            {
                RequireSelectedSegment().TipTwistDeg = value;
                OnPropertyChanged();
            }
        }

        public double SegmentDihedralDeg
        {
            get => RequireSelectedSegment().TipDihedralDeg;
            set
            {
                RequireSelectedSegment().TipDihedralDeg = value;
                OnPropertyChanged();
            }
        }

        public bool IsSpanTipChordSweep
        {
            get => IsDriverGroup(DriverGroupPreset.SpanTipChordSweep);
            set => SetDriverGroupIfChecked(value, DriverGroupPreset.SpanTipChordSweep);
        }

        public bool IsSpanTaperSweep
        {
            get => IsDriverGroup(DriverGroupPreset.SpanTaperSweep);
            set => SetDriverGroupIfChecked(value, DriverGroupPreset.SpanTaperSweep);
        }

        public bool IsSpanAreaSweep
        {
            get => IsDriverGroup(DriverGroupPreset.SpanAreaSweep);
            set => SetDriverGroupIfChecked(value, DriverGroupPreset.SpanAreaSweep);
        }

        public bool IsSpanAspectRatioSweep
        {
            get => IsDriverGroup(DriverGroupPreset.SpanAspectRatioSweep);
            set => SetDriverGroupIfChecked(value, DriverGroupPreset.SpanAspectRatioSweep);
        }

        public bool IsAreaAspectRatioSweep
        {
            get => IsDriverGroup(DriverGroupPreset.AreaAspectRatioSweep);
            set => SetDriverGroupIfChecked(value, DriverGroupPreset.AreaAspectRatioSweep);
        }

        public bool IsSpanAverageChordSweep
        {
            get => IsDriverGroup(DriverGroupPreset.SpanAverageChordSweep);
            set => SetDriverGroupIfChecked(value, DriverGroupPreset.SpanAverageChordSweep);
        }

        public bool IsAreaTaperSweep
        {
            get => IsDriverGroup(DriverGroupPreset.AreaTaperSweep);
            set => SetDriverGroupIfChecked(value, DriverGroupPreset.AreaTaperSweep);
        }

        public bool IsSpanActive => IsActive(DriverParameter.Span);

        public bool IsAreaActive => IsActive(DriverParameter.Area);

        public bool IsAspectRatioActive => IsActive(DriverParameter.AspectRatio);

        public bool IsTaperActive => IsActive(DriverParameter.Taper);

        public bool IsAverageChordActive => IsActive(DriverParameter.AverageChord);

        public bool IsTipChordActive => IsActive(DriverParameter.TipChord);

        public bool IsSweepActive => IsActive(DriverParameter.Sweep);

        public void RebuildPreview()
        {
            SyncSegmentsToDesign();
            Geometry = _geometryBuilder.Build(Design);
            RefreshSelectedSegmentBindings();
            StatusText = "预览已更新";
        }

        public void SaveDesign(string path)
        {
            SyncSegmentsToDesign();
            _serializer.Save(Design, path);
            StatusText = $"已保存 JSON：{Path.GetFileName(path)}";
        }

        public void LoadDesign(string path)
        {
            var loaded = _serializer.Load(path);
            Design = loaded;
            Segments.Clear();
            foreach (var segment in loaded.Segments)
            {
                Segments.Add(segment);
            }

            SelectedSegment = Segments.Count > 0 ? Segments[0] : null;
            RebuildPreview();
            StatusText = $"已加载 JSON：{Path.GetFileName(path)}";
        }

        public WingDesign GetCurrentDesign()
        {
            SyncSegmentsToDesign();
            return Design;
        }

        private void AddSegment()
        {
            var last = Segments.Count > 0 ? Segments[Segments.Count - 1] : null;
            var segment = CreateNewSegment(Segments.Count + 1, last);
            Segments.Add(segment);
            SelectedSegment = segment;
            RaiseSectionCommandStates();
            RebuildPreview();
        }

        private void InsertSegment()
        {
            var index = SelectedSegmentIndex;
            if (index < 0)
            {
                return;
            }

            var segment = CreateNewSegment(index + 2, SelectedSegment);
            Segments.Insert(index + 1, segment);
            RenumberGeneratedSegmentNames();
            SelectedSegment = segment;
            RaiseSectionCommandStates();
            RebuildPreview();
        }

        private WingSegment CreateNewSegment(int number, WingSegment? template)
        {
            return new WingSegment
            {
                Name = $"Segment_{number:00}",
                DriverGroup = DriverGroupPreset.SpanTaperSweep,
                Span = template?.Span ?? 1000.0,
                Taper = 0.8,
                SweepDeg = template?.SweepDeg ?? 15.0,
                SweepLocation = template?.SweepLocation ?? 0.25,
                TipTwistDeg = template?.TipTwistDeg ?? 0.0,
                TipDihedralDeg = template?.TipDihedralDeg ?? 0.0,
                TipAirfoil = template?.TipAirfoil.Clone() ?? AirfoilRef.Naca("0012")
            };
        }

        private void RemoveSelectedSegment()
        {
            if (SelectedSegment == null || Segments.Count <= 1)
            {
                return;
            }

            var oldIndex = SelectedSegmentIndex;
            Segments.Remove(SelectedSegment);
            RenumberGeneratedSegmentNames();
            SelectedSegment = Segments[Math.Min(oldIndex, Segments.Count - 1)];
            RaiseSectionCommandStates();
            RebuildPreview();
        }

        private void SelectFirstSection()
        {
            SelectedSegmentIndex = 0;
        }

        private void SelectPreviousSection()
        {
            SelectedSegmentIndex = SelectedSegmentIndex - 1;
        }

        private void SelectNextSection()
        {
            SelectedSegmentIndex = SelectedSegmentIndex + 1;
        }

        private void SelectLastSection()
        {
            SelectedSegmentIndex = Segments.Count - 1;
        }

        private void SyncSegmentsToDesign()
        {
            Design.Segments.Clear();
            foreach (var segment in Segments)
            {
                Design.Segments.Add(segment);
            }
        }

        private WingSegment RequireSelectedSegment()
        {
            if (SelectedSegment == null)
            {
                throw new InvalidOperationException("当前没有选中的翼段。");
            }

            return SelectedSegment;
        }

        private bool IsDriverGroup(DriverGroupPreset group)
        {
            return SelectedSegment != null && SelectedSegment.DriverGroup == group;
        }

        private void SetDriverGroupIfChecked(bool isChecked, DriverGroupPreset group)
        {
            if (!isChecked || SelectedSegment == null || SelectedSegment.DriverGroup == group)
            {
                return;
            }

            SelectedSegment.DriverGroup = group;
            RefreshDriverGroupBindings();
        }

        private bool IsActive(DriverParameter parameter)
        {
            return SelectedSegment != null && SelectedSegment.IsActive(parameter);
        }

        private void RefreshSelectedSegmentBindings()
        {
            OnPropertyChanged(nameof(SelectedSegmentIndex));
            OnPropertyChanged(nameof(SectionPositionText));
            OnPropertyChanged(nameof(SegmentName));
            OnPropertyChanged(nameof(SegmentTipAirfoilCode));
            OnPropertyChanged(nameof(SegmentSpan));
            OnPropertyChanged(nameof(SegmentArea));
            OnPropertyChanged(nameof(SegmentAspectRatio));
            OnPropertyChanged(nameof(SegmentTaper));
            OnPropertyChanged(nameof(SegmentAverageChord));
            OnPropertyChanged(nameof(SegmentRootChord));
            OnPropertyChanged(nameof(SegmentTipChord));
            OnPropertyChanged(nameof(SegmentSweepDeg));
            OnPropertyChanged(nameof(SegmentSweepLocation));
            OnPropertyChanged(nameof(SegmentTwistDeg));
            OnPropertyChanged(nameof(SegmentDihedralDeg));
            RefreshDriverGroupBindings();
        }

        private void RefreshDriverGroupBindings()
        {
            OnPropertyChanged(nameof(IsSpanTipChordSweep));
            OnPropertyChanged(nameof(IsSpanTaperSweep));
            OnPropertyChanged(nameof(IsSpanAreaSweep));
            OnPropertyChanged(nameof(IsSpanAspectRatioSweep));
            OnPropertyChanged(nameof(IsAreaAspectRatioSweep));
            OnPropertyChanged(nameof(IsSpanAverageChordSweep));
            OnPropertyChanged(nameof(IsAreaTaperSweep));
            OnPropertyChanged(nameof(IsSpanActive));
            OnPropertyChanged(nameof(IsAreaActive));
            OnPropertyChanged(nameof(IsAspectRatioActive));
            OnPropertyChanged(nameof(IsTaperActive));
            OnPropertyChanged(nameof(IsAverageChordActive));
            OnPropertyChanged(nameof(IsTipChordActive));
            OnPropertyChanged(nameof(IsSweepActive));
        }

        private void RaiseSectionCommandStates()
        {
            RemoveSegmentCommand.RaiseCanExecuteChanged();
            InsertSegmentCommand.RaiseCanExecuteChanged();
            FirstSectionCommand.RaiseCanExecuteChanged();
            PreviousSectionCommand.RaiseCanExecuteChanged();
            NextSectionCommand.RaiseCanExecuteChanged();
            LastSectionCommand.RaiseCanExecuteChanged();
        }

        private void RenumberGeneratedSegmentNames()
        {
            for (var i = 0; i < Segments.Count; i++)
            {
                if (Segments[i].Name.StartsWith("Segment_", StringComparison.Ordinal) ||
                    Segments[i].Name.StartsWith("Segment", StringComparison.Ordinal))
                {
                    Segments[i].Name = $"Segment_{i + 1:00}";
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
