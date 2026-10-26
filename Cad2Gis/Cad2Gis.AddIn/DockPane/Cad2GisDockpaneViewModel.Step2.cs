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
/// Partial class for Cad2GisDockpaneViewModel - STEP 2
/// Handles parcel polygonization from CAD layers/lines, 2D planar cleaning, and Cad_Constructed_Parcels output.
/// </summary>
public partial class Cad2GisDockpaneViewModel
{
    public async Task ExecuteStep2Async(bool isPartOfPipeline = false)
    {
        if (!isPartOfPipeline) IsBusy = true;
        ReportProgress(5, "الخطوة 2: استخراج وبلمرة مضلعات البارسلات", "فحص مصدر البيانات ونطاق الطبقات...", phase: "المرحلة 2 من 5");

        try
        {
            // Save any pending user map edits in ArcGIS Pro to ensure they are picked up in the GDB
            await GdbFeatureClassService.SaveMapEditsAsync();
            // Step 2 Input Verification (Previous Step 1 or Own Data)
            string step2Input;
            if (GetSource(2) == StepSource.OwnData)
            {
                step2Input = ResolveOwnDataOrFail(2);
                await ValidateInputOrFailAsync(step2Input);
            }
            else
            {
                var step1Rec = PipelineStateManager.GetVerifiedOutput(_helperManager.PipelineStateFile, step: 1);
                if (step1Rec != null && !string.IsNullOrWhiteSpace(step1Rec.Path) && (File.Exists(step1Rec.Path) || Directory.Exists(step1Rec.Path) || await GdbFeatureClassService.ItemExistsAsync(step1Rec.Path)))
                {
                    step2Input = step1Rec.Path;
                }
                else if (await GdbFeatureClassService.DatasetExistsAsync(GdbPath, "Cad_Dataset"))
                {
                    step2Input = Path.Combine(GdbPath, "Cad_Dataset");
                    RegisterStepOutput(1, step2Input, CadLayers.Count > 0 ? CadLayers.Count : 1, SelectedUtmZone);
                }
                else if (!string.IsNullOrWhiteSpace(CadDrawingPath) && File.Exists(CadDrawingPath))
                {
                    step2Input = CadDrawingPath;
                    RegisterStepOutput(1, step2Input, CadLayers.Count > 0 ? CadLayers.Count : 1, SelectedUtmZone);
                }
                else
                {
                    step2Input = PipelineStateManager.GetVerifiedOutputOrFail(_helperManager.PipelineStateFile, step: 1).Path;
                }
                await ValidateInputOrFailAsync(step2Input);
            }
            System.Diagnostics.Debug.WriteLine($"[Step2] option={GetSource(2)} source={step2Input}");

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
                    await GdbFeatureClassService.ConvertCadToDatasetAsync(CadDrawingPath, GdbPath, "Cad_Dataset", SelectedUtmZone);
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
                                if (cat == "Ignore Layers" || cat == "Roads & References")
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
            var constructedParcelsWithClass = new List<(Polygon Polygon, ParcelClass Class, string GroupName)>();
            var constructedParcelsWithAttribution = new List<(Polygon Polygon, LayerAttributionResult Attribution)>();
            bool isLineOnlyMode = !LayerGeometryModeService.ShouldIngestPolygons(SelectedGeometryMode);
            FeatureToPolygonReport? f2pReport = null;

            if (isLineMode)
            {
                if (!await GdbFeatureClassService.DatasetExistsAsync(GdbPath, "Cad_Dataset"))
                {
                    string err = "قاعدة البيانات الجغرافية لا تحتوي على داتاسيت 'Cad_Dataset' المطلوبة لتحويل الخطوط. يرجى تشغيل الخطوة 1 أولاً.";
                    StatusMessage = err;
                    ReportError(err, "خطأ في مصدر الخطوة 2");
                    ResetProgress(err, "توقف تحويل الخطوط");
                    return;
                }

                bool isLineWithDataLayer = LayerGeometryModeService.IsLineWithDataLayer(SelectedGeometryMode);
                if (isLineWithDataLayer)
                {
                    var allEligibleLayers = CadLayers.Where(l => l.IsSelected && l.Category != "Ignore Layers" && l.Category != "Roads & References" && l.Category != "Text & Annotations")
                        .Select(l => l.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    if (allEligibleLayers.Count == 0)
                    {
                        string err = "الخطوة 2: لم يتم اختيار أي طبقات مؤهلة في الخطوة 1 لبناء المضلعات في نمط الخطوط مع طبقة البيانات.";
                        StatusMessage = err;
                        ReportError(err, "خطأ في اختيار الطبقات");
                        ResetProgress(err, "توقف تحويل الخطوط");
                        return;
                    }

                    var subLayerNames = new HashSet<string>(
                        CadLayers.Where(l => l.Category == "Sub-Parcels / Utilities" || l.Role == LayerRole.Street).Select(l => l.LayerName),
                        StringComparer.OrdinalIgnoreCase);

                    var layerUsageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var layerUsageTextMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var l in CadLayers)
                    {
                        string? uCode = !string.IsNullOrWhiteSpace(l.SelectedUsage)
                            ? MunicipalLookups.ExtractUsageCode(l.SelectedUsage) ?? l.SelectedUsage.Trim()
                            : null;

                        if (string.IsNullOrWhiteSpace(uCode))
                        {
                            if (subLayerNames.Contains(l.LayerName))
                            {
                                uCode = "UTL";
                            }
                            else
                            {
                                uCode = MunicipalLookups.ClassifyUsageFromText(l.LayerName) ??
                                        MunicipalLookups.ClassifyUsageFromText(l.DecodedArabicText);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(uCode) && MunicipalLookups.IsOfficialUsageType(uCode))
                        {
                            layerUsageMap[l.LayerName] = uCode;
                        }

                        if (!string.IsNullOrWhiteSpace(l.DecodedArabicText))
                        {
                            layerUsageTextMap[l.LayerName] = l.DecodedArabicText;
                        }
                    }

                    ReportProgress(65, "الخطوة 2: تحويل الخطوط مع طبقة البيانات (Line with data layer)", $"تشغيل SplitLine و FeatureToPolygon لـ {allEligibleLayers.Count} طبقة...", phase: "المرحلة 2 من 5");

                    var rep = await GdbFeatureClassService.RunLineWithDataLayerReportAsync(
                        GdbPath,
                        "Cad_Dataset",
                        allEligibleLayers,
                        subLayerNames,
                        layerUsageMap,
                        layerUsageTextMap,
                        SnapToleranceMeters,
                        SelectedUtmZone,
                        "Cad_Constructed_Parcels");

                    if (!rep.Success)
                    {
                        string failMsg = $"الخطوة 2: فشل بناء المضلعات في نمط الخطوط مع طبقة البيانات:\n{rep.DiagnosticMessage}";
                        StatusMessage = failMsg;
                        ReportError(failMsg, "فشل تحويل خطوط مع طبقة البيانات");
                        ResetProgress(failMsg, "توقف تحويل الخطوط");
                        return;
                    }

                    foreach (var item in rep.ConstructedParcels)
                    {
                        constructedParcelsWithAttribution.Add(item);
                    }

                    ReportProgress(70, "الخطوة 2: نتيجة نمط الخطوط مع طبقة البيانات", rep.DiagnosticMessage, phase: "المرحلة 2 من 5");
                }
                else
                {
                    // Section 6: Class assignment (MAIN / SUB) without losing the layer name
                    // Run once per class group: Main Parcels tab layers, then Sub-Parcels tab layers.
                    var mainLayers = CadLayers.Where(l => (l.Category == "Main Parcels" || l.Role == LayerRole.Boundary) && l.IsSelected)
                        .Select(l => l.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    var subLayers = CadLayers.Where(l => (l.Category == "Sub-Parcels / Utilities" || l.Role == LayerRole.Street) && l.IsSelected)
                        .Select(l => l.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    var otherSelected = CadLayers.Where(l => l.IsSelected && l.Category != "Ignore Layers" && l.Category != "Roads & References" && l.Category != "Text & Annotations"
                        && !mainLayers.Contains(l.LayerName, StringComparer.OrdinalIgnoreCase)
                        && !subLayers.Contains(l.LayerName, StringComparer.OrdinalIgnoreCase))
                        .Select(l => l.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    if (mainLayers.Count == 0 && subLayers.Count == 0 && otherSelected.Count > 0)
                    {
                        mainLayers.AddRange(otherSelected);
                    }

                    var groupRuns = new List<(string GroupName, string OutFcName, ParcelClass Class, List<string> Layers)>();
                    if (mainLayers.Count > 0)
                        groupRuns.Add(("Main Parcels", "temp_f2p_main", ParcelClass.Main, mainLayers));
                    if (subLayers.Count > 0)
                        groupRuns.Add(("Sub-Parcels", "temp_f2p_sub", ParcelClass.Sub, subLayers));

                    if (groupRuns.Count == 0)
                    {
                        string err = "الخطوة 2: لم يتم اختيار أي طبقات مؤهلة في الخطوة 1 لبناء المضلعات من الخطوط.";
                        StatusMessage = err;
                        ReportError(err, "خطأ في اختيار الطبقات");
                        ResetProgress(err, "توقف تحويل الخطوط");
                        return;
                    }

                    foreach (var grp in groupRuns)
                    {
                        ReportProgress(65, $"الخطوة 2: تحويل خطوط {grp.GroupName} إلى مضلعات (Feature to Polygon)", $"تشغيل أداة Feature to Polygon لمجموعة {grp.GroupName} ({grp.Layers.Count} طبقة)...", phase: "المرحلة 2 من 5");

                        var rep = await GdbFeatureClassService.RunFeatureToPolygonReportAsync(
                            GdbPath,
                            "Cad_Dataset",
                            grp.Layers,
                            SnapToleranceMeters,
                            SelectedUtmZone,
                            grp.OutFcName,
                            grp.Class);

                        f2pReport = rep;

                        if (!rep.Success)
                        {
                            string failMsg = $"الخطوة 2: فشل بناء المضلعات لمجموعة {grp.GroupName}:\n{rep.DiagnosticMessage}";
                            StatusMessage = failMsg;
                            ReportError(failMsg, $"فشل تحويل خطوط {grp.GroupName}");
                            ResetProgress(failMsg, "توقف تحويل الخطوط");
                            return; // ANY FAILURE IN STEPS 1-3 STOPS THE STEP WITH A CLEAR MESSAGE. NO FALLBACK.
                        }

                        foreach (var (poly, cls) in rep.ConstructedClassParcels)
                        {
                            constructedParcelsWithClass.Add((poly, cls, grp.GroupName));
                        }
                        if (!string.IsNullOrWhiteSpace(rep.DiagnosticMessage))
                        {
                            ReportProgress(70, $"الخطوة 2: نتيجة {grp.GroupName}", rep.DiagnosticMessage, phase: "المرحلة 2 من 5");
                        }

                        // Delete intermediate temporary feature class from GDB immediately
                        try
                        {
                            var delArgs = Geoprocessing.MakeValueArray(Path.Combine(GdbPath, grp.OutFcName));
                            await Geoprocessing.ExecuteToolAsync("management.Delete", delArgs);
                        }
                        catch { }
                    }
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

                            string layerUsageCode = "";
                            if (lyrInfo != null && !string.IsNullOrWhiteSpace(lyrInfo.SelectedUsage))
                            {
                                layerUsageCode = MunicipalLookups.ExtractUsageCode(lyrInfo.SelectedUsage) ?? lyrInfo.SelectedUsage.Trim();
                            }
                            else if (!isMain)
                            {
                                layerUsageCode = "UTL";
                            }
                            else if (lyrInfo != null)
                            {
                                string? candidate = MunicipalLookups.ClassifyUsageFromText(lyrInfo.LayerName) ??
                                                    MunicipalLookups.ClassifyUsageFromText(lyrInfo.DecodedArabicText);
                                if (!string.IsNullOrWhiteSpace(candidate) && MunicipalLookups.IsOfficialUsageType(candidate))
                                {
                                    layerUsageCode = candidate;
                                }
                            }

                            string plotText = item.RawText;
                            long newId = _parcels.Count + 1;
                            string initialPlotId = !string.IsNullOrWhiteSpace(plotText) ? plotText : $"P{newId}";

                            var parcel = new ParcelRecord
                            {
                                InternalId = newId,
                                PlotId = initialPlotId,
                                Class = isMain ? ParcelClass.Main : ParcelClass.Sub,
                                AreaSqm = metrics.areaSqm,
                                LayerUsage = layerUsageCode,
                                Usage = layerUsageCode,
                                UsageText = lyrInfo?.DecodedArabicText ?? "",
                                Remarks = string.Empty,
                                Provenance = $"From Dataset layer [{item.LayerName}]"
                            };
                            _parcels.Add(parcel);
                            _parcelGeometries[parcel.InternalId] = cleaned;
                        }
                    }

                    // B.1 Integrate Polygons Constructed in "Line with data layer" mode
                    if (constructedParcelsWithAttribution.Count > 0)
                    {
                        foreach (var (cpPoly, cpAttr) in constructedParcelsWithAttribution)
                        {
                            var cleaned = ParcelPolygonizer.Clean2DPlanar(cpPoly);
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

                            long newId = _parcels.Count + 1;
                            var parcel = new ParcelRecord
                            {
                                InternalId = newId,
                                PlotId = $"P{newId}",
                                Class = cpAttr.AssignedClass,
                                AreaSqm = metrics.areaSqm,
                                LayerUsage = cpAttr.AssignedUsage,
                                Usage = cpAttr.AssignedUsage,
                                UsageText = !string.IsNullOrWhiteSpace(cpAttr.UsageText) ? cpAttr.UsageText : (cpAttr.AssignedUsage ?? ""),
                                Remarks = string.Empty,
                                Provenance = $"Line with data layer: {cpAttr.GetSummary()}"
                            };
                            _parcels.Add(parcel);
                            _parcelGeometries[parcel.InternalId] = cleaned;
                        }
                    }

                    // B.2 Integrate Polygons Constructed from Lines via FeatureToPolygon (Regular Lines mode)
                    if (constructedParcelsWithClass.Count > 0)
                    {
                        foreach (var (cpPoly, cpClass, cpGrp) in constructedParcelsWithClass)
                        {
                            var cleaned = ParcelPolygonizer.Clean2DPlanar(cpPoly);
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

                            long newId = _parcels.Count + 1;
                            var parcel = new ParcelRecord
                            {
                                InternalId = newId,
                                PlotId = $"P{newId}",
                                Class = cpClass,
                                AreaSqm = metrics.areaSqm,
                                Usage = cpClass == ParcelClass.Main ? "قطعة رئيسية" : "خدمات / فرعية",
                                Remarks = string.Empty,
                                Provenance = $"From FeatureToPolygon [{cpGrp}]"
                            };
                            _parcels.Add(parcel);
                            _parcelGeometries[parcel.InternalId] = cleaned;
                        }
                    }

                    // C. Curve-Preserving Line Polygonization and Integration (Only if not in pure Lines mode)
                    if (!isLineOnlyMode && LayerGeometryModeService.ShouldPolygonizeLines(SelectedGeometryMode, BuildPolygonsFromLines))
                    {
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
                                Remarks = string.Empty,
                                Provenance = ring.WasUsedAsIs 
                                    ? $"From Closed Polyline [{ring.SourceLayer}]" 
                                    : $"Polygonized from CAD layer [{ring.SourceLayer}]"
                            };
                            _parcels.Add(parcel);
                            _parcelGeometries[parcel.InternalId] = cleaned;
                        }

                        manifest.TotalPolygonsBuilt = _parcels.Count;
                        LastPolygonizationManifest = manifest;
                        IsPolygonizationStale = false;
                        PolygonizationStaleReason = string.Empty;
                    }

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
                        string? explicitPlotLayer = PlotIdExtractorService.CleanLayerName(SelectedPlotTextLayer);

                        var secondaryPlotLayers = new HashSet<string>(
                            CadLayers.Where(l => l.Role == LayerRole.PlotText).Select(l => l.LayerName.Trim()),
                            StringComparer.OrdinalIgnoreCase);

                        if (string.IsNullOrEmpty(explicitPlotLayer) && secondaryPlotLayers.Count == 0)
                        {
                            foreach (var cl in CadLayers.Where(l => l.Category == "Text & Annotations" || CadExtractionService.InferRoleFromName(l.LayerName) == LayerRole.PlotText))
                            {
                                secondaryPlotLayers.Add(cl.LayerName.Trim());
                            }
                        }

                        var targetUsageLayers = new HashSet<string>(
                            CadLayers.Where(l => l.Role == LayerRole.UsageText).Select(l => l.LayerName.Trim()),
                            StringComparer.OrdinalIgnoreCase);

                        // Build parcel centers dictionary
                        var parcelCenters = new Dictionary<long, (double CenterX, double CenterY)>();
                        foreach (var p in _parcels)
                        {
                            if (_parcelGeometries.TryGetValue(p.InternalId, out var polyGeom) && polyGeom != null)
                            {
                                var center = polyGeom.Extent.Center;
                                parcelCenters[p.InternalId] = (center.X, center.Y);
                            }
                        }

                        // Flatten all annotations
                        var allAnnotsFlattened = datasetFeatures.Annotations
                            .Where(a => a.Location != null && !string.IsNullOrWhiteSpace(a.Text))
                            .Select(a => (a.Text, a.Location.X, a.Location.Y, a.LayerName))
                            .ToList();

                        // Execute robust PlotIdExtractorService (Priority 1: Direct Intersect, Fallback: Proximity <= 1.0m to boundary)
                        var matchReport = PlotIdExtractorService.AssignPlotIds(
                            _parcels,
                            parcelAnnotationsMap,
                            allAnnotsFlattened,
                            parcelCenters,
                            explicitPlotLayer,
                            secondaryPlotLayers,
                            targetUsageLayers,
                            maxProximityMeters: 1.0,
                            distanceToParcelFunc: (parcelId, x, y) =>
                            {
                                if (_parcelGeometries.TryGetValue(parcelId, out var polyGeom) && polyGeom != null)
                                {
                                    var pt = MapPointBuilderEx.CreateMapPoint(x, y, polyGeom.SpatialReference);
                                    return GeometryEngine.Instance.Distance(polyGeom, pt);
                                }
                                if (parcelCenters.TryGetValue(parcelId, out var c))
                                {
                                    return Math.Sqrt(Math.Pow(x - c.CenterX, 2) + Math.Pow(y - c.CenterY, 2));
                                }
                                return double.MaxValue;
                            });

                        System.Diagnostics.Debug.WriteLine($"[Audit Log] {matchReport.GetSummary()}");

                        // Assign UsageText / Remarks from annotations map (Spatial Join)
                        foreach (var p in _parcels)
                        {
                            p.PlotNo1 = p.PlotId;

                            if (parcelAnnotationsMap.TryGetValue(p.InternalId, out var insideAnnots) && insideAnnots.Count > 0)
                            {
                                // Gather all annotations from the user's chosen UsageText layer(s)
                                var usageTextsInside = insideAnnots
                                    .Where(a => targetUsageLayers.Contains(a.LayerName))
                                    .Select(a => a.Text.Trim())
                                    .Where(t => !string.IsNullOrWhiteSpace(t) && !PlotIdExtractorService.IsPlotNumberText(t))
                                    .Distinct()
                                    .ToList();

                                if (usageTextsInside.Count == 0 && targetUsageLayers.Count == 0)
                                {
                                    usageTextsInside = insideAnnots
                                        .Where(a => CadExtractionService.InferRoleFromName(a.LayerName) == LayerRole.UsageText || PlotIdExtractorService.IsUsageText(a.Text))
                                        .Select(a => a.Text.Trim())
                                        .Where(t => !string.IsNullOrWhiteSpace(t) && !PlotIdExtractorService.IsPlotNumberText(t))
                                        .Distinct()
                                        .ToList();
                                }

                                if (usageTextsInside.Count > 0)
                                {
                                    string combinedUsageText = string.Join(" - ", usageTextsInside);
                                    p.UsageText = combinedUsageText;
                                    p.Remarks = combinedUsageText; // REMARKS takes data from the layer(s) chosen as UsageText via spatial join!
                                }
                            }

                            // Proximity fallback for REMARKS if no annotation fell strictly inside
                            if (string.IsNullOrWhiteSpace(p.Remarks) && targetUsageLayers.Count > 0 && parcelCenters.TryGetValue(p.InternalId, out var center))
                            {
                                var nearestUsage = allAnnotsFlattened
                                    .Where(a => targetUsageLayers.Contains(a.LayerName) && !PlotIdExtractorService.IsPlotNumberText(a.Text))
                                    .Select(a => new { a.Text, Distance = Math.Sqrt(Math.Pow(a.X - center.CenterX, 2) + Math.Pow(a.Y - center.CenterY, 2)) })
                                    .Where(x => x.Distance <= 15.0)
                                    .OrderBy(x => x.Distance)
                                    .FirstOrDefault();

                                if (nearestUsage != null && !string.IsNullOrWhiteSpace(nearestUsage.Text))
                                {
                                    p.UsageText = nearestUsage.Text.Trim();
                                    p.Remarks = nearestUsage.Text.Trim();
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
                                                Remarks = string.Empty,
                                                Provenance = $"From CAD layer [{layerVal}]"
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
                                Remarks = string.Empty,
                                Provenance = ring.WasUsedAsIs 
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
                // Clean up any legacy intermediate feature classes from GDB
                try
                {
                    var delMain = Geoprocessing.MakeValueArray(Path.Combine(GdbPath, "Cad_Constructed_MAIN"));
                    await Geoprocessing.ExecuteToolAsync("management.Delete", delMain);
                }
                catch { }
                try
                {
                    var delSub = Geoprocessing.MakeValueArray(Path.Combine(GdbPath, "Cad_Constructed_SUB"));
                    await Geoprocessing.ExecuteToolAsync("management.Delete", delSub);
                }
                catch { }

                string fcParcelName = IsDemoProject ? "DEMO_Cad_Constructed_Parcels" : "Cad_Constructed_Parcels";
                string templateParcel = Path.Combine(_helperManager.TemplatesDir, "PARCEL.shp");
                await GdbFeatureClassService.EnsureFeatureClassFromTemplateAsync(GdbPath, fcParcelName, templateParcel, SelectedUtmZone);
                await QueuedTask.Run(() =>
                {
                    GdbFeatureClassService.WriteParcelsToFeatureClass(GdbPath, fcParcelName, _parcels, _parcelGeometries);
                    
                    var map = MapView.Active?.Map;
                    if (map != null)
                    {
                        var toRemove = map.GetLayersAsFlattenedList()
                            .Where(l => l.Name.StartsWith("temp_f2p", StringComparison.OrdinalIgnoreCase) ||
                                        l.Name.Equals("Cad_Constructed_MAIN", StringComparison.OrdinalIgnoreCase) ||
                                        l.Name.Equals("Cad_Constructed_SUB", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        foreach (var l in toRemove)
                        {
                            try { map.RemoveLayer(l); } catch { }
                        }
                    }

                    GdbFeatureClassService.AddOrRefreshMapLayer(MapView.Active?.Map, GdbPath, fcParcelName, fcParcelName);
                });

                // Register authoritative single Step 2 output feature class reference
                string verifiedFcPath = Path.Combine(GdbPath, fcParcelName);
                RegisterStepOutput(2, verifiedFcPath, _parcels.Count, SelectedUtmZone);
                Step2OutputFeatureClassPath = verifiedFcPath;
                Step2OutputTimestamp = DateTime.Now;
                Step2OutputFeatureCount = _parcels.Count;
                System.Diagnostics.Debug.WriteLine($"[Audit Log] Step 2 Output registered: Path='{Step2OutputFeatureClassPath}', Count={Step2OutputFeatureCount}, Time={Step2OutputTimestamp:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception gdbSaveEx)
            {
                ReportError($"Step 2 output 'Cad_Constructed_Parcels' could not be saved: {gdbSaveEx.Message}", "Step 2 Error");
                return;
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


    public Task ExportStep2ParcelsAsync() =>
        ShowStepOutputOnMapAsync(2, IsDemoProject ? "DEMO_Cad_Constructed_Parcels" : "Cad_Constructed_Parcels");

}
