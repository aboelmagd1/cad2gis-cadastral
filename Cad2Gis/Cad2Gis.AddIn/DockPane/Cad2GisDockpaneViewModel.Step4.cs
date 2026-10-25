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
/// Partial class for Cad2GisDockpaneViewModel - STEP 4
/// Handles border boundary extraction, ray-casting street detection, and BORDER_Cardinal_Approved output.
/// </summary>
public partial class Cad2GisDockpaneViewModel
{
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

            var mode = GetSource(4);
            string step4SourcePath = mode == StepSource.OwnData
                ? ResolveOwnDataOrFail(4)
                : PipelineStateManager.GetVerifiedOutputOrFail(_helperManager.PipelineStateFile, step: 3).Path;

            await ValidateInputOrFailAsync(step4SourcePath, GeometryType.Polygon);

            System.Diagnostics.Debug.WriteLine($"[Step4] option={mode} source={step4SourcePath}");

            ReportProgress(15, "الخطوة 4: قراءة مخرجات الخطوة 3", $"قراءة مضلعات البارسلات من '{Path.GetFileName(step4SourcePath)}'...", phase: "المرحلة 4 من 5");

            await QueuedTask.Run(() =>
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

                // Ensure latest official border types and usage types from Required Fields in the Shapefile.xlsx
                if (File.Exists(_helperManager.RequiredFieldsExcel))
                {
                    MunicipalLookups.LoadFromRequiredFieldsExcel(_helperManager.RequiredFieldsExcel);
                    MunicipalLookups.LoadUsageTypesFromRequiredFieldsExcel(_helperManager.RequiredFieldsExcel);
                }

                // Ensure keywords table is loaded
                string keywordsCsv = Path.Combine(_helperManager.LookupsDir, "BorderType_Keywords.csv");
                MunicipalLookups.LoadBorderTypeKeywords(keywordsCsv, msg => System.Diagnostics.Debug.WriteLine($"[Keywords QA] {msg}"));

                // Part A: Build spatial index once before the parcel loop
                var swTotal = System.Diagnostics.Stopwatch.StartNew();
                var swDecompose = new System.Diagnostics.Stopwatch();
                var swNeighborCheck = new System.Diagnostics.Stopwatch();
                var swWidthInference = new System.Diagnostics.Stopwatch();
                var swWriting = new System.Diagnostics.Stopwatch();

                var spatialIndex = new Step4SpatialIndex(parcelTuples, null, infOptions.MaxSearchDistance);
                DateTime lastProgressTime = DateTime.MinValue;
                int lastReportedPct = -1;

                foreach (var p in mainParcelsList)
                {
                    mainIdx++;
                    double curPct = totalMainParcels > 0 ? 15.0 + (70.0 * mainIdx / totalMainParcels) : 50.0;
                    int curPctInt = (int)Math.Round(curPct);

                    // Throttled progress: at most once every ~250ms or 1%
                    if ((DateTime.Now - lastProgressTime).TotalMilliseconds >= 250 || curPctInt > lastReportedPct || mainIdx == totalMainParcels)
                    {
                        lastProgressTime = DateTime.Now;
                        lastReportedPct = curPctInt;
                        ReportProgress(curPct, "الخطوة 4: تفكيك الواجهات واستشعار الشوارع", $"القطعة {mainIdx} من {totalMainParcels} (القطعة: {p.PlotId}): تفكيك الاتجاهات واستشعار الشوارع", phase: "المرحلة 4 من 5");
                    }

                    if (!_parcelGeometries.TryGetValue(p.InternalId, out var poly)) continue;

                    swDecompose.Start();
                    var cardinalDict = FacadeDecomposer.DecomposeTo4Cardinals(
                        poly, 
                        p, 
                        MergeChamfers ? ChamferThresholdMeters : 0.0,
                        ChamferAngleDeg,
                        ChamferAngleToleranceDeg,
                        borderOptions,
                        SelectedUtmZone);
                    swDecompose.Stop();

                    // Sense neighbors (shared segment >= 0.5m) using Spatial Index or infer undrawn road width
                    OutwardRaySensor.SenseAndEnrichBorders(
                        p, 
                        cardinalDict, 
                        parcelTuples, 
                        inferenceOptions: infOptions, 
                        minSharedSegmentLength: MinSharedSegmentLength,
                        qaLog: msg => System.Diagnostics.Debug.WriteLine($"[Step 4 QA] {msg}"),
                        spatialIndex: spatialIndex,
                        swNeighborCheck: swNeighborCheck,
                        swWidthInference: swWidthInference);

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

                swTotal.Stop();
                string timingLog = $"[Step 4 Performance] Total: {swTotal.ElapsedMilliseconds} ms. Decompose: {swDecompose.ElapsedMilliseconds} ms, Neighbor Check: {swNeighborCheck.ElapsedMilliseconds} ms, Width Inference: {swWidthInference.ElapsedMilliseconds} ms, Writing/Init: {swWriting.ElapsedMilliseconds} ms for {totalMainParcels} MAIN parcels.";
                System.Diagnostics.Debug.WriteLine(timingLog);
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

            // Commit borders to GDB as BORDER_Cardinal_Approved with manual edit check
            string fcBorderName = IsDemoProject ? "DEMO_BORDER_Cardinal_Approved" : "BORDER_Cardinal_Approved";
            string verifiedBorderFc = Path.Combine(GdbPath, fcBorderName);

            string? borderEditDetails = null;
            if (await GdbFeatureClassService.FeatureClassExistsAsync(verifiedBorderFc) &&
                PipelineStateManager.IsEditedSinceWritten(_helperManager.PipelineStateFile, 4, out borderEditDetails))
            {
                var promptResult = ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"تم رصد تعديلات يدوية على طبقة حدود الخطوة 4 ({fcBorderName}): {borderEditDetails}\n\nهل ترغب في استبدال هذه التعديلات وإعادة إنشاء الحدود؟",
                    "تأكيد استبدال الحدود المعدلة",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (promptResult != System.Windows.MessageBoxResult.Yes)
                {
                    StatusMessage = "تم إلغاء إعادة إنشاء الحدود للحفاظ على التعديلات اليدوية.";
                    return;
                }
            }

            string templateBorder = Path.Combine(_helperManager.TemplatesDir, "border.shp");
            await GdbFeatureClassService.EnsureFeatureClassFromTemplateAsync(GdbPath, fcBorderName, templateBorder, SelectedUtmZone);

            await QueuedTask.Run(() =>
            {
                GdbFeatureClassService.WriteBordersToFeatureClass(GdbPath, fcBorderName, _borders, _borderGeometries);
                GdbFeatureClassService.AddOrRefreshMapLayer(MapView.Active?.Map, GdbPath, fcBorderName, fcBorderName);
            });

            var (isBorderVerified, borderVerifyReason) = await GdbFeatureClassService.VerifyPolylineFeatureClassAsync(verifiedBorderFc, _borders.Count, SelectedUtmZone);
            if (!isBorderVerified)
            {
                ReportError($"فشل التحقق من صحة طبقة الحدود: {borderVerifyReason}", "خطأ في التحقق من الخطوة 4");
                return;
            }

            RegisterStepOutput(4, verifiedBorderFc, _borders.Count, SelectedUtmZone);
            BorderCountSummary = $"Borders: {_borders.Count} records (MAIN parcels only, 4 per parcel)";
            ResetProgress($"الخطوة 4 اكتملت: تم إنشاء {_borders.Count} خط حدود وحفظها داخل '{fcBorderName}'.", "اكتمل توليد الحدود");
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


    public Task ExportStep4BordersAsync() =>
        ShowStepOutputOnMapAsync(4, IsDemoProject ? "DEMO_BORDER_Cardinal_Approved" : "BORDER_Cardinal_Approved");

}
