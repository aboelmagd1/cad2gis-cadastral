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

/// <summary>
/// Partial class for Cad2GisDockpaneViewModel - STEP 1
/// Handles CAD reading, layer scanning, coordinate reference system, and feature dataset conversion.
/// </summary>
public partial class Cad2GisDockpaneViewModel
{
    public async Task ExecuteStep1Async(CancellationToken token = default, bool isPartOfPipeline = false)
    {
        if (!isPartOfPipeline)
        {
            IsBusy = true;
            token = StartNewOperationToken();
        }
        ReportProgress(5, "الخطوة 1: فحص وتحويل الكاد إلى داتاسيت", "جاري التحقق من مصدر الرسم ونطاق الإسقاط...", phase: "المرحلة 1 من 5");

        try
        {
            token.ThrowIfCancellationRequested();
            // Ensure Export_Output directory exists!
            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                string baseDir = ArcGIS.Desktop.Core.Project.Current?.HomeFolderPath ?? ResolveBaseFolder();
                OutputDirectory = Path.Combine(baseDir, "Export_Output");
            }
            if (!Directory.Exists(OutputDirectory))
            {
                try { Directory.CreateDirectory(OutputDirectory); } catch { }
            }
            if (string.IsNullOrWhiteSpace(GdbPath) || !GdbPath.Contains(".gdb", StringComparison.OrdinalIgnoreCase))
            {
                GdbPath = GdbFeatureClassService.GetEffectiveGdbPath(OutputDirectory);
            }

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
                                if (gt.Equals("Polygon", StringComparison.OrdinalIgnoreCase) && role == LayerRole.Ignore && (cat == "Ignore Layers" || cat == "Roads & References"))
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
                                    IsSelected = (cat != "Ignore Layers" && cat != "Roads & References" && role != LayerRole.Ignore)
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

            string step1Output = Path.Combine(GdbPath, "Cad_Dataset");
            if (!Directory.Exists(step1Output) && !GdbFeatureClassService.DatasetExists(GdbPath, "Cad_Dataset") && !string.IsNullOrWhiteSpace(CadDrawingPath) && File.Exists(CadDrawingPath))
            {
                step1Output = CadDrawingPath;
            }
            RegisterStepOutput(1, step1Output, CadLayers.Count, SelectedUtmZone);
        }
        catch (OperationCanceledException)
        {
            ReportProgress(0, "تم إلغاء الخطوة 1", "تم إيقاف فحص واستيراد الكاد بناءً على طلبك.", status: "تم إلغاء الخطوة 1 بواسطة المستخدم.", phase: "ملغاة");
            if (isPartOfPipeline) throw;
        }
        catch (Exception ex)
        {
            ReportError($"Step 1 Error: {ex.Message}", "خطأ في الخطوة 1");
        }
        finally
        {
            if (!isPartOfPipeline) IsBusy = false;
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
                    var userRoleMap = CadLayers.ToDictionary(l => l.LayerName, l => l.Role, StringComparer.OrdinalIgnoreCase);
                    var userCatMap = CadLayers.ToDictionary(l => l.LayerName, l => l.Category, StringComparer.OrdinalIgnoreCase);
                    var userSelectedMap = CadLayers.ToDictionary(l => l.LayerName, l => l.IsSelected, StringComparer.OrdinalIgnoreCase);
                    var userUsageMap = CadLayers.ToDictionary(l => l.LayerName, l => l.SelectedUsage, StringComparer.OrdinalIgnoreCase);
                    string savedPlotTextLayer = SelectedPlotTextLayer;

                    CadLayers.Clear();
                    foreach (var l in datasetLayers)
                    {
                        if (userRoleMap.TryGetValue(l.LayerName, out var role)) l.Role = role;
                        if (userCatMap.TryGetValue(l.LayerName, out var cat)) l.Category = cat;
                        if (userSelectedMap.TryGetValue(l.LayerName, out var isSel)) l.IsSelected = isSel;
                        if (userUsageMap.TryGetValue(l.LayerName, out var usage)) l.SelectedUsage = usage;

                        AddCadLayerWithEvents(l);
                    }

                    // Match with Expected Layers if available (apply to layers not already customized by user)
                    if (File.Exists(ExpectedLayersExcelPath))
                    {
                        try
                        {
                            var rules = ExpectedLayersService.ReadExpectedLayersExcel(ExpectedLayersExcelPath);
                            var matchReport = ExpectedLayersService.MatchCadLayers(datasetLayers, rules, SelectedProfile);
                            foreach (var matched in matchReport.CadLayersMatched)
                            {
                                var target = CadLayers.FirstOrDefault(c => c.LayerName.Equals(matched.CadLayerName, StringComparison.OrdinalIgnoreCase));
                                if (target != null && !userRoleMap.ContainsKey(target.LayerName))
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

                    UpdatePlotTextLayersList();
                    if (!string.IsNullOrWhiteSpace(savedPlotTextLayer) && AvailablePlotTextLayers.Contains(savedPlotTextLayer))
                    {
                        SelectedPlotTextLayer = savedPlotTextLayer;
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

    public ObservableCollection<string> LayerUsageOptions { get; } = new();

    private ICommand? _refreshUsageTypesCommand;
    public ICommand RefreshUsageTypesCommand =>
        _refreshUsageTypesCommand ??= new RelayCommand(() => RefreshLayerUsageOptions(notifyUser: true));

    public void RefreshLayerUsageOptions(bool notifyUser = false)
    {
        // 1. Re-read official Usage Types from Required Fields Excel
        if (File.Exists(_helperManager.RequiredFieldsExcel))
        {
            MunicipalLookups.LoadUsageTypesFromRequiredFieldsExcel(_helperManager.RequiredFieldsExcel);
        }

        // 2. Re-populate LayerUsageOptions collection
        LayerUsageOptions.Clear();
        LayerUsageOptions.Add(""); // Empty / Default: no layer-level USAGE

        foreach (var kvp in MunicipalLookups.OfficialUsageTypes)
        {
            LayerUsageOptions.Add($"{kvp.Key} — {kvp.Value}");
        }

        // 3. Validate existing SelectedUsage values on CadLayers
        int clearedCount = 0;
        foreach (var cl in CadLayers)
        {
            if (string.IsNullOrWhiteSpace(cl.SelectedUsage)) continue;
            string code = cl.SelectedUsage;
            int dashIdx = code.IndexOf(" — ");
            if (dashIdx > 0) code = code.Substring(0, dashIdx).Trim();
            code = code.Trim();

            if (!MunicipalLookups.IsOfficialUsageType(code))
            {
                System.Diagnostics.Debug.WriteLine($"[Layer Usage Warning] Stored usage code '{cl.SelectedUsage}' for layer '{cl.LayerName}' is no longer in 'Usage Types' sheet. Cleared.");
                cl.SelectedUsage = string.Empty;
                clearedCount++;
            }
        }

        if (notifyUser)
        {
            string msg = $"تم تحديث قائمة أنواع الاستخدام ({MunicipalLookups.OfficialUsageTypes.Count} نوع معتمد).";
            if (clearedCount > 0)
                msg += $"\nتم مسح {clearedCount} قيمة غير مطابقة لورقة الاستخدامات.";
            StatusMessage = msg;
        }
    }
}

