using System;
using System.Collections.Specialized;
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
        private WingPlanformStation? _selectedPlanformStation;
        private bool _suspendPlanformAutoRebuild;
        private string _statusText = "就绪";

        public MainWindowViewModel()
        {
            _design = WingDesign.CreateDefault();
            Segments = new ObservableCollection<WingSegment>(_design.Segments);
            PlanformStations = new ObservableCollection<WingPlanformStation>(_design.PlanformStations);
            AttachPlanformStationEvents(PlanformStations);
            RebuildCommand = new RelayCommand(RebuildPreview);
            AddSegmentCommand = new RelayCommand(AddSegment);
            RemoveSegmentCommand = new RelayCommand(RemoveSelectedSegment, () => SelectedSegment != null && Segments.Count > 1);
            InsertSegmentCommand = new RelayCommand(InsertSegment, () => SelectedSegment != null);
            AddPlanformStationCommand = new RelayCommand(AddPlanformStation);
            RemovePlanformStationCommand = new RelayCommand(RemoveSelectedPlanformStation, CanRemoveSelectedPlanformStation);
            FirstSectionCommand = new RelayCommand(SelectFirstSection, () => SelectedSegmentIndex > 0);
            PreviousSectionCommand = new RelayCommand(SelectPreviousSection, () => SelectedSegmentIndex > 0);
            NextSectionCommand = new RelayCommand(SelectNextSection, () => SelectedSegmentIndex >= 0 && SelectedSegmentIndex < Segments.Count - 1);
            LastSectionCommand = new RelayCommand(SelectLastSection, () => SelectedSegmentIndex >= 0 && SelectedSegmentIndex < Segments.Count - 1);
            SelectedSegment = Segments.Count > 0 ? Segments[0] : null;
            SelectedPlanformStation = PlanformStations.Count > 0 ? PlanformStations[0] : null;
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
                OnPropertyChanged(nameof(IsSegmentDrivenPlanform));
                OnPropertyChanged(nameof(IsCustomEdgeSplinePlanform));
            }
        }

        public ObservableCollection<WingSegment> Segments { get; }

        public ObservableCollection<WingPlanformStation> PlanformStations { get; }

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

        public WingPlanformStation? SelectedPlanformStation
        {
            get => _selectedPlanformStation;
            set
            {
                if (ReferenceEquals(_selectedPlanformStation, value))
                {
                    return;
                }

                _selectedPlanformStation = value;
                OnPropertyChanged();
                RemovePlanformStationCommand.RaiseCanExecuteChanged();
            }
        }

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

        public bool IsSegmentDrivenPlanform
        {
            get => Design.PlanformMode == WingPlanformMode.SegmentDriven;
            set => SetPlanformModeIfChecked(value, WingPlanformMode.SegmentDriven);
        }

        public bool IsCustomEdgeSplinePlanform
        {
            get => Design.PlanformMode == WingPlanformMode.CustomEdgeSpline;
            set => SetPlanformModeIfChecked(value, WingPlanformMode.CustomEdgeSpline);
        }

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

        public RelayCommand AddPlanformStationCommand { get; }

        public RelayCommand RemovePlanformStationCommand { get; }

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

            _suspendPlanformAutoRebuild = true;
            PlanformStations.Clear();
            if (loaded.PlanformStations != null)
            {
                foreach (var station in loaded.PlanformStations)
                {
                    PlanformStations.Add(station);
                }
            }
            _suspendPlanformAutoRebuild = false;

            SelectedSegment = Segments.Count > 0 ? Segments[0] : null;
            SelectedPlanformStation = PlanformStations.Count > 0 ? PlanformStations[0] : null;
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

        private void AddPlanformStation()
        {
            if (PlanformStations.Count < 2)
            {
                ResetPlanformStationsFromCurrentGeometry();
                return;
            }

            var selectedIndex = SelectedPlanformStation == null ? PlanformStations.Count - 1 : PlanformStations.IndexOf(SelectedPlanformStation);
            if (selectedIndex < 0)
            {
                selectedIndex = PlanformStations.Count - 1;
            }

            var insertIndex = selectedIndex + 1;
            WingPlanformStation station;
            if (insertIndex < PlanformStations.Count)
            {
                station = InterpolatePlanformStation(PlanformStations[insertIndex - 1], PlanformStations[insertIndex], insertIndex + 1);
            }
            else
            {
                station = ExtrapolatePlanformStation(PlanformStations[PlanformStations.Count - 2], PlanformStations[PlanformStations.Count - 1], insertIndex + 1);
            }

            PlanformStations.Insert(insertIndex, station);
            SelectedPlanformStation = station;
            RemovePlanformStationCommand.RaiseCanExecuteChanged();
        }

        private void RemoveSelectedPlanformStation()
        {
            if (!CanRemoveSelectedPlanformStation())
            {
                return;
            }

            var oldIndex = PlanformStations.IndexOf(SelectedPlanformStation!);
            PlanformStations.Remove(SelectedPlanformStation!);
            SelectedPlanformStation = PlanformStations[Math.Min(oldIndex, PlanformStations.Count - 1)];
            RemovePlanformStationCommand.RaiseCanExecuteChanged();
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

            Design.PlanformStations.Clear();
            foreach (var station in PlanformStations)
            {
                Design.PlanformStations.Add(station);
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

        private void SetPlanformModeIfChecked(bool isChecked, WingPlanformMode mode)
        {
            if (!isChecked || Design.PlanformMode == mode)
            {
                return;
            }

            if (mode == WingPlanformMode.CustomEdgeSpline && PlanformStations.Count < 3)
            {
                ResetPlanformStationsFromCurrentGeometry();
            }

            Design.PlanformMode = mode;
            OnPropertyChanged(nameof(IsSegmentDrivenPlanform));
            OnPropertyChanged(nameof(IsCustomEdgeSplinePlanform));
            TryRebuildPreviewFromPlanformChange();
        }

        private void AttachPlanformStationEvents(ObservableCollection<WingPlanformStation> stations)
        {
            stations.CollectionChanged += OnPlanformStationsCollectionChanged;
            foreach (var station in stations)
            {
                station.PropertyChanged += OnPlanformStationPropertyChanged;
            }
        }

        private void OnPlanformStationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.OldItems != null)
            {
                foreach (WingPlanformStation station in args.OldItems)
                {
                    station.PropertyChanged -= OnPlanformStationPropertyChanged;
                }
            }

            if (args.NewItems != null)
            {
                foreach (WingPlanformStation station in args.NewItems)
                {
                    station.PropertyChanged += OnPlanformStationPropertyChanged;
                }
            }

            RemovePlanformStationCommand.RaiseCanExecuteChanged();
            TryRebuildPreviewFromPlanformChange();
        }

        private void OnPlanformStationPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            TryRebuildPreviewFromPlanformChange();
        }

        private void TryRebuildPreviewFromPlanformChange()
        {
            if (_suspendPlanformAutoRebuild || Design.PlanformMode != WingPlanformMode.CustomEdgeSpline)
            {
                return;
            }

            try
            {
                RebuildPreview();
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        private bool CanRemoveSelectedPlanformStation()
        {
            if (SelectedPlanformStation == null || PlanformStations.Count <= 3)
            {
                return false;
            }

            return PlanformStations.IndexOf(SelectedPlanformStation) > 0;
        }

        private void ResetPlanformStationsFromCurrentGeometry()
        {
            var sourceGeometry = Geometry;
            if (sourceGeometry == null || sourceGeometry.Sections.Count < 3)
            {
                var previousMode = Design.PlanformMode;
                Design.PlanformMode = WingPlanformMode.SegmentDriven;
                sourceGeometry = _geometryBuilder.Build(Design);
                Design.PlanformMode = previousMode;
            }

            PlanformStations.Clear();
            for (var i = 0; i < sourceGeometry.Sections.Count; i++)
            {
                var section = sourceGeometry.Sections[i];
                var trailing = GetTrailingEdgeMidPointX(section);
                var dihedral = i == 0
                    ? 0.0
                    : RadiansToDegrees(Math.Atan2(
                        section.LeadingEdge.Z - sourceGeometry.Sections[i - 1].LeadingEdge.Z,
                        section.LeadingEdge.Y - sourceGeometry.Sections[i - 1].LeadingEdge.Y));

                PlanformStations.Add(new WingPlanformStation
                {
                    Name = i == 0 ? "Root" : $"Station_{i:00}",
                    SpanY = section.LeadingEdge.Y,
                    LeadingEdgeX = section.LeadingEdge.X,
                    TrailingEdgeX = trailing,
                    TwistDeg = section.TwistDeg,
                    DihedralDegFromPrevious = dihedral,
                    Airfoil = section.Airfoil.Clone()
                });
            }

            SelectedPlanformStation = PlanformStations.Count > 0 ? PlanformStations[0] : null;
        }

        private static double GetTrailingEdgeMidPointX(WingSection section)
        {
            var upperTrailing = section.UpperRawPoints[section.UpperRawPoints.Count - 1];
            var lowerTrailing = section.LowerRawPoints[section.LowerRawPoints.Count - 1];
            return (upperTrailing.X + lowerTrailing.X) * 0.5;
        }

        private static WingPlanformStation InterpolatePlanformStation(WingPlanformStation first, WingPlanformStation second, int number)
        {
            return new WingPlanformStation
            {
                Name = $"Station_{number:00}",
                SpanY = (first.SpanY + second.SpanY) * 0.5,
                LeadingEdgeX = (first.LeadingEdgeX + second.LeadingEdgeX) * 0.5,
                TrailingEdgeX = (first.TrailingEdgeX + second.TrailingEdgeX) * 0.5,
                TwistDeg = (first.TwistDeg + second.TwistDeg) * 0.5,
                DihedralDegFromPrevious = second.DihedralDegFromPrevious,
                Airfoil = second.Airfoil.Clone()
            };
        }

        private static WingPlanformStation ExtrapolatePlanformStation(WingPlanformStation previous, WingPlanformStation last, int number)
        {
            return new WingPlanformStation
            {
                Name = $"Station_{number:00}",
                SpanY = last.SpanY + Math.Max(100.0, last.SpanY - previous.SpanY),
                LeadingEdgeX = last.LeadingEdgeX + (last.LeadingEdgeX - previous.LeadingEdgeX),
                TrailingEdgeX = last.TrailingEdgeX + (last.TrailingEdgeX - previous.TrailingEdgeX),
                TwistDeg = last.TwistDeg,
                DihedralDegFromPrevious = last.DihedralDegFromPrevious,
                Airfoil = last.Airfoil.Clone()
            };
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
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
