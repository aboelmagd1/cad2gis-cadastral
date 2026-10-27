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
using ArcGIS.Desktop.Core.Geoprocessing;
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

public partial class Cad2GisDockpaneViewModel : ArcGIS.Desktop.Framework.Contracts.DockPane
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
        PipelineStateManager.ContentStampResolver = GdbFeatureClassService.ComputeFeatureClassContentStamp;

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
        if (!Directory.Exists(OutputDirectory))
        {
            try { Directory.CreateDirectory(OutputDirectory); } catch { }
        }
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
                    else if (targetCat == "Ignore Layers" || targetCat == "Roads & References") { cur.Role = LayerRole.Ignore; cur.IsSelected = false; }
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

        BrowseOwnDataCommand = new RelayCommandParam(BrowseOwnDataAction);
        OpenStepFolderCommand = new RelayCommandParam(OpenStepFolderAction);
        AddStepSourceToMapCommand = new RelayCommandParam(AddStepSourceToMapAction);

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
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                NotifyPropertyChanged(nameof(WorkingSourceMode));
                NotifyPropertyChanged(nameof(IsSourceFromLatestExport));
                NotifyPropertyChanged(nameof(IsSourceFromSession));
                NotifyPropertyChanged(nameof(IsSourceFromCustom));
                NotifyPropertyChanged(nameof(IsSchemaFromStandard));
                NotifyPropertyChanged(nameof(IsSchemaFromCustom));
                NotifyPropertyChanged(nameof(Stage3SourceMode));
                UpdateWorkingFromSummary();
                NotifyStepSourceProperties(CurrentStep);
            }
        }
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
            System.Windows.Application.Current.Dispatcher.BeginInvoke(Update);
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
            System.Windows.Application.Current.Dispatcher.BeginInvoke(Update);
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
            System.Windows.Application.Current.Dispatcher.BeginInvoke(Update);
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
            System.Windows.Application.Current.Dispatcher.BeginInvoke(Update);
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
                if (!string.IsNullOrWhiteSpace(value) && !Directory.Exists(value))
                {
                    try { Directory.CreateDirectory(value); } catch { }
                }
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

    // =========================================================================
    // Working Data Source — Exactly Two Options per Step: PreviousStep, OwnData
    // =========================================================================
    private readonly Dictionary<int, StepSource> _sourceByStep = new()
    {
        [2] = StepSource.PreviousStep,
        [3] = StepSource.PreviousStep,
        [4] = StepSource.PreviousStep,
        [5] = StepSource.PreviousStep
    };
    private readonly Dictionary<int, string> _ownDataPathByStep = new();

    public StepSource GetSource(int step) => _sourceByStep.TryGetValue(step, out var s) ? s : StepSource.PreviousStep;

    public void SetSource(int step, StepSource s)
    {
        _sourceByStep[step] = s;
        NotifyStepSourceProperties(step);
    }

    public string GetOwnDataPath(int step) => _ownDataPathByStep.TryGetValue(step, out var p) ? p : string.Empty;

    public void SetOwnDataPath(int step, string path)
    {
        _ownDataPathByStep[step] = path;
        NotifyStepSourceProperties(step);
    }

    public void NotifyStepSourceProperties(int step)
    {
        NotifyPropertyChanged($"IsStep{step}SourcePreviousStep");
        NotifyPropertyChanged($"IsStep{step}SourceOwnData");
        NotifyPropertyChanged($"Step{step}SourceSummary");
        NotifyPropertyChanged($"Step{step}OwnDataPath");
        NotifyPropertyChanged(nameof(WorkingSourceMode));
        NotifyPropertyChanged(nameof(IsSourceFromSession));
        NotifyPropertyChanged(nameof(IsSourceFromCustom));
        NotifyPropertyChanged(nameof(IsSchemaFromStandard));
        NotifyPropertyChanged(nameof(IsSchemaFromCustom));
        NotifyPropertyChanged(nameof(WorkingFromSummary));
    }

    // Step 2 Properties
    public bool IsStep2SourcePreviousStep
    {
        get => GetSource(2) == StepSource.PreviousStep;
        set { if (value) SetSource(2, StepSource.PreviousStep); }
    }
    public bool IsStep2SourceOwnData
    {
        get => GetSource(2) == StepSource.OwnData;
        set { if (value) SetSource(2, StepSource.OwnData); }
    }
    public string Step2OwnDataPath
    {
        get => GetOwnDataPath(2);
        set => SetOwnDataPath(2, value);
    }
    public string Step2SourceSummary => GetStepSourceSummary(2);

    // Step 3 Properties
    public bool IsStep3SourcePreviousStep
    {
        get => GetSource(3) == StepSource.PreviousStep;
        set { if (value) SetSource(3, StepSource.PreviousStep); }
    }
    public bool IsStep3SourceOwnData
    {
        get => GetSource(3) == StepSource.OwnData;
        set { if (value) SetSource(3, StepSource.OwnData); }
    }
    public string Step3OwnDataPath
    {
        get => GetOwnDataPath(3);
        set => SetOwnDataPath(3, value);
    }
    public string Step3SourceSummary => GetStepSourceSummary(3);

    // Step 4 Properties
    public bool IsStep4SourcePreviousStep
    {
        get => GetSource(4) == StepSource.PreviousStep;
        set { if (value) SetSource(4, StepSource.PreviousStep); }
    }
    public bool IsStep4SourceOwnData
    {
        get => GetSource(4) == StepSource.OwnData;
        set { if (value) SetSource(4, StepSource.OwnData); }
    }
    public string Step4OwnDataPath
    {
        get => GetOwnDataPath(4);
        set => SetOwnDataPath(4, value);
    }
    public string Step4SourceSummary => GetStepSourceSummary(4);

    // Step 5 Properties
    public bool IsStep5SourcePreviousStep
    {
        get => GetSource(5) == StepSource.PreviousStep;
        set { if (value) SetSource(5, StepSource.PreviousStep); }
    }
    public bool IsStep5SourceOwnData
    {
        get => GetSource(5) == StepSource.OwnData;
        set { if (value) SetSource(5, StepSource.OwnData); }
    }
    public string Step5OwnDataPath
    {
        get => GetOwnDataPath(5);
        set => SetOwnDataPath(5, value);
    }
    public string Step5SourceSummary => GetStepSourceSummary(5);

    public string GetStepSourceSummary(int step)
    {
        var source = GetSource(step);
        if (source == StepSource.OwnData)
        {
            string path = GetOwnDataPath(step);
            if (string.IsNullOrWhiteSpace(path))
            {
                return "المصدر: بياناتي الخاصة (لم يتم تحديد مسار الملف بعد — انقر فوق 'استعراض')";
            }
            if (File.Exists(path) || Directory.Exists(path) || GdbFeatureClassService.ItemExists(path))
            {
                return $"المصدر: بياناتي الخاصة ({path})";
            }
            return $"⚠️ ملف البيانات الخاصة غير موجود: {path}";
        }
        else
        {
            int priorStep = step - 1;
            var output = PipelineStateManager.LoadStepOutput(_helperManager.PipelineStateFile, priorStep);
            if (output == null || string.IsNullOrWhiteSpace(output.Path) || (!File.Exists(output.Path) && !Directory.Exists(output.Path) && !GdbFeatureClassService.ItemExists(output.Path)))
            {
                if (step == 2)
                {
                    if (GdbFeatureClassService.DatasetExists(GdbPath, "Cad_Dataset"))
                    {
                        return $"المصدر: داتاسيت الخطوة 1 بقاعدة البيانات ({Path.Combine(GdbPath, "Cad_Dataset")})";
                    }
                    if (!string.IsNullOrWhiteSpace(CadDrawingPath) && File.Exists(CadDrawingPath))
                    {
                        return $"المصدر: ملف الكاد بالخطوة 1 ({Path.GetFileName(CadDrawingPath)})";
                    }
                }
                return $"⚠️ مخرجات الخطوة {priorStep} غير متوفرة بعد — يرجى تشغيل الخطوة {priorStep} أولاً أو اختيار 'بياناتي الخاصة'";
            }

            bool isEdited = PipelineStateManager.IsEditedSinceWritten(_helperManager.PipelineStateFile, priorStep, out var details);
            string editStatus = isEdited ? $"نعم ({details})" : "لا";
            return $"المصدر: مخرجات الخطوة {priorStep} المعتمدة ({output.Path}) — {output.FeatureCount:N0} معلماً — تم الكتابة: {output.WrittenAt:HH:mm:ss} — معدل منذ الكتابة: {editStatus}";
        }
    }

    public string ResolveOwnDataOrFail(int step)
    {
        string path = GetOwnDataPath(step);
        if (string.IsNullOrWhiteSpace(path))
        {
            string msg = $"يرجى تحديد مسار الملف لبياناتك الخاصة بالخطوة {step}. انقر فوق 'استعراض' لاختيار الطبقة أو الملف.";
            StatusMessage = msg;
            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(msg, $"بيانات خاصة - الخطوة {step}", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            throw new InvalidOperationException(msg);
        }

        if (!File.Exists(path) && !Directory.Exists(path) && !GdbFeatureClassService.ItemExists(path))
        {
            string msg = $"ملف البيانات الخاصة للخطوة {step} غير موجود في المسار: {path}";
            StatusMessage = msg;
            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(msg, $"بيانات خاصة - الخطوة {step}", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            throw new FileNotFoundException(msg, path);
        }

        return path;
    }

    public void ValidateInputOrFail(string path, GeometryType? expectedGeom = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("مسار البيانات المدخلة فارغ.");

        if (!File.Exists(path) && !Directory.Exists(path) && !GdbFeatureClassService.ItemExists(path))
            throw new FileNotFoundException($"مصدر البيانات غير موجود: {path}", path);

        if (expectedGeom == GeometryType.Polygon)
        {
            var (ok, reason) = GdbFeatureClassService.VerifyPolygonFeatureClass(path, 0, SelectedUtmZone);
            if (!ok)
            {
                throw new InvalidOperationException($"البيانات المدخلة غير صالحة كمضلعات: {reason}");
            }
        }
        else if (expectedGeom == GeometryType.Polyline)
        {
            var (ok, reason) = GdbFeatureClassService.VerifyPolylineFeatureClass(path, 0, SelectedUtmZone);
            if (!ok)
            {
                throw new InvalidOperationException($"البيانات المدخلة غير صالحة كخطوط: {reason}");
            }
        }
    }

    public async Task ValidateInputOrFailAsync(string path, GeometryType? expectedGeom = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("مسار البيانات المدخلة فارغ.");

        bool exists = File.Exists(path) || Directory.Exists(path) || await GdbFeatureClassService.ItemExistsAsync(path);
        if (!exists)
            throw new FileNotFoundException($"مصدر البيانات غير موجود: {path}", path);

        if (expectedGeom == GeometryType.Polygon)
        {
            var (ok, reason) = await GdbFeatureClassService.VerifyPolygonFeatureClassAsync(path, 0, SelectedUtmZone);
            if (!ok)
            {
                throw new InvalidOperationException($"البيانات المدخلة غير صالحة كمضلعات: {reason}");
            }
        }
        else if (expectedGeom == GeometryType.Polyline)
        {
            var (ok, reason) = await GdbFeatureClassService.VerifyPolylineFeatureClassAsync(path, 0, SelectedUtmZone);
            if (!ok)
            {
                throw new InvalidOperationException($"البيانات المدخلة غير صالحة كخطوط: {reason}");
            }
        }
    }

    private int ParseStepParam(object? param)
    {
        if (param is int i && i >= 1 && i <= 5) return i;
        if (param is string s && int.TryParse(s, out var parsed) && parsed >= 1 && parsed <= 5) return parsed;
        return CurrentStep;
    }

    private void BrowseOwnDataAction(object? param)
    {
        int step = ParseStepParam(param);
        var openDialog = new ArcGIS.Desktop.Catalog.OpenItemDialog
        {
            Title = $"اختر بياناتك الخاصة للخطوة {step}",
            InitialLocation = OutputDirectory,
            MultiSelect = false,
            Filter = "All Supported (*.shp;*.dxf;*.dwg;*.gdb)|*.shp;*.dxf;*.dwg;*.gdb|Shapefiles (*.shp)|*.shp|CAD (*.dwg;*.dxf)|*.dwg;*.dxf|All Files (*.*)|*.*"
        };

        if (openDialog.ShowDialog() == true && openDialog.Items.Any())
        {
            string selectedPath = openDialog.Items.First().Path;
            SetOwnDataPath(step, selectedPath);
            SetSource(step, StepSource.OwnData);
            StatusMessage = $"تم تعيين بياناتك الخاصة للخطوة {step}: {selectedPath}";
        }
    }

    private void OpenStepFolderAction(object? param)
    {
        int step = ParseStepParam(param);
        string? targetPath = null;
        if (GetSource(step) == StepSource.OwnData)
        {
            targetPath = GetOwnDataPath(step);
        }
        else
        {
            targetPath = PipelineStateManager.LoadStepOutput(_helperManager.PipelineStateFile, step - 1)?.Path;
        }

        string targetDir = OutputDirectory;
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            if (Directory.Exists(targetPath) && !targetPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                targetDir = targetPath;
            else
            {
                string? pDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(pDir) && pDir.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                    pDir = Path.GetDirectoryName(pDir);
                if (!string.IsNullOrWhiteSpace(pDir) && Directory.Exists(pDir))
                    targetDir = pDir;
            }
        }

        if (Directory.Exists(targetDir))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = targetDir, UseShellExecute = true });
            }
            catch { }
        }
    }

    private async void AddStepSourceToMapAction(object? param)
    {
        int step = ParseStepParam(param);
        string? targetPath = null;
        if (GetSource(step) == StepSource.OwnData)
        {
            targetPath = GetOwnDataPath(step);
        }
        else
        {
            targetPath = PipelineStateManager.LoadStepOutput(_helperManager.PipelineStateFile, step - 1)?.Path;
        }

        if (string.IsNullOrWhiteSpace(targetPath) || (!File.Exists(targetPath) && !Directory.Exists(targetPath) && !GdbFeatureClassService.ItemExists(targetPath)))
        {
            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                $"مصدر الخطوة {step} غير متوفر بعد على القرص لإضافته إلى الخريطة.",
                "معاينة المصدر في الخريطة",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            await QueuedTask.Run(() =>
            {
                var map = MapView.Active?.Map;
                if (map != null)
                {
                    if (targetPath.Contains(".gdb", StringComparison.OrdinalIgnoreCase))
                    {
                        int gdbIdx = targetPath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
                        string gdbPath = targetPath.Substring(0, gdbIdx + 4);
                        string itemName = targetPath.Substring(gdbIdx + 4).TrimStart('\\', '/');
                        if (GdbFeatureClassService.DatasetExists(gdbPath, itemName))
                        {
                            GdbFeatureClassService.AddCadDatasetToMap(map, gdbPath, itemName);
                        }
                        else
                        {
                            GdbFeatureClassService.AddOrRefreshMapLayer(map, gdbPath, itemName, $"{itemName} (Step {step} Source)");
                        }
                    }
                    else
                    {
                        LayerFactory.Instance.CreateLayer(new Uri(targetPath), map);
                    }
                }
            });
            StatusMessage = $"تمت إضافة مصدر الخطوة {step} إلى الخريطة للمعاينة والتعديل.";
        }
        catch (Exception ex)
        {
            ReportError($"فشل إضافة المصدر إلى الخريطة: {ex.Message}", "إضافة طبقة إلى الخريطة");
        }
    }

    public void RegisterStepOutput(int step, string path, int count, int wkid)
    {
        try
        {
            var record = new StepOutputRecord
            {
                Step = step,
                Path = path,
                FeatureCount = count,
                Wkid = wkid,
                WrittenAt = DateTime.Now,
                ContentStamp = PipelineStateManager.ComputeContentStamp(path),
                RunId = Guid.NewGuid().ToString("N")
            };
            PipelineStateManager.TrySaveStepOutput(_helperManager.PipelineStateFile, record, out var err);
            if (!string.IsNullOrEmpty(err))
            {
                System.Diagnostics.Debug.WriteLine($"[PipelineStateManager] Failed to persist Step {step} output: {err}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PipelineStateManager] Exception saving Step {step} output: {ex.Message}");
        }

        NotifyStepSourceProperties(step + 1);
    }

    public int CurrentStep => SelectedTabIndex == 0 ? 2 : (SelectedTabIndex + 1);

    public string WorkingSourceMode
    {
        get => GetSource(CurrentStep) == StepSource.OwnData ? "UserOwnData" : "FromPreviousStep";
        set => SetSource(CurrentStep, value == "UserOwnData" ? StepSource.OwnData : StepSource.PreviousStep);
    }

    public bool IsSourceFromLatestExport
    {
        get => false;
        set { /* Inert: Replaced by PreviousStep / OwnData */ }
    }

    public bool IsSourceFromSession
    {
        get => GetSource(CurrentStep) == StepSource.PreviousStep;
        set { if (value) SetSource(CurrentStep, StepSource.PreviousStep); }
    }

    public bool IsSourceFromCustom
    {
        get => GetSource(CurrentStep) == StepSource.OwnData;
        set { if (value) SetSource(CurrentStep, StepSource.OwnData); }
    }

    public string Stage3SourceMode
    {
        get => WorkingSourceMode;
        set => WorkingSourceMode = value;
    }

    public bool IsSchemaFromStandard
    {
        get => GetSource(3) == StepSource.PreviousStep;
        set { if (value) SetSource(3, StepSource.PreviousStep); }
    }

    public bool IsSchemaFromCustom
    {
        get => GetSource(3) == StepSource.OwnData;
        set { if (value) SetSource(3, StepSource.OwnData); }
    }

    private bool _showLatestExportBanner;
    public bool ShowLatestExportBanner
    {
        get => _showLatestExportBanner;
        set => SetProperty(ref _showLatestExportBanner, value);
    }

    private string _latestExportBannerMessage = string.Empty;
    public string LatestExportBannerMessage
    {
        get => _latestExportBannerMessage;
        set => SetProperty(ref _latestExportBannerMessage, value);
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
        set
        {
            if (SetProperty(ref _selectedPlotTextLayer, value))
            {
                OnSelectedPlotTextLayerChanged(value);
            }
        }
    }

    private void OnSelectedPlotTextLayerChanged(string? value)
    {
        string? clean = PlotIdExtractorService.CleanLayerName(value);
        if (string.IsNullOrWhiteSpace(clean)) return;

        var matching = CadLayers.FirstOrDefault(l => string.Equals(l.LayerName, clean, StringComparison.OrdinalIgnoreCase));
        if (matching != null && matching.Role != LayerRole.PlotText)
        {
            matching.Role = LayerRole.PlotText;
            matching.Category = "Text & Annotations";
        }
    }

    // Width Inference & Sensing (Section 3)
    private double _passageThresholdMeters = 10.0;
    public double PassageThresholdMeters
    {
        get => _passageThresholdMeters;
        set => SetProperty(ref _passageThresholdMeters, value);
    }

    private int _rayCount = 1;
    public int RayCount
    {
        get => _rayCount;
        set => SetProperty(ref _rayCount, value);
    }

    private double _minSharedSegmentLength = 0.5;
    public double MinSharedSegmentLength
    {
        get => _minSharedSegmentLength;
        set => SetProperty(ref _minSharedSegmentLength, value);
    }

    private MainUsageMode _mainUsageMode = MainUsageMode.InferFromText;
    public MainUsageMode MainUsageMode
    {
        get => _mainUsageMode;
        set => SetProperty(ref _mainUsageMode, value);
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

    private double _multiSegmentAngleDeviationDeg = 15.0;
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
                NotifyPropertyChanged(nameof(IsIgnoreTabSelected));
                NotifyPropertyChanged(nameof(IsRoadsReferencesTabSelected));
                NotifyPropertyChanged(nameof(IsAllSelectedInCurrentTab));
                NotifyPropertyChanged(nameof(SelectedLayersStatus));
            }
        }
    }

    public bool IsMainParcelsTabSelected => SelectedCadTab == "Main Parcels";
    public bool IsSubParcelsTabSelected => SelectedCadTab == "Sub-Parcels / Utilities";
    public bool IsTextAnnotationsTabSelected => SelectedCadTab == "Text & Annotations";
    public bool IsIgnoreTabSelected => SelectedCadTab == "Ignore Layers" || SelectedCadTab == "Roads & References";
    public bool IsRoadsReferencesTabSelected => IsIgnoreTabSelected;

    public int MainParcelsCount => CadLayers.Count(c => c.Category == "Main Parcels");
    public int SubParcelsCount => CadLayers.Count(c => c.Category == "Sub-Parcels / Utilities");
    public int TextAnnotationsCount => CadLayers.Count(c => c.Category == "Text & Annotations");
    public int IgnoreCount => CadLayers.Count(c => c.Category == "Ignore Layers" || c.Category == "Roads & References");
    public int RoadsReferencesCount => IgnoreCount;

    public int SelectedMainParcelsCount => CadLayers.Count(c => c.Category == "Main Parcels" && c.IsSelected);
    public int SelectedSubParcelsCount => CadLayers.Count(c => c.Category == "Sub-Parcels / Utilities" && c.IsSelected);
    public int SelectedTextAnnotationsCount => CadLayers.Count(c => c.Category == "Text & Annotations" && c.IsSelected);
    public int SelectedIgnoreCount => CadLayers.Count(c => (c.Category == "Ignore Layers" || c.Category == "Roads & References") && c.IsSelected);
    public int SelectedRoadsReferencesCount => SelectedIgnoreCount;

    public string MainParcelsBadge => MainParcelsCount > 0 ? $" ({SelectedMainParcelsCount}/{MainParcelsCount})" : " (0)";
    public string SubParcelsBadge => SubParcelsCount > 0 ? $" ({SelectedSubParcelsCount}/{SubParcelsCount})" : " (0)";
    public string TextAnnotationsBadge => TextAnnotationsCount > 0 ? $" ({SelectedTextAnnotationsCount}/{TextAnnotationsCount})" : " (0)";
    public string IgnoreBadge => IgnoreCount > 0 ? $" ({SelectedIgnoreCount}/{IgnoreCount})" : " (0)";
    public string RoadsReferencesBadge => IgnoreBadge;

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
        NotifyPropertyChanged(nameof(IgnoreCount));
        NotifyPropertyChanged(nameof(RoadsReferencesCount));
        NotifyPropertyChanged(nameof(SelectedMainParcelsCount));
        NotifyPropertyChanged(nameof(SelectedSubParcelsCount));
        NotifyPropertyChanged(nameof(SelectedTextAnnotationsCount));
        NotifyPropertyChanged(nameof(SelectedIgnoreCount));
        NotifyPropertyChanged(nameof(SelectedRoadsReferencesCount));
        NotifyPropertyChanged(nameof(MainParcelsBadge));
        NotifyPropertyChanged(nameof(SubParcelsBadge));
        NotifyPropertyChanged(nameof(TextAnnotationsBadge));
        NotifyPropertyChanged(nameof(IgnoreBadge));
        NotifyPropertyChanged(nameof(RoadsReferencesBadge));
        NotifyPropertyChanged(nameof(IsIgnoreTabSelected));
        NotifyPropertyChanged(nameof(IsRoadsReferencesTabSelected));
        NotifyPropertyChanged(nameof(IsAllSelectedInCurrentTab));
        NotifyPropertyChanged(nameof(SelectedLayersStatus));
        UpdatePlotTextLayersList();
    }

    public void UpdatePlotTextLayersList()
    {
        string current = SelectedPlotTextLayer;
        string? currentRaw = PlotIdExtractorService.CleanLayerName(current);

        var list = new List<string> { "(تلقائي من دور الطبقة - PlotText)" };
        foreach (var l in CadLayers)
        {
            string label = l.LayerName;
            if (l.Role == LayerRole.PlotText) label += " (★ PlotText)";
            list.Add(label);
        }

        AvailablePlotTextLayers.Clear();
        foreach (var item in list) AvailablePlotTextLayers.Add(item);

        string? match = AvailablePlotTextLayers.FirstOrDefault(x =>
            string.Equals(x, current, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(currentRaw) && string.Equals(PlotIdExtractorService.CleanLayerName(x), currentRaw, StringComparison.OrdinalIgnoreCase)));

        if (match != null)
        {
            _selectedPlotTextLayer = match;
            NotifyPropertyChanged(nameof(SelectedPlotTextLayer));
        }
        else
        {
            var ptLayer = CadLayers.FirstOrDefault(l => l.Role == LayerRole.PlotText);
            _selectedPlotTextLayer = ptLayer != null ? $"{ptLayer.LayerName} (★ PlotText)" : "(تلقائي من دور الطبقة - PlotText)";
            NotifyPropertyChanged(nameof(SelectedPlotTextLayer));
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
            if (sender is CadLayerInfo cl && cl.Role == LayerRole.PlotText)
            {
                string expectedLabel = $"{cl.LayerName} (★ PlotText)";
                if (!string.Equals(_selectedPlotTextLayer, expectedLabel, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(_selectedPlotTextLayer, cl.LayerName, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedPlotTextLayer = expectedLabel;
                    NotifyPropertyChanged(nameof(SelectedPlotTextLayer));
                }
            }
            UpdatePlotTextLayersList();
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

    public ICommand BrowseOwnDataCommand { get; }
    public ICommand OpenStepFolderCommand { get; }
    public ICommand AddStepSourceToMapCommand { get; }

    #endregion

    #region Execution Methods
    #region Execution Methods (Moved to dedicated partial class files per step)

    // Step 1: See Cad2GisDockpaneViewModel.Step1.cs (ExecuteStep1Async, ConvertCadToDatasetActionAsync)
    // Step 2: See Cad2GisDockpaneViewModel.Step2.cs (ExecuteStep2Async, ExportStep2ParcelsAsync)
    // Step 3: See Cad2GisDockpaneViewModel.Step3.cs (ExecuteStep3PopulateSchemaAsync, ExportStep3PopulatedParcelsAsync)
    // Step 4: See Cad2GisDockpaneViewModel.Step4.cs (ExecuteStep4BordersAsync, ExportStep4BordersAsync)
    // Step 5: See Cad2GisDockpaneViewModel.Step5.cs (ExecuteStep5ExportQaAsync)
    // Pipeline: See Cad2GisDockpaneViewModel.Pipeline.cs (ExecutePipelineAsync, ShowStepOutputOnMapAsync)

    #endregion

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
            MinSharedSegmentLength = p.MinSharedSegmentLength > 0 ? p.MinSharedSegmentLength : 0.5;
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
                ShowLatestExportBanner = true;
                LatestExportBannerMessage = "Latest export is available — use it for this step? (أحدث تصدير متاح — هل ترغب باستخدامه لهذه الخطوة؟)";
                StatusMessage = "تنبيه: أحدث تصدير متاح. يمكنك اختياره يدوياً من خيارات المصدر إذا رغبت في المتابعة منه.";
            }
            if (!string.IsNullOrWhiteSpace(p.GeometryMode))
            {
                SelectedGeometryMode = LayerGeometryModeService.NormalizeMode(p.GeometryMode);
            }
            if (p.LayerUsageMap != null && p.LayerUsageMap.Count > 0)
            {
                foreach (var cl in CadLayers)
                {
                    if (p.LayerUsageMap.TryGetValue(cl.LayerName, out var storedUsage))
                    {
                        if (string.IsNullOrWhiteSpace(storedUsage) || MunicipalLookups.IsOfficialUsageType(storedUsage))
                        {
                            cl.SelectedUsage = storedUsage;
                        }
                        else
                        {
                            cl.SelectedUsage = string.Empty;
                            StatusMessage = $"تحذير: كود الاستخدام '{storedUsage}' للطبقة '{cl.LayerName}' غير معتمد في ورقة Usage Types وتم مسحه.";
                        }
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(p.PlotNoTextLayer))
            {
                string? cleanPt = PlotIdExtractorService.CleanLayerName(p.PlotNoTextLayer);
                if (!string.IsNullOrWhiteSpace(cleanPt))
                {
                    var match = CadLayers.FirstOrDefault(l => string.Equals(l.LayerName, cleanPt, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        match.Role = LayerRole.PlotText;
                        match.Category = "Text & Annotations";
                    }
                    SelectedPlotTextLayer = cleanPt;
                }
            }
            if (!string.IsNullOrWhiteSpace(p.UsageTextLayer))
            {
                string? cleanUt = PlotIdExtractorService.CleanLayerName(p.UsageTextLayer);
                if (!string.IsNullOrWhiteSpace(cleanUt))
                {
                    var match = CadLayers.FirstOrDefault(l => string.Equals(l.LayerName, cleanUt, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        match.Role = LayerRole.UsageText;
                        match.Category = "Text & Annotations";
                    }
                }
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
            InitializePipelineState();
            RefreshLatestExportStatus();
            // Default working data source remains CAD Session / Dataset as requested
            WorkingSourceMode = "FromPreviousStep";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cad2Gis] InitializeLatestExportState error: {ex.Message}");
        }
    }

    private void InitializePipelineState()
    {
        try
        {
            var step3 = PipelineStateManager.LoadStepOutput(_helperManager.PipelineStateFile, 3);
            if (step3 != null && !string.IsNullOrWhiteSpace(step3.Path) &&
                (File.Exists(step3.Path) || GdbFeatureClassService.FeatureClassExists(step3.Path)))
            {
                Step3ExportedParcelsFeatureClassPath = step3.Path;
                Step3ExportedFeatureCount = step3.FeatureCount;
                Step3ExportedTimestamp = step3.Timestamp;
                System.Diagnostics.Debug.WriteLine($"[PipelineState] Restored Step 3 output from pipeline_state.json: Path='{step3.Path}', Count={step3.FeatureCount}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PipelineState] InitializePipelineState error: {ex.Message}");
        }
    }

    public void RegisterVerifiedStep3Output(string path, int count, int wkid)
    {
        Step3ExportedParcelsFeatureClassPath = path;
        Step3ExportedTimestamp = DateTime.Now;
        Step3ExportedFeatureCount = count;

        try
        {
            var record = new StepOutputRecord
            {
                Step = 3,
                Path = path,
                FeatureCount = count,
                Wkid = wkid,
                Timestamp = Step3ExportedTimestamp ?? DateTime.Now,
                RunId = Guid.NewGuid().ToString("N")
            };
            PipelineStateManager.TrySaveStepOutput(_helperManager.PipelineStateFile, record, out var err);
            if (!string.IsNullOrEmpty(err))
            {
                System.Diagnostics.Debug.WriteLine($"[PipelineStateManager] Failed to persist Step 3 output: {err}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PipelineStateManager] Exception saving Step 3 output: {ex.Message}");
        }

        System.Diagnostics.Debug.WriteLine($"[Audit Log] Step 3 Output registered & verified: Path='{Step3ExportedParcelsFeatureClassPath}', Count={Step3ExportedFeatureCount}, Time={Step3ExportedTimestamp:yyyy-MM-dd HH:mm:ss}");
    }

    private string? ResolveStep4UserOwnData()
    {
        if (!string.IsNullOrWhiteSpace(CustomSchemaDataPath) && (File.Exists(CustomSchemaDataPath) || GdbFeatureClassService.FeatureClassExists(CustomSchemaDataPath)))
        {
            return CustomSchemaDataPath;
        }
        else if (!string.IsNullOrWhiteSpace(SourceParcelShpPath) && File.Exists(SourceParcelShpPath))
        {
            return SourceParcelShpPath;
        }
        else if (SelectedMapLayer?.Layer != null)
        {
            return SelectedMapLayer.Name;
        }
        else
        {
            string err = "المصدر المخصص غير محدد أو غير موجود. يرجى اختيار ملف أو طبقة صراحة.";
            ReportError(err, "خطأ في مصدر الخطوة 4");
            return null;
        }
    }

    public string? ResolveStep3ApprovedPath()
    {
        if (!string.IsNullOrWhiteSpace(Step3ExportedParcelsFeatureClassPath) && IsDatasetPathValid(Step3ExportedParcelsFeatureClassPath))
        {
            return Step3ExportedParcelsFeatureClassPath;
        }

        try
        {
            var step3 = PipelineStateManager.LoadStepOutput(_helperManager.PipelineStateFile, 3);
            if (step3 != null && !string.IsNullOrWhiteSpace(step3.Path) && IsDatasetPathValid(step3.Path))
            {
                Step3ExportedParcelsFeatureClassPath = step3.Path;
                Step3ExportedFeatureCount = step3.FeatureCount;
                Step3ExportedTimestamp = step3.Timestamp;
                return step3.Path;
            }
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(GdbPath))
        {
            string candidate = Path.Combine(GdbPath, "PARCEL_Approved_Schema");
            if (IsDatasetPathValid(candidate))
            {
                Step3ExportedParcelsFeatureClassPath = candidate;
                return candidate;
            }
        }

        return null;
    }

    private static bool IsDatasetPathValid(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (File.Exists(path)) return true;
        int gdbIdx = path.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
        if (gdbIdx >= 0)
        {
            string gdbDir = path.Substring(0, gdbIdx + 4);
            if (Directory.Exists(gdbDir))
            {
                try
                {
                    return GdbFeatureClassService.ItemExists(path);
                }
                catch
                {
                    return true;
                }
            }
        }
        return false;
    }

    private string? ResolveStep4LatestExportOrFail()
    {
        var state = ExportStateManager.LoadLatestExport(_helperManager.LastExportStateFile);
        bool validState = state != null && !string.IsNullOrWhiteSpace(state.ParcelPath) && IsDatasetPathValid(state.ParcelPath);

        if (validState && (!Step3ExportedTimestamp.HasValue || state!.Timestamp >= Step3ExportedTimestamp.Value))
        {
            IsWorkingFromMissing = false;
            return state!.ParcelPath;
        }

        string? step3Path = ResolveStep3ApprovedPath();
        if (!string.IsNullOrEmpty(step3Path) && IsDatasetPathValid(step3Path))
        {
            IsWorkingFromMissing = false;
            return step3Path;
        }

        IsWorkingFromMissing = true;
        WorkingFromMissingWarning = $"Latest export dataset not found. Please export Step 3 ('PARCEL_Approved_Schema') or use session data.";
        ReportError(WorkingFromMissingWarning, "Missing Latest Export");
        return null;
    }

    private string? ResolveStep4VerifiedStep3OutputOrFail()
    {
        string? targetPath = ResolveStep3ApprovedPath();

        if (string.IsNullOrWhiteSpace(targetPath) || !IsDatasetPathValid(targetPath))
        {
            string missingMsg = "الخطوة 4: لم يتم العثور على مخرجات معتمدة من الخطوة 3 (Export Step 3).\n" +
                $"المسار المطلوب: '{(targetPath ?? "غير محدد")}'.\n" +
                "يرجى تصدير الخطوة 3 أولاً (Export Step 3) لإنتاج طبقة البارسلات المعتمدة، أو النقر على 'تغيير / استعراض' لاختيار مصدر بديل صراحة.";
            StatusMessage = missingMsg;
            ReportError(missingMsg, "مصدر الخطوة 4 مفقود");
            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(missingMsg, "تنبيه مصدر الخطوة 4", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return null;
        }

        var (ok, reason) = GdbFeatureClassService.VerifyPolygonFeatureClass(targetPath, 0, SelectedUtmZone);
        if (!ok)
        {
            string verifyErr = $"الخطوة 4: فشل التحقق من صحة مخرجات الخطوة 3: {reason}";
            ReportError(verifyErr, "خطأ في التحقق من مخرجات الخطوة 3");
            return null;
        }

        if (Step3ExportedFeatureCount > 0)
        {
            var (cntOk, _) = GdbFeatureClassService.VerifyPolygonFeatureClass(targetPath, Step3ExportedFeatureCount, SelectedUtmZone);
            if (!cntOk)
            {
                System.Diagnostics.Debug.WriteLine($"[Step 4 Warning] Verified output count differs from recorded Step 3 count ({Step3ExportedFeatureCount}).");
            }
        }

        return targetPath;
    }

    public void RefreshLatestExportStatus()
    {
        try
        {
            var state = ExportStateManager.LoadLatestExport(_helperManager.LastExportStateFile);
            string? step3Path = ResolveStep3ApprovedPath();

            if (CurrentStep == 4)
            {
                if (!string.IsNullOrEmpty(step3Path) &&
                    (state == null || string.IsNullOrEmpty(state.ParcelPath) ||
                     (Step3ExportedTimestamp.HasValue && Step3ExportedTimestamp.Value >= state.Timestamp)))
                {
                    HasLatestExport = true;
                    LatestExportPath = step3Path;
                    LatestExportTimestamp = Step3ExportedTimestamp.HasValue
                        ? Step3ExportedTimestamp.Value.ToString("yyyy-MM-dd HH:mm:ss")
                        : (File.Exists(step3Path) ? File.GetLastWriteTime(step3Path).ToString("yyyy-MM-dd HH:mm:ss") : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    IsWorkingFromMissing = false;
                    IsEditedAfterExport = false;
                    ShowEditedAfterExportBanner = false;
                }
                else if (state != null && !string.IsNullOrEmpty(state.ParcelPath) && IsDatasetPathValid(state.ParcelPath))
                {
                    HasLatestExport = true;
                    LatestExportPath = state.ParcelPath;
                    LatestExportTimestamp = state.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

                    ExportHistoryItems.Clear();
                    if (state.History != null)
                    {
                        foreach (var h in state.History)
                            ExportHistoryItems.Add(h);
                    }

                    bool parcelChanged = state.Datasets.TryGetValue("PARCEL", out var pDs) &&
                        ExportStateManager.HasPhysicalDatasetChanged(state.ParcelPath, pDs);
                    IsEditedAfterExport = parcelChanged;
                    ShowEditedAfterExportBanner = IsEditedAfterExport;
                    IsWorkingFromMissing = false;
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
            }
            else
            {
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

                    if (!string.IsNullOrEmpty(state.ParcelPath) && IsDatasetPathValid(state.ParcelPath))
                    {
                        IsWorkingFromMissing = false;
                        bool parcelChanged = state.Datasets.TryGetValue("PARCEL", out var pDs) &&
                            ExportStateManager.HasPhysicalDatasetChanged(state.ParcelPath, pDs);
                        bool borderChanged = !string.IsNullOrEmpty(state.BorderPath) &&
                            IsDatasetPathValid(state.BorderPath) &&
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
        if (WorkingSourceMode == "LatestExport")
        {
            if (HasLatestExport && !string.IsNullOrEmpty(LatestExportPath))
            {
                string edited = IsEditedAfterExport ? "Yes" : "No";
                WorkingFromSummary = $"Working from: {LatestExportPath} — exported {LatestExportTimestamp} — edited after export: {edited}";
            }
            else
            {
                string? step3Path = ResolveStep3ApprovedPath();
                if (!string.IsNullOrEmpty(step3Path))
                {
                    WorkingFromSummary = $"Working from: {step3Path} — (Step 3 Approved Schema)";
                }
                else
                {
                    WorkingFromSummary = "Working from: Latest Export (No prior export found — please export Step 3 first or use Session Data)";
                }
            }
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
        else // FromPreviousStep (جلسة العمل الحالية - Session data)
        {
            if (CurrentStep == 4)
            {
                string? step3Path = ResolveStep3ApprovedPath();
                if (!string.IsNullOrEmpty(step3Path))
                {
                    int cnt = Step3ExportedFeatureCount > 0 ? Step3ExportedFeatureCount : _parcels.Count;
                    WorkingFromSummary = $"Working from: {step3Path} — ({cnt} Approved Parcels in Session)";
                }
                else if (_parcels.Count > 0)
                {
                    WorkingFromSummary = $"Working from: Current Session Approved Parcels ({_parcels.Count} Parcels in Memory)";
                }
                else
                {
                    WorkingFromSummary = "Working from: Current CAD Session Data (Step 3 Approved Parcels)";
                }
            }
            else if (CurrentStep == 3)
            {
                if (_parcels.Count > 0)
                {
                    WorkingFromSummary = $"Working from: Current Session Extracted Parcels ({_parcels.Count} Parcels from Step 2)";
                }
                else
                {
                    WorkingFromSummary = "Working from: Current CAD Session Data (Step 2 Parcels)";
                }
            }
            else if (CurrentStep == 2)
            {
                if (GdbFeatureClassService.DatasetExists(GdbPath, "Cad_Dataset"))
                {
                    WorkingFromSummary = $"Working from: GDB Feature Dataset 'Cad_Dataset' ({Path.GetFileName(GdbPath)}) — Editable Map Layers";
                }
                else
                {
                    WorkingFromSummary = "Working from: Current CAD Session Data (Step 1 CAD Layers)";
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
                    WorkingFromSummary = "Working from: Current CAD Session Data";
                }
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
                LatestExportPath = path;
                HasLatestExport = true;
                WorkingSourceMode = "LatestExport";
                LatestExportTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                IsEditedAfterExport = false;
                ShowEditedAfterExportBanner = false;
                IsWorkingFromMissing = false;
                UpdateWorkingFromSummary();
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
            LatestExportPath = dlg.FileName;
            HasLatestExport = true;
            WorkingSourceMode = "LatestExport";
            LatestExportTimestamp = File.GetLastWriteTime(dlg.FileName).ToString("yyyy-MM-dd HH:mm:ss");
            IsEditedAfterExport = false;
            ShowEditedAfterExportBanner = false;
            IsWorkingFromMissing = false;
            UpdateWorkingFromSummary();
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

            // Update the Step 3 output (PARCEL_Approved_Schema) if it already exists: it is the single Step 3 output
            if (_parcels.Count > 0 && Directory.Exists(GdbPath))
            {
                string fcParcelName = IsDemoProject ? "DEMO_PARCEL_Approved_Schema" : "PARCEL_Approved_Schema";
                try
                {
                    _ = QueuedTask.Run(() =>
                    {
                        string fcFullPath = Path.Combine(GdbPath, fcParcelName);
                        if (!GdbFeatureClassService.FeatureClassExists(fcFullPath)) return;

                        bool wasUntouched = !PipelineStateManager.IsEditedSinceWritten(_helperManager.PipelineStateFile, 3, out _);
                        GdbFeatureClassService.UpdateParcelAttributesInFeatureClass(GdbPath, fcParcelName, _parcels);
                        // The tool itself changed the data: record the new state so it is not reported as a manual edit.
                        if (wasUntouched) RegisterStepOutput(3, fcFullPath, _parcels.Count, SelectedUtmZone);
                        GdbFeatureClassService.AddOrRefreshMapLayer(MapView.Active?.Map, GdbPath, fcParcelName, fcParcelName);
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

    // Note: ConvertCadToDatasetActionAsync moved to Cad2GisDockpaneViewModel.Step1.cs

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

            // Streets & Corridors (Mapped to Sub-Parcels / Utilities)
            new CadLayerInfo { LayerName = "boundary roads", EntityCount = 13, GeometryType = "Line", Color = "#94A3B8", RawCadText = "STR_30", DecodedArabicText = "شارع عرض 30 متر", Role = LayerRole.Street, Category = "Sub-Parcels / Utilities", IsSelected = true },
            new CadLayerInfo { LayerName = "pathes", EntityCount = 104, GeometryType = "Line", Color = "#94A3B8", RawCadText = "PATH_6", DecodedArabicText = "ممر مشاة عرض 6 متر", Role = LayerRole.Street, Category = "Sub-Parcels / Utilities", IsSelected = true },

            // Ignore Layers (All set to Ignore / IsSelected = false)
            new CadLayerInfo { LayerName = "water canals", EntityCount = 153, GeometryType = "Polygon", Color = "#38BDF8", RawCadText = "CANAL", DecodedArabicText = "قناة مائية", Role = LayerRole.Ignore, Category = "Ignore Layers", IsSelected = false },
            new CadLayerInfo { LayerName = "التنظيم", EntityCount = 2, GeometryType = "Line", Color = "#94A3B8", RawCadText = "SETBACK", DecodedArabicText = "خط تنظيم", Role = LayerRole.Ignore, Category = "Ignore Layers", IsSelected = false },
            new CadLayerInfo { LayerName = "Deed", EntityCount = 34, GeometryType = "Line", Color = "#94A3B8", RawCadText = "DEED", DecodedArabicText = "صك ملكية", Role = LayerRole.Ignore, Category = "Ignore Layers", IsSelected = false },
            new CadLayerInfo { LayerName = "0", EntityCount = 6347, GeometryType = "Polygon", Color = "#64748B", RawCadText = "0", DecodedArabicText = "طبقة افتراضية", Role = LayerRole.Ignore, Category = "Ignore Layers", IsSelected = false }
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
