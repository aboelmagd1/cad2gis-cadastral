using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Win32;
using ArcGIS.Core.Geometry;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Catalog;
using Cad2Gis.AddIn.Geometry;
using Cad2Gis.AddIn.IO;
using Cad2Gis.AddIn.SpatialEnrichment;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Services;
using Cad2Gis.Core.Lookups;
using Cad2Gis.Core.Utils;

namespace Cad2Gis.AddIn.DockPane;

public class RelayCommandParam : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;
    public RelayCommandParam(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public class MapLayerItem
{
    public string Name { get; set; } = string.Empty;
    public string GeometryType { get; set; } = string.Empty;
    public string FeatureClassPath { get; set; } = string.Empty;
    public FeatureLayer? Layer { get; set; }
    public string DisplayText => $"{Name} ({GeometryType})";
}

public class InferredBorderItem : System.ComponentModel.INotifyPropertyChanged
{
    private string _descriptio = string.Empty;
    private string _borderType = "STR";
    private double? _measuredWidth;
    private double? _suggestedWidth;
    private bool _isAccepted = true;

    public long InternalId { get; set; }
    public string PlotId { get; set; } = string.Empty;
    public int BorderName { get; set; }
    public string DirectionArabic { get; set; } = string.Empty;

    public double? MeasuredWidth
    {
        get => _measuredWidth;
        set { _measuredWidth = value; OnPropertyChanged(nameof(MeasuredWidth)); }
    }

    public double? SuggestedWidth
    {
        get => _suggestedWidth;
        set { _suggestedWidth = value; OnPropertyChanged(nameof(SuggestedWidth)); }
    }

    public string Descriptio
    {
        get => _descriptio;
        set { _descriptio = value; OnPropertyChanged(nameof(Descriptio)); }
    }

    public string BorderType
    {
        get => _borderType;
        set { _borderType = value; OnPropertyChanged(nameof(BorderType)); }
    }

    public bool IsAccepted
    {
        get => _isAccepted;
        set { _isAccepted = value; OnPropertyChanged(nameof(IsAccepted)); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class Cad2GisDockpaneViewModel : ArcGIS.Desktop.Framework.Contracts.DockPane
{
    private const string DockPaneId = "Cad2Gis_Dockpane";
    private static Cad2GisDockpaneViewModel? _instance;
    public static Cad2GisDockpaneViewModel? Instance => _instance;

    private readonly ProjectMetadata _projectMeta = new();
    private HelperFilesManager _helperManager;
    private ConsultantProfileModel? _profileModel;
    private AdminAreaDetector? _adminDetector;
    private readonly object _cadLayersLock = new();
    private readonly object _inferredBordersLock = new();

    // In-memory data caches
    private readonly List<ParcelRecord> _parcels = new();
    private readonly Dictionary<long, Polygon> _parcelGeometries = new();
    private readonly List<BorderRecord> _borders = new();
    private readonly Dictionary<long, Polyline> _borderGeometries = new();

    public Cad2GisDockpaneViewModel()
    {
        _instance = this;

        // Enable thread-safe WPF ObservableCollection modifications across threads
        BindingOperations.EnableCollectionSynchronization(CadLayers, _cadLayersLock);
        BindingOperations.EnableCollectionSynchronization(InferredBorders, _inferredBordersLock);

        // Initialize HelperFiles resolution & bootstrap (Section 10)
        string baseDir = ResolveBaseFolder();
        _helperManager = new HelperFilesManager(projectHomeFolder: baseDir);
        if (_helperManager.IsMissingFromProject)
        {
            StatusMessage = "يرجى نسخ HelperFiles في مجلد المشروع";
        }

        MunicipalLookups.LoadFromLookupsDirectory(_helperManager.LookupsDir);
        MunicipalLookups.LoadFromRequiredFieldsExcel(_helperManager.RequiredFieldsExcel);

        _adminDetector = new AdminAreaDetector(_helperManager.AdminAreasDir);

        // Production output directory setup: prioritize active Project folder
        string? activeProjHome = ArcGIS.Desktop.Core.Project.Current?.HomeFolderPath;
        if (string.IsNullOrWhiteSpace(activeProjHome) && !string.IsNullOrWhiteSpace(ArcGIS.Desktop.Core.Project.Current?.DefaultGeodatabasePath))
        {
            activeProjHome = Path.GetDirectoryName(ArcGIS.Desktop.Core.Project.Current.DefaultGeodatabasePath);
        }
        if (!string.IsNullOrWhiteSpace(activeProjHome) && Directory.Exists(activeProjHome))
        {
            baseDir = activeProjHome;
        }

        OutputDirectory = Path.Combine(baseDir, "Export_Output");
        GdbPath = GdbFeatureClassService.GetEffectiveGdbPath(OutputDirectory);

        // Subscribe to ProjectOpenedEvent to automatically adapt when a project is opened in Pro
        try
        {
            ArcGIS.Desktop.Core.Events.ProjectOpenedEvent.Subscribe(OnProjectOpened);
        }
        catch { }

        // Do not auto-load files on startup; paths remain clean for user selection
        if (File.Exists(_helperManager.ExpectedLayersExcel))
        {
            ExpectedLayersExcelPath = _helperManager.ExpectedLayersExcel;
        }

        // Profile setup
        LoadAvailableProfiles();

        // Initialize Commands
        ScanCadLayersCommand = new RelayCommand(async () => await ExecuteStep1Async(), () => !IsBusy);
        BuildParcelsCommand = new RelayCommand(async () => await ExecuteStep2Async(), () => !IsBusy);
        PopulateSchemaCommand = new RelayCommand(async () => await ExecuteStep3PopulateSchemaAsync(), () => !IsBusy);
        BuildBordersCommand = new RelayCommand(async () => await ExecuteStep4BordersAsync(), () => !IsBusy);
        ExportAndQaCommand = new RelayCommand(async () => await ExecuteStep5ExportQaAsync(), () => !IsBusy);
        RunAllPipelineCommand = new RelayCommand(async () => await ExecutePipelineAsync(), () => !IsBusy);
        LoadDemoProjectCommand = new RelayCommand(LoadDemoProject);

        ExportStep2ParcelsCommand = new RelayCommand(async () => await ExportStep2ParcelsAsync(), () => !IsBusy);
        ExportStep3PopulatedParcelsCommand = new RelayCommand(async () => await ExportStep3PopulatedParcelsAsync(), () => !IsBusy);
        ExportStep4BordersCommand = new RelayCommand(async () => await ExportStep4BordersAsync(), () => !IsBusy);

        BrowseCadDrawingCommand = new RelayCommand(BrowseCadDrawing);
        BrowseLandUseCommand = new RelayCommand(BrowseLandUse);
        BrowseSourceParcelCommand = new RelayCommand(BrowseSourceParcel);
        BrowseAreasExcelCommand = new RelayCommand(BrowseAreasExcel);
        BrowseExpectedLayersCommand = new RelayCommand(BrowseExpectedLayers);
        LoadExpectedLayersCommand = new RelayCommand(() => LoadLayersFromExpectedExcel());
        BrowseOutputFolderCommand = new RelayCommand(BrowseOutputFolder);
        BrowseGdbPathCommand = new RelayCommand(BrowseGdbPathAction, () => !IsBusy);
        UseExportOutputGdbCommand = new RelayCommand(UseExportOutputGdbAction, () => !IsBusy);
        UseProjectGdbCommand = new RelayCommand(UseProjectGdbAction, () => !IsBusy);
        BrowseCustomSchemaDataCommand = new RelayCommand(BrowseCustomSchemaData);
        AutoMapFieldsCommand = new RelayCommand(AutoMapFields);

        foreach (var target in CustomSchemaDataService.MomrahTargetFields)
        {
            FieldMappings.Add(new FieldMappingItem
            {
                TargetField = target.TargetField,
                TargetDescription = target.DescriptionArabic,
                IsMandatory = target.IsMandatory
            });
        }

        AcceptAllInferredCommand = new RelayCommand(AcceptAllInferred);
        ApplySuggestedWidthCommand = new RelayCommandParam(param => { if (param is InferredBorderItem item) ApplySuggestedWidth(item); });

        ConvertCadToDatasetCommand = new RelayCommand(async () => await ConvertCadToDatasetActionAsync(), () => !IsBusy);
        DetectAddressFromMainParcelsCommand = new RelayCommand(DetectAddressFromMainParcelsAction, () => !IsBusy);

        // CAD Layer Tabbed Filter & Commands (Image 2 specification)
        _filteredCadLayers = CollectionViewSource.GetDefaultView(CadLayers);
        _filteredCadLayers.Filter = obj =>
        {
            if (obj is CadLayerInfo l)
            {
                return string.Equals(l.Category, SelectedCadTab, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        };

        SelectCadTabCommand = new RelayCommandParam(param =>
        {
            if (param is string tab)
            {
                SelectedCadTab = tab;
            }
        });

        MoveLayerToCategoryCommand = new RelayCommandParam(param =>
        {
            if (param is string targetCat)
            {
                var cur = _filteredCadLayers?.CurrentItem as CadLayerInfo;
                if (cur != null)
                {
                    cur.Category = targetCat;
                    if (targetCat == "Main Parcels") { cur.Role = LayerRole.Boundary; cur.IsSelected = true; }
                    else if (targetCat == "Sub-Parcels / Utilities") { cur.Role = LayerRole.Service; cur.IsSelected = true; }
                    else if (targetCat == "Text & Annotations") { cur.Role = LayerRole.PlotText; cur.IsSelected = true; }
                    else if (targetCat == "Roads & References") { cur.Role = LayerRole.Ignore; cur.IsSelected = false; }
                    _filteredCadLayers?.Refresh();
                    UpdateCadTabCounts();
                }
            }
        });

        SelectOnlyCurrentLayerCommand = new RelayCommandParam(param =>
        {
            var target = param as CadLayerInfo ?? SelectedCadLayer ?? _filteredCadLayers?.CurrentItem as CadLayerInfo;
            if (target == null) return;
            foreach (var l in CadLayers)
            {
                l.IsSelected = (l == target || l.LayerName.Equals(target.LayerName, StringComparison.OrdinalIgnoreCase));
            }
            _filteredCadLayers?.Refresh();
            UpdateCadTabCounts();
            StatusMessage = $"تم تحديد الطبقة [{target.LayerName}] فقط واستبعاد باقي الطبقات لبناء المضلعات.";
        });

        SelectLayerAndProceedToStep2Command = new RelayCommandParam(param =>
        {
            var target = param as CadLayerInfo ?? SelectedCadLayer ?? _filteredCadLayers?.CurrentItem as CadLayerInfo;
            if (target == null) return;
            foreach (var l in CadLayers)
            {
                l.IsSelected = (l == target || l.LayerName.Equals(target.LayerName, StringComparison.OrdinalIgnoreCase));
            }
            _filteredCadLayers?.Refresh();
            UpdateCadTabCounts();
            WorkingSourceMode = "FromPreviousStep";
            SelectedTabIndex = 1;
            StatusMessage = $"تم تحديد الطبقة [{target.LayerName}] فقط والانتقال إلى الخطوة 2 لبناء واستخراج البارسلات.";

            // Save selected layer into Expected Layers Excel
            string targetExcel = !string.IsNullOrWhiteSpace(ExpectedLayersExcelPath) ? ExpectedLayersExcelPath : _helperManager.ExpectedLayersExcel;
            if (!string.IsNullOrWhiteSpace(targetExcel))
            {
                try
                {
                    ExpectedLayersService.AppendOrUpdateLayersInExpectedExcel(targetExcel, SelectedProfile, new[] { target });
                }
                catch { }
            }
        });

        ProceedToStep2Command = new RelayCommand(() =>
        {
            var selected = CadLayers.Where(l => l.IsSelected).ToList();
            if (selected.Count == 0)
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    "يرجى تحديد طبقة واحدة على الأقل بعلامة (✔) لاستخراج البارسلات منها قبل الانتقال للخطوة 2.",
                    "تحديد الطبقات",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            WorkingSourceMode = "FromPreviousStep";
            SelectedTabIndex = 1;
            StatusMessage = $"تم اعتماد {selected.Count} طبقة مختارة والانتقال إلى الخطوة 2 (إنشاء البارسلات).";

            // Save newly selected layers into Expected Layers Excel
            string targetExcel = !string.IsNullOrWhiteSpace(ExpectedLayersExcelPath) ? ExpectedLayersExcelPath : _helperManager.ExpectedLayersExcel;
            if (!string.IsNullOrWhiteSpace(targetExcel))
            {
                try
                {
                    ExpectedLayersService.AppendOrUpdateLayersInExpectedExcel(targetExcel, SelectedProfile, selected);
                }
                catch { }
            }
        }, () => !IsBusy);

        SelectAllLayersCommand = new RelayCommand(() =>
        {
            var targetList = CadLayers.Where(c => string.Equals(c.Category, SelectedCadTab, StringComparison.OrdinalIgnoreCase)).ToList();
            if (targetList.Count == 0) targetList = CadLayers.ToList();
            foreach (var l in targetList) l.IsSelected = true;
            _filteredCadLayers?.Refresh();
            UpdateCadTabCounts();
            StatusMessage = $"تم تفعيل جميع طبقات قسم [{SelectedCadTab}].";
        });

        DeselectAllLayersCommand = new RelayCommand(() =>
        {
            var targetList = CadLayers.Where(c => string.Equals(c.Category, SelectedCadTab, StringComparison.OrdinalIgnoreCase)).ToList();
            if (targetList.Count == 0) targetList = CadLayers.ToList();
            foreach (var l in targetList) l.IsSelected = false;
            _filteredCadLayers?.Refresh();
            UpdateCadTabCounts();
            StatusMessage = $"تم إلغاء تفعيل طبقات قسم [{SelectedCadTab}].";
        });

        // Part B commands wiring
        UseLatestExportCommand = new RelayCommand(() =>
        {
            WorkingSourceMode = "LatestExport";
            RefreshLatestExportStatus();
        });

        UseSessionDataCommand = new RelayCommand(() =>
        {
            WorkingSourceMode = "FromPreviousStep";
            UpdateWorkingFromSummary();
        });

        OpenExportFolderCommand = new RelayCommand(() =>
        {
            string targetDir = "";
            if (!string.IsNullOrEmpty(LatestExportPath) && Directory.Exists(LatestExportPath))
            {
                targetDir = LatestExportPath;
            }
            else if (!string.IsNullOrEmpty(OutputDirectory) && Directory.Exists(OutputDirectory))
            {
                targetDir = OutputDirectory;
            }
            else if (!string.IsNullOrEmpty(GdbPath) && Directory.Exists(GdbPath))
            {
                targetDir = Path.GetDirectoryName(GdbPath) ?? GdbPath;
            }
            else
            {
                targetDir = ResolveBaseFolder();
            }

            if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = targetDir,
                    UseShellExecute = true
                });
            }
        });

        BrowseFeatureClassCommand = new RelayCommand(BrowseFeatureClassAction);
        RefreshMapLayersCommand = new RelayCommand(async () => await RefreshMapLayersAsync());
        BrowseCustomFileCommand = new RelayCommand(BrowseCustomFileAction);

        ChangeSourceCommand = new RelayCommand(() =>
        {
            if (WorkingSourceMode == "UserOwnData")
            {
                BrowseFeatureClassAction();
            }
            else
            {
                BrowseExportDeliverablesAction();
            }
        });

        RecomputeFrozenMetricsCommand = new RelayCommand(RecomputeFrozenMetricsAction);
        KeepStoredFrozenMetricsCommand = new RelayCommand(KeepStoredFrozenMetricsAction);
        KeepEditedBordersCommand = new RelayCommand(() =>
        {
            ShowBorderOverwritePrompt = false;
            StatusMessage = "تم الاحتفاظ بالحدود المعدلة يدوياً من ملف التصدير.";
        });
        RegenerateBordersCommand = new RelayCommand(async () =>
        {
            ShowBorderOverwritePrompt = false;
            await ExecuteStep4BordersAsync();
        });
        RefreshLatestExportStatusCommand = new RelayCommand(RefreshLatestExportStatus);

        InitializeLatestExportState();
        _ = RefreshMapLayersAsync();
    }

    public static void Show()
    {
        var pane = FrameworkApplication.DockPaneManager.Find(DockPaneId);
        if (pane != null && !pane.IsVisible)
        {
            pane.Activate();
        }
    }

    #region Observable Properties

    private int _selectedTabIndex = 0;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    private bool _isBusy;
    public new bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private string _statusMessage = "Ready. Select Source CAD Drawing (.dwg / .dxf) to start processing.";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private double _progressValue = 0;
    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    private bool _isProgressIndeterminate = false;
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set => SetProperty(ref _isProgressIndeterminate, value);
    }

    private string _progressPercentageText = "0%";
    public string ProgressPercentageText
    {
        get => _progressPercentageText;
        set => SetProperty(ref _progressPercentageText, value);
    }

    private string _currentActionText = "جاهز للعمل (Ready)";
    public string CurrentActionText
    {
        get => _currentActionText;
        set => SetProperty(ref _currentActionText, value);
    }

    private string _progressDetailsText = "اختر ملف الكاد (.dwg / .dxf) أو طبقة البارسلات للبدء";
    public string ProgressDetailsText
    {
        get => _progressDetailsText;
        set => SetProperty(ref _progressDetailsText, value);
    }

    private string _activePhaseSummary = string.Empty;
    public string ActivePhaseSummary
    {
        get => _activePhaseSummary;
        set => SetProperty(ref _activePhaseSummary, value);
    }

    private Brush _progressStatusBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // #10B981 Emerald
    public Brush ProgressStatusBrush
    {
        get => _progressStatusBrush;
        set => SetProperty(ref _progressStatusBrush, value);
    }

    public void ReportProgress(double percentage, string? actionName = null, string? details = null, string? status = null, string? phase = null)
    {
        void Update()
        {
            IsProgressIndeterminate = false;
            ProgressValue = Math.Clamp(percentage, 0, 100);
            ProgressPercentageText = $"{Math.Round(ProgressValue)}%";
            ProgressStatusBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)); // #38BDF8 Sky Cyan
            if (!string.IsNullOrEmpty(actionName)) CurrentActionText = actionName;
            if (details != null) ProgressDetailsText = details;
            if (!string.IsNullOrEmpty(phase)) ActivePhaseSummary = phase;
            if (!string.IsNullOrEmpty(status)) StatusMessage = status;
        }

        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(Update);
        }
        else
        {
            Update();
        }
    }

    public void ReportIndeterminateProgress(string actionName, string? details = null, string? status = null, string? phase = null)
    {
        void Update()
        {
            IsProgressIndeterminate = true;
            ProgressValue = 0;
            ProgressPercentageText = "...";
            ProgressStatusBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)); // #38BDF8 Sky Cyan
            CurrentActionText = actionName;
            if (details != null) ProgressDetailsText = details;
            if (!string.IsNullOrEmpty(phase)) ActivePhaseSummary = phase;
            if (!string.IsNullOrEmpty(status)) StatusMessage = status;
        }

        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(Update);
        }
        else
        {
            Update();
        }
    }

    public void ResetProgress(string completionStatus = "Ready", string actionText = "اكتمل بنجاح")
    {
        void Update()
        {
            IsProgressIndeterminate = false;
            ProgressValue = 100;
            ProgressPercentageText = "100%";
            ProgressDetailsText = string.Empty;
            ActivePhaseSummary = string.Empty;
            CurrentActionText = actionText;
            ProgressStatusBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // #10B981 Emerald
            StatusMessage = completionStatus;
        }

        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(Update);
        }
        else
        {
            Update();
        }
    }

    public void ReportError(string errorMessage, string actionText = "حدث خطأ")
    {
        void Update()
        {
            IsProgressIndeterminate = false;
            ProgressStatusBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // #EF4444 Red
            CurrentActionText = actionText;
            ProgressPercentageText = "خطأ";
            ProgressDetailsText = errorMessage;
            StatusMessage = errorMessage;
        }

        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(Update);
        }
        else
        {
            Update();
        }
    }

    // Fictional default project attributes
    public string PlanNo
    {
        get => _projectMeta.PlanNo;
        set { if (_projectMeta.PlanNo != value) { _projectMeta.PlanNo = value; NotifyPropertyChanged(); } }
    }

    /// <summary>
    /// Neighbourhood Code (كود الحي) - stored in NEIGHBOURH field in PARCEL feature class/shapefile.
    /// </summary>
    public string NeighbourhoodCode
    {
        get => _projectMeta.NeighbourhoodCode;
        set { if (_projectMeta.NeighbourhoodCode != value) { _projectMeta.NeighbourhoodCode = value; NotifyPropertyChanged(); } }
    }

    /// <summary>
    /// Neighbourhood Name (اسم الحي) - used in the structured ADDRESS string: حي/{NeighbourhoodName}/ب{CityName}/بمنطقة/{RegionName}
    /// </summary>
    public string NeighbourhoodName
    {
        get => _projectMeta.NeighbourhoodName;
        set
        {
            if (_projectMeta.NeighbourhoodName != value)
            {
                _projectMeta.NeighbourhoodName = value;
                NotifyPropertyChanged();
                UpdateAddressPreview();
                SyncParcelsAddress();
            }
        }
    }

    /// <summary>
    /// City Name (اسم المدينة) - detected from location of main parcels or input by user.
    /// </summary>
    public string CityName
    {
        get => _projectMeta.CityName;
        set
        {
            if (_projectMeta.CityName != value)
            {
                _projectMeta.CityName = value;
                NotifyPropertyChanged();
                UpdateAddressPreview();
                SyncParcelsAddress();
            }
        }
    }

    /// <summary>
    /// Region Name (اسم المنطقة) - detected from location of main parcels or input by user.
    /// </summary>
    public string RegionName
    {
        get => _projectMeta.RegionName;
        set
        {
            if (_projectMeta.RegionName != value)
            {
                _projectMeta.RegionName = value;
                NotifyPropertyChanged();
                UpdateAddressPreview();
                SyncParcelsAddress();
            }
        }
    }

    public string CityId
    {
        get => _projectMeta.CityId;
        set
        {
            if (_projectMeta.CityId != value)
            {
                _projectMeta.CityId = value;
                NotifyPropertyChanged();
                SyncParcelsAddress();
            }
        }
    }

    public string RegionId
    {
        get => _projectMeta.RegionId;
        set
        {
            if (_projectMeta.RegionId != value)
            {
                _projectMeta.RegionId = value;
                NotifyPropertyChanged();
                SyncParcelsAddress();
            }
        }
    }

    /// <summary>
    /// Backward-compatibility accessor mapping to NeighbourhoodCode.
    /// </summary>
    public string Neighbourhood
    {
        get => NeighbourhoodCode;
        set => NeighbourhoodCode = value;
    }

    public string RequestType
    {
        get => _projectMeta.RequestType;
        set { if (_projectMeta.RequestType != value) { _projectMeta.RequestType = value; NotifyPropertyChanged(); } }
    }

    public string ReportNo
    {
        get => _projectMeta.ReportNo;
        set { if (_projectMeta.ReportNo != value) { _projectMeta.ReportNo = value; NotifyPropertyChanged(); } }
    }

    private string _cadDrawingPath = string.Empty;
    public string CadDrawingPath
    {
        get => _cadDrawingPath;
        set
        {
            if (SetProperty(ref _cadDrawingPath, value))
            {
                if (File.Exists(value))
                {
                    if (string.IsNullOrWhiteSpace(PlanNo) || PlanNo == "0000/0")
                    {
                        PlanNo = Path.GetFileNameWithoutExtension(value);
                    }
                }
            }
        }
    }

    private string _gdbPath = string.Empty;
    public string GdbPath
    {
        get => _gdbPath;
        set => SetProperty(ref _gdbPath, value);
    }

    private string _landUseExcelPath = string.Empty;
    public string LandUseExcelPath
    {
        get => _landUseExcelPath;
        set => SetProperty(ref _landUseExcelPath, value);
    }

    private string _sourceParcelShpPath = string.Empty;
    public string SourceParcelShpPath
    {
        get => _sourceParcelShpPath;
        set => SetProperty(ref _sourceParcelShpPath, value);
    }

    private string _areasExcelPath = string.Empty;
    public string AreasExcelPath
    {
        get => _areasExcelPath;
        set => SetProperty(ref _areasExcelPath, value);
    }

    private string _expectedLayersExcelPath = string.Empty;
    public string ExpectedLayersExcelPath
    {
        get => _expectedLayersExcelPath;
        set => SetProperty(ref _expectedLayersExcelPath, value);
    }

    private string _outputDirectory = string.Empty;
    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value))
            {
                if (string.IsNullOrWhiteSpace(_gdbPath) || _gdbPath.EndsWith("Cad2Gis_Workspace.gdb", StringComparison.OrdinalIgnoreCase))
                {
                    GdbPath = GdbFeatureClassService.GetEffectiveGdbPath(value);
                }
            }
        }
    }

    // Source Selector per step (Section 5)
    private string _stage2SourceMode = "FromPreviousStep"; // "FromPreviousStep" or "UserOwnData"
    public string Stage2SourceMode
    {
        get => _stage2SourceMode;
        set
        {
            if (SetProperty(ref _stage2SourceMode, value))
            {
                NotifyPropertyChanged(nameof(IsSourceFromCad));
                NotifyPropertyChanged(nameof(IsStage2SourceFromCustom));
            }
        }
    }

    public bool IsSourceFromCad
    {
        get => Stage2SourceMode != "UserOwnData";
        set
        {
            if (value)
            {
                Stage2SourceMode = "FromPreviousStep";
            }
        }
    }

    public bool IsStage2SourceFromCustom
    {
        get => Stage2SourceMode == "UserOwnData";
        set
        {
            if (value)
            {
                Stage2SourceMode = "UserOwnData";
            }
        }
    }

    // Source Selector per step (Part B: Continue from Latest Export)
    private string _workingSourceMode = "FromPreviousStep"; // Default to "FromPreviousStep" (CAD Session / Dataset)
    public string WorkingSourceMode
    {
        get => _workingSourceMode;
        set
        {
            if (SetProperty(ref _workingSourceMode, value))
            {
                NotifyPropertyChanged(nameof(IsSourceFromLatestExport));
                NotifyPropertyChanged(nameof(IsSourceFromSession));
                NotifyPropertyChanged(nameof(IsSourceFromCustom));
                NotifyPropertyChanged(nameof(IsSchemaFromStandard));
                NotifyPropertyChanged(nameof(IsSchemaFromCustom));
                NotifyPropertyChanged(nameof(Stage3SourceMode));
                UpdateWorkingFromSummary();
            }
        }
    }

    public bool IsSourceFromLatestExport
    {
        get => WorkingSourceMode == "LatestExport";
        set
        {
            if (value)
            {
                WorkingSourceMode = "LatestExport";
                RefreshLatestExportStatus();
            }
        }
    }

    public bool IsSourceFromSession
    {
        get => WorkingSourceMode == "FromPreviousStep";
        set
        {
            if (value)
            {
                WorkingSourceMode = "FromPreviousStep";
            }
        }
    }

    public bool IsSourceFromCustom
    {
        get => WorkingSourceMode == "UserOwnData";
        set
        {
            if (value)
            {
                WorkingSourceMode = "UserOwnData";
            }
        }
    }

    public string Stage3SourceMode
    {
        get => WorkingSourceMode;
        set => WorkingSourceMode = value;
    }

    public bool IsSchemaFromStandard
    {
        get => WorkingSourceMode != "UserOwnData";
        set
        {
            if (value)
            {
                WorkingSourceMode = HasLatestExport ? "LatestExport" : "FromPreviousStep";
            }
        }
    }

    public bool IsSchemaFromCustom
    {
        get => WorkingSourceMode == "UserOwnData";
        set
        {
            if (value)
            {
                WorkingSourceMode = "UserOwnData";
            }
        }
    }

    // Part B Observable Properties
    private bool _hasLatestExport;
    public bool HasLatestExport
    {
        get => _hasLatestExport;
        set => SetProperty(ref _hasLatestExport, value);
    }

    private string _latestExportPath = string.Empty;
    public string LatestExportPath
    {
        get => _latestExportPath;
        set => SetProperty(ref _latestExportPath, value);
    }

    private string _latestExportTimestamp = string.Empty;
    public string LatestExportTimestamp
    {
        get => _latestExportTimestamp;
        set => SetProperty(ref _latestExportTimestamp, value);
    }

    private bool _isEditedAfterExport;
    public bool IsEditedAfterExport
    {
        get => _isEditedAfterExport;
        set => SetProperty(ref _isEditedAfterExport, value);
    }

    private string _workingFromSummary = "Working from: Current CAD Session Data";
    public string WorkingFromSummary
    {
        get => _workingFromSummary;
        set => SetProperty(ref _workingFromSummary, value);
    }

    private bool _isWorkingFromMissing;
    public bool IsWorkingFromMissing
    {
        get => _isWorkingFromMissing;
        set => SetProperty(ref _isWorkingFromMissing, value);
    }

    private string _workingFromMissingWarning = string.Empty;
    public string WorkingFromMissingWarning
    {
        get => _workingFromMissingWarning;
        set => SetProperty(ref _workingFromMissingWarning, value);
    }

    // Authoritative pipeline step continuity references
    private string? _step2OutputFeatureClassPath;
    public string? Step2OutputFeatureClassPath
    {
        get => _step2OutputFeatureClassPath;
        set => SetProperty(ref _step2OutputFeatureClassPath, value);
    }

    private DateTime? _step2OutputTimestamp;
    public DateTime? Step2OutputTimestamp
    {
        get => _step2OutputTimestamp;
        set => SetProperty(ref _step2OutputTimestamp, value);
    }

    private int _step2OutputFeatureCount;
    public int Step2OutputFeatureCount
    {
        get => _step2OutputFeatureCount;
        set => SetProperty(ref _step2OutputFeatureCount, value);
    }

    private string? _step3ExportedParcelsFeatureClassPath;
    public string? Step3ExportedParcelsFeatureClassPath
    {
        get => _step3ExportedParcelsFeatureClassPath;
        set => SetProperty(ref _step3ExportedParcelsFeatureClassPath, value);
    }

    private DateTime? _step3ExportedTimestamp;
    public DateTime? Step3ExportedTimestamp
    {
        get => _step3ExportedTimestamp;
        set => SetProperty(ref _step3ExportedTimestamp, value);
    }

    private int _step3ExportedFeatureCount;
    public int Step3ExportedFeatureCount
    {
        get => _step3ExportedFeatureCount;
        set => SetProperty(ref _step3ExportedFeatureCount, value);
    }


    private bool _showEditedAfterExportBanner;
    public bool ShowEditedAfterExportBanner
    {
        get => _showEditedAfterExportBanner;
        set => SetProperty(ref _showEditedAfterExportBanner, value);
    }

    private string _diffDetailsSummary = string.Empty;
    public string DiffDetailsSummary
    {
        get => _diffDetailsSummary;
        set => SetProperty(ref _diffDetailsSummary, value);
    }

    public ObservableCollection<ExportHistoryItem> ExportHistoryItems { get; } = new();

    private ExportHistoryItem? _selectedHistoryItem;
    public ExportHistoryItem? SelectedHistoryItem
    {
        get => _selectedHistoryItem;
        set
        {
            if (SetProperty(ref _selectedHistoryItem, value) && value != null)
            {
                OnHistoryItemSelected(value);
            }
        }
    }

    private bool _showFrozenMetricPrompt;
    public bool ShowFrozenMetricPrompt
    {
        get => _showFrozenMetricPrompt;
        set => SetProperty(ref _showFrozenMetricPrompt, value);
    }

    private string _frozenMetricDiscrepancyMessage = string.Empty;
    public string FrozenMetricDiscrepancyMessage
    {
        get => _frozenMetricDiscrepancyMessage;
        set => SetProperty(ref _frozenMetricDiscrepancyMessage, value);
    }

    public ObservableCollection<FeatureGeometryDiff> GeometryDiffList { get; } = new();

    private bool _showBorderOverwritePrompt;
    public bool ShowBorderOverwritePrompt
    {
        get => _showBorderOverwritePrompt;
        set => SetProperty(ref _showBorderOverwritePrompt, value);
    }

    private string _borderOverwriteWarningMessage = string.Empty;
    public string BorderOverwriteWarningMessage
    {
        get => _borderOverwriteWarningMessage;
        set => SetProperty(ref _borderOverwriteWarningMessage, value);
    }

    // Custom Schema Data Properties (Step 3: Use My Own Data)
    private string _customSchemaDataPath = string.Empty;
    public string CustomSchemaDataPath
    {
        get => _customSchemaDataPath;
        set
        {
            if (SetProperty(ref _customSchemaDataPath, value))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    LoadCustomSchemaFields();
                }
                else
                {
                    HasCustomFileLoaded = false;
                }
            }
        }
    }

    private bool _hasCustomFileLoaded;
    public bool HasCustomFileLoaded
    {
        get => _hasCustomFileLoaded;
        set => SetProperty(ref _hasCustomFileLoaded, value);
    }

    private bool _isSchemaIdentical;
    public bool IsSchemaIdentical
    {
        get => _isSchemaIdentical;
        set => SetProperty(ref _isSchemaIdentical, value);
    }

    private bool _isFieldMappingExpanded = true;
    public bool IsFieldMappingExpanded
    {
        get => _isFieldMappingExpanded;
        set => SetProperty(ref _isFieldMappingExpanded, value);
    }

    private string _schemaStatusMessage = string.Empty;
    public string SchemaStatusMessage
    {
        get => _schemaStatusMessage;
        set => SetProperty(ref _schemaStatusMessage, value);
    }

    private string _schemaStatusBgColor = "#1E293B";
    public string SchemaStatusBgColor
    {
        get => _schemaStatusBgColor;
        set => SetProperty(ref _schemaStatusBgColor, value);
    }

    private string _schemaStatusBorderColor = "#334155";
    public string SchemaStatusBorderColor
    {
        get => _schemaStatusBorderColor;
        set => SetProperty(ref _schemaStatusBorderColor, value);
    }

    private string _schemaStatusTextColor = "#F8FAFC";
    public string SchemaStatusTextColor
    {
        get => _schemaStatusTextColor;
        set => SetProperty(ref _schemaStatusTextColor, value);
    }

    public ObservableCollection<FieldMappingItem> FieldMappings { get; } = new();

    // Map Layers & Feature Class Selection Properties (Working Data Source)
    public ObservableCollection<MapLayerItem> AvailableMapPolygonLayers { get; } = new();
    public ObservableCollection<MapLayerItem> AvailableMapAllFeatureLayers { get; } = new();

    private MapLayerItem? _selectedMapLayer;
    public MapLayerItem? SelectedMapLayer
    {
        get => _selectedMapLayer;
        set
        {
            if (SetProperty(ref _selectedMapLayer, value) && value != null)
            {
                CustomSchemaDataPath = value.Name;
                WorkingSourceMode = "UserOwnData";
                Stage2SourceMode = "UserOwnData";
                UpdateWorkingFromSummary();
                StatusMessage = $"تم اختيار طبقة الخريطة: '{value.Name}' ({value.GeometryType})";
            }
        }
    }

    // CRS Selector: UTM Zones 35N to 40N (Section 7)
    public ObservableCollection<int> SupportedUtmZones { get; } = new(CrsProjectionService.SupportedUtmZones);
    private int _selectedUtmZone = 32637; // Default 37N
    public int SelectedUtmZone
    {
        get => _selectedUtmZone;
        set => SetProperty(ref _selectedUtmZone, value);
    }

    // Layer Geometry Mode: Polygon (مضلعات فقط) or Line (خطوط فقط)
    public ObservableCollection<string> GeometryModeOptions { get; } = new(LayerGeometryModeService.AvailableModes);

    private string _selectedGeometryMode = LayerGeometryModeService.ModeBoth;
    public string SelectedGeometryMode
    {
        get => _selectedGeometryMode;
        set
        {
            if (SetProperty(ref _selectedGeometryMode, value))
            {
                if (value == LayerGeometryModeService.ModeLine || value == LayerGeometryModeService.ModeBoth)
                {
                    BuildPolygonsFromLines = true;
                }
                else if (value == LayerGeometryModeService.ModePolygon)
                {
                    BuildPolygonsFromLines = false;
                }
                NotifyPropertyChanged(nameof(IsLineOptionActive));
            }
        }
    }

    // Lines -> Polygons controls (Section 4)
    private bool _buildPolygonsFromLines = true;
    public bool BuildPolygonsFromLines
    {
        get => _buildPolygonsFromLines;
        set
        {
            if (SetProperty(ref _buildPolygonsFromLines, value))
            {
                MarkPolygonizationStale("تم تغيير خيار تحويل الخطوط إلى مضلعات.");
                NotifyPropertyChanged(nameof(IsLineOptionActive));
            }
        }
    }

    public bool IsLineOptionActive => LayerGeometryModeService.ShouldPolygonizeLines(SelectedGeometryMode, BuildPolygonsFromLines);

    private bool _useArcPyLineToPolygon = true;
    public bool UseArcPyLineToPolygon
    {
        get => _useArcPyLineToPolygon;
        set
        {
            if (SetProperty(ref _useArcPyLineToPolygon, value))
            {
                MarkPolygonizationStale("تم تغيير محرك تحويل الخطوط إلى مضلعات (ArcPy).");
            }
        }
    }

    private bool _polygonizeGroupOfLayers = true;
    public bool PolygonizeGroupOfLayers
    {
        get => _polygonizeGroupOfLayers;
        set
        {
            if (SetProperty(ref _polygonizeGroupOfLayers, value))
            {
                MarkPolygonizationStale("تم تغيير وضع تجميع الطبقات (Group vs Per-layer).");
            }
        }
    }

    private bool _extendLinesAndCurves = true;
    public bool ExtendLinesAndCurves
    {
        get => _extendLinesAndCurves;
        set
        {
            if (SetProperty(ref _extendLinesAndCurves, value))
            {
                MarkPolygonizationStale("تم تغيير خيار تمديد الخطوط والمنحنيات المفتوحة (Extend Lines & Curves).");
            }
        }
    }

    private double _extendToleranceMeters = 0.05; // 5 cm default
    public double ExtendToleranceMeters
    {
        get => _extendToleranceMeters;
        set
        {
            if (SetProperty(ref _extendToleranceMeters, value))
            {
                MarkPolygonizationStale("تم تعديل مسافة تمديد الخطوط والمنحنيات (Extend Distance).");
            }
        }
    }

    private double _snapToleranceMeters = 0.05;
    public double SnapToleranceMeters
    {
        get => _snapToleranceMeters;
        set
        {
            if (SetProperty(ref _snapToleranceMeters, value))
            {
                MarkPolygonizationStale("تم تعديل تفاوت إغلاق الفجوات (Snap Tolerance).");
            }
        }
    }

    // Geometry Preservation & Manifest (Mandatory Requirements)
    private PolygonizationManifestReport? _lastPolygonizationManifest;
    public PolygonizationManifestReport? LastPolygonizationManifest
    {
        get => _lastPolygonizationManifest;
        set => SetProperty(ref _lastPolygonizationManifest, value);
    }

    private bool _isPolygonizationStale;
    public bool IsPolygonizationStale
    {
        get => _isPolygonizationStale;
        set => SetProperty(ref _isPolygonizationStale, value);
    }

    private string _polygonizationStaleReason = string.Empty;
    public string PolygonizationStaleReason
    {
        get => _polygonizationStaleReason;
        set => SetProperty(ref _polygonizationStaleReason, value);
    }

    private double _maxCurveExportDeviationMeters = 0.001;
    public double MaxCurveExportDeviationMeters
    {
        get => _maxCurveExportDeviationMeters;
        set => SetProperty(ref _maxCurveExportDeviationMeters, value);
    }

    private bool _alsoKeepExactUtmGdbWithTrueCurves = true;
    public bool AlsoKeepExactUtmGdbWithTrueCurves
    {
        get => _alsoKeepExactUtmGdbWithTrueCurves;
        set => SetProperty(ref _alsoKeepExactUtmGdbWithTrueCurves, value);
    }

    private string _geometryPreservationNotice = string.Empty;
    public string GeometryPreservationNotice
    {
        get => _geometryPreservationNotice;
        set
        {
            if (SetProperty(ref _geometryPreservationNotice, value))
            {
                NotifyPropertyChanged(nameof(HasGeometryPreservationNotice));
            }
        }
    }
    public bool HasGeometryPreservationNotice => !string.IsNullOrWhiteSpace(_geometryPreservationNotice);

    private GeometryPreservationReport? _lastPreservationReport;
    public GeometryPreservationReport? LastPreservationReport
    {
        get => _lastPreservationReport;
        set => SetProperty(ref _lastPreservationReport, value);
    }

    // Polygon Creation Scope (Section 4)
    public ObservableCollection<string> PolygonLayerScopeOptions { get; } = new(PolygonLayerScopeService.AvailableScopes);

    private string _selectedPolygonLayerScope = PolygonLayerScopeService.ScopeMainAndSub;
    public string SelectedPolygonLayerScope
    {
        get => _selectedPolygonLayerScope;
        set => SetProperty(ref _selectedPolygonLayerScope, value);
    }

    public bool IsLayerEligibleForPolygonCreation(CadLayerInfo layer)
    {
        bool isLineMode = LayerGeometryModeService.ShouldPolygonizeLines(SelectedGeometryMode, BuildPolygonsFromLines);
        return PolygonLayerScopeService.IsLayerEligible(layer, SelectedPolygonLayerScope, isLineMode);
    }

    // Plot Text Layer Selector (Step 2)
    public ObservableCollection<string> AvailablePlotTextLayers { get; } = new();

    private string _selectedPlotTextLayer = "(تلقائي من دور الطبقة - PlotText)";
    public string SelectedPlotTextLayer
    {
        get => _selectedPlotTextLayer;
        set => SetProperty(ref _selectedPlotTextLayer, value);
    }

    // Width Inference & Sensing (Section 3)
    private double _passageThresholdMeters = 10.0;
    public double PassageThresholdMeters
    {
        get => _passageThresholdMeters;
        set => SetProperty(ref _passageThresholdMeters, value);
    }

    private int _rayCount = 3;
    public int RayCount
    {
        get => _rayCount;
        set => SetProperty(ref _rayCount, value);
    }

    private bool _mergeChamfers = true;
    public bool MergeChamfers
    {
        get => _mergeChamfers;
        set => SetProperty(ref _mergeChamfers, value);
    }

    private double _aMinDeg = 10.0;
    public double AMinDeg
    {
        get => _aMinDeg;
        set => SetProperty(ref _aMinDeg, value);
    }

    private double _aMaxDeg = 160.0;
    public double AMaxDeg
    {
        get => _aMaxDeg;
        set => SetProperty(ref _aMaxDeg, value);
    }

    private double _multiSegmentAngleDeviationDeg = 10.0;
    public double MultiSegmentAngleDeviationDeg
    {
        get => _multiSegmentAngleDeviationDeg;
        set => SetProperty(ref _multiSegmentAngleDeviationDeg, value);
    }

    private bool _pauseAtReviewTables = true;
    public bool PauseAtReviewTables
    {
        get => _pauseAtReviewTables;
        set => SetProperty(ref _pauseAtReviewTables, value);
    }

    // Export format selector: Shapefile vs Feature Class (Section 1)
    private bool _exportShapefiles = true;
    public bool ExportShapefiles
    {
        get => _exportShapefiles;
        set => SetProperty(ref _exportShapefiles, value);
    }

    private bool _exportFeatureClass = true;
    public bool ExportFeatureClass
    {
        get => _exportFeatureClass;
        set => SetProperty(ref _exportFeatureClass, value);
    }

    private bool _generateReports = true;
    public bool GenerateReports
    {
        get => _generateReports;
        set => SetProperty(ref _generateReports, value);
    }

    // UI Badges and Summaries
    private string _adminBadgeText = "Admin jurisdiction will be detected from CAD centroid.";
    public string AdminBadgeText
    {
        get => _adminBadgeText;
        set => SetProperty(ref _adminBadgeText, value);
    }

    private string _addressPreviewText = string.Empty;
    public string AddressPreviewText
    {
        get => _addressPreviewText;
        set => SetProperty(ref _addressPreviewText, value);
    }

    // Demo Project status & export guards (Section 3)
    private bool _isDemoProject = false;
    public bool IsDemoProject
    {
        get => _isDemoProject;
        set
        {
            if (SetProperty(ref _isDemoProject, value))
            {
                NotifyPropertyChanged(nameof(IsDemoBannerVisible));
            }
        }
    }
    public bool IsDemoBannerVisible => _isDemoProject;

    private bool _allowDemoExportConfirmation = false;
    public bool AllowDemoExportConfirmation
    {
        get => _allowDemoExportConfirmation;
        set => SetProperty(ref _allowDemoExportConfirmation, value);
    }

    // Configurable Tolerances (Area & Chamfer)
    private double _areaToleranceSqm = 0.5;
    public double AreaToleranceSqm
    {
        get => _areaToleranceSqm;
        set => SetProperty(ref _areaToleranceSqm, value);
    }

    private double _areaTolerancePercent = 0.1;
    public double AreaTolerancePercent
    {
        get => _areaTolerancePercent;
        set => SetProperty(ref _areaTolerancePercent, value);
    }

    private double _chamferThresholdMeters = 7.0;
    public double ChamferThresholdMeters
    {
        get => _chamferThresholdMeters;
        set => SetProperty(ref _chamferThresholdMeters, value);
    }

    private double _chamferAngleDeg = 45.0;
    public double ChamferAngleDeg
    {
        get => _chamferAngleDeg;
        set => SetProperty(ref _chamferAngleDeg, value);
    }

    private double _chamferAngleToleranceDeg = 15.0;
    public double ChamferAngleToleranceDeg
    {
        get => _chamferAngleToleranceDeg;
        set => SetProperty(ref _chamferAngleToleranceDeg, value);
    }

    // CRS Plausibility Suggestion (Section 1)
    private string _crsPlausibilityRecommendation = string.Empty;
    public string CrsPlausibilityRecommendation
    {
        get => _crsPlausibilityRecommendation;
        set => SetProperty(ref _crsPlausibilityRecommendation, value);
    }

    // HelperFiles status display (Section 5)
    public string HelperFilesPathDisplay => _helperManager?.HelperFilesRoot ?? string.Empty;
    public bool IsHelperFallbackActive => _helperManager?.IsFallbackToLocalAppData ?? false;
    public string HelperFallbackWarning => _helperManager?.FallbackWarningMessage ?? string.Empty;

    private string _parcelCountSummary = "Parcels: 0 loaded (0 MAIN, 0 SUB)";
    public string ParcelCountSummary
    {
        get => _parcelCountSummary;
        set => SetProperty(ref _parcelCountSummary, value);
    }

    private string _parcelAreaSummary = "Total Area: 0.00 m²";
    public string ParcelAreaSummary
    {
        get => _parcelAreaSummary;
        set => SetProperty(ref _parcelAreaSummary, value);
    }

    private string _borderCountSummary = "Borders: 0 generated (MAIN parcels only)";
    public string BorderCountSummary
    {
        get => _borderCountSummary;
        set => SetProperty(ref _borderCountSummary, value);
    }

    private string _qaStatusSummary = "QA Status: Pending Execution";
    public string QaStatusSummary
    {
        get => _qaStatusSummary;
        set => SetProperty(ref _qaStatusSummary, value);
    }

    private Brush _qaStatusColor = Brushes.SlateGray;
    public Brush QaStatusColor
    {
        get => _qaStatusColor;
        set => SetProperty(ref _qaStatusColor, value);
    }

    private string _exportStatusSummary = "No export performed yet.";
    public string ExportStatusSummary
    {
        get => _exportStatusSummary;
        set => SetProperty(ref _exportStatusSummary, value);
    }

    public ObservableCollection<CadLayerInfo> CadLayers { get; } = new();
    public ObservableCollection<InferredBorderItem> InferredBorders { get; } = new();
    public ObservableCollection<string> AvailableProfiles { get; } = new();
    public Array AvailableLayerRoles => Enum.GetValues(typeof(LayerRole));
    private string _selectedProfile = "Example_Profile";
    public string SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                ApplyProfile(value);
            }
        }
    }

    private CadLayerInfo? _selectedCadLayer;
    public CadLayerInfo? SelectedCadLayer
    {
        get => _selectedCadLayer;
        set => SetProperty(ref _selectedCadLayer, value);
    }

    private ICollectionView? _filteredCadLayers;
    public ICollectionView FilteredCadLayers => _filteredCadLayers ??= CollectionViewSource.GetDefaultView(CadLayers);

    private string _selectedCadTab = "Main Parcels";
    public string SelectedCadTab
    {
        get => _selectedCadTab;
        set
        {
            if (SetProperty(ref _selectedCadTab, value))
            {
                _filteredCadLayers?.Refresh();
                NotifyPropertyChanged(nameof(IsMainParcelsTabSelected));
                NotifyPropertyChanged(nameof(IsSubParcelsTabSelected));
                NotifyPropertyChanged(nameof(IsTextAnnotationsTabSelected));
                NotifyPropertyChanged(nameof(IsRoadsReferencesTabSelected));
                NotifyPropertyChanged(nameof(IsAllSelectedInCurrentTab));
                NotifyPropertyChanged(nameof(SelectedLayersStatus));
            }
        }
    }

    public bool IsMainParcelsTabSelected => SelectedCadTab == "Main Parcels";
    public bool IsSubParcelsTabSelected => SelectedCadTab == "Sub-Parcels / Utilities";
    public bool IsTextAnnotationsTabSelected => SelectedCadTab == "Text & Annotations";
    public bool IsRoadsReferencesTabSelected => SelectedCadTab == "Roads & References";

    public int MainParcelsCount => CadLayers.Count(c => c.Category == "Main Parcels");
    public int SubParcelsCount => CadLayers.Count(c => c.Category == "Sub-Parcels / Utilities");
    public int TextAnnotationsCount => CadLayers.Count(c => c.Category == "Text & Annotations");
    public int RoadsReferencesCount => CadLayers.Count(c => c.Category == "Roads & References");

    public int SelectedMainParcelsCount => CadLayers.Count(c => c.Category == "Main Parcels" && c.IsSelected);
    public int SelectedSubParcelsCount => CadLayers.Count(c => c.Category == "Sub-Parcels / Utilities" && c.IsSelected);
    public int SelectedTextAnnotationsCount => CadLayers.Count(c => c.Category == "Text & Annotations" && c.IsSelected);
    public int SelectedRoadsReferencesCount => CadLayers.Count(c => c.Category == "Roads & References" && c.IsSelected);

    public string MainParcelsBadge => MainParcelsCount > 0 ? $" ({SelectedMainParcelsCount}/{MainParcelsCount})" : " (0)";
    public string SubParcelsBadge => SubParcelsCount > 0 ? $" ({SelectedSubParcelsCount}/{SubParcelsCount})" : " (0)";
    public string TextAnnotationsBadge => TextAnnotationsCount > 0 ? $" ({SelectedTextAnnotationsCount}/{TextAnnotationsCount})" : " (0)";
    public string RoadsReferencesBadge => RoadsReferencesCount > 0 ? $" ({SelectedRoadsReferencesCount}/{RoadsReferencesCount})" : " (0)";

    public string SelectedLayersStatus
    {
        get
        {
            var tabItems = CadLayers.Where(c => string.Equals(c.Category, SelectedCadTab, StringComparison.OrdinalIgnoreCase)).ToList();
            int selectedInTab = tabItems.Count(c => c.IsSelected);
            int totalInTab = tabItems.Count;
            int totalEntitiesInTab = tabItems.Where(c => c.IsSelected).Sum(c => c.EntityCount);

            if (totalInTab == 0) return "لا توجد طبقات مدرجة في هذا القسم";
            return $"الطبقات المحددة: {selectedInTab} من {totalInTab} (إجمالي {totalEntitiesInTab:N0} عنصر)";
        }
    }

    public bool? IsAllSelectedInCurrentTab
    {
        get
        {
            var items = CadLayers.Where(c => string.Equals(c.Category, SelectedCadTab, StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count == 0) return false;
            int selected = items.Count(c => c.IsSelected);
            if (selected == items.Count) return true;
            if (selected == 0) return false;
            return null;
        }
        set
        {
            bool newVal = value ?? false;
            var items = CadLayers.Where(c => string.Equals(c.Category, SelectedCadTab, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var item in items)
            {
                item.IsSelected = newVal;
            }
            NotifyPropertyChanged(nameof(IsAllSelectedInCurrentTab));
            UpdateCadTabCounts();
        }
    }

    public void UpdateCadTabCounts()
    {
        NotifyPropertyChanged(nameof(MainParcelsCount));
        NotifyPropertyChanged(nameof(SubParcelsCount));
        NotifyPropertyChanged(nameof(TextAnnotationsCount));
        NotifyPropertyChanged(nameof(RoadsReferencesCount));
        NotifyPropertyChanged(nameof(SelectedMainParcelsCount));
        NotifyPropertyChanged(nameof(SelectedSubParcelsCount));
        NotifyPropertyChanged(nameof(SelectedTextAnnotationsCount));
        NotifyPropertyChanged(nameof(SelectedRoadsReferencesCount));
        NotifyPropertyChanged(nameof(MainParcelsBadge));
        NotifyPropertyChanged(nameof(SubParcelsBadge));
        NotifyPropertyChanged(nameof(TextAnnotationsBadge));
        NotifyPropertyChanged(nameof(RoadsReferencesBadge));
        NotifyPropertyChanged(nameof(IsAllSelectedInCurrentTab));
        NotifyPropertyChanged(nameof(SelectedLayersStatus));
        UpdatePlotTextLayersList();
    }

    public void UpdatePlotTextLayersList()
    {
        string current = SelectedPlotTextLayer;
        var list = new List<string> { "(تلقائي من دور الطبقة - PlotText)" };
        foreach (var l in CadLayers)
        {
            string label = l.LayerName;
            if (l.Role == LayerRole.PlotText) label += " (★ PlotText)";
            list.Add(label);
        }

        AvailablePlotTextLayers.Clear();
        foreach (var item in list) AvailablePlotTextLayers.Add(item);

        if (!string.IsNullOrWhiteSpace(current) && AvailablePlotTextLayers.Contains(current))
        {
            SelectedPlotTextLayer = current;
        }
        else
        {
            var ptLayer = CadLayers.FirstOrDefault(l => l.Role == LayerRole.PlotText);
            SelectedPlotTextLayer = ptLayer != null ? $"{ptLayer.LayerName} (★ PlotText)" : "(تلقائي من دور الطبقة - PlotText)";
        }
    }

    private void AddCadLayerWithEvents(CadLayerInfo layer)
    {
        layer.PropertyChanged -= OnCadLayerPropertyChanged;
        layer.PropertyChanged += OnCadLayerPropertyChanged;
        CadLayers.Add(layer);
    }

    private void OnCadLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CadLayerInfo.IsSelected))
        {
            NotifyPropertyChanged(nameof(IsAllSelectedInCurrentTab));
            NotifyPropertyChanged(nameof(SelectedLayersStatus));
            UpdateCadTabCounts();
            MarkPolygonizationStale("تم تعديل اختيار الطبقات المشاركة في بناء المضلعات.");
        }
        else if (e.PropertyName == nameof(CadLayerInfo.Role) || e.PropertyName == nameof(CadLayerInfo.Category))
        {
            _filteredCadLayers?.Refresh();
            UpdateCadTabCounts();
            NotifyPropertyChanged(nameof(SelectedLayersStatus));
            NotifyPropertyChanged(nameof(IsAllSelectedInCurrentTab));
            MarkPolygonizationStale("تم تعديل دور أو تصنيف الطبقة.");
        }
    }

    public void MarkPolygonizationStale(string reason)
    {
        if (_lastPolygonizationManifest != null)
        {
            _lastPolygonizationManifest.IsStale = true;
            _lastPolygonizationManifest.StaleReason = reason;
            IsPolygonizationStale = true;
            PolygonizationStaleReason = reason;
            NotifyPropertyChanged(nameof(LastPolygonizationManifest));
            NotifyPropertyChanged(nameof(IsPolygonizationStale));
            NotifyPropertyChanged(nameof(PolygonizationStaleReason));
        }
    }

    #endregion

    #region Commands

    public ICommand BrowseCadDrawingCommand { get; }
    public ICommand ScanCadLayersCommand { get; }
    public ICommand BuildParcelsCommand { get; }
    public ICommand PopulateSchemaCommand { get; }
    public ICommand BuildBordersCommand { get; }
    public ICommand ExportAndQaCommand { get; }
    public ICommand RunAllPipelineCommand { get; }
    public ICommand LoadDemoProjectCommand { get; }

    public ICommand ExportStep2ParcelsCommand { get; }
    public ICommand ExportStep3PopulatedParcelsCommand { get; }
    public ICommand ExportStep4BordersCommand { get; }

    public ICommand SelectCadTabCommand { get; }
    public ICommand MoveLayerToCategoryCommand { get; }
    public ICommand SelectOnlyCurrentLayerCommand { get; }
    public ICommand SelectAllLayersCommand { get; }
    public ICommand DeselectAllLayersCommand { get; }

    public ICommand BrowseLandUseCommand { get; }
    public ICommand BrowseSourceParcelCommand { get; }
    public ICommand BrowseAreasExcelCommand { get; }
    public ICommand BrowseExpectedLayersCommand { get; }
    public ICommand LoadExpectedLayersCommand { get; }
    public ICommand BrowseOutputFolderCommand { get; }
    public ICommand BrowseGdbPathCommand { get; }
    public ICommand UseExportOutputGdbCommand { get; }
    public ICommand UseProjectGdbCommand { get; }

    public ICommand AcceptAllInferredCommand { get; }
    public ICommand ApplySuggestedWidthCommand { get; }

    public ICommand ConvertCadToDatasetCommand { get; }
    public ICommand DetectAddressFromMainParcelsCommand { get; }
    public ICommand ProceedToStep2Command { get; }
    public ICommand SelectLayerAndProceedToStep2Command { get; }

    public ICommand BrowseCustomSchemaDataCommand { get; }
    public ICommand AutoMapFieldsCommand { get; }

    // Part B Commands
    public ICommand UseLatestExportCommand { get; }
    public ICommand UseSessionDataCommand { get; }
    public ICommand OpenExportFolderCommand { get; }
    public ICommand ChangeSourceCommand { get; }
    public ICommand RecomputeFrozenMetricsCommand { get; }
    public ICommand KeepStoredFrozenMetricsCommand { get; }
    public ICommand KeepEditedBordersCommand { get; }
    public ICommand RegenerateBordersCommand { get; }
    public ICommand RefreshLatestExportStatusCommand { get; }
    public ICommand BrowseFeatureClassCommand { get; }
    public ICommand RefreshMapLayersCommand { get; }
    public ICommand BrowseCustomFileCommand { get; }

    #endregion

    #region Execution Methods

    public async Task ExecuteStep1Async()
    {
        IsBusy = true;
        ReportProgress(5, "الخطوة 1: فحص وتحويل الكاد إلى داتاسيت", "جاري التحقق من مصدر الرسم ونطاق الإسقاط...", phase: "المرحلة 1 من 5");

        try
        {
            // 0. Convert CAD to Geodatabase Feature Dataset using the user-chosen coordinate system
            if (!string.IsNullOrWhiteSpace(CadDrawingPath) && File.Exists(CadDrawingPath))
            {
                ReportProgress(20, "الخطوة 1: تحويل الكاد إلى داتاسيت", $"تشغيل أداة التحويل CADToGeodatabase (EPSG:{SelectedUtmZone})...", phase: "المرحلة 1 من 5");
                try
                {
                    bool ok = await GdbFeatureClassService.ConvertCadToDatasetAsync(CadDrawingPath, GdbPath, "Cad_Dataset", SelectedUtmZone);
                    if (ok)
                    {
                        await QueuedTask.Run(() =>
                        {
                            GdbFeatureClassService.AddCadDatasetToMap(MapView.Active?.Map, GdbPath, "Cad_Dataset");
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CADToGeodatabase notice: {ex.Message}");
                }
            }

            var discoveredLayers = new List<CadLayerInfo>();

            // 1. Authoritative extraction directly from the converted GDB Dataset
            if (GdbFeatureClassService.DatasetExists(GdbPath, "Cad_Dataset"))
            {
                ReportProgress(50, "الخطوة 1: قراءة طبقات الـ Dataset", "فهرسة وحساب عناصر الطبقات من قاعدة البيانات...", phase: "المرحلة 1 من 5");
                await QueuedTask.Run(() =>
                {
                    discoveredLayers = GdbFeatureClassService.GetCadLayersFromDataset(GdbPath, "Cad_Dataset");
                });
            }

            // Fallback 1: Direct extraction from the CAD drawing file (.dwg or .dxf)
            if (discoveredLayers.Count == 0 && !string.IsNullOrWhiteSpace(CadDrawingPath) && File.Exists(CadDrawingPath))
            {
                var directLayers = CadExtractionService.ExtractLayers(CadDrawingPath, _helperManager.HelperFilesRoot);
                if (directLayers != null && directLayers.Count > 0)
                {
                    discoveredLayers = directLayers;
                }
            }

            // Fallback 2: Query Active Map for authoritative live CAD feature classes & counts
            if (discoveredLayers.Count == 0)
            {
                await QueuedTask.Run(() =>
                {
                    var map = MapView.Active?.Map;
                    if (map != null)
                    {
                        var cadLayersOnMap = FindCadFeatureLayersOnMap(map, CadDrawingPath);

                    if (cadLayersOnMap.Count > 0)
                    {
                        var cadCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        var cadGeoms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var cadTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var l in cadLayersOnMap)
                        {
                            try
                            {
                                var fc = l.GetFeatureClass();
                                if (fc == null) continue;
                                var def = fc.GetDefinition();
                                int layerFieldIdx = def.FindField("Layer");
                                int textFieldIdx = def.FindField("Text");
                                if (textFieldIdx < 0) textFieldIdx = def.FindField("TxtMemo");

                                string geomType = def.GetShapeType() switch
                                {
                                    ArcGIS.Core.Geometry.GeometryType.Polygon => "Polygon",
                                    ArcGIS.Core.Geometry.GeometryType.Polyline => "Line",
                                    ArcGIS.Core.Geometry.GeometryType.Point or ArcGIS.Core.Geometry.GeometryType.Multipoint => "Point",
                                    _ => "Line"
                                };

                                if (layerFieldIdx >= 0)
                                {
                                    string subfields = textFieldIdx >= 0 ? "Layer, Text" : "Layer";
                                    using var cursor = fc.Search(new ArcGIS.Core.Data.QueryFilter { SubFields = subfields }, true);
                                    while (cursor.MoveNext())
                                    {
                                        using var row = cursor.Current;
                                        var val = row[layerFieldIdx]?.ToString();
                                        if (string.IsNullOrWhiteSpace(val)) continue;

                                        cadCounts[val] = cadCounts.TryGetValue(val, out int cnt) ? cnt + 1 : 1;
                                        if (!cadGeoms.ContainsKey(val) || geomType == "Polygon")
                                        {
                                            cadGeoms[val] = geomType;
                                        }

                                        if (textFieldIdx >= 0 && !cadTexts.ContainsKey(val))
                                        {
                                            var txt = row[textFieldIdx]?.ToString()?.Trim();
                                            if (!string.IsNullOrWhiteSpace(txt) && txt.Length > 1)
                                            {
                                                cadTexts[val] = txt;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error scanning CAD layer on map: {ex.Message}");
                            }
                        }

                        if (cadCounts.Count > 0)
                        {
                            var liveMapLayers = new List<CadLayerInfo>();
                            foreach (var kvp in cadCounts.OrderBy(k => k.Key))
                            {
                                var existing = discoveredLayers.FirstOrDefault(d => d.LayerName.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
                                var role = existing?.Role ?? CadExtractionService.InferRoleFromName(kvp.Key);
                                var cat = existing?.Category ?? CadLayerInfo.DetermineCategory(role, kvp.Key);

                                string gt = cadGeoms.TryGetValue(kvp.Key, out string? g) ? g : (existing?.GeometryType ?? "Polygon");
                                if (gt.Equals("Polygon", StringComparison.OrdinalIgnoreCase) && role == LayerRole.Ignore && cat == "Roads & References")
                                {
                                    role = LayerRole.Boundary;
                                    cat = "Main Parcels";
                                }

                                var item = new CadLayerInfo
                                {
                                    LayerName = kvp.Key,
                                    EntityCount = kvp.Value,
                                    GeometryType = gt,
                                    Role = role,
                                    Category = cat,
                                    IsSelected = (cat != "Roads & References" && role != LayerRole.Ignore)
                                };

                                if (cadTexts.TryGetValue(kvp.Key, out string? rawTxt))
                                {
                                    item.RawCadText = rawTxt;
                                    item.DecodedArabicText = ArabicCadDecoder.Decode(rawTxt);
                                }
                                else if (existing != null && !string.IsNullOrWhiteSpace(existing.RawCadText))
                                {
                                    item.RawCadText = existing.RawCadText;
                                    item.DecodedArabicText = existing.DecodedArabicText;
                                }

                                CadExtractionService.EnrichCadLayerMetadata(item);
                                liveMapLayers.Add(item);
                            }
                            discoveredLayers = liveMapLayers;
                        }
                    }
                }
            });
            }

            // 3. Update UI ObservableCollection on Dispatcher Thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                CadLayers.Clear();
                foreach (var d in discoveredLayers)
                {
                    AddCadLayerWithEvents(d);
                }
                _filteredCadLayers?.Refresh();
                UpdateCadTabCounts();
            });

            if (CadLayers.Count == 0)
            {
                StatusMessage = "لم يتم العثور على طبقات في ملف الكاد. يرجى اختيار ملف رسم صالح (.dwg أو .dxf).";
                return;
            }

            // 4. Expected layers matching against Excel
            if (string.IsNullOrWhiteSpace(ExpectedLayersExcelPath) || !File.Exists(ExpectedLayersExcelPath))
            {
                if (File.Exists(_helperManager.ExpectedLayersExcel))
                {
                    ExpectedLayersExcelPath = _helperManager.ExpectedLayersExcel;
                }
            }

            if (File.Exists(ExpectedLayersExcelPath))
            {
                ReportProgress(80, "الخطوة 1: مطابقة الطبقات مع الإكسل", $"مطابقة {CadLayers.Count} طبقة مع قواعد {SelectedProfile}...", phase: "المرحلة 1 من 5");
                var rules = ExpectedLayersService.ReadExpectedLayersExcel(ExpectedLayersExcelPath);
                var matchReport = ExpectedLayersService.MatchCadLayers(discoveredLayers, rules, SelectedProfile);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var matched in matchReport.CadLayersMatched)
                    {
                        var target = CadLayers.FirstOrDefault(c => c.LayerName.Equals(matched.CadLayerName, StringComparison.OrdinalIgnoreCase));
                        if (target != null)
                        {
                            target.Role = ExpectedLayersService.ParseRole(matched.AssignedRole);
                            target.Category = CadLayerInfo.DetermineCategory(target.Role, target.LayerName);
                        }
                    }
                    _filteredCadLayers?.Refresh();
                    UpdateCadTabCounts();
                });

                // Auto-sync newly selected layers into Expected Layers Excel
                try
                {
                    var selectedLayers = CadLayers.Where(l => l.IsSelected).ToList();
                    if (selectedLayers.Count > 0)
                    {
                        ExpectedLayersService.AppendOrUpdateLayersInExpectedExcel(ExpectedLayersExcelPath, SelectedProfile, selectedLayers);
                    }
                }
                catch { }

                ResetProgress($"اكتملت الخطوة 1: تم قراءة {CadLayers.Count} طبقة فعلية من '{Path.GetFileName(CadDrawingPath)}' ومطابقتها وتحديث إكسل '{Path.GetFileName(ExpectedLayersExcelPath)}'.", "اكتمل فحص ومطابقة الطبقات");
            }
            else
            {
                ExpectedLayersService.EnsureExpectedLayersExcel(_helperManager.ExpectedLayersDir, SelectedProfile, CadLayers);
                if (File.Exists(_helperManager.ExpectedLayersExcel))
                {
                    ExpectedLayersExcelPath = _helperManager.ExpectedLayersExcel;
                }
                WorkingSourceMode = "FromPreviousStep";
                ResetProgress($"اكتملت الخطوة 1: تم قراءة {CadLayers.Count} طبقة فعلية من '{Path.GetFileName(CadDrawingPath)}'.", "اكتمل فحص الطبقات");
            }
        }
        catch (Exception ex)
        {
            ReportError($"Step 1 Error: {ex.Message}", "خطأ في الخطوة 1");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteStep2Async(bool isPartOfPipeline = false)
    {
        if (!isPartOfPipeline) IsBusy = true;
        ReportProgress(5, "الخطوة 2: استخراج وبلمرة مضلعات البارسلات", "فحص مصدر البيانات ونطاق الطبقات...", phase: "المرحلة 2 من 5");

        try
        {
            // Save any pending user map edits in ArcGIS Pro to ensure they are picked up in the GDB
            await GdbFeatureClassService.SaveMapEditsAsync();
            // Step 2: Read Dataset from Step 1 without re-converting CAD files or recreating dataset unnecessarily
            bool datasetExists = await GdbFeatureClassService.DatasetExistsAsync(GdbPath, "Cad_Dataset");
            if (datasetExists)
            {
                ReportProgress(15, "الخطوة 2: قراءة داتاسيت الخطوة 1", "الاعتماد مباشرة على داتاسيت 'Cad_Dataset' الناتج من الخطوة 1 دون إعادة تحويل...", phase: "المرحلة 2 من 5");
            }
            else if (!string.IsNullOrWhiteSpace(CadDrawingPath) && File.Exists(CadDrawingPath))
            {
                ReportProgress(15, "الخطوة 2: تحويل الكاد إلى داتاسيت", $"تحويل الكاد بالنظام الإحداثي EPSG:{SelectedUtmZone}...", phase: "المرحلة 2 من 5");
                try
                {
                    bool ok = await GdbFeatureClassService.ConvertCadToDatasetAsync(CadDrawingPath, GdbPath, "Cad_Dataset", SelectedUtmZone);
                    if (ok)
                    {
                        await QueuedTask.Run(() =>
                        {
                            GdbFeatureClassService.AddCadDatasetToMap(MapView.Active?.Map, GdbPath, "Cad_Dataset");
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CADToGeodatabase notice: {ex.Message}");
                }
            }

            _parcels.Clear();
            _parcelGeometries.Clear();

            // Ensure CadLayers is populated if Step 1 hasn't been run yet
            if (CadLayers.Count == 0)
            {
                var directLayers = new List<CadLayerInfo>();
                if (GdbFeatureClassService.DatasetExists(GdbPath, "Cad_Dataset"))
                {
                    await QueuedTask.Run(() =>
                    {
                        directLayers = GdbFeatureClassService.GetCadLayersFromDataset(GdbPath, "Cad_Dataset");
                    });
                }
                else if (!string.IsNullOrWhiteSpace(CadDrawingPath) && File.Exists(CadDrawingPath))
                {
                    directLayers = CadExtractionService.ExtractLayers(CadDrawingPath, _helperManager.HelperFilesRoot);
                }

                if (directLayers.Count == 0)
                {
                    var map = MapView.Active?.Map;
                    await QueuedTask.Run(() =>
                    {
                        var cadLayersOnMap = FindCadFeatureLayersOnMap(map, CadDrawingPath);
                        if (cadLayersOnMap.Count > 0)
                        {
                            var discoveredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var cl in cadLayersOnMap)
                            {
                                try
                                {
                                    var fc = cl.GetFeatureClass();
                                    if (fc == null) continue;
                                    var def = fc.GetDefinition();
                                    int lIdx = def.FindField("Layer");
                                    if (lIdx < 0) continue;
                                    using var cur = fc.Search(new ArcGIS.Core.Data.QueryFilter { SubFields = "Layer" }, true);
                                    while (cur.MoveNext())
                                    {
                                        using var row = cur.Current;
                                        var val = row[lIdx]?.ToString()?.Trim();
                                        if (!string.IsNullOrWhiteSpace(val)) discoveredNames.Add(val);
                                    }
                                }
                                catch { }
                            }

                            foreach (var name in discoveredNames.OrderBy(n => n))
                            {
                                var role = CadExtractionService.InferRoleFromName(name);
                                var cat = CadLayerInfo.DetermineCategory(role, name);
                                if (cat == "Roads & References")
                                {
                                    role = LayerRole.Ignore;
                                }
                                var item = new CadLayerInfo
                                {
                                    LayerName = name,
                                    Role = role,
                                    Category = cat,
                                    IsSelected = (cat == "Main Parcels" && role != LayerRole.Ignore)
                                };
                                CadExtractionService.EnrichCadLayerMetadata(item);
                                directLayers.Add(item);
                            }
                        }
                    });
                }

                if (directLayers.Count > 0)
                {
                    CadLayers.Clear();
                    foreach (var d in directLayers) AddCadLayerWithEvents(d);
                    _filteredCadLayers?.Refresh();
                    UpdateCadTabCounts();
                }
            }

            var checkedLayers = CadLayers.Where(l => l.IsSelected).ToList();
            if (CadLayers.Count > 0 && checkedLayers.Count == 0)
            {
                string msg = "لم يتم تحديد أي طبقة لاستخراج المضلعات منها. يرجى تفعيل علامة الصح (✔) بجانب الطبقة المراد استخراجها أو استخدام زر 'تحديد هذه الطبقة فقط'.";
                StatusMessage = msg;
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(msg, "تنبيه اختيار الطبقات", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            bool isLineMode = LayerGeometryModeService.ShouldPolygonizeLines(SelectedGeometryMode, BuildPolygonsFromLines);
            var eligibleLayers = checkedLayers.Where(l => PolygonLayerScopeService.IsLayerEligible(l, SelectedPolygonLayerScope, isLineMode)).ToList();
            if (eligibleLayers.Count == 0)
            {
                eligibleLayers = checkedLayers; // respect user's explicit checkbox selection!
            }
            var eligibleLayerNames = new HashSet<string>(eligibleLayers.Select(l => l.LayerName), StringComparer.OrdinalIgnoreCase);

            var activeSr = SpatialReferenceBuilder.CreateSpatialReference(SelectedUtmZone);

            // B. Build Polygons from Lines (automatically active on Line or Both)
            // Run native ArcGIS Geoprocessing FeatureToPolygon outside QueuedTask to prevent deadlocks!
            var constructedParcels = new List<(Polygon Polygon, string LayerName)>();
            FeatureToPolygonReport? f2pReport = null;
            bool shouldPolyLines = (isLineMode || _parcels.Count == 0)
                                   && (WorkingSourceMode != "UserOwnData" || !File.Exists(SourceParcelShpPath))
                                   && (await GdbFeatureClassService.DatasetExistsAsync(GdbPath, "Cad_Dataset"));

            if (shouldPolyLines && eligibleLayerNames.Count > 0)
            {
                if (UseArcPyLineToPolygon)
                {
                    ReportProgress(65, "الخطوة 2: تحويل الخطوط عبر أداة ArcPy", $"تشغيل أداة ArcPy Line to Polygon للطبقات المحددة ({eligibleLayerNames.Count} طبقة)...", phase: "المرحلة 2 من 5");
                    f2pReport = await GdbFeatureClassService.RunArcPyLineToPolygonToolAsync(
                        GdbPath,
                        "Cad_Dataset",
                        eligibleLayerNames,
                        SnapToleranceMeters,
                        SelectedUtmZone,
                        _helperManager.HelperFilesRoot);
                }
                else
                {
                    ReportProgress(65, "الخطوة 2: تحويل الخطوط إلى مضلعات (Feature to Polygon)", $"تشغيل أداة Feature to Polygon للطبقات المحددة ({eligibleLayerNames.Count} طبقة)...", phase: "المرحلة 2 من 5");
                    f2pReport = await GdbFeatureClassService.RunFeatureToPolygonReportAsync(
                        GdbPath,
                        "Cad_Dataset",
                        eligibleLayerNames,
                        SnapToleranceMeters,
                        SelectedUtmZone);
                }

                constructedParcels = f2pReport.ConstructedParcels;
                if (!string.IsNullOrWhiteSpace(f2pReport.DiagnosticMessage))
                {
                    ReportProgress(70, "الخطوة 2: تشخيص تحويل الخطوط", f2pReport.DiagnosticMessage, phase: "المرحلة 2 من 5");
                }
            }

            await QueuedTask.Run(() =>
            {
                // 0. Ingest Data: From Latest Export if selected
                if (WorkingSourceMode == "LatestExport" && HasLatestExport)
                {
                    var state = ExportStateManager.LoadLatestExport(_helperManager.LastExportStateFile);
                    if (state != null && !string.IsNullOrWhiteSpace(state.ParcelPath) && File.Exists(state.ParcelPath))
                    {
                        ReportProgress(35, "الخطوة 2: استيراد أحدث تصدير", $"قراءة البارسلات من أحدث تصدير: '{Path.GetFileName(state.ParcelPath)}'...", phase: "المرحلة 2 من 5");
                        var (loadedParcels, loadedGeoms, wasCopied, notice) = GdbFeatureClassService.ReadParcelsFromDataset(state.ParcelPath, SelectedUtmZone, _helperManager.ScratchDir);
                        _parcels.Clear();
                        _parcels.AddRange(loadedParcels);
                        _parcelGeometries.Clear();
                        foreach (var kvp in loadedGeoms)
                            _parcelGeometries[kvp.Key] = kvp.Value;
                        return;
                    }
                }

                // 1. Ingest Data: From User-Provided file if explicitly loaded
                if (WorkingSourceMode == "UserOwnData" && File.Exists(SourceParcelShpPath))
                {
                    ReportProgress(35, "الخطوة 2: استيراد البيانات الخاصة", $"قراءة طبقة البارسلات وتجريد Z-Value من '{Path.GetFileName(SourceParcelShpPath)}'...", phase: "المرحلة 2 من 5");
                    var loaded = ShapefileReader.LoadParcels(SourceParcelShpPath);
                    foreach (var p in loaded.Parcels)
                    {
                        // Ensure Class is tagged (MAIN vs SUB)
                        if (string.IsNullOrEmpty(p.PlotId) || p.PlotId.StartsWith("GARDEN", StringComparison.OrdinalIgnoreCase) || p.PlotId.StartsWith("CORR", StringComparison.OrdinalIgnoreCase))
                        {
                            p.Class = ParcelClass.Sub;
                        }
                        else
                        {
                            p.Class = ParcelClass.Main;
                        }

                        // Apply scope filtering if Main Only
                        if (SelectedPolygonLayerScope.Contains("Main Only") || SelectedPolygonLayerScope.Contains("الرئيسية فقط"))
                        {
                            if (p.Class != ParcelClass.Main) continue;
                        }

                        _parcels.Add(p);
                        if (loaded.Geometries.TryGetValue(p.InternalId, out var g))
                        {
                            if (g.SpatialReference == null || g.SpatialReference.Wkid != SelectedUtmZone)
                            {
                                var b = new PolygonBuilderEx(g) { SpatialReference = activeSr };
                                b.HasZ = false;
                                b.HasM = false;
                                g = (Polygon)b.ToGeometry();
                            }
                            _parcelGeometries[p.InternalId] = g;
                        }
                    }
                }
                else if (GdbFeatureClassService.DatasetExists(GdbPath, "Cad_Dataset"))
                {
                    // 2. Authoritative Extraction DIRECTLY from the Converted GDB Dataset
                    ReportProgress(35, "الخطوة 2: قراءة مضلعات وخطوط الداتاسيت", $"قراءة عناصر الطبقات المؤهلة (النمط: {SelectedGeometryMode})...", phase: "المرحلة 2 من 5");
                    var datasetFeatures = GdbFeatureClassService.ReadDatasetFeatures(GdbPath, "Cad_Dataset", eligibleLayerNames);

                    // A. Read Polygons from dataset Polygon feature class
                    if (LayerGeometryModeService.ShouldIngestPolygons(SelectedGeometryMode))
                    {
                        foreach (var item in datasetFeatures.Polygons)
                        {
                            if (!eligibleLayerNames.Contains(item.LayerName)) continue;

                            var cleaned = ParcelPolygonizer.Clean2DPlanar(item.Polygon);
                            if (cleaned.SpatialReference == null || cleaned.SpatialReference.Wkid != SelectedUtmZone)
                            {
                                var b = new PolygonBuilderEx(cleaned) { SpatialReference = activeSr };
                                b.HasZ = false;
                                b.HasM = false;
                                cleaned = (Polygon)b.ToGeometry();
                            }
                            var metrics = ParcelPolygonizer.ComputeMetrics(cleaned);
                            var lyrInfo = eligibleLayers.FirstOrDefault(el => el.LayerName.Equals(item.LayerName, StringComparison.OrdinalIgnoreCase));
                            bool isMain = lyrInfo == null || lyrInfo.Category == "Main Parcels" || lyrInfo.Role == LayerRole.Boundary;

                            string plotText = item.RawText;
                            long newId = _parcels.Count + 1;
                            string initialPlotId = !string.IsNullOrWhiteSpace(plotText) ? plotText : $"P{newId}";

                            var parcel = new ParcelRecord
                            {
                                InternalId = newId,
                                PlotId = initialPlotId,
                                Class = isMain ? ParcelClass.Main : ParcelClass.Sub,
                                AreaSqm = metrics.areaSqm,
                                Usage = lyrInfo?.DecodedArabicText ?? "",
                                Remarks = $"From Dataset layer [{item.LayerName}]"
                            };
                            _parcels.Add(parcel);
                            _parcelGeometries[parcel.InternalId] = cleaned;
                        }
                    }

                    // B. Integrate Polygons Constructed from Lines via ArcPy / GP
                    if (constructedParcels.Count > 0)
                    {
                        foreach (var cp in constructedParcels)
                        {
                            var cleaned = ParcelPolygonizer.Clean2DPlanar(cp.Polygon);
                            var metrics = ParcelPolygonizer.ComputeMetrics(cleaned);

                            bool isDuplicate = false;
                            foreach (var existing in _parcelGeometries.Values)
                            {
                                if (Math.Abs(existing.Area - metrics.areaSqm) < 0.5)
                                {
                                    var ec = existing.Extent.Center;
                                    if (Math.Abs(ec.X - metrics.centroid.X) < 0.2 && Math.Abs(ec.Y - metrics.centroid.Y) < 0.2)
                                    {
                                        isDuplicate = true;
                                        break;
                                    }
                                }
                            }
                            if (isDuplicate) continue;

                            var lyrInfo = eligibleLayers.FirstOrDefault(el => el.LayerName.Equals(cp.LayerName, StringComparison.OrdinalIgnoreCase));
                            bool isMain = lyrInfo == null || lyrInfo.Category == "Main Parcels" || lyrInfo.Role == LayerRole.Boundary;

                            long newId = _parcels.Count + 1;
                            var parcel = new ParcelRecord
                            {
                                InternalId = newId,
                                PlotId = $"P{newId}",
                                Class = isMain ? ParcelClass.Main : ParcelClass.Sub,
                                AreaSqm = metrics.areaSqm,
                                Remarks = $"Constructed from Lines via ArcPy [{cp.LayerName}]"
                            };
                            _parcels.Add(parcel);
                            _parcelGeometries[parcel.InternalId] = cleaned;
                        }
                    }

                    // C. Curve-Preserving Line Polygonization and Integration
                    // 1. Run authoritative planar curve-preserving polygonizer
                    var (curveRings, manifest) = CurvePreservingPolygonizerService.Polygonize(
                        datasetFeatures.CurveEntitiesToPolygonize,
                        eligibleLayerNames.ToList(),
                        new PolygonizerOptions
                        {
                            SnapToleranceMeters = SnapToleranceMeters,
                            GroupLayersTogether = PolygonizeGroupOfLayers,
                            ExtendLinesAndCurves = ExtendLinesAndCurves,
                            ExtendToleranceMeters = ExtendToleranceMeters,
                            KeepAlreadyClosedPolylines = LayerGeometryModeService.ShouldIngestPolygons(SelectedGeometryMode)
                        },
                        category: "Main Parcels");

                    // 2. Add curve rings as parcels
                    foreach (var ring in curveRings)
                    {
                        if (ring.Segments.Count < 2) continue;
                        var cleaned = NativeCurveAuditHelper.BuildNativePolygonFromRing(ring, activeSr);
                        var metrics = ParcelPolygonizer.ComputeMetrics(cleaned);

                        // Check duplicate
                        bool isDuplicate = false;
                        foreach (var existing in _parcelGeometries.Values)
                        {
                            if (Math.Abs(existing.Area - metrics.areaSqm) < 0.5)
                            {
                                var ec = existing.Extent.Center;
                                if (Math.Abs(ec.X - metrics.centroid.X) < 0.2 && Math.Abs(ec.Y - metrics.centroid.Y) < 0.2)
                                {
                                    isDuplicate = true;
                                    break;
                                }
                            }
                        }
                        if (isDuplicate) continue;

                        var lyrInfo = eligibleLayers.FirstOrDefault(el => el.LayerName.Equals(ring.SourceLayer, StringComparison.OrdinalIgnoreCase));
                        bool isMain = lyrInfo == null || lyrInfo.Category == "Main Parcels" || lyrInfo.Role == LayerRole.Boundary;

                        long newId = _parcels.Count + 1;
                        var parcel = new ParcelRecord
                        {
                            InternalId = newId,
                            PlotId = $"P{newId}",
                            Class = isMain ? ParcelClass.Main : ParcelClass.Sub,
                            AreaSqm = metrics.areaSqm > 0 ? metrics.areaSqm : ring.Area,
                            Remarks = ring.WasUsedAsIs 
                                ? $"From Closed Polyline [{ring.SourceLayer}]" 
                                : $"Polygonized from CAD layer [{ring.SourceLayer}]"
                        };
                        _parcels.Add(parcel);
                        _parcelGeometries[parcel.InternalId] = cleaned;
                    }

                    // 3. Integrate constructed parcels from FeatureToPolygon (if available and not duplicate)
                    if (constructedParcels.Count > 0)
                    {
                        foreach (var item in constructedParcels)
                        {
                            var cleaned = ParcelPolygonizer.Ensure2D(item.Polygon);
                            if (cleaned.SpatialReference == null || cleaned.SpatialReference.Wkid != SelectedUtmZone)
                            {
                                var b = new PolygonBuilderEx(cleaned) { SpatialReference = activeSr };
                                b.HasZ = false;
                                b.HasM = false;
                                cleaned = (Polygon)b.ToGeometry();
                            }
                            var metrics = ParcelPolygonizer.ComputeMetrics(cleaned);

                            // Check duplicate
                            bool isDuplicate = false;
                            foreach (var existing in _parcelGeometries.Values)
                            {
                                if (Math.Abs(existing.Area - metrics.areaSqm) < 0.5)
                                {
                                    var ec = existing.Extent.Center;
                                    if (Math.Abs(ec.X - metrics.centroid.X) < 0.2 && Math.Abs(ec.Y - metrics.centroid.Y) < 0.2)
                                    {
                                        isDuplicate = true;
                                        break;
                                    }
                                }
                            }
                            if (isDuplicate) continue;

                            string effectiveLayer = item.LayerName;
                            if (string.IsNullOrWhiteSpace(effectiveLayer) && eligibleLayerNames.Count > 0)
                            {
                                effectiveLayer = eligibleLayerNames.First();
                            }

                            var lyrInfo = eligibleLayers.FirstOrDefault(el => el.LayerName.Equals(effectiveLayer, StringComparison.OrdinalIgnoreCase));
                            bool isMain = lyrInfo == null || lyrInfo.Category == "Main Parcels" || lyrInfo.Role == LayerRole.Boundary;

                            long newId = _parcels.Count + 1;
                            var parcel = new ParcelRecord
                            {
                                InternalId = newId,
                                PlotId = $"P{newId}",
                                Class = isMain ? ParcelClass.Main : ParcelClass.Sub,
                                AreaSqm = metrics.areaSqm,
                                Remarks = $"FeatureToPolygon from [{effectiveLayer}]"
                            };
                            _parcels.Add(parcel);
                            _parcelGeometries[parcel.InternalId] = cleaned;

                            // Update manifest
                            var mItem = manifest.Layers.FirstOrDefault(l => l.LayerName.Equals(effectiveLayer, StringComparison.OrdinalIgnoreCase));
                            if (mItem != null)
                            {
                                mItem.PolygonsContributed++;
                            }
                        }
                    }

                    manifest.TotalPolygonsBuilt = _parcels.Count;
                    LastPolygonizationManifest = manifest;
                    IsPolygonizationStale = false;
                    PolygonizationStaleReason = string.Empty;

                    // C. Spatially Match Annotations to Parcels (PlotId and Usage) via High-Speed 2D Spatial Grid Index
                    if (datasetFeatures.Annotations.Count > 0 && _parcels.Count > 0)
                    {
                        // 1. Build a 2D spatial grid index for parcels based on bounding envelope (Extent)
                        double gridCellSize = 100.0; // 100m cells in UTM
                        var parcelGrid = new Dictionary<(long gx, long gy), List<(ParcelRecord parcel, Polygon geom, Envelope extent)>>();

                        foreach (var p in _parcels)
                        {
                            if (!_parcelGeometries.TryGetValue(p.InternalId, out var polyGeom) || polyGeom == null) continue;
                            var extent = polyGeom.Extent;
                            long minGx = (long)Math.Floor(extent.XMin / gridCellSize);
                            long maxGx = (long)Math.Floor(extent.XMax / gridCellSize);
                            long minGy = (long)Math.Floor(extent.YMin / gridCellSize);
                            long maxGy = (long)Math.Floor(extent.YMax / gridCellSize);

                            for (long gx = minGx; gx <= maxGx; gx++)
                            {
                                for (long gy = minGy; gy <= maxGy; gy++)
                                {
                                    if (!parcelGrid.TryGetValue((gx, gy), out var list))
                                    {
                                        list = new List<(ParcelRecord, Polygon, Envelope)>();
                                        parcelGrid[(gx, gy)] = list;
                                    }
                                    list.Add((p, polyGeom, extent));
                                }
                            }
                        }

                        // 2. Query annotations against candidate parcels in matching grid cell
                        var parcelAnnotationsMap = new Dictionary<long, List<(string Text, string LayerName)>>();

                        foreach (var annot in datasetFeatures.Annotations)
                        {
                            if (annot.Location == null) continue;
                            long gx = (long)Math.Floor(annot.Location.X / gridCellSize);
                            long gy = (long)Math.Floor(annot.Location.Y / gridCellSize);

                            if (parcelGrid.TryGetValue((gx, gy), out var candidates))
                            {
                                var testedParcels = new HashSet<long>();
                                for (int ci = 0; ci < candidates.Count; ci++)
                                {
                                    var item = candidates[ci];
                                    if (!testedParcels.Add(item.parcel.InternalId)) continue;

                                    // Fast bounding box rejection
                                    var ext = item.extent;
                                    if (annot.Location.X < ext.XMin || annot.Location.X > ext.XMax ||
                                        annot.Location.Y < ext.YMin || annot.Location.Y > ext.YMax)
                                    {
                                        continue;
                                    }

                                    // Accurate contains test only if point is within bounding box
                                    if (GeometryEngine.Instance.Contains(item.geom, annot.Location) ||
                                        GeometryEngine.Instance.Intersects(item.geom, annot.Location))
                                    {
                                        if (!parcelAnnotationsMap.TryGetValue(item.parcel.InternalId, out var aList))
                                        {
                                            aList = new List<(string, string)>();
                                            parcelAnnotationsMap[item.parcel.InternalId] = aList;
                                        }
                                        aList.Add((annot.Text, annot.LayerName));
                                        break; // Point belongs to at most one parcel
                                    }
                                }
                            }
                        }

                        // Resolve target PlotText and Usage layers based on user selection
                        var targetPlotLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        // 1. Explicit user selection from ComboBox
                        if (!string.IsNullOrWhiteSpace(SelectedPlotTextLayer))
                        {
                            string rawName = SelectedPlotTextLayer;
                            int starIdx = rawName.IndexOf(" (★");
                            if (starIdx > 0) rawName = rawName.Substring(0, starIdx);
                            rawName = rawName.Trim();

                            if (!rawName.StartsWith("(") && !rawName.Contains("تلقائي"))
                            {
                                targetPlotLayers.Add(rawName);
                            }
                        }

                        // 2. All layers marked as PlotText in CadLayers table
                        foreach (var cl in CadLayers.Where(l => l.Role == LayerRole.PlotText))
                        {
                            targetPlotLayers.Add(cl.LayerName.Trim());
                        }

                        // 3. Fallback: Any text layer in CadLayers or inferred as PlotText
                        if (targetPlotLayers.Count == 0)
                        {
                            foreach (var cl in CadLayers.Where(l => l.Category == "Text & Annotations" || CadExtractionService.InferRoleFromName(l.LayerName) == LayerRole.PlotText))
                            {
                                targetPlotLayers.Add(cl.LayerName.Trim());
                            }
                        }

                        var targetUsageLayers = new HashSet<string>(
                            CadLayers.Where(l => l.Role == LayerRole.UsageText).Select(l => l.LayerName.Trim()),
                            StringComparer.OrdinalIgnoreCase);

                        // 3. Assign matched annotations to parcels
                        foreach (var p in _parcels)
                        {
                            if (!parcelAnnotationsMap.TryGetValue(p.InternalId, out var insideAnnots) || insideAnnots.Count == 0) continue;

                            // Priority 1: Text from user-selected PlotText layer!
                            var plotNoAnnot = insideAnnots.FirstOrDefault(a => targetPlotLayers.Contains(a.LayerName));

                            // Priority 2: Any text matching plot number format
                            if (string.IsNullOrWhiteSpace(plotNoAnnot.Text))
                            {
                                plotNoAnnot = insideAnnots.FirstOrDefault(a => 
                                    !targetUsageLayers.Contains(a.LayerName) && 
                                    IsPlotNumberText(a.Text));
                            }

                            if (!string.IsNullOrWhiteSpace(plotNoAnnot.Text))
                            {
                                p.PlotId = plotNoAnnot.Text.Trim();
                            }
                            else if (p.PlotId.StartsWith("P") && int.TryParse(p.PlotId.Substring(1), out _))
                            {
                                // Priority 3: Any non-usage annotation inside the parcel
                                var nonUsage = insideAnnots.FirstOrDefault(a => 
                                    !targetUsageLayers.Contains(a.LayerName) && 
                                    !IsUsageText(a.Text));
                                if (!string.IsNullOrWhiteSpace(nonUsage.Text))
                                {
                                    p.PlotId = nonUsage.Text.Trim();
                                }
                            }

                            // Usage assignment
                            var usageAnnot = insideAnnots.FirstOrDefault(a => 
                                targetUsageLayers.Contains(a.LayerName) ||
                                CadExtractionService.InferRoleFromName(a.LayerName) == LayerRole.UsageText ||
                                IsUsageText(a.Text));

                            if (!string.IsNullOrWhiteSpace(usageAnnot.Text))
                            {
                                p.Usage = usageAnnot.Text.Trim();
                            }
                        }

                        // 4. Proximity matching for any remaining parcels whose PlotId is still dummy (P1, P2...)
                        var remainingParcels = _parcels.Where(pr => pr.PlotId.StartsWith("P") && int.TryParse(pr.PlotId.Substring(1), out _)).ToList();
                        if (remainingParcels.Count > 0)
                        {
                            var targetAnnots = datasetFeatures.Annotations
                                .Where(a => targetPlotLayers.Contains(a.LayerName) && !string.IsNullOrWhiteSpace(a.Text) && a.Location != null)
                                .ToList();

                            if (targetAnnots.Count > 0)
                            {
                                var usedPlots = new HashSet<string>(_parcels.Where(pr => !pr.PlotId.StartsWith("P")).Select(pr => pr.PlotId));
                                foreach (var p in remainingParcels)
                                {
                                    if (!_parcelGeometries.TryGetValue(p.InternalId, out var polyGeom) || polyGeom == null) continue;
                                    var center = polyGeom.Extent.Center;

                                    var closest = targetAnnots
                                        .Where(a => !usedPlots.Contains(a.Text))
                                        .Select(a => new { Annot = a, Dist = Math.Sqrt(Math.Pow(a.Location.X - center.X, 2) + Math.Pow(a.Location.Y - center.Y, 2)) })
                                        .Where(x => x.Dist <= 20.0) // within 20m of parcel center
                                        .OrderBy(x => x.Dist)
                                        .FirstOrDefault();

                                    if (closest != null)
                                    {
                                        p.PlotId = closest.Annot.Text.Trim();
                                        usedPlots.Add(p.PlotId);
                                    }
                                }
                            }
                        }
                    }

                    if (_parcels.Count == 0)
                    {
                        StatusMessage = "No CAD data available in dataset — nothing was generated";
                        throw new InvalidOperationException("No CAD data available in dataset — nothing was generated");
                    }
                }
                else
                {
                    // Fallback: Authoritative Extraction from CAD drawing / Active Map
                    var curveEntitiesToPolygonize = new List<CurvePolylineEntity>();
                    var map = MapView.Active?.Map;
                    var cadLayersOnMap = FindCadFeatureLayersOnMap(map, CadDrawingPath);

                    if (cadLayersOnMap != null && cadLayersOnMap.Count > 0)
                    {
                        foreach (var l in cadLayersOnMap)
                        {
                            try
                            {
                                var fc = l.GetFeatureClass();
                                if (fc == null) continue;
                                var def = fc.GetDefinition();
                                int layerIdx = def.FindField("Layer");
                                int textIdx = def.FindField("Text");
                                if (textIdx < 0) textIdx = def.FindField("TxtMemo");

                                var shapeType = def.GetShapeType();

                                // Read Polygons
                                if (shapeType == ArcGIS.Core.Geometry.GeometryType.Polygon && LayerGeometryModeService.ShouldIngestPolygons(SelectedGeometryMode))
                                {
                                    using var cursor = fc.Search(null, false);
                                    while (cursor.MoveNext())
                                    {
                                        using var feat = (Feature)cursor.Current;
                                        string layerVal = (layerIdx >= 0 ? feat[layerIdx]?.ToString() : l.Name)?.Trim() ?? "";

                                        if (!eligibleLayerNames.Contains(layerVal)) continue;

                                        if (feat.GetShape() is Polygon cadPoly && !cadPoly.IsEmpty)
                                        {
                                            var cleaned = ParcelPolygonizer.Clean2DPlanar(cadPoly);
                                            var metrics = ParcelPolygonizer.ComputeMetrics(cleaned);
                                            var lyrInfo = eligibleLayers.FirstOrDefault(el => el.LayerName.Equals(layerVal, StringComparison.OrdinalIgnoreCase));
                                            bool isMain = lyrInfo == null || lyrInfo.Category == "Main Parcels" || lyrInfo.Role == LayerRole.Boundary;

                                            string plotText = textIdx >= 0 ? (feat[textIdx]?.ToString()?.Trim() ?? "") : "";
                                            if (string.IsNullOrWhiteSpace(plotText) && lyrInfo != null)
                                            {
                                                plotText = !string.IsNullOrWhiteSpace(lyrInfo.DecodedArabicText) ? lyrInfo.DecodedArabicText : lyrInfo.RawCadText;
                                            }

                                            long newId = _parcels.Count + 1;
                                            var parcel = new ParcelRecord
                                            {
                                                InternalId = newId,
                                                PlotId = !string.IsNullOrWhiteSpace(plotText) ? plotText : $"P{newId}",
                                                Class = isMain ? ParcelClass.Main : ParcelClass.Sub,
                                                AreaSqm = metrics.areaSqm,
                                                Remarks = $"From CAD layer [{layerVal}]"
                                            };
                                            _parcels.Add(parcel);
                                            _parcelGeometries[parcel.InternalId] = cleaned;
                                        }
                                    }
                                }
                                // Read Polylines for polygonization
                                else if (shapeType == ArcGIS.Core.Geometry.GeometryType.Polyline && (LayerGeometryModeService.ShouldPolygonizeLines(SelectedGeometryMode, BuildPolygonsFromLines) || _parcels.Count == 0))
                                {
                                    using var cursor = fc.Search(null, false);
                                    while (cursor.MoveNext())
                                    {
                                        using var feat = (Feature)cursor.Current;
                                        string layerVal = (layerIdx >= 0 ? feat[layerIdx]?.ToString() : l.Name)?.Trim() ?? "";

                                        if (!eligibleLayerNames.Contains(layerVal)) continue;

                                        if (feat.GetShape() is Polyline polyline && !polyline.IsEmpty)
                                        {
                                            var lyrInfo = eligibleLayers.FirstOrDefault(el => el.LayerName.Equals(layerVal, StringComparison.OrdinalIgnoreCase));
                                            bool isMain = lyrInfo == null || lyrInfo.Category == "Main Parcels" || lyrInfo.Role == LayerRole.Boundary;

                                            var curveEntity = new CurvePolylineEntity
                                            {
                                                LayerName = layerVal,
                                                Category = isMain ? "Main Parcels" : "Sub Parcels",
                                                NativeGeometryTag = polyline
                                            };

                                            foreach (var part in polyline.Parts)
                                            {
                                                foreach (var seg in part)
                                                {
                                                    var pStart = seg.StartPoint;
                                                    var pEnd = seg.EndPoint;
                                                    var s2D = new Point2D(pStart.X, pStart.Y);
                                                    var e2D = new Point2D(pEnd.X, pEnd.Y);

                                                    if (seg is EllipticArcSegment arc)
                                                    {
                                                        var cPt = arc.CenterPoint;
                                                        curveEntity.Segments.Add(new CoreBoundarySegment(s2D, e2D, CurveType.CircularArc, seg.Length)
                                                        {
                                                            Center = new Point2D(cPt.X, cPt.Y),
                                                            Radius = arc.SemiMajorAxis,
                                                            IsCounterClockwise = arc.IsCounterClockwise,
                                                            NativeGeometryTag = seg
                                                        });
                                                    }
                                                    else if (seg is CubicBezierSegment)
                                                    {
                                                        curveEntity.Segments.Add(new CoreBoundarySegment(s2D, e2D, CurveType.Bezier, seg.Length)
                                                        {
                                                            NativeGeometryTag = seg
                                                        });
                                                    }
                                                    else
                                                    {
                                                        curveEntity.Segments.Add(new CoreBoundarySegment(s2D, e2D, CurveType.Straight, seg.Length)
                                                        {
                                                            NativeGeometryTag = seg
                                                        });
                                                    }
                                                }
                                            }

                                            if (curveEntity.Segments.Count > 0)
                                            {
                                                curveEntitiesToPolygonize.Add(curveEntity);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error reading feature class {l.Name}: {ex.Message}");
                            }
                        }
                    }

                    // Build Polygons from Lines option (automatically on Line or Both)
                    bool shouldPolyFallback = LayerGeometryModeService.ShouldPolygonizeLines(SelectedGeometryMode, BuildPolygonsFromLines) || _parcels.Count == 0;
                    if (curveEntitiesToPolygonize.Count > 0 && shouldPolyFallback)
                    {
                        var (curveRings, manifest) = CurvePreservingPolygonizerService.Polygonize(
                            curveEntitiesToPolygonize,
                            eligibleLayerNames.ToList(),
                            new PolygonizerOptions
                            {
                                SnapToleranceMeters = SnapToleranceMeters,
                                GroupLayersTogether = PolygonizeGroupOfLayers,
                                ExtendLinesAndCurves = ExtendLinesAndCurves,
                                ExtendToleranceMeters = ExtendToleranceMeters,
                                KeepAlreadyClosedPolylines = true
                            },
                            category: "Main Parcels");

                        LastPolygonizationManifest = manifest;
                        IsPolygonizationStale = false;
                        PolygonizationStaleReason = string.Empty;

                        foreach (var ring in curveRings)
                        {
                            if (ring.Segments.Count < 3) continue;
                            var cleaned = NativeCurveAuditHelper.BuildNativePolygonFromRing(ring, activeSr);
                            var metrics = ParcelPolygonizer.ComputeMetrics(cleaned);

                            var lyrInfo = eligibleLayers.FirstOrDefault(el => el.LayerName.Equals(ring.SourceLayer, StringComparison.OrdinalIgnoreCase));
                            bool isMain = lyrInfo == null || lyrInfo.Category == "Main Parcels" || lyrInfo.Role == LayerRole.Boundary;

                            long newId = _parcels.Count + 1;
                            var parcel = new ParcelRecord
                            {
                                InternalId = newId,
                                PlotId = $"P{newId}",
                                Class = isMain ? ParcelClass.Main : ParcelClass.Sub,
                                AreaSqm = metrics.areaSqm > 0 ? metrics.areaSqm : ring.Area,
                                Remarks = ring.WasUsedAsIs 
                                    ? $"From Closed Polyline [{ring.SourceLayer}]" 
                                    : $"Polygonized from CAD layer [{ring.SourceLayer}]"
                            };
                            _parcels.Add(parcel);
                            _parcelGeometries[parcel.InternalId] = cleaned;
                        }
                    }

                    // Production code: never generate synthetic parcels (Section 3).
                    if (_parcels.Count == 0)
                    {
                        string reason = (f2pReport != null && !string.IsNullOrWhiteSpace(f2pReport.DiagnosticMessage))
                            ? f2pReport.DiagnosticMessage
                            : "لم يتم تكوين أي مضلعات من بيانات الكاد — يرجى التحقق من إغلاق خطوط الحدود وتحديد الطبقات الصحيحة.";
                        StatusMessage = reason;
                        throw new InvalidOperationException(reason);
                    }
                }
            });

            int mainCount = _parcels.Count(p => p.Class == ParcelClass.Main);
            int subCount = _parcels.Count(p => p.Class != ParcelClass.Main);

            ParcelCountSummary = $"Parcels: {_parcels.Count} loaded ({mainCount} MAIN, {subCount} SUB)";
            ParcelAreaSummary = $"Total Area: {_parcels.Sum(p => p.AreaSqm):F2} m²";

            // Save parcels to live GDB layer so they are visible on the map in active UTM projection immediately!
            try
            {
                string fcParcelName = IsDemoProject ? "DEMO_PARCEL" : "PARCEL";
                string templateParcel = Path.Combine(_helperManager.TemplatesDir, "PARCEL.shp");
                await GdbFeatureClassService.EnsureFeatureClassFromTemplateAsync(GdbPath, fcParcelName, templateParcel, SelectedUtmZone);
                await QueuedTask.Run(() =>
                {
                    GdbFeatureClassService.WriteParcelsToFeatureClass(GdbPath, fcParcelName, _parcels, _parcelGeometries);
                    GdbFeatureClassService.AddOrRefreshMapLayer(MapView.Active?.Map, GdbPath, fcParcelName, $"{fcParcelName} Live GDB");
                });

                // Register authoritative Step 2 output feature class reference
                Step2OutputFeatureClassPath = Path.Combine(GdbPath, fcParcelName);
                Step2OutputTimestamp = DateTime.Now;
                Step2OutputFeatureCount = _parcels.Count;
                System.Diagnostics.Debug.WriteLine($"[Audit Log] Step 2 Output registered: Path='{Step2OutputFeatureClassPath}', Count={Step2OutputFeatureCount}, Time={Step2OutputTimestamp:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception gdbSaveEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Step 2 GDB Save Notice] {gdbSaveEx.Message}");
            }

            ResetProgress($"الخطوة 2 اكتملت: تم استخراج {_parcels.Count} مضلع/قطعة (2D Planar) بنجاح ({mainCount} رئيسية، {subCount} خدمات).", "اكتمل استخراج البارسلات");
        }
        catch (Exception ex)
        {
            ReportError($"Step 2 Error: {ex.Message}", "خطأ في الخطوة 2");
        }
        finally
        {
            if (!isPartOfPipeline) IsBusy = false;
        }
    }

    public async Task ExecuteStep3PopulateSchemaAsync(bool isPartOfPipeline = false)
    {
        if (!isPartOfPipeline) IsBusy = true;
        ReportProgress(10, "Step 3: Populate Schema & Municipal Data", "Applying MOMRAH / Balady data model (26 attributes)...", phase: "Phase 3 of 5");

        try
        {
            await QueuedTask.Run(() =>
            {
                IngestWorkingParcelsIfNeeded("الخطوة 3: تعبئة الاسكيما والبيانات البلدية");

                if (_parcels.Count == 0)
                {
                    StatusMessage = "No parcels loaded to populate schema. Please run Step 2 first.";
                    throw new InvalidOperationException("No parcels loaded to populate schema.");
                }

                if (IsSchemaFromCustom)
                {
                    var map = MapView.Active?.Map;
                    bool isMapLayer = SelectedMapLayer?.Layer != null || (map != null && map.GetLayersAsFlattenedList().OfType<FeatureLayer>().Any(l => string.Equals(l.Name, CustomSchemaDataPath, StringComparison.OrdinalIgnoreCase)));
                    if (string.IsNullOrWhiteSpace(CustomSchemaDataPath) || 
                        (!File.Exists(CustomSchemaDataPath) && !CustomSchemaDataPath.Contains(".gdb", StringComparison.OrdinalIgnoreCase) && !isMapLayer))
                    {
                        StatusMessage = "يرجى تحديد ملف أو طبقة البيانات الخاصة أولاً.";
                        throw new InvalidOperationException("Custom schema data file not specified or not found.");
                    }

                    ReportProgress(30, "الخطوة 3: قراءة البيانات الخاصة", $"قراءة السجلات من '{Path.GetFileName(CustomSchemaDataPath)}'...", phase: "المرحلة 3 من 5");
                    var customRecords = CustomSchemaDataHelper.ReadRecords(CustomSchemaDataPath);
                    if (customRecords.Count == 0)
                    {
                        StatusMessage = "الملف المختار لا يحتوي على أي سجلات بيانات.";
                        throw new InvalidOperationException("No records found in custom data file.");
                    }

                    // Index custom records by the mapped PLOT_ID if mapped
                    var plotIdMapping = FieldMappings.FirstOrDefault(m => m.TargetField.Equals("PLOT_ID", StringComparison.OrdinalIgnoreCase) && m.IsMapped);
                    string? sourcePlotIdKey = plotIdMapping?.SelectedSourceField;

                    var recordsByPlotId = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrEmpty(sourcePlotIdKey))
                    {
                        foreach (var row in customRecords)
                        {
                            if (row.TryGetValue(sourcePlotIdKey, out var pidObj) && pidObj != null)
                            {
                                string pidStr = pidObj.ToString()?.Trim() ?? string.Empty;
                                if (!string.IsNullOrEmpty(pidStr))
                                {
                                    recordsByPlotId[pidStr] = row;
                                }
                            }
                        }
                    }

                    // Apply mapped values
                    int mappedCount = 0;
                    for (int i = 0; i < _parcels.Count; i++)
                    {
                        var p = _parcels[i];
                        Dictionary<string, object>? matchedRow = null;

                        if (!string.IsNullOrEmpty(p.PlotId) && recordsByPlotId.TryGetValue(p.PlotId, out var r))
                        {
                            matchedRow = r;
                        }
                        else if (recordsByPlotId.Count == 0 && i < customRecords.Count)
                        {
                            matchedRow = customRecords[i];
                        }

                        if (matchedRow != null)
                        {
                            mappedCount++;
                            ApplyCustomRowToParcel(p, matchedRow);
                        }

                        // Apply standard fallback defaults for unmapped fields
                        MissingDataPolicy.ApplyDefaults(p, _projectMeta, forceUpdate: false);
                    }

                    ReportProgress(70, "الخطوة 3: مطابقة البيانات", $"تم ربط بيانات {mappedCount} قطعة بالبيانات الخاصة بنجاح.", phase: "المرحلة 3 من 5");
                }
                else
                {
                    // 1. Read Land Use Table & join by PLOT_ID
                    if (File.Exists(LandUseExcelPath))
                    {
                        ReportProgress(35, "Step 3: Join Land-Use Table", $"Reading and joining land uses from '{Path.GetFileName(LandUseExcelPath)}'...", phase: "Phase 3 of 5");
                        var luMap = LandUseExcelService.ReadLandUseFile(LandUseExcelPath);
                        var discrepancies = new List<string>();
                        LandUseExcelService.EnrichParcels(_parcels, luMap, discrepancies);
                    }

                    // 2. Validate Areas Excel if provided
                    if (File.Exists(AreasExcelPath))
                    {
                        ReportProgress(55, "Step 3: Validate Areas Excel", $"Comparing area tolerances with '{Path.GetFileName(AreasExcelPath)}'...", phase: "Phase 3 of 5");
                        try
                        {
                            var areaOptions = new AreaComparisonOptions
                            {
                                AbsoluteToleranceSqm = AreaToleranceSqm,
                                PercentTolerance = AreaTolerancePercent
                            };
                            var valResult = QaValidationEngine.ValidateAll(_parcels, Array.Empty<BorderRecord>(), areasExcelPath: AreasExcelPath, areaOptions: areaOptions);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Areas Excel comparison notice: {ex.Message}");
                        }
                    }

                    // 3. Detect admin jurisdiction based on location of MAIN parcels
                    MapPoint? mainParcelsCenter = null;
                    var mainParcels = _parcels.Where(p => p.Class == ParcelClass.Main).ToList();
                    if (mainParcels.Count > 0)
                    {
                        double sumX = 0, sumY = 0;
                        int cnt = 0;
                        foreach (var mp in mainParcels)
                        {
                            if (_parcelGeometries.TryGetValue(mp.InternalId, out var g) && !g.IsEmpty)
                            {
                                sumX += g.Extent.Center.X;
                                sumY += g.Extent.Center.Y;
                                cnt++;
                            }
                        }
                        if (cnt > 0)
                        {
                            mainParcelsCenter = MapPointBuilderEx.CreateMapPoint(sumX / cnt, sumY / cnt);
                        }
                    }
                    else if (_parcelGeometries.Count > 0)
                    {
                        var firstGeom = _parcelGeometries.Values.FirstOrDefault(g => !g.IsEmpty);
                        if (firstGeom != null) mainParcelsCenter = firstGeom.Extent.Center;
                    }

                    // Detect administrative location (Region & City) from main parcels location
                    DetectAdminJurisdiction(mainParcelsCenter);

                    // Build structured address combining detected City/Region with user parameters (NeighbourhoodName, etc.)
                    string structuredAddress = _projectMeta.BuildAddress(_projectMeta.NeighbourhoodName);

                    // Apply MOMRAH Metadata Policy and structured address to all parcels
                    foreach (var p in _parcels)
                    {
                        MissingDataPolicy.ApplyDefaults(p, _projectMeta, forceUpdate: true);
                        p.Neighbourhood = _projectMeta.NeighbourhoodCode;
                        p.RegionId = _projectMeta.RegionId;
                        p.CityId = _projectMeta.CityId;
                        p.Address = structuredAddress;
                    }
                }

                ReportProgress(80, "Step 3: Apply Schema & Metadata", $"Applying schema and address to {_parcels.Count} parcels...", phase: "Phase 3 of 5");
            });

            // 4. Commit to Native GDB Feature Class with MOMRAH schema (GP tool executed outside QueuedTask)
            string fcParcelName = IsDemoProject ? "DEMO_PARCEL" : "PARCEL";
            string templateParcel = Path.Combine(_helperManager.TemplatesDir, "PARCEL.shp");
            await GdbFeatureClassService.EnsureFeatureClassFromTemplateAsync(GdbPath, fcParcelName, templateParcel, SelectedUtmZone);

            await QueuedTask.Run(() =>
            {
                GdbFeatureClassService.WriteParcelsToFeatureClass(GdbPath, fcParcelName, _parcels, _parcelGeometries);
                GdbFeatureClassService.AddOrRefreshMapLayer(MapView.Active?.Map, GdbPath, fcParcelName, $"{fcParcelName} Live GDB");
            });

            // Register Step 3 output reference
            Step3ExportedParcelsFeatureClassPath = Path.Combine(GdbPath, fcParcelName);
            Step3ExportedTimestamp = DateTime.Now;
            Step3ExportedFeatureCount = _parcels.Count;
            System.Diagnostics.Debug.WriteLine($"[Audit Log] Step 3 Output registered: Path='{Step3ExportedParcelsFeatureClassPath}', Count={Step3ExportedFeatureCount}, Time={Step3ExportedTimestamp:yyyy-MM-dd HH:mm:ss}");

            ResetProgress($"Step 3 completed: Populated schema and municipal data successfully for {_parcels.Count} parcels and saved to GDB.", "Step 3 Complete");
        }
        catch (Exception ex)
        {
            ReportError($"Step 3 Error: {ex.Message}", "Step 3 Error");
        }
        finally
        {
            if (!isPartOfPipeline) IsBusy = false;
        }
    }

    public async Task ExecuteStep4BordersAsync(bool isPartOfPipeline = false)
    {
        if (!isPartOfPipeline) IsBusy = true;
        ReportProgress(10, "الخطوة 4: تفكيك الواجهات واستشعار الشوارع", "حصر القطع الرئيسية وتجهيز المحرك الهندسي...", phase: "المرحلة 4 من 5");

        try
        {
            // Protection of manual edits: confirm before overwriting existing or manually modified borders
            if (!isPartOfPipeline && (_borders.Count > 0 || InferredBorders.Count > 0))
            {
                var confirmRes = ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    "توجد حدود أو توصيفات يدوية مسجلة مسبقاً. هل ترغب في إعادة استخراج الحدود واستبدال التعديلات اليدوية؟\n(اختر 'نعم' لإعادة التوليد، أو 'لا' للاحتفاظ بالتعديلات الحالية دون تغيير)",
                    "تأكيد استخراج الحدود",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                if (confirmRes != System.Windows.MessageBoxResult.Yes)
                {
                    StatusMessage = "تم الإبقاء على الحدود والتعديلات اليدوية الحالية بناءً على اختيار المستخدم.";
                    return;
                }
            }

            var activeSr = SpatialReferenceBuilder.CreateSpatialReference(SelectedUtmZone);
            var localInferred = new List<InferredBorderItem>();
            ExportDiffReport? diff = null;

            string step4SourcePath = "";
            if (WorkingSourceMode == "UserOwnData")
            {
                if (!string.IsNullOrWhiteSpace(CustomSchemaDataPath) && (File.Exists(CustomSchemaDataPath) || GdbFeatureClassService.FeatureClassExists(CustomSchemaDataPath)))
                {
                    step4SourcePath = CustomSchemaDataPath;
                }
                else if (!string.IsNullOrWhiteSpace(SourceParcelShpPath) && File.Exists(SourceParcelShpPath))
                {
                    step4SourcePath = SourceParcelShpPath;
                }
                else if (SelectedMapLayer?.Layer != null)
                {
                    step4SourcePath = SelectedMapLayer.Name;
                }
                else
                {
                    string err = "المصدر المخصص غير محدد أو غير موجود. يرجى اختيار ملف أو طبقة صراحة.";
                    ReportError(err, "خطأ في مصدر الخطوة 4");
                    return;
                }
            }
            else if (WorkingSourceMode == "LatestExport")
            {
                var state = ExportStateManager.LoadLatestExport(_helperManager.LastExportStateFile);
                bool validPath = state != null && !string.IsNullOrWhiteSpace(state.ParcelPath) &&
                    (File.Exists(state.ParcelPath) || Directory.Exists(state.ParcelPath) ||
                     (state.ParcelPath.Contains(".gdb") && Directory.Exists(state.ParcelPath.Substring(0, state.ParcelPath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase) + 4))));
                if (!validPath)
                {
                    IsWorkingFromMissing = true;
                    WorkingFromMissingWarning = $"Latest export dataset not found at '{state?.ParcelPath ?? "null"}'. Please select an export from history, browse, or use session data.";
                    ReportError(WorkingFromMissingWarning, "Missing Latest Export");
                    return;
                }

                IsWorkingFromMissing = false;
                step4SourcePath = state!.ParcelPath;
            }
            else
            {
                // Authoritative pipeline source: MUST consume Step 3 output / exported feature class
                if (string.IsNullOrWhiteSpace(Step3ExportedParcelsFeatureClassPath) ||
                    !GdbFeatureClassService.FeatureClassExists(Step3ExportedParcelsFeatureClassPath))
                {
                    string missingMsg = "الخطوة 4: لم يتم العثور على مخرجات معتمدة من الخطوة 3 (Export Step 3).\n" +
                        $"المسار المطلوب: '{(Step3ExportedParcelsFeatureClassPath ?? "غير محدد")}'.\n" +
                        "يرجى تصدير الخطوة 3 أولاً (Export Step 3) لإنتاج طبقة البارسلات المعتمدة، أو النقر على 'تغيير / استعراض' لاختيار مصدر بديل صراحة.";
                    StatusMessage = missingMsg;
                    ReportError(missingMsg, "مصدر الخطوة 4 مفقود");
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(missingMsg, "تنبيه مصدر الخطوة 4", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                step4SourcePath = Step3ExportedParcelsFeatureClassPath;
            }

            ReportProgress(15, "الخطوة 4: قراءة مخرجات الخطوة 3", $"قراءة مضلعات البارسلات من '{Path.GetFileName(step4SourcePath)}'...", phase: "المرحلة 4 من 5");

            await QueuedTask.Run(() =>
            {
                if (WorkingSourceMode == "LatestExport")
                {
                    var state = ExportStateManager.LoadLatestExport(_helperManager.LastExportStateFile)!;
                    var (loadedParcels, loadedGeoms, wasCopiedP, noticeP) = GdbFeatureClassService.ReadParcelsFromDataset(state.ParcelPath, SelectedUtmZone, _helperManager.ScratchDir);
                    var (loadedBorders, loadedBorderGeoms, wasCopiedB, noticeB) = (!string.IsNullOrEmpty(state.BorderPath) && (File.Exists(state.BorderPath) || state.BorderPath.Contains(".gdb")))
                        ? GdbFeatureClassService.ReadBordersFromDataset(state.BorderPath, SelectedUtmZone, _helperManager.ScratchDir)
                        : (new List<BorderRecord>(), new Dictionary<long, Polyline>(), false, (string?)null);

                    diff = ExportStateManager.DetectEdits(state, loadedParcels, loadedBorders,
                        areaToleranceSqm: _profileModel?.AreaToleranceSqm ?? 0.5);

                    _parcels.Clear();
                    _parcels.AddRange(loadedParcels);
                    _parcelGeometries.Clear();
                    foreach (var kvp in loadedGeoms)
                        _parcelGeometries[kvp.Key] = kvp.Value;
                }
                else
                {
                    var (loadedParcels, loadedGeoms, wasCopied, notice) = GdbFeatureClassService.ReadParcelsFromDataset(step4SourcePath, SelectedUtmZone, _helperManager.ScratchDir);
                    if (loadedParcels.Count == 0)
                    {
                        string emptyMsg = $"الطبقة المدخلة للخطوة 4 فارغة ولا تحتوي على معالم مضلعات: '{step4SourcePath}'.";
                        StatusMessage = emptyMsg;
                        throw new InvalidOperationException(emptyMsg);
                    }

                    _parcels.Clear();
                    _parcels.AddRange(loadedParcels);
                    _parcelGeometries.Clear();
                    foreach (var kvp in loadedGeoms)
                        _parcelGeometries[kvp.Key] = kvp.Value;

                    string auditLog = $"[Step 4 Authoritative Input Verification] Input Path: '{step4SourcePath}', Geometry: Polygon, FeatureCount: {_parcels.Count}, CRS: EPSG:{SelectedUtmZone}, Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    System.Diagnostics.Debug.WriteLine(auditLog);
                    StatusMessage = $"الخطوة 4: تم تحميل {_parcels.Count} قطعة من '{Path.GetFileName(step4SourcePath)}' بنجاح (EPSG:{SelectedUtmZone}).";
                }

                // Strictly harmonize ALL parcel geometries to the active working UTM spatial reference
                foreach (var id in _parcelGeometries.Keys.ToList())
                {
                    var g = _parcelGeometries[id];
                    if (g == null || g.IsEmpty) continue;
                    if (g.SpatialReference == null || g.SpatialReference.Wkid != SelectedUtmZone)
                    {
                        var b = new PolygonBuilderEx(g) { SpatialReference = activeSr };
                        b.HasZ = false;
                        b.HasM = false;
                        _parcelGeometries[id] = (Polygon)b.ToGeometry();
                    }
                }

                _borders.Clear();
                _borderGeometries.Clear();
                var localInferred = new List<InferredBorderItem>();
                long borderIdSeq = 1;

                var parcelTuples = _parcels.Select(p => (p, _parcelGeometries.TryGetValue(p.InternalId, out var g) ? g : null!)).Where(t => t.Item2 != null).ToList();

                var infOptions = new WidthInferenceOptions
                {
                    PassageStreetThreshold = PassageThresholdMeters,
                    RayCount = RayCount,
                    StandardWidths = _helperManager.LoadStandardWidths()
                };

                // [OVERRIDES] MAIN parcels only get dimensions! Sub-parcels act as neighbors only.
                var mainParcelsList = _parcels.Where(p => p.Class == ParcelClass.Main).ToList();
                int totalMainParcels = mainParcelsList.Count;
                int mainIdx = 0;
                var borderOptions = GetBorderExtractionOptions();

                foreach (var p in mainParcelsList)
                {
                    mainIdx++;
                    double curPct = totalMainParcels > 0 ? 15.0 + (70.0 * mainIdx / totalMainParcels) : 50.0;
                    ReportProgress(curPct, "الخطوة 4: تفكيك الواجهات واستشعار الشوارع", $"القطعة {mainIdx} من {totalMainParcels} (القطعة: {p.PlotId}): تفكيك الاتجاهات واستشعار الشوارع", phase: "المرحلة 4 من 5");
                    if (!_parcelGeometries.TryGetValue(p.InternalId, out var poly)) continue;

                    var cardinalDict = FacadeDecomposer.DecomposeTo4Cardinals(
                        poly, 
                        p, 
                        MergeChamfers ? ChamferThresholdMeters : 0.0,
                        ChamferAngleDeg,
                        ChamferAngleToleranceDeg,
                        borderOptions,
                        SelectedUtmZone);

                    // Sense neighbors or infer undrawn road width
                    OutwardRaySensor.SenseAndEnrichBorders(p, cardinalDict, parcelTuples, inferenceOptions: infOptions);

                    p.Borders.Clear();
                    foreach (var item in cardinalDict.Values)
                    {
                        item.Record.InternalId = borderIdSeq++;
                        MissingDataPolicy.ApplyDefaults(item.Record, p);
                        _borders.Add(item.Record);
                        _borderGeometries[item.Record.InternalId] = item.Geometry;
                        p.Borders.Add(item.Record);

                        if (item.Record.IsInferred)
                        {
                            localInferred.Add(new InferredBorderItem
                            {
                                InternalId = item.Record.InternalId,
                                PlotId = item.Record.PlotId,
                                BorderName = item.Record.BorderName,
                                DirectionArabic = item.Record.BorderNameArabic,
                                MeasuredWidth = item.Record.MeasuredWidth,
                                SuggestedWidth = item.Record.SuggestedWidth,
                                Descriptio = item.Record.Descriptio,
                                BorderType = item.Record.BorderType,
                                IsAccepted = true
                            });
                        }
                    }
                }
            });

            // UI updates directly on the UI thread
            if (diff != null)
            {
                IsEditedAfterExport = diff.WasEditedAfterExport;
                ShowEditedAfterExportBanner = diff.WasEditedAfterExport;
                if (diff.WasEditedAfterExport)
                {
                    DiffDetailsSummary = diff.GetSummaryText();
                }

                if (diff.GeometryChanges.Any(g => g.ExceedsTolerance))
                {
                    GeometryDiffList.Clear();
                    foreach (var gc in diff.GeometryChanges.Where(g => g.ExceedsTolerance))
                        GeometryDiffList.Add(gc);

                    ShowFrozenMetricPrompt = true;
                    FrozenMetricDiscrepancyMessage = $"تم رصد تعديلات هندسية في {GeometryDiffList.Count} قطعة تختلف عن المقاييس المحفوظة (AREA_SQM / الأطوال). هل ترغب في إعادة حساب المقاييس أم الإبقاء عليها؟";
                }
            }

            InferredBorders.Clear();
            foreach (var inf in localInferred)
            {
                InferredBorders.Add(inf);
            }

            // Commit borders to GDB (GP tool executed outside QueuedTask)
            string fcBorderName = IsDemoProject ? "DEMO_Border" : "Border";
            string templateBorder = Path.Combine(_helperManager.TemplatesDir, "border.shp");
            await GdbFeatureClassService.EnsureFeatureClassFromTemplateAsync(GdbPath, fcBorderName, templateBorder, SelectedUtmZone);

            await QueuedTask.Run(() =>
            {
                GdbFeatureClassService.WriteBordersToFeatureClass(GdbPath, fcBorderName, _borders, _borderGeometries);
                GdbFeatureClassService.AddOrRefreshMapLayer(MapView.Active?.Map, GdbPath, fcBorderName, $"{fcBorderName} Live GDB");
            });

            BorderCountSummary = $"Borders: {_borders.Count} records (MAIN parcels only, 4 per parcel)";
            ResetProgress($"الخطوة 4 اكتملت: تم إنشاء {_borders.Count} خط حدود ({InferredBorders.Count} مستنتج).", "اكتمل توليد الحدود");
        }
        catch (Exception ex)
        {
            ReportError($"Step 4 Error: {ex.Message}", "خطأ في الخطوة 4");
        }
        finally
        {
            if (!isPartOfPipeline) IsBusy = false;
        }
    }

    public async Task ExecuteStep5ExportQaAsync(bool isPartOfPipeline = false)
    {
        if (!isPartOfPipeline) IsBusy = true;
        ReportProgress(10, "الخطوة 5: التدقيق الشامل والتصدير النهائي", "بدء تدقيق الجودة وتجهيز مجلد المخرجات...", phase: "المرحلة 5 من 5");

        try
        {
            string baseOut = string.IsNullOrWhiteSpace(OutputDirectory) ? ResolveBaseFolder() : OutputDirectory;
            string finalExportDir = ResolveUniqueExportFolder(baseOut, PlanNo, CityName ?? CityId, NeighbourhoodName ?? Neighbourhood);

            // Step 5 & 6 Requirements: Create structured 'Final' folder inside Export/Output
            string finalDir = Path.Combine(finalExportDir, "Final");
            Directory.CreateDirectory(finalDir);

            string finalGdbPath = Path.Combine(finalDir, "Final.gdb");
            string finalShapeDir = Path.Combine(finalDir, "Shape");
            Directory.CreateDirectory(finalShapeDir);

            string finalReportsDir = Path.Combine(finalDir, "Reports");
            Directory.CreateDirectory(finalReportsDir);

            var exportWarnings = new List<string>();
            string templateFolder = _helperManager.TemplatesDir;
            string parcelExportName = "PARCEL";
            string borderExportName = "BORDER";

            ValidationResult? valResult = null;
            SchemaComplianceResult? complianceResult = null;

            await QueuedTask.Run(() =>
            {
                if (_parcels.Count == 0)
                {
                    IngestWorkingParcelsIfNeeded("الخطوة 5: التصدير والتدقيق");
                }
                if (_borders.Count == 0 && Directory.Exists(GdbPath))
                {
                    var bLoaded = GdbFeatureClassService.ReadBordersFromFeatureClass(GdbPath, "Border", SelectedUtmZone);
                    _borders.AddRange(bLoaded.Borders);
                    foreach (var kvp in bLoaded.Geometries) _borderGeometries[kvp.Key] = kvp.Value;
                }

                // Sync review edits from InferredBorders table into _borders records
                foreach (var inf in InferredBorders)
                {
                    var b = _borders.FirstOrDefault(x => x.InternalId == inf.InternalId);
                    if (b != null)
                    {
                        b.Descriptio = inf.Descriptio;
                        b.BorderType = inf.BorderType;
                        b.MeasuredWidth = inf.MeasuredWidth;
                        b.SuggestedWidth = inf.SuggestedWidth;
                    }
                }

                // Guard Demo Project Export (Section 3)
                if (IsDemoProject && !AllowDemoExportConfirmation)
                {
                    StatusMessage = "Export Blocked: Dataset was generated from Demo Project. Production export of demo data is blocked unless explicit confirmation is provided.";
                    ExportStatusSummary = "⚠️ Export Blocked: Demo dataset cannot be exported without explicit confirmation.";
                    return;
                }

                // 1. QA/QC Validation & Frozen Metrics Integrity Check
                valResult = QaValidationEngine.ValidateAll(
                    _parcels, 
                    _borders, 
                    areasExcelPath: AreasExcelPath,
                    areaOptions: new AreaComparisonOptions { AbsoluteToleranceSqm = AreaToleranceSqm, PercentTolerance = AreaTolerancePercent },
                    verifyExportSchema: true,
                    exportFieldNames: DbfTextLengthService.DefaultParcelFieldWidths.Keys
                );

                // Verify frozen metric values
                foreach (var p in _parcels)
                {
                    QaValidationEngine.ValidateFrozenMetrics(p, p.AreaSqm, p.ShapeArea > 0 ? p.ShapeArea : p.AreaSqm, p.ShapeLength, valResult, AreaToleranceSqm);
                }
                foreach (var b in _borders)
                {
                    QaValidationEngine.ValidateFrozenMetrics(b, b.ShapeLength, b.BorderDesc, valResult);
                }

                if (valResult.IsValid)
                {
                    QaStatusSummary = "QA Status: PASSED (MOMRAH 26 Parcel & 9 Border Attributes Compliant)";
                    QaStatusColor = Brushes.ForestGreen;
                }
                else
                {
                    QaStatusSummary = $"QA Status: {valResult.Errors.Count} Errors, {valResult.Warnings.Count} Warnings (جارٍ التصدير رغم وجود ملاحظات)";
                    QaStatusColor = Brushes.Crimson;
                }

                // 2. Export Shapefiles into Final\Shape
                if (ExportShapefiles)
                {
                    ReportProgress(40, "الخطوة 5: تصدير ملفات Shapefile", $"تصدير PARCEL.shp و BORDER.shp إلى '{Path.GetFileName(finalDir)}\\Shape'...", phase: "المرحلة 5 من 5");
                    try
                    {
                        ShapefileExporter.ExportToShapefile(
                            finalShapeDir,
                            templateFolder,
                            _parcels,
                            _parcelGeometries,
                            _borders,
                            _borderGeometries,
                            out var shpWarns,
                            parcelBaseName: parcelExportName,
                            borderBaseName: borderExportName,
                            epsgCode: SelectedUtmZone,
                            maxDeviationMeters: MaxCurveExportDeviationMeters,
                            alsoKeepExactUtmGdb: AlsoKeepExactUtmGdbWithTrueCurves
                        );
                        exportWarnings.AddRange(shpWarns);

                        // Generate and save Geometry Preservation Report (Rule 4)
                        var parcelAudits = _parcels
                            .Where(p => _parcelGeometries.ContainsKey(p.InternalId))
                            .Select(p => NativeCurveAuditHelper.AuditNativeGeometry(_parcelGeometries[p.InternalId], p.InternalId, p.PlotId))
                            .ToList();
                        int curveCount = parcelAudits.Sum(a => a.TotalCurveSegmentsCount);

                        var presReport = new GeometryPreservationReport
                        {
                            UtmZoneEpsg = SelectedUtmZone,
                            FeatureAudits = parcelAudits
                        };

                        var exportComp = new StagePreservationComparison
                        {
                            StageName = "Stage 5: Shapefile/GCS Deliverable",
                            FeatureClass = parcelExportName,
                            FeaturesChecked = parcelAudits.Count,
                            TotalCurveSegmentsBefore = curveCount,
                            TotalCurveSegmentsAfter = 0,
                            UnavoidableConversionsCount = curveCount,
                            MaxDeviationWhereConverted = MaxCurveExportDeviationMeters
                        };
                        if (curveCount > 0)
                        {
                            exportComp.Notes.Add($"Shapefile deliverable cannot store true curves. Converted {curveCount} curve segments to vertices with max deviation <= {MaxCurveExportDeviationMeters:F4}m.");
                            if (AlsoKeepExactUtmGdbWithTrueCurves)
                            {
                                exportComp.Notes.Add("An exact UTM copy keeping 100% true curves was saved in 'Exact_UTM_Curves.gdb'.");
                            }
                            GeometryPreservationNotice = $"تنبيه الحفاظ على الهندسة: تم تحويل {curveCount} قوساً إلى خطوط مستقيمة بتفاوت أقصى {MaxCurveExportDeviationMeters:F4}م لمطابقة قيود صيغة Shapefile. القياسات المجمدة AREA_SQM مأخوذة بدقة متناهية من الأقواس الأصلية.";
                        }
                        presReport.StageComparisons.Add(exportComp);
                        LastPreservationReport = presReport;

                        string presReportPath = Path.Combine(finalReportsDir, "Geometry_Preservation_Report.txt");
                        File.WriteAllText(presReportPath, CurveAuditService.FormatPreservationReport(presReport));
                        exportWarnings.Add($"[Geometry Preservation] Report written to: {presReportPath}");

                        ExportStatusSummary = $"تم تصدير ملفات Shapefiles بنجاح إلى:\n📁 {finalShapeDir}\n✔ PARCEL.shp & BORDER.shp (2D Planar, UTF-8 .cpg)";
                    }
                    catch (Exception shpEx)
                    {
                        exportWarnings.Add($"[Shapefile Warning] {shpEx.Message}");
                    }
                }

                // 3. Export Excel & Reports into Final\Reports
                if (GenerateReports && valResult != null)
                {
                    ReportProgress(70, "الخطوة 5: توليد تقارير الجودة والرقابة", "إنشاء تقرير QA_QC_Report.xlsx وجدول الاستخدامات في مجلد Reports...", phase: "المرحلة 5 من 5");
                    try
                    {
                        string sourceExportInfo = WorkingSourceMode == "LatestExport" ? $"{LatestExportPath} ({LatestExportTimestamp})" : "Fresh Session (CAD)";
                        ReportGenerator.GenerateAllReports(
                            finalReportsDir,
                            _parcels,
                            _borders,
                            valResult,
                            _projectMeta,
                            sourceCadName: !string.IsNullOrWhiteSpace(CadDrawingPath) ? Path.GetFileName(CadDrawingPath) : "Plan.dwg",
                            profileName: SelectedProfile,
                            utmEpsg: SelectedUtmZone,
                            continuedFromExport: sourceExportInfo
                        );

                        string landUseExportPath = Path.Combine(finalReportsDir, "PARCEs_landuse.xlsx");
                        LandUseExcelService.ExportUpdatedLandUseFile(landUseExportPath, _parcels);
                    }
                    catch (Exception repEx)
                    {
                        exportWarnings.Add($"[Reports Warning] {repEx.Message}");
                    }
                }

                // 4. Schema Compliance & Deviations Audit
                if (valResult != null)
                {
                    try
                    {
                        complianceResult = SchemaComplianceService.AuditAndCompare(
                            _helperManager.RequiredFieldsExcel,
                            finalReportsDir,
                            _parcels,
                            _borders,
                            valResult.AreaReport
                        );

                        if (complianceResult.HasDeviations)
                        {
                            if (ExportShapefiles)
                            {
                                try
                                {
                                    ShapefileExporter.ExportDeviatingParcelsToShapefile(
                                        finalShapeDir,
                                        templateFolder,
                                        complianceResult.DeviatingParcels,
                                        _parcelGeometries
                                    );
                                }
                                catch (Exception devEx)
                                {
                                    exportWarnings.Add($"[Deviations Warning] {devEx.Message}");
                                }
                            }

                            exportWarnings.Add($"[Deviations] {complianceResult.DeviatingParcels.Count} parcels with discrepancies exported to 'PARCEL_Differences'.");
                        }
                    }
                    catch (Exception compEx)
                    {
                        exportWarnings.Add($"[Compliance Notice] {compEx.Message}");
                    }
                }
            });

            // 5. Geodatabase Export into Final\Final.gdb (GP tools safely outside QueuedTask)
            if (ExportFeatureClass)
            {
                ReportProgress(85, "الخطوة 5: حفظ قاعدة البيانات الجغرافية Final.gdb", "حفظ المخرجات في Final.gdb الموحدة...", phase: "المرحلة 5 من 5");
                try
                {
                    await ShapefileExporter.ExportToFeatureClassAsync(
                        finalGdbPath,
                        parcelExportName,
                        borderExportName,
                        templateFolder,
                        _parcels,
                        _parcelGeometries,
                        _borders,
                        _borderGeometries,
                        exportWarnings,
                        SelectedUtmZone
                    );

                    if (complianceResult != null && complianceResult.HasDeviations)
                    {
                        try
                        {
                            string templateParcel = Path.Combine(templateFolder, "PARCEL.shp");
                            await GdbFeatureClassService.ExportParcelsToGdbAsync(
                                finalGdbPath,
                                "PARCEL_Differences",
                                templateParcel,
                                complianceResult.DeviatingParcels.Select(d => d.Parcel).ToList(),
                                _parcelGeometries,
                                SelectedUtmZone
                            );
                        }
                        catch (Exception devGdbEx)
                        {
                            exportWarnings.Add($"[Deviations GDB Warning] {devGdbEx.Message}");
                        }
                    }

                    // Add exported Feature Classes directly to the active ArcGIS Pro Map
                    await QueuedTask.Run(() =>
                    {
                        var map = MapView.Active?.Map;
                        if (map != null && Directory.Exists(finalGdbPath))
                        {
                            GdbFeatureClassService.AddOrRefreshMapLayer(map, finalGdbPath, parcelExportName, $"{parcelExportName} Final GDB");
                            GdbFeatureClassService.AddOrRefreshMapLayer(map, finalGdbPath, borderExportName, $"{borderExportName} Final GDB");
                            if (complianceResult != null && complianceResult.HasDeviations)
                            {
                                GdbFeatureClassService.AddOrRefreshMapLayer(map, finalGdbPath, "PARCEL_Differences", "PARCEL_Differences Final GDB");
                            }
                        }
                    });
                }
                catch (Exception gdbEx)
                {
                    exportWarnings.Add($"[GDB Notice] Geodatabase export skipped ({gdbEx.Message}). Deliverables preserved strictly in Shapefiles.");
                }
            }

            // 6. Export Integrity & Deliverables Verification (Requirement 9)
            bool finalGdbValid = Directory.Exists(finalGdbPath);
            string expectedParcelShp = Path.Combine(finalShapeDir, $"{parcelExportName}.shp");
            string expectedBorderShp = Path.Combine(finalShapeDir, $"{borderExportName}.shp");
            bool parcelShpValid = File.Exists(expectedParcelShp) && new FileInfo(expectedParcelShp).Length > 0;
            bool borderShpValid = File.Exists(expectedBorderShp) && new FileInfo(expectedBorderShp).Length > 0;
            bool reportsValid = Directory.Exists(finalReportsDir) && Directory.GetFiles(finalReportsDir).Length > 0;

            if (!finalGdbValid || !parcelShpValid || !borderShpValid || !reportsValid)
            {
                var failSb = new System.Text.StringBuilder();
                failSb.AppendLine("فشل التحقق من سلامة المخرجات النهائية داخل مجلد Final:");
                if (!finalGdbValid) failSb.AppendLine("- لم يتم إنشاء قاعدة البيانات Final.gdb بنجاح.");
                if (!parcelShpValid) failSb.AppendLine($"- ملف Shapefile للقطع ({parcelExportName}.shp) مفقود أو فارغ.");
                if (!borderShpValid) failSb.AppendLine($"- ملف Shapefile للحدود ({borderExportName}.shp) مفقود أو فارغ.");
                if (!reportsValid) failSb.AppendLine("- مجلد التقارير Reports فارغ أو لم يكتمل توليد ملفاته.");
                failSb.AppendLine("تم إيقاف اعتماد التصدير وعدم تحديث مؤشر Latest Export لحماية سلامة البيانات.");

                ReportError(failSb.ToString(), "خطأ في سلامة التصدير");
                return;
            }

            // 7. Save export state and update Latest Export only after complete verification
            try
            {
                string parcelPathForState = finalGdbValid ? Path.Combine(finalGdbPath, parcelExportName) : expectedParcelShp;
                string borderPathForState = finalGdbValid ? Path.Combine(finalGdbPath, borderExportName) : expectedBorderShp;

                var state = new ExportStateRecord
                {
                    RunId = Guid.NewGuid().ToString("N")[..8],
                    Timestamp = DateTime.UtcNow,
                    ExportFolder = finalDir,
                    ParcelPath = parcelPathForState,
                    BorderPath = borderPathForState,
                    ExportCrs = "WGS84 / GCS",
                    WorkingUtmZone = SelectedUtmZone,
                    SourceCadName = !string.IsNullOrWhiteSpace(CadDrawingPath) ? Path.GetFileName(CadDrawingPath) : "Plan.dwg",
                    ProfileName = SelectedProfile
                };

                if (File.Exists(expectedParcelShp))
                {
                    var pFi = new FileInfo(expectedParcelShp);
                    var pDs = new ExportDatasetInfo
                    {
                        DatasetName = "PARCEL",
                        FilePath = expectedParcelShp,
                        FileHash = ExportStateManager.ComputeFileSha256(expectedParcelShp),
                        FileSize = pFi.Length,
                        LastModifiedUtc = pFi.LastWriteTimeUtc,
                        FeatureCount = _parcels.Count
                    };
                    foreach (var p in _parcels)
                    {
                        pDs.Features.Add(new ExportFeatureMetadata
                        {
                            PlotId = p.PlotId,
                            AreaSqm = p.AreaSqm,
                            ShapeArea = p.ShapeArea,
                            ShapeLength = p.ShapeLength,
                            Usage = p.Usage,
                            Address = p.Address,
                            Neighbourhood = p.Neighbourhood,
                            Remarks = p.Remarks,
                            ParcelTyp = p.ParcelTyp
                        });
                    }
                    state.Datasets["PARCEL"] = pDs;
                }

                if (File.Exists(expectedBorderShp))
                {
                    var bFi = new FileInfo(expectedBorderShp);
                    var bDs = new ExportDatasetInfo
                    {
                        DatasetName = "BORDER",
                        FilePath = expectedBorderShp,
                        FileHash = ExportStateManager.ComputeFileSha256(expectedBorderShp),
                        FileSize = bFi.Length,
                        LastModifiedUtc = bFi.LastWriteTimeUtc,
                        FeatureCount = _borders.Count
                    };
                    foreach (var b in _borders)
                    {
                        bDs.Features.Add(new ExportFeatureMetadata
                        {
                            PlotId = b.PlotId,
                            BorderName = b.BorderName,
                            RingIndex = b.RingIndex,
                            Descriptio = b.Descriptio,
                            BorderType = b.BorderType,
                            ShapeLength = b.ShapeLength
                        });
                    }
                    state.Datasets["BORDER"] = bDs;
                }

                int maxHist = _profileModel?.MaxExportHistoryCount ?? 10;
                ExportStateManager.SaveExportState(_helperManager.LastExportStateFile, state, maxHist);
                LatestExportPath = finalDir;
                LatestExportTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                HasLatestExport = true;
                RefreshLatestExportStatus();
            }
            catch (Exception stateEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Part B] SaveExportState failed: {stateEx.Message}");
            }

            // Informative ExportStatusSummary
            ExportStatusSummary = $"تم تصدير المخرجات النهائية بنجاح إلى هيكل Final الموحد:\n📁 {finalDir}";
            ExportStatusSummary += $"\n✔ Final.gdb: PARCEL, BORDER";
            ExportStatusSummary += $"\n✔ Shape: PARCEL.shp, BORDER.shp (2D Planar, UTF-8 .cpg)";
            ExportStatusSummary += $"\n✔ Reports: كافة تقارير الجودة (QA_QC_Report.xlsx, Missing_Data_Report.csv, Audit_Log.json, PARCEs_landuse.xlsx)";

            if (valResult != null && valResult.Errors.Count > 0)
            {
                ExportStatusSummary += $"\n⚠️ ملاحظة: يحتوي تقرير التدقيق على {valResult.Errors.Count} ملاحظة (مفصلة داخل Reports).";
            }
            if (complianceResult != null && complianceResult.HasDeviations)
            {
                ExportStatusSummary += $"\n⚠️ {complianceResult.DeviatingParcels.Count} قطعة بها فروقات تم تصديرها إلى 'PARCEL_Differences'.";
            }

            ResetProgress($"الخطوة 5 اكتملت: تم تصدير مجلد Final بكافة محتوياته بنجاح.", "اكتمل التصدير والتدقيق");
        }
        catch (Exception ex)
        {
            ReportError($"Step 5 Error: {ex.Message}", "خطأ في الخطوة 5");
        }
        finally
        {
            if (!isPartOfPipeline) IsBusy = false;
        }
    }

    // Backward-compatibility aliases
    public Task ExecuteStep3Async(bool isPartOfPipeline = false) => ExecuteStep4BordersAsync(isPartOfPipeline);
    public Task ExecuteStep4Async(bool isPartOfPipeline = false) => ExecuteStep5ExportQaAsync(isPartOfPipeline);

    #region Intermediate Step Exports

    public async Task ExportStep2ParcelsAsync()
    {
        if (_parcels.Count == 0)
        {
            StatusMessage = "لا توجد بيانات بارسلات مستخرجة حالياً للتصدير. يرجى تشغيل الخطوة 2 أولاً.";
            return;
        }

        IsBusy = true;
        ReportProgress(20, "تصدير بارسلات الخطوة 2", "تجهيز مضلعات البارسلات ثنائية الأبعاد (2D)...");
        try
        {
            string baseOut = string.IsNullOrWhiteSpace(OutputDirectory) ? ResolveBaseFolder() : OutputDirectory;
            string outDir = ResolveUniqueExportFolder(baseOut, PlanNo, CityName ?? CityId, NeighbourhoodName ?? Neighbourhood);
            Directory.CreateDirectory(outDir);
            string baseName = "PARCEL_Step2_Raw";
            var warns = new List<string>();

            ReportProgress(50, "تصدير بارسلات الخطوة 2", $"كتابة {_parcels.Count} مضلع إلى '{baseName}'...");
            if (ExportShapefiles)
            {
                try
                {
                    ShapefileExporter.ExportSingleParcelShapefile(outDir, _helperManager.TemplatesDir, _parcels, _parcelGeometries, baseName, SelectedUtmZone);
                }
                catch (Exception shpEx)
                {
                    warns.Add($"[Shapefile Notice] {shpEx.Message}");
                }
            }

            try
            {
                await GdbFeatureClassService.ExportParcelsToGdbAsync(GdbPath, baseName, _parcels, _parcelGeometries, _helperManager.TemplatesDir, SelectedUtmZone);
            }
            catch (Exception gdbEx)
            {
                warns.Add($"[GDB Notice] {gdbEx.Message}");
            }

            string msg = $"تم بنجاح تصدير {_parcels.Count} قطعة مستخرجة (2D Planar) إلى '{Path.GetFileName(outDir)}\\{baseName}'.";
            if (warns.Count > 0) msg += $" ({string.Join(" | ", warns)})";
            ResetProgress(msg, "اكتمل تصدير الخطوة 2");
        }
        catch (Exception ex)
        {
            ReportError($"خطأ أثناء تصدير بارسلات الخطوة 2: {ex.Message}", "خطأ في التصدير");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportStep3PopulatedParcelsAsync()
    {
        if (_parcels.Count == 0)
        {
            StatusMessage = "No populated parcels available to export. Please run Step 3 first.";
            return;
        }

        IsBusy = true;
        ReportProgress(20, "Step 3 Export: Populated Parcels", "Preparing 26 MOMRAH schema fields...");
        try
        {
            string baseOut = string.IsNullOrWhiteSpace(OutputDirectory) ? ResolveBaseFolder() : OutputDirectory;
            string outDir = ResolveUniqueExportFolder(baseOut, PlanNo, CityName ?? CityId, NeighbourhoodName ?? Neighbourhood);
            Directory.CreateDirectory(outDir);
            string baseName = "PARCEL_Approved_Schema";
            var warns = new List<string>();

            ReportProgress(50, "Step 3 Export: Writing Schema Parcels", $"Writing {_parcels.Count} parcels with schema to '{baseName}'...");
            if (ExportShapefiles)
            {
                try
                {
                    ShapefileExporter.ExportSingleParcelShapefile(outDir, _helperManager.TemplatesDir, _parcels, _parcelGeometries, baseName, SelectedUtmZone);
                }
                catch (Exception shpEx)
                {
                    warns.Add($"[Shapefile Notice] {shpEx.Message}");
                }
            }

            try
            {
                await GdbFeatureClassService.ExportParcelsToGdbAsync(GdbPath, baseName, _parcels, _parcelGeometries, _helperManager.TemplatesDir, SelectedUtmZone);

                // Register authoritative Step 3 export reference for Step 4 consumption
                Step3ExportedParcelsFeatureClassPath = Path.Combine(GdbPath, baseName);
                Step3ExportedTimestamp = DateTime.Now;
                Step3ExportedFeatureCount = _parcels.Count;
                System.Diagnostics.Debug.WriteLine($"[Audit Log] Step 3 Export registered: Path='{Step3ExportedParcelsFeatureClassPath}', Count={Step3ExportedFeatureCount}, Time={Step3ExportedTimestamp:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception gdbEx)
            {
                warns.Add($"[GDB Notice] {gdbEx.Message}");
            }

            string msg = $"Step 3 Export Complete: Successfully exported {_parcels.Count} parcels (2D Planar) to '{Path.GetFileName(outDir)}\\{baseName}'.";
            if (warns.Count > 0) msg += $" ({string.Join(" | ", warns)})";
            ResetProgress(msg, "Step 3 Export Complete");
        }
        catch (Exception ex)
        {
            ReportError($"Step 3 Export Error: {ex.Message}", "Export Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportStep4BordersAsync()
    {
        if (_borders.Count == 0)
        {
            StatusMessage = "لا توجد حدود وأبعاد مولدة للتصدير. يرجى تشغيل الخطوة 4 أولاً.";
            return;
        }

        IsBusy = true;
        ReportProgress(20, "تصدير حدود الخطوة 4", "تجهيز خطوط الحدود وعروض الشوارع...");
        try
        {
            string baseOut = string.IsNullOrWhiteSpace(OutputDirectory) ? ResolveBaseFolder() : OutputDirectory;
            string outDir = ResolveUniqueExportFolder(baseOut, PlanNo, CityName ?? CityId, NeighbourhoodName ?? Neighbourhood);
            Directory.CreateDirectory(outDir);
            string baseName = "border_Step4";

            ReportProgress(50, "تصدير حدود الخطوة 4", $"كتابة {_borders.Count} خط حدود إلى '{baseName}'...");
            if (ExportShapefiles)
            {
                ShapefileExporter.ExportSingleBorderShapefile(outDir, _helperManager.TemplatesDir, _borders, _borderGeometries, baseName, SelectedUtmZone);
            }

            await GdbFeatureClassService.ExportBordersToGdbAsync(GdbPath, baseName, _borders, _borderGeometries, _helperManager.TemplatesDir, SelectedUtmZone);

            ResetProgress($"تم بنجاح تصدير {_borders.Count} خط حدود (2D Planar) إلى '{Path.GetFileName(outDir)}\\{baseName}'.", "اكتمل تصدير الخطوة 4");
        }
        catch (Exception ex)
        {
            ReportError($"خطأ أثناء تصدير حدود الخطوة 4: {ex.Message}", "خطأ في التصدير");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    public async Task ExecutePipelineAsync()
    {
        IsBusy = true;
        ReportProgress(5, "خط المعالجة: بدء التشغيل المتتابع", "تشغيل الخطوات من 1 إلى 5 بالكامل...", phase: "خط المعالجة");

        try
        {
            SelectedTabIndex = 0;
            ReportProgress(10, "خط المعالجة: المرحلة 1/5", "فحص واستيراد طبقات الكاد وتحويله لداتاسيت...", phase: "المرحلة 1 من 5");
            await ExecuteStep1Async();

            SelectedTabIndex = 1;
            ReportProgress(30, "خط المعالجة: المرحلة 2/5", "استخراج وبلمرة مضلعات البارسلات (2D Planar)...", phase: "المرحلة 2 من 5");
            await ExecuteStep2Async(isPartOfPipeline: true);

            SelectedTabIndex = 2;
            ReportProgress(50, "خط المعالجة: المرحلة 3/5", "تعبئة البيانات والسكيما المعتمدة وجداول الاستخدام...", phase: "المرحلة 3 من 5");
            await ExecuteStep3PopulateSchemaAsync(isPartOfPipeline: true);

            SelectedTabIndex = 3;
            ReportProgress(70, "خط المعالجة: المرحلة 4/5", "تفكيك الواجهات واستشعار الشوارع بالأشعة...", phase: "المرحلة 4 من 5");
            await ExecuteStep4BordersAsync(isPartOfPipeline: true);

            if (PauseAtReviewTables && InferredBorders.Count > 0)
            {
                ReportProgress(80, "توقف مؤقت للمراجعة", $"جاهز للمراجعة: {InferredBorders.Count} حد مستنتج. انقر 'الخطوة 5' للاستمرار والتصدير.", phase: "المرحلة 4 من 5");
                SelectedTabIndex = 3;
                return;
            }

            SelectedTabIndex = 4;
            ReportProgress(90, "خط المعالجة: المرحلة 5/5", "التصدير النهائي والتدقيق الشامل...", phase: "المرحلة 5 من 5");
            await ExecuteStep5ExportQaAsync(isPartOfPipeline: true);

            ResetProgress("🎉 اكتمل خط المعالجة المتتابع بالكامل بنجاح 100%!", "اكتملت جميع المراحل 100%");
        }
        catch (Exception ex)
        {
            ReportError($"Pipeline Error: {ex.Message}", "خطأ في خط المعالجة");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void LoadDemoProject()
    {
        try
        {
            IsDemoProject = true;
            AllowDemoExportConfirmation = false;
            string demoDir = _helperManager.DemoDir;
            Directory.CreateDirectory(demoDir);

            var demo = SyntheticDataGenerator.CreateSyntheticProject(demoDir);

            _parcels.Clear();
            _parcelGeometries.Clear();
            _borders.Clear();
            _borderGeometries.Clear();
            InferredBorders.Clear();

            foreach (var p in demo.AllParcels)
            {
                _parcels.Add(p);
            }

            PlanNo = demo.Metadata.PlanNo;
            Neighbourhood = demo.Metadata.Neighbourhood;
            RequestType = demo.Metadata.RequestType;
            ReportNo = demo.Metadata.ReportNo;
            CadDrawingPath = Path.Combine(demoDir, "DEMO_Subdivision.dwg");
            AreasExcelPath = demo.AreasExcelPath;
            ExpectedLayersExcelPath = demo.ExpectedLayersExcelPath;

            int mainCount = _parcels.Count(p => p.Class == ParcelClass.Main);
            int subCount = _parcels.Count(p => p.Class != ParcelClass.Main);

            ParcelCountSummary = $"DEMO Parcels: {_parcels.Count} loaded ({mainCount} MAIN, {subCount} SUB)";
            ParcelAreaSummary = $"Total Area: {_parcels.Sum(p => p.AreaSqm):F2} m²";

            StatusMessage = $"Demo project loaded: {mainCount} MAIN parcels, {subCount} SUB parcels (Labeled as DEMO). Production export is guarded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load Demo Error: {ex.Message}";
        }
    }

    #endregion

    #region Inferred Borders Review Table Helpers

    private void AcceptAllInferred()
    {
        foreach (var item in InferredBorders)
        {
            item.IsAccepted = true;
        }
        ResetProgress("تم اعتماد كافة الحدود وعروض الشوارع المستنتجة بنجاح.", "تم اعتماد الحدود");
    }

    private void ApplySuggestedWidth(InferredBorderItem? item)
    {
        if (item != null && item.SuggestedWidth.HasValue)
        {
            item.MeasuredWidth = item.SuggestedWidth.Value;
            if (item.MeasuredWidth < PassageThresholdMeters)
            {
                item.Descriptio = $"ممر بعرض {item.MeasuredWidth} م";
                item.BorderType = "LNE";
            }
            else
            {
                item.Descriptio = $"شارع بعرض {item.MeasuredWidth} م";
                item.BorderType = "STR";
            }
        }
    }

    #endregion

    #region Helper Methods

    private void LoadAvailableProfiles()
    {
        AvailableProfiles.Clear();

        if (Directory.Exists(_helperManager.ProfilesDir))
        {
            foreach (var ext in new[] { "*.xlsx", "*.json" })
            {
                foreach (var f in Directory.GetFiles(_helperManager.ProfilesDir, ext))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (!AvailableProfiles.Contains(name))
                        AvailableProfiles.Add(name);
                }
            }
        }

        if (AvailableProfiles.Count == 0)
        {
            AvailableProfiles.Add("Example_Profile");
        }

        if (AvailableProfiles.Contains("Example_Profile"))
        {
            SelectedProfile = "Example_Profile";
        }
        else if (AvailableProfiles.Count > 0)
        {
            SelectedProfile = AvailableProfiles[0];
        }
    }

    private void ApplyProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName)) return;

        string xlsx = Path.Combine(_helperManager.ProfilesDir, profileName + ".xlsx");
        ConsultantProfileModel? p = null;
        if (File.Exists(xlsx))
        {
            p = ProfileExcelService.LoadProfileFromExcel(xlsx);
        }

        if (p != null)
        {
            _profileModel = p;
            PassageThresholdMeters = p.PassageStreetThresholdMeters;
            RayCount = p.RayCount;
            MergeChamfers = p.MergeChamfers;
            AMinDeg = p.AMinDeg;
            AMaxDeg = p.AMaxDeg;
            MultiSegmentAngleDeviationDeg = p.MultiSegmentAngleDeviationDeg;
            BuildPolygonsFromLines = p.BuildPolygonsFromLines;
            SnapToleranceMeters = p.SnapToleranceMeters;
            MaxCurveExportDeviationMeters = p.MaxCurveExportDeviationMeters;
            AlsoKeepExactUtmGdbWithTrueCurves = p.AlsoKeepExactUtmGdbWithTrueCurves;
            if (SupportedUtmZones.Contains(p.UtmEpsg)) SelectedUtmZone = p.UtmEpsg;
            if (string.IsNullOrWhiteSpace(PlanNo) || PlanNo == "0000/0") PlanNo = p.DefaultPlanNo;
            if (string.IsNullOrWhiteSpace(Neighbourhood) || Neighbourhood == "District 1") Neighbourhood = p.DefaultNeighbourhood;
            RequestType = p.DefaultRequestType;
            ReportNo = p.DefaultReportNo;
            if (p.ContinueFromLatestExport && HasLatestExport)
            {
                WorkingSourceMode = "LatestExport";
            }
            StatusMessage = $"Applied profile settings from '{profileName}.xlsx'.";
        }
    }

    public BorderExtractionOptions GetBorderExtractionOptions()
    {
        if (_profileModel != null)
        {
            return new BorderExtractionOptions
            {
                AMinDeg = AMinDeg,
                AMaxDeg = AMaxDeg,
                MultiSegmentAngleDeviationDeg = MultiSegmentAngleDeviationDeg,
                IncludeReflexCorners = _profileModel.IncludeReflexCorners,
                ArcCenterToleranceMeters = _profileModel.ArcCenterToleranceMeters,
                ArcRadiusToleranceMeters = _profileModel.ArcRadiusToleranceMeters,
                ArcTangentToleranceDeg = _profileModel.ArcTangentToleranceDeg,
                ClusterLengthMeters = MergeChamfers ? ChamferThresholdMeters : _profileModel.ClusterLengthMeters,
                Rule1LengthRatioGuard = _profileModel.Rule1LengthRatioGuard,
                TrueNorthCorrection = _profileModel.TrueNorthCorrection
            };
        }
        return new BorderExtractionOptions
        {
            AMinDeg = AMinDeg,
            AMaxDeg = AMaxDeg,
            MultiSegmentAngleDeviationDeg = MultiSegmentAngleDeviationDeg,
            ClusterLengthMeters = MergeChamfers ? ChamferThresholdMeters : 7.0
        };
    }

    private void InitializeLatestExportState()
    {
        try
        {
            RefreshLatestExportStatus();
            // Default working data source remains CAD Session / Dataset as requested
            WorkingSourceMode = "FromPreviousStep";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cad2Gis] InitializeLatestExportState error: {ex.Message}");
        }
    }

    public void RefreshLatestExportStatus()
    {
        try
        {
            var state = ExportStateManager.LoadLatestExport(_helperManager.LastExportStateFile);
            if (state != null)
            {
                HasLatestExport = true;
                LatestExportPath = state.ExportFolder;
                LatestExportTimestamp = state.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

                ExportHistoryItems.Clear();
                if (state.History != null)
                {
                    foreach (var h in state.History)
                        ExportHistoryItems.Add(h);
                }

                if (!string.IsNullOrEmpty(state.ParcelPath) && File.Exists(state.ParcelPath))
                {
                    IsWorkingFromMissing = false;
                    bool parcelChanged = state.Datasets.TryGetValue("PARCEL", out var pDs) &&
                        ExportStateManager.HasPhysicalDatasetChanged(state.ParcelPath, pDs);
                    bool borderChanged = !string.IsNullOrEmpty(state.BorderPath) &&
                        File.Exists(state.BorderPath) &&
                        state.Datasets.TryGetValue("Border", out var bDs) &&
                        ExportStateManager.HasPhysicalDatasetChanged(state.BorderPath, bDs);

                    IsEditedAfterExport = parcelChanged || borderChanged;
                    ShowEditedAfterExportBanner = IsEditedAfterExport;
                }
                else
                {
                    IsWorkingFromMissing = true;
                    WorkingFromMissingWarning = $"Latest export dataset not found at '{state.ParcelPath}'.";
                }
            }
            else
            {
                HasLatestExport = false;
                LatestExportPath = string.Empty;
                LatestExportTimestamp = string.Empty;
                IsEditedAfterExport = false;
                ShowEditedAfterExportBanner = false;
                IsWorkingFromMissing = false;
            }
            UpdateWorkingFromSummary();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cad2Gis] RefreshLatestExportStatus failed: {ex.Message}");
        }
    }

    private void UpdateWorkingFromSummary()
    {
        if (WorkingSourceMode == "LatestExport" && HasLatestExport)
        {
            string edited = IsEditedAfterExport ? "Yes" : "No";
            WorkingFromSummary = $"Working from: {LatestExportPath} — exported {LatestExportTimestamp} — edited after export: {edited}";
        }
        else if (WorkingSourceMode == "UserOwnData")
        {
            if (SelectedMapLayer != null)
            {
                WorkingFromSummary = $"Working from: Map Layer '{SelectedMapLayer.Name}' ({SelectedMapLayer.GeometryType})";
            }
            else if (!string.IsNullOrEmpty(CustomSchemaDataPath))
            {
                WorkingFromSummary = $"Working from: Feature Class / Dataset ({Path.GetFileName(CustomSchemaDataPath)})";
            }
            else
            {
                WorkingFromSummary = "Working from: User Own Data (No file / layer selected)";
            }
        }
        else
        {
            if (GdbFeatureClassService.DatasetExists(GdbPath, "Cad_Dataset"))
            {
                WorkingFromSummary = $"Working from: GDB Feature Dataset 'Cad_Dataset' ({Path.GetFileName(GdbPath)}) — Editable Map Layers";
            }
            else
            {
                WorkingFromSummary = "Working from: Current CAD Session Data (Step 2 / Step 3)";
            }
        }
    }

    public async Task RefreshMapLayersAsync()
    {
        try
        {
            var layers = new List<MapLayerItem>();
            await QueuedTask.Run(() =>
            {
                var map = MapView.Active?.Map;
                if (map == null) return;

                var fLayers = map.GetLayersAsFlattenedList().OfType<FeatureLayer>();
                foreach (var fl in fLayers)
                {
                    try
                    {
                        var shapeType = fl.ShapeType.ToString();
                        string typeAr = shapeType.Contains("Polygon") ? "مضلعات" : (shapeType.Contains("Polyline") ? "خطوط" : (shapeType.Contains("Point") ? "نقاط" : shapeType));
                        string fcName = fl.Name;
                        try
                        {
                            using var fc = fl.GetFeatureClass();
                            if (fc != null) fcName = fc.GetName();
                        }
                        catch { }

                        layers.Add(new MapLayerItem
                        {
                            Name = fl.Name,
                            GeometryType = typeAr,
                            FeatureClassPath = fcName,
                            Layer = fl
                        });
                    }
                    catch { }
                }
            });

            AvailableMapPolygonLayers.Clear();
            AvailableMapAllFeatureLayers.Clear();

            foreach (var item in layers)
            {
                AvailableMapAllFeatureLayers.Add(item);
                if (item.GeometryType == "مضلعات" || item.GeometryType.Contains("Polygon"))
                {
                    AvailableMapPolygonLayers.Add(item);
                }
            }

            if (_selectedMapLayer != null)
            {
                var matching = AvailableMapPolygonLayers.FirstOrDefault(l => l.Name == _selectedMapLayer.Name)
                            ?? AvailableMapAllFeatureLayers.FirstOrDefault(l => l.Name == _selectedMapLayer.Name);
                if (matching != null)
                {
                    SetProperty(ref _selectedMapLayer, matching, nameof(SelectedMapLayer));
                }
                else
                {
                    SetProperty(ref _selectedMapLayer, null, nameof(SelectedMapLayer));
                }
            }
            // Do NOT auto-assign SelectedMapLayer when null, to preserve authoritative pipeline continuity and avoid overriding WorkingSourceMode.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cad2Gis] RefreshMapLayersAsync error: {ex.Message}");
        }
    }

    private void BrowseFeatureClassAction()
    {
        try
        {
            var openDlg = new ArcGIS.Desktop.Catalog.OpenItemDialog
            {
                Title = "اختر Feature Class من قاعدة البيانات الجغرافية (GDB)",
                Filter = "esri_browseDialogFilters_featureClasses",
                MultiSelect = false
            };
            if (openDlg.ShowDialog() == true && openDlg.Items.Any())
            {
                string path = openDlg.Items.First().Path;
                CustomSchemaDataPath = path;
                WorkingSourceMode = "UserOwnData";
                Stage2SourceMode = "UserOwnData";
                UpdateWorkingFromSummary();
                StatusMessage = $"تم اختيار Feature Class بنجاح: '{Path.GetFileName(path)}'";
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cad2Gis] OpenItemDialog error: {ex.Message}");
            BrowseCustomFileAction();
        }
    }

    private void BrowseCustomFileAction()
    {
        var dlg = new OpenFileDialog
        {
            Title = "اختر ملف أو طبقة البيانات الخاصة (Shapefile / Excel)",
            Filter = "All Supported (*.shp;*.xlsx;*.xls)|*.shp;*.xlsx;*.xls|Shapefiles (*.shp)|*.shp|Excel Workbooks (*.xlsx;*.xls)|*.xlsx;*.xls|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            CustomSchemaDataPath = dlg.FileName;
            WorkingSourceMode = "UserOwnData";
            Stage2SourceMode = "UserOwnData";
            UpdateWorkingFromSummary();
            StatusMessage = $"تم اختيار ملف البيانات الخاصة: '{Path.GetFileName(dlg.FileName)}'";
        }
    }

    private void BrowseExportDeliverablesAction()
    {
        try
        {
            var openDlg = new ArcGIS.Desktop.Catalog.OpenItemDialog
            {
                Title = "اختر مجلد أو Feature Class تصدير سابق",
                Filter = "esri_browseDialogFilters_featureClasses",
                MultiSelect = false
            };
            if (openDlg.ShowDialog() == true && openDlg.Items.Any())
            {
                string path = openDlg.Items.First().Path;
                string folder = path;
                if (path.Contains(".gdb", StringComparison.OrdinalIgnoreCase))
                {
                    int gdbIdx = path.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
                    folder = Path.GetDirectoryName(path.Substring(0, gdbIdx + 4)) ?? path;
                }
                else if (File.Exists(path))
                {
                    folder = Path.GetDirectoryName(path) ?? path;
                }
                LatestExportPath = folder;
                HasLatestExport = true;
                WorkingSourceMode = "LatestExport";
                RefreshLatestExportStatus();
                return;
            }
        }
        catch { }

        var dlg = new OpenFileDialog
        {
            Title = "اختر ملف تصدير سابق (PARCEL.shp)",
            Filter = "ESRI Shapefile (*.shp)|*.shp|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            string folder = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
            LatestExportPath = folder;
            HasLatestExport = true;
            WorkingSourceMode = "LatestExport";
            RefreshLatestExportStatus();
        }
    }

    private bool IngestWorkingParcelsIfNeeded(string stepName)
    {
        // 1. If User Own Data selected
        if (WorkingSourceMode == "UserOwnData")
        {
            if (SelectedMapLayer?.Layer != null)
            {
                ReportProgress(20, stepName, $"تحميل القطع من طبقة الخريطة الحالية '{SelectedMapLayer.Name}'...", phase: "تحميل البيانات");
                var layerLoaded = GdbFeatureClassService.ReadParcelsFromFeatureLayer(SelectedMapLayer.Layer, SelectedUtmZone);
                if (layerLoaded.Parcels.Count > 0)
                {
                    _parcels.Clear();
                    _parcels.AddRange(layerLoaded.Parcels);
                    _parcelGeometries.Clear();
                    foreach (var kvp in layerLoaded.Geometries)
                        _parcelGeometries[kvp.Key] = kvp.Value;
                    return true;
                }
            }
            if (!string.IsNullOrWhiteSpace(CustomSchemaDataPath))
            {
                ReportProgress(20, stepName, $"تحميل القطع من المسار '{Path.GetFileName(CustomSchemaDataPath)}'...", phase: "تحميل البيانات");
                var (loadedParcels, loadedGeoms, wasCopied, notice) = GdbFeatureClassService.ReadParcelsFromDataset(CustomSchemaDataPath, SelectedUtmZone, _helperManager.ScratchDir);
                if (loadedParcels.Count > 0)
                {
                    _parcels.Clear();
                    _parcels.AddRange(loadedParcels);
                    _parcelGeometries.Clear();
                    foreach (var kvp in loadedGeoms)
                        _parcelGeometries[kvp.Key] = kvp.Value;
                    return true;
                }
            }
            if (!string.IsNullOrWhiteSpace(SourceParcelShpPath) && File.Exists(SourceParcelShpPath))
            {
                ReportProgress(20, stepName, $"تحميل القطع من '{Path.GetFileName(SourceParcelShpPath)}'...", phase: "تحميل البيانات");
                var (loadedParcels, loadedGeoms, wasCopied, notice) = GdbFeatureClassService.ReadParcelsFromDataset(SourceParcelShpPath, SelectedUtmZone, _helperManager.ScratchDir);
                if (loadedParcels.Count > 0)
                {
                    _parcels.Clear();
                    _parcels.AddRange(loadedParcels);
                    _parcelGeometries.Clear();
                    foreach (var kvp in loadedGeoms)
                        _parcelGeometries[kvp.Key] = kvp.Value;
                    return true;
                }
            }
        }

        // 2. If Latest Export selected
        if (WorkingSourceMode == "LatestExport" && HasLatestExport)
        {
            var state = ExportStateManager.LoadLatestExport(_helperManager.LastExportStateFile);
            string pPath = state?.ParcelPath ?? string.Empty;
            if (string.IsNullOrEmpty(pPath) || (!File.Exists(pPath) && !pPath.Contains(".gdb")))
            {
                string gdbCandidate = !string.IsNullOrWhiteSpace(GdbPath) && Directory.Exists(GdbPath)
                    ? GdbPath
                    : Path.Combine(LatestExportPath, "Cad2Gis_Workspace.gdb");
                if (!Directory.Exists(gdbCandidate))
                {
                    gdbCandidate = Path.Combine(LatestExportPath, "Export_Deliverables.gdb");
                }

                if (Directory.Exists(gdbCandidate))
                {
                    pPath = Path.Combine(gdbCandidate, "PARCEL");
                }
                else
                {
                    pPath = Path.Combine(LatestExportPath, "PARCEL.shp");
                }
            }

            ReportProgress(20, stepName, $"تحميل القطع من أحدث تصدير '{pPath}'...", phase: "تحميل البيانات");
            var (loadedParcels, loadedGeoms, wasCopied, notice) = GdbFeatureClassService.ReadParcelsFromDataset(pPath, SelectedUtmZone, _helperManager.ScratchDir);
            if (loadedParcels.Count > 0)
            {
                _parcels.Clear();
                _parcels.AddRange(loadedParcels);
                _parcelGeometries.Clear();
                foreach (var kvp in loadedGeoms)
                    _parcelGeometries[kvp.Key] = kvp.Value;
                return true;
            }
        }

        // 3. Fallback to GDB / Session
        if (_parcels.Count == 0 && Directory.Exists(GdbPath))
        {
            var gdbLoaded = GdbFeatureClassService.ReadParcelsFromFeatureClass(GdbPath, "PARCEL", SelectedUtmZone);
            if (gdbLoaded.Parcels.Count > 0)
            {
                _parcels.AddRange(gdbLoaded.Parcels);
                foreach (var kvp in gdbLoaded.Geometries)
                    _parcelGeometries[kvp.Key] = kvp.Value;
                return true;
            }
        }

        return _parcels.Count > 0;
    }

    private void OnHistoryItemSelected(ExportHistoryItem item)
    {
        if (item == null) return;
        LatestExportPath = item.ExportFolder;
        LatestExportTimestamp = item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        HasLatestExport = true;
        WorkingSourceMode = "LatestExport";
        UpdateWorkingFromSummary();
    }

    private void RecomputeFrozenMetricsAction()
    {
        int recomputedCount = 0;
        foreach (var p in _parcels)
        {
            if (_parcelGeometries.TryGetValue(p.InternalId, out var g) && g != null && !g.IsEmpty)
            {
                p.AreaSqm = g.Area;
                p.ShapeArea = g.Area;
                p.ShapeLength = g.Length;
                recomputedCount++;
            }
        }
        ShowFrozenMetricPrompt = false;
        StatusMessage = $"تمت إعادة حساب المساحات والأطوال لـ {recomputedCount} مضلع بنجاح بناءً على الهندسة المعدلة. تم الحفاظ على جميع الحقول الوصفية اليدوية.";
    }

    private void KeepStoredFrozenMetricsAction()
    {
        ShowFrozenMetricPrompt = false;
        StatusMessage = "تم الإبقاء على المقاييس المحفوظة مسبقاً وتسجيل تحذير تدقيق هندسي.";
    }

    private void DetectAdminJurisdiction(MapPoint? samplePoint = null)
    {
        try
        {
            if (samplePoint == null && _parcels.Count > 0)
            {
                var mainParcels = _parcels.Where(p => p.Class == ParcelClass.Main).ToList();
                var candidate = mainParcels.Count > 0 ? mainParcels.FirstOrDefault() : _parcels.FirstOrDefault();
                if (candidate != null && _parcelGeometries.TryGetValue(candidate.InternalId, out var g) && !g.IsEmpty)
                {
                    samplePoint = g.Extent.Center;
                }
                else if (candidate != null && candidate.CentroidX != 0 && candidate.CentroidY != 0)
                {
                    samplePoint = MapPointBuilderEx.CreateMapPoint(candidate.CentroidX, candidate.CentroidY);
                }
            }

            if (samplePoint == null && Directory.Exists(GdbPath))
            {
                try
                {
                    using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(GdbPath)));
                    var fcs = gdb.GetDefinitions<FeatureClassDefinition>();
                    var candidateFc = fcs.FirstOrDefault(f => f.GetName().Contains("Polygon", StringComparison.OrdinalIgnoreCase))
                                   ?? fcs.FirstOrDefault(f => f.GetName().Contains("Polyline", StringComparison.OrdinalIgnoreCase));
                    if (candidateFc != null)
                    {
                        using var fc = gdb.OpenDataset<FeatureClass>(candidateFc.GetName());
                        using var cursor = fc.Search(null, false);
                        if (cursor.MoveNext())
                        {
                            using var feat = (Feature)cursor.Current;
                            var s = feat.GetShape();
                            if (s != null && !s.IsEmpty) samplePoint = s.Extent.Center;
                        }
                    }
                }
                catch { }
            }

            if (samplePoint == null && MapView.Active != null)
            {
                samplePoint = MapView.Active.Extent?.Center;
            }

            // Always ensure detector is instantiated and loaded with valid admin files
            if (_adminDetector == null || !_adminDetector.HasValidFiles)
            {
                _adminDetector = new AdminAreaDetector(_helperManager.AdminAreasDir);
            }

            if (samplePoint != null && _adminDetector != null)
            {
                int zone = SelectedUtmZone - 32600;
                var res = _adminDetector.DetectAdminLocation(samplePoint, zone);
                if (res.IsSuccess)
                {
                    _projectMeta.RegionId = res.RegionId;
                    _projectMeta.RegionName = res.RegionName;
                    _projectMeta.CityId = res.CityId;
                    _projectMeta.CityName = res.CityName;
                    NotifyPropertyChanged(nameof(RegionName));
                    NotifyPropertyChanged(nameof(CityName));
                    NotifyPropertyChanged(nameof(RegionId));
                    NotifyPropertyChanged(nameof(CityId));
                    AdminBadgeText = $"📍 Region {res.RegionName} ({res.RegionId}) | City {res.CityName} ({res.CityId})";
                }
                else
                {
                    // Preserve manual/user inputs if detection had no hit
                    if (string.IsNullOrWhiteSpace(_projectMeta.CityId) && string.IsNullOrWhiteSpace(_projectMeta.RegionId))
                    {
                        AdminBadgeText = !string.IsNullOrWhiteSpace(res.Warning) ? $"⚠️ {res.Warning}" : "⚠️ No administrative polygon hit. Fields left blank.";
                    }
                    else
                    {
                        AdminBadgeText = $"📍 Region {_projectMeta.RegionName} ({_projectMeta.RegionId}) | City {_projectMeta.CityName} ({_projectMeta.CityId}) [Manual/Preserved]";
                    }
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_projectMeta.CityId))
                {
                    AdminBadgeText = "Admin jurisdiction will be detected from CAD centroid.";
                }
            }

            UpdateAddressPreview();
            SyncParcelsAddress();
        }
        catch (Exception ex)
        {
            AdminBadgeText = $"Detection Warning: {ex.Message}";
        }
    }

    public void DetectAddressFromMainParcelsAction()
    {
        try
        {
            ReportProgress(30, "جلب وتحديث العنوان", "حساب المركز الهندسي للقطع والتقاطع المكاني مع البلديات والمناطق...");
            MapPoint? mainCenter = null;
            var mainParcels = _parcels.Where(p => p.Class == ParcelClass.Main).ToList();
            if (mainParcels.Count > 0)
            {
                double sumX = 0, sumY = 0;
                int cnt = 0;
                foreach (var mp in mainParcels)
                {
                    if (_parcelGeometries.TryGetValue(mp.InternalId, out var g) && !g.IsEmpty)
                    {
                        sumX += g.Extent.Center.X;
                        sumY += g.Extent.Center.Y;
                        cnt++;
                    }
                }
                if (cnt > 0)
                {
                    mainCenter = MapPointBuilderEx.CreateMapPoint(sumX / cnt, sumY / cnt);
                }
            }
            else if (_parcelGeometries.Count > 0)
            {
                var first = _parcelGeometries.Values.FirstOrDefault(g => !g.IsEmpty);
                if (first != null) mainCenter = first.Extent.Center;
            }

            DetectAdminJurisdiction(mainCenter);
            SyncParcelsAddress(forceUpdate: true);

            // Update live GDB layer attributes if PARCEL exists
            if (_parcels.Count > 0 && Directory.Exists(GdbPath))
            {
                string fcParcelName = IsDemoProject ? "DEMO_PARCEL" : "PARCEL";
                try
                {
                    _ = QueuedTask.Run(() =>
                    {
                        GdbFeatureClassService.UpdateParcelAttributesInFeatureClass(GdbPath, fcParcelName, _parcels);
                        GdbFeatureClassService.AddOrRefreshMapLayer(MapView.Active?.Map, GdbPath, fcParcelName, $"{fcParcelName} Live GDB");
                    });
                }
                catch { }
            }

            ResetProgress($"تم تحديث العنوان وكود المدينة بنجاح: {AddressPreviewText} (مدينة: {CityName} [{CityId}]، منطقة: {RegionName} [{RegionId}])", "تم تحديث العنوان");
        }
        catch (Exception ex)
        {
            ReportError($"خطأ أثناء تحديث العنوان: {ex.Message}", "خطأ في العنوان");
        }
    }

    public async Task<bool> ConvertCadToDatasetActionAsync()
    {
        if (string.IsNullOrWhiteSpace(CadDrawingPath) || !File.Exists(CadDrawingPath))
        {
            ReportError("يرجى اختيار ملف كاد صالح (.dwg أو .dxf) أولاً للتحويل إلى داتاسيت.", "ملف الكاد غير محدد");
            return false;
        }

        IsBusy = true;
        ReportProgress(15, "تحويل الكاد إلى داتاسيت", $"جاري تحويل '{Path.GetFileName(CadDrawingPath)}' بالنظام EPSG:{SelectedUtmZone}...", phase: "المرحلة 1");

        try
        {
            string datasetName = "Cad_Dataset";
            ReportProgress(40, "تحويل الكاد إلى داتاسيت", "تشغيل أداة التحويل CADToGeodatabase داخل الجيوداتابيز...", phase: "المرحلة 1");
            bool ok = await GdbFeatureClassService.ConvertCadToDatasetAsync(CadDrawingPath, GdbPath, datasetName, SelectedUtmZone);
            if (ok)
            {
                ReportProgress(75, "تحويل الكاد إلى داتاسيت", "إضافة الـ Dataset للخريطة وفهرسة الطبقات...", phase: "المرحلة 1");
                var datasetLayers = new List<CadLayerInfo>();
                await QueuedTask.Run(() =>
                {
                    GdbFeatureClassService.AddCadDatasetToMap(MapView.Active?.Map, GdbPath, datasetName);
                    datasetLayers = GdbFeatureClassService.GetCadLayersFromDataset(GdbPath, datasetName);
                });

                if (datasetLayers.Count > 0)
                {
                    CadLayers.Clear();
                    foreach (var l in datasetLayers) AddCadLayerWithEvents(l);

                    // Match with Expected Layers if available
                    if (File.Exists(ExpectedLayersExcelPath))
                    {
                        try
                        {
                            var rules = ExpectedLayersService.ReadExpectedLayersExcel(ExpectedLayersExcelPath);
                            var matchReport = ExpectedLayersService.MatchCadLayers(datasetLayers, rules, SelectedProfile);
                            foreach (var matched in matchReport.CadLayersMatched)
                            {
                                var target = CadLayers.FirstOrDefault(c => c.LayerName.Equals(matched.CadLayerName, StringComparison.OrdinalIgnoreCase));
                                if (target != null)
                                {
                                    target.Role = ExpectedLayersService.ParseRole(matched.AssignedRole);
                                    target.Category = CadLayerInfo.DetermineCategory(target.Role, target.LayerName);
                                    if (target.Category == "Main Parcels" && target.Role != LayerRole.Ignore)
                                    {
                                        target.IsSelected = true;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // Ensure at least one boundary/parcel layer is selected
                    if (!CadLayers.Any(l => l.IsSelected))
                    {
                        var candidate = CadLayers.FirstOrDefault(l => l.Category == "Main Parcels")
                                     ?? CadLayers.FirstOrDefault(l => l.Role == LayerRole.Boundary)
                                     ?? CadLayers.FirstOrDefault(l => l.GeometryType == "Polygon" || l.GeometryType == "Line");
                        if (candidate != null)
                        {
                            candidate.IsSelected = true;
                        }
                    }

                    _filteredCadLayers?.Refresh();
                    UpdateCadTabCounts();
                }

                // Set default working data source to CAD Dataset / Session
                WorkingSourceMode = "FromPreviousStep";

                // Automatically advance to Step 2 (Extract Parcels)
                SelectedTabIndex = 1;

                ResetProgress($"تم بنجاح تحويل الكاد، وإضافة طبقات الداتاسيت للخريطة للتعديل، والانتقال للخطوة 2 لبناء البارسلات.", "اكتمل تحويل الكاد");
                return true;
            }
            else
            {
                ReportProgress(100, "تنبيه تحويل الكاد", "تعذر أداة CADToGeodatabase، سيتم استيراد الطبقات مباشرة من الرسم.");
                return false;
            }
        }
        catch (Exception ex)
        {
            ReportError($"خطأ أثناء تحويل الكاد إلى داتاسيت: {ex.Message}", "خطأ في تحويل الكاد");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsPlotNumberText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim();
        if (t.All(c => char.IsDigit(c) || c == '/' || c == '-' || c == ' ' || c == '.' || c == '\\')) return true;
        if (t.StartsWith("قطعة", StringComparison.OrdinalIgnoreCase) || t.StartsWith("رقم", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsUsageText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim().ToLowerInvariant();
        return t.Contains("سكني") || t.Contains("تجاري") || t.Contains("مسجد") || t.Contains("حديقة") ||
               t.Contains("خدمات") || t.Contains("مرفق") || t.Contains("مدرسة") || t.Contains("res") ||
               t.Contains("comm") || t.Contains("mosque") || t.Contains("park");
    }

    private void SyncParcelsAddress(bool forceUpdate = false)
    {
        string addr = _projectMeta.BuildAddress(_projectMeta.NeighbourhoodName);
        foreach (var p in _parcels)
        {
            if (forceUpdate || string.IsNullOrWhiteSpace(p.Address) || (!string.IsNullOrWhiteSpace(addr) && p.Address != addr))
            {
                p.Address = addr;
            }
            if (forceUpdate || string.IsNullOrWhiteSpace(p.Neighbourhood) || (!string.IsNullOrWhiteSpace(_projectMeta.NeighbourhoodCode) && p.Neighbourhood != _projectMeta.NeighbourhoodCode))
            {
                p.Neighbourhood = _projectMeta.NeighbourhoodCode;
            }
            if (forceUpdate || string.IsNullOrWhiteSpace(p.CityId) || (!string.IsNullOrWhiteSpace(_projectMeta.CityId) && p.CityId != _projectMeta.CityId))
            {
                p.CityId = _projectMeta.CityId;
            }
            if (forceUpdate || string.IsNullOrWhiteSpace(p.RegionId) || (!string.IsNullOrWhiteSpace(_projectMeta.RegionId) && p.RegionId != _projectMeta.RegionId))
            {
                p.RegionId = _projectMeta.RegionId;
            }
        }
    }

    private void UpdateAddressPreview()
    {
        AddressPreviewText = _projectMeta.BuildAddress(_projectMeta.NeighbourhoodName);
    }

    private void BrowseCadDrawing()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Source CAD Drawing (اختيار ملف الكاد)",
            Filter = "AutoCAD Files (*.dwg;*.dxf)|*.dwg;*.dxf|AutoCAD Drawing (*.dwg)|*.dwg|AutoCAD DXF (*.dxf)|*.dxf|All Files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true)
        {
            CadDrawingPath = dlg.FileName;
            if (string.IsNullOrWhiteSpace(PlanNo) || PlanNo == "0000/0")
            {
                PlanNo = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
            StatusMessage = $"تم اختيار ملف الكاد: {Path.GetFileName(dlg.FileName)}. انقر 'تحويل الكاد إلى داتاسيت' أو 'فحص واستيراد طبقات الكاد'.";
        }
    }

    private void BrowseLandUse()
    {
        var dlg = new OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*" };
        if (dlg.ShowDialog() == true) LandUseExcelPath = dlg.FileName;
    }

    private void TestDialog()
    {
        var dlg = new OpenItemDialog
        {
            Title = "Select Feature Class",
            MultiSelect = false,
            Filter = "esri_browseDialogFilters_featureClasses"
        };
        dlg.ShowDialog();
    }

    private void BrowseSourceParcel()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Parcels Layer (Shapefile / GDB Feature Class)",
            Filter = "Shapefiles (*.shp)|*.shp|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            SourceParcelShpPath = dlg.FileName;
            Stage2SourceMode = "UserOwnData";
            StatusMessage = $"تم اختيار طبقة البيانات الخاصة: '{Path.GetFileName(dlg.FileName)}'. انقر 'تشغيل الخطوة 2' لتحميل البارسلات.";
        }
    }

    private void BrowseCustomSchemaData()
    {
        BrowseFeatureClassAction();
    }

    public async void LoadCustomSchemaFields()
    {
        if (string.IsNullOrWhiteSpace(CustomSchemaDataPath))
            return;

        List<string> sourceFields = new();
        try
        {
            await QueuedTask.Run(() =>
            {
                sourceFields = CustomSchemaDataHelper.ExtractFields(CustomSchemaDataPath);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cad2Gis] LoadCustomSchemaFields error: {ex.Message}");
        }

        if (sourceFields.Count == 0)
        {
            SchemaStatusMessage = "⚠️ لم يتم العثور على أي حقول في الملف المختار أو تعذر قراءته.";
            SchemaStatusBgColor = "#451A03";
            SchemaStatusBorderColor = "#F59E0B";
            SchemaStatusTextColor = "#FCD34D";
            HasCustomFileLoaded = false;
            return;
        }

        HasCustomFileLoaded = true;

        // Check if schema is identical
        var checkResult = CustomSchemaDataService.CheckSchema(sourceFields);
        IsSchemaIdentical = checkResult.IsIdentical;

        // Update FieldMappings dropdown options and perform auto-matching
        var availableList = new List<string> { "-- غير محدد --" };
        availableList.AddRange(sourceFields);

        int autoMatchedCount = 0;
        foreach (var mapping in FieldMappings)
        {
            mapping.AvailableSourceFields.Clear();
            foreach (var sf in availableList)
            {
                mapping.AvailableSourceFields.Add(sf);
            }

            // Automatic mapping if same names or synonyms exist
            string matched = CustomSchemaDataService.AutoMatchField(mapping.TargetField, sourceFields);
            if (!string.IsNullOrEmpty(matched))
            {
                mapping.SelectedSourceField = matched;
                autoMatchedCount++;
            }
            else
            {
                mapping.SelectedSourceField = "-- غير محدد --";
            }
        }

        if (IsSchemaIdentical)
        {
            SchemaStatusMessage = $"✅ السكيما مطابقة تماماً للمواصفات المعتمدة (100% Identical MOMRAH Schema) - تم العثور على جميع الحقول الـ {checkResult.MatchedCount}.";
            SchemaStatusBgColor = "#064E3B";
            SchemaStatusBorderColor = "#10B981";
            SchemaStatusTextColor = "#34D399";
            IsFieldMappingExpanded = false;
        }
        else
        {
            SchemaStatusMessage = $"⚠️ السكيما تختلف عن الاسكيما المعتمدة ({checkResult.MatchedCount}/{checkResult.TotalStandard} حقول معتمدة، وتم ربط {autoMatchedCount} حقلاً تلقائياً). تم فتح جدول ربط الحقول.";
            SchemaStatusBgColor = "#451A03";
            SchemaStatusBorderColor = "#F59E0B";
            SchemaStatusTextColor = "#FCD34D";
            IsFieldMappingExpanded = true;
        }

        StatusMessage = $"Loaded custom schema data: {sourceFields.Count} fields detected, {autoMatchedCount} fields auto-mapped.";
    }

    private void AutoMapFields()
    {
        if (string.IsNullOrWhiteSpace(CustomSchemaDataPath)) return;
        var sourceFields = CustomSchemaDataHelper.ExtractFields(CustomSchemaDataPath);
        if (sourceFields.Count == 0) return;

        int autoMatched = 0;
        foreach (var mapping in FieldMappings)
        {
            string matched = CustomSchemaDataService.AutoMatchField(mapping.TargetField, sourceFields);
            if (!string.IsNullOrEmpty(matched))
            {
                mapping.SelectedSourceField = matched;
                autoMatched++;
            }
        }
        StatusMessage = $"Auto-mapping completed: {autoMatched} fields mapped.";
    }

    private void ApplyCustomRowToParcel(ParcelRecord p, Dictionary<string, object> row)
    {
        foreach (var m in FieldMappings.Where(x => x.IsMapped))
        {
            if (row.TryGetValue(m.SelectedSourceField, out var objVal) && objVal != null)
            {
                string s = objVal.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(s)) continue;

                switch (m.TargetField.ToUpperInvariant())
                {
                    case "PLOT_ID":
                        p.PlotId = s;
                        break;
                    case "PLAN_NO":
                        p.PlanNo = s;
                        break;
                    case "USAGE":
                        p.Usage = s;
                        break;
                    case "BLOCK_NO":
                        p.BlockNo = s;
                        break;
                    case "NEIGHBOURH":
                        p.Neighbourhood = s;
                        break;
                    case "ADDRESS":
                        p.Address = s;
                        break;
                    case "CITY_ID":
                        p.CityId = s;
                        break;
                    case "REGION_ID":
                        p.RegionId = s;
                        break;
                    case "REALESTA_1":
                        if (int.TryParse(s, out int re)) p.RealestateType = re;
                        break;
                    case "ISBUILDING":
                        if (int.TryParse(s, out int ib)) p.IsBuilding = ib;
                        break;
                    case "BUILDINGTY":
                        if (int.TryParse(s, out int bt)) p.BuildingType = bt;
                        break;
                    case "REMARKS":
                        p.Remarks = s;
                        break;
                    case "LAYOUT":
                        p.Layout = s;
                        break;
                    case "REQUEST_TY":
                        p.RequestType = s;
                        break;
                    case "REQUEST_NO":
                        p.RequestNo = s;
                        break;
                    case "REPORT_NO":
                        p.ReportNo = s;
                        break;
                    case "AREA_SQM":
                        if (double.TryParse(s, out double ar)) p.AreaSqm = ar;
                        break;
                    case "PLOT_NO1":
                        p.PlotNo1 = s;
                        break;
                    case "PLOT_NO2":
                        p.PlotNo2 = s;
                        break;
                    case "PLOT_NO3":
                        p.PlotNo3 = s;
                        break;
                    case "PLOT_NO4":
                        p.PlotNo4 = s;
                        break;
                }
            }
        }
    }

    private void BrowseAreasExcel()
    {
        var dlg = new OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*" };
        if (dlg.ShowDialog() == true) AreasExcelPath = dlg.FileName;
    }

    private void BrowseExpectedLayers()
    {
        var dlg = new OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*" };
        if (dlg.ShowDialog() == true)
        {
            ExpectedLayersExcelPath = dlg.FileName;
            StatusMessage = $"تم اختيار ملف إكسل الطبقات: {Path.GetFileName(dlg.FileName)}. انقر 'اقرأ واستورد الطبقات من الإكسل' لقراءته.";
        }
    }

    public void LoadLayersFromExpectedExcel(string? excelPath = null)
    {
        string path = !string.IsNullOrWhiteSpace(excelPath) ? excelPath : ExpectedLayersExcelPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusMessage = "ملف إكسل الطبقات غير موجود. يرجى اختيار ملف Excel صالح (.xlsx).";
            return;
        }

        try
        {
            var loaded = ExpectedLayersService.LoadLayersFromExpectedExcel(path, SelectedProfile);
            if (loaded.Count == 0)
            {
                StatusMessage = $"لم يتم العثور على طبقات في '{Path.GetFileName(path)}' للبروفايل '{SelectedProfile}'.";
                return;
            }

            // If we already have discovered layers from CAD, match and enrich them
            if (CadLayers.Count > 0 && CadLayers.Any(c => c.EntityCount > 0))
            {
                var rules = ExpectedLayersService.ReadExpectedLayersExcel(path);
                var matchReport = ExpectedLayersService.MatchCadLayers(CadLayers, rules, SelectedProfile);
                foreach (var matched in matchReport.CadLayersMatched)
                {
                    var target = CadLayers.FirstOrDefault(c => c.LayerName.Equals(matched.CadLayerName, StringComparison.OrdinalIgnoreCase));
                    if (target != null)
                    {
                        target.Role = ExpectedLayersService.ParseRole(matched.AssignedRole);
                        target.Category = CadLayerInfo.DetermineCategory(target.Role, target.LayerName);
                    }
                }
                StatusMessage = $"تمت مطابقة {CadLayers.Count} طبقة كاد مع ملف الإكسل '{Path.GetFileName(path)}' (البروفايل: {SelectedProfile}).";
            }
            else
            {
                // Populate layers directly from Expected Layers Excel
                CadLayers.Clear();
                foreach (var l in loaded)
                {
                    AddCadLayerWithEvents(l);
                }
                ResetProgress($"تم بنجاح قراءة {CadLayers.Count} طبقة متوقعة من إكسل '{Path.GetFileName(path)}' (البروفايل: {SelectedProfile}).", "اكتمل استيراد الإكسل");
            }

            _filteredCadLayers?.Refresh();
            UpdateCadTabCounts();
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ أثناء قراءة ملف إكسل الطبقات: {ex.Message}";
        }
    }

    private void BrowseOutputFolder()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true) OutputDirectory = dlg.FolderName;
    }

    private void BrowseGdbPathAction()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
        {
            GdbPath = dlg.FolderName;
            StatusMessage = $"تم تحديد قاعدة البيانات: {GdbPath}";
        }
    }

    private void UseExportOutputGdbAction()
    {
        string baseOut = string.IsNullOrWhiteSpace(OutputDirectory) ? ResolveBaseFolder() : OutputDirectory;
        GdbPath = GdbFeatureClassService.GetEffectiveGdbPath(baseOut);
        StatusMessage = $"تم تعيين Geodatabase الموحدة داخل مجلد التصدير (الموصى به):\n{GdbPath}";
    }

    private void UseProjectGdbAction()
    {
        try
        {
            string? projGdb = ArcGIS.Desktop.Core.Project.Current?.DefaultGeodatabasePath;
            if (!string.IsNullOrWhiteSpace(projGdb))
            {
                GdbPath = projGdb;
                StatusMessage = $"تم تعيين قاعدة البيانات الموحدة إلى Geodatabase مشروع ArcGIS Pro الافتراضية:\n{projGdb}";
                return;
            }
        }
        catch { }
        StatusMessage = "لا يوجد مشروع ArcGIS Pro مفتوح حالياً لتحديد قاعدة بياناته الافتراضية.";
    }

    private void OnProjectOpened(ArcGIS.Desktop.Core.Events.ProjectEventArgs args)
    {
        try
        {
            if (args.Project != null)
            {
                string home = args.Project.HomeFolderPath;
                if (string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(args.Project.DefaultGeodatabasePath))
                {
                    home = Path.GetDirectoryName(args.Project.DefaultGeodatabasePath) ?? string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(home) && Directory.Exists(home))
                {
                    OutputDirectory = Path.Combine(home, "Export_Output");
                    GdbPath = GdbFeatureClassService.GetEffectiveGdbPath(OutputDirectory);
                    _helperManager = new HelperFilesManager(projectHomeFolder: home);
                    NotifyPropertyChanged(nameof(HelperFilesPathDisplay));
                    NotifyPropertyChanged(nameof(IsHelperFallbackActive));
                    NotifyPropertyChanged(nameof(HelperFallbackWarning));
                    if (_helperManager.IsMissingFromProject)
                    {
                        StatusMessage = "يرجى نسخ HelperFiles في مجلد المشروع";
                    }
                }
            }
        }
        catch { }
    }

    private static string ResolveBaseFolder()
    {
        try
        {
            var project = ArcGIS.Desktop.Core.Project.Current;
            if (project != null)
            {
                var projectHome = project.HomeFolderPath;
                if (!string.IsNullOrWhiteSpace(projectHome) && Directory.Exists(projectHome))
                    return projectHome;

                var defGdb = project.DefaultGeodatabasePath;
                if (!string.IsNullOrWhiteSpace(defGdb))
                {
                    var parentDir = Path.GetDirectoryName(defGdb);
                    if (!string.IsNullOrWhiteSpace(parentDir) && Directory.Exists(parentDir))
                        return parentDir;
                }
            }
        }
        catch { }

        string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string arcgisProjDir = Path.Combine(myDocs, "ArcGIS", "Projects");
        if (Directory.Exists(arcgisProjDir))
            return arcgisProjDir;

        string docsExport = Path.Combine(myDocs, "CAD2GIS_Export");
        Directory.CreateDirectory(docsExport);
        return docsExport;
    }

    public static string ResolveUniqueExportFolder(string baseOutputDir, string? planNo, string? cityName, string? districtName)
    {
        string cleanBase = string.IsNullOrWhiteSpace(baseOutputDir) ? ResolveBaseFolder() : baseOutputDir;
        return ExportNamingService.ResolveUniqueExportFolder(cleanBase, planNo, cityName, districtName);
    }

    private void SeedInitialCadLayers()
    {
        if (CadLayers.Count > 0) return;

        var seeds = new List<CadLayerInfo>
        {
            // Main Parcels (Matching Image 2 exact rows and colors)
            new CadLayerInfo { LayerName = "PARCEL_RESIDENTIAL", EntityCount = 1452, GeometryType = "Polygon", Color = "#FACC15", RawCadText = "S_RES_01", DecodedArabicText = "سكني - 1", Role = LayerRole.Boundary, Category = "Main Parcels", IsSelected = true },
            new CadLayerInfo { LayerName = "PARCEL_COMMERCIAL", EntityCount = 342, GeometryType = "Line", Color = "#D946EF", RawCadText = "S_RES_01", DecodedArabicText = "سكني - 1", Role = LayerRole.Boundary, Category = "Main Parcels", IsSelected = false },
            new CadLayerInfo { LayerName = "SERVICE_LANE", EntityCount = 128, GeometryType = "Point", Color = "#06B6D4", RawCadText = "SERV_LANE", DecodedArabicText = "ممر خدمة", Role = LayerRole.Boundary, Category = "Main Parcels", IsSelected = true },
            new CadLayerInfo { LayerName = "PARCEL_GOVERNMENT", EntityCount = 45, GeometryType = "Point", Color = "#22C55E", RawCadText = "", DecodedArabicText = "", Role = LayerRole.Boundary, Category = "Main Parcels", IsSelected = false },
            new CadLayerInfo { LayerName = "UTILITY_EASEMENT", EntityCount = 92, GeometryType = "Polygon", Color = "#86EFAC", RawCadText = "", DecodedArabicText = "", Role = LayerRole.Boundary, Category = "Main Parcels", IsSelected = false },

            // Sub-Parcels / Utilities
            new CadLayerInfo { LayerName = "11 SB_Plots", EntityCount = 128, GeometryType = "Polygon", Color = "#06B6D4", RawCadText = "SB_01", DecodedArabicText = "قطع خدمات فرعية", Role = LayerRole.Service, Category = "Sub-Parcels / Utilities", IsSelected = true },
            new CadLayerInfo { LayerName = "elicktric room", EntityCount = 300, GeometryType = "Polygon", Color = "#06B6D4", RawCadText = "ELEC", DecodedArabicText = "غرفة كهرباء", Role = LayerRole.Service, Category = "Sub-Parcels / Utilities", IsSelected = true },
            new CadLayerInfo { LayerName = "garden", EntityCount = 24, GeometryType = "Polygon", Color = "#22C55E", RawCadText = "PARK", DecodedArabicText = "حديقة عامة", Role = LayerRole.Service, Category = "Sub-Parcels / Utilities", IsSelected = true },
            new CadLayerInfo { LayerName = "mosque", EntityCount = 18, GeometryType = "Polygon", Color = "#22C55E", RawCadText = "MSQ", DecodedArabicText = "مسجد", Role = LayerRole.Service, Category = "Sub-Parcels / Utilities", IsSelected = true },
            new CadLayerInfo { LayerName = "Parking_Basement_Area", EntityCount = 30, GeometryType = "Polygon", Color = "#86EFAC", RawCadText = "PRK", DecodedArabicText = "مواقف وقبو", Role = LayerRole.Service, Category = "Sub-Parcels / Utilities", IsSelected = true },

            // Text & Annotations
            new CadLayerInfo { LayerName = "DIM", EntityCount = 3436, GeometryType = "Line", Color = "#F59E0B", RawCadText = "20.00", DecodedArabicText = "٢٠.٠٠", Role = LayerRole.Dimensions, Category = "Text & Annotations", IsSelected = true },
            new CadLayerInfo { LayerName = "number", EntityCount = 384, GeometryType = "Text", Color = "#F59E0B", RawCadText = "101", DecodedArabicText = "١٠١", Role = LayerRole.PlotText, Category = "Text & Annotations", IsSelected = true },
            new CadLayerInfo { LayerName = "Text", EntityCount = 334, GeometryType = "Text", Color = "#F59E0B", RawCadText = "TEXT", DecodedArabicText = "نصوص المخطط", Role = LayerRole.PlotText, Category = "Text & Annotations", IsSelected = true },
            new CadLayerInfo { LayerName = "ترقيم تجاري الاحياء", EntityCount = 110, GeometryType = "Text", Color = "#F59E0B", RawCadText = "COM_NUM", DecodedArabicText = "ترقيم تجاري", Role = LayerRole.PlotText, Category = "Text & Annotations", IsSelected = true },

            // Roads & References (All set to Ignore / IsSelected = false)
            new CadLayerInfo { LayerName = "boundary roads", EntityCount = 13, GeometryType = "Line", Color = "#94A3B8", RawCadText = "STR_30", DecodedArabicText = "شارع عرض 30 متر", Role = LayerRole.Ignore, Category = "Roads & References", IsSelected = false },
            new CadLayerInfo { LayerName = "pathes", EntityCount = 104, GeometryType = "Line", Color = "#94A3B8", RawCadText = "PATH_6", DecodedArabicText = "ممر مشاة عرض 6 متر", Role = LayerRole.Ignore, Category = "Roads & References", IsSelected = false },
            new CadLayerInfo { LayerName = "water canals", EntityCount = 153, GeometryType = "Polygon", Color = "#38BDF8", RawCadText = "CANAL", DecodedArabicText = "قناة مائية", Role = LayerRole.Ignore, Category = "Roads & References", IsSelected = false },
            new CadLayerInfo { LayerName = "التنظيم", EntityCount = 2, GeometryType = "Line", Color = "#94A3B8", RawCadText = "SETBACK", DecodedArabicText = "خط تنظيم", Role = LayerRole.Ignore, Category = "Roads & References", IsSelected = false },
            new CadLayerInfo { LayerName = "Deed", EntityCount = 34, GeometryType = "Line", Color = "#94A3B8", RawCadText = "DEED", DecodedArabicText = "صك ملكية", Role = LayerRole.Ignore, Category = "Roads & References", IsSelected = false },
            new CadLayerInfo { LayerName = "0", EntityCount = 6347, GeometryType = "Polygon", Color = "#64748B", RawCadText = "0", DecodedArabicText = "طبقة افتراضية", Role = LayerRole.Ignore, Category = "Roads & References", IsSelected = false }
        };

        foreach (var s in seeds)
        {
            AddCadLayerWithEvents(s);
        }

        UpdateCadTabCounts();
        _filteredCadLayers?.Refresh();
    }

    private List<FeatureLayer> FindCadFeatureLayersOnMap(Map? map, string? cadFilePath)
    {
        var result = new List<FeatureLayer>();
        if (map == null) return result;

        string fileName = !string.IsNullOrWhiteSpace(cadFilePath) ? Path.GetFileName(cadFilePath) : "";
        string baseName = !string.IsNullOrWhiteSpace(fileName) ? Path.GetFileNameWithoutExtension(fileName) : "";

        var allFeatureLayers = map.GetLayersAsFlattenedList().OfType<FeatureLayer>().ToList();

        // 1. Match by Parent GroupLayer or CadLayer hierarchy
        if (!string.IsNullOrWhiteSpace(baseName))
        {
            foreach (var fl in allFeatureLayers)
            {
                if (LayerBelongsToCad(fl, baseName, fileName))
                {
                    result.Add(fl);
                }
            }
        }

        // 2. If no layers found by parent name, check FeatureClass data source path
        if (result.Count == 0 && !string.IsNullOrWhiteSpace(baseName))
        {
            foreach (var fl in allFeatureLayers)
            {
                try
                {
                    var fc = fl.GetFeatureClass();
                    if (fc != null)
                    {
                        string fcPath = fc.GetPath()?.ToString() ?? "";
                        if (fcPath.Contains(baseName, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(fileName) && fcPath.Contains(fileName, StringComparison.OrdinalIgnoreCase)))
                        {
                            result.Add(fl);
                        }
                    }
                }
                catch { }
            }
        }

        // 3. If still no layers found, find ANY CAD feature layers on the active map
        // (identified by CAD-specific fields: "Layer" + ("Color" | "Linetype" | "RefName" | "Handle"))
        if (result.Count == 0)
        {
            foreach (var fl in allFeatureLayers)
            {
                try
                {
                    var fc = fl.GetFeatureClass();
                    if (fc != null)
                    {
                        var def = fc.GetDefinition();
                        bool isCad = def.FindField("Layer") >= 0 &&
                            (def.FindField("Color") >= 0 || def.FindField("Linetype") >= 0 ||
                             def.FindField("RefName") >= 0 || def.FindField("Handle") >= 0);
                        if (isCad)
                        {
                            result.Add(fl);
                        }
                    }
                }
                catch { }
            }
        }

        // 4. If still none on the map and a CAD drawing file path exists, add it to the map
        if (result.Count == 0 && !string.IsNullOrWhiteSpace(cadFilePath) && File.Exists(cadFilePath))
        {
            try
            {
                var createdLayer = LayerFactory.Instance.CreateLayer(new Uri(cadFilePath), map);
                if (createdLayer is GroupLayer gl)
                {
                    result.AddRange(gl.GetLayersAsFlattenedList().OfType<FeatureLayer>());
                }
                else if (createdLayer is FeatureLayer newFl)
                {
                    result.Add(newFl);
                }
                else
                {
                    allFeatureLayers = map.GetLayersAsFlattenedList().OfType<FeatureLayer>().ToList();
                    foreach (var layerItem in allFeatureLayers)
                    {
                        if (LayerBelongsToCad(layerItem, baseName, fileName))
                        {
                            result.Add(layerItem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not add CAD layer to map: {ex.Message}");
            }
        }

        return result;
    }

    private static bool LayerBelongsToCad(Layer? layer, string baseName, string fileName)
    {
        var current = layer;
        while (current != null)
        {
            if ((!string.IsNullOrWhiteSpace(fileName) && current.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(baseName) && current.Name.Contains(baseName, StringComparison.OrdinalIgnoreCase)) ||
                current.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) ||
                current.Name.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = current.Parent as Layer;
        }
        return false;
    }

    #endregion
}
