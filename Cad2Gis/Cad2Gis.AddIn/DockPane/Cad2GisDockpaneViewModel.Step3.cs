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
/// Partial class for Cad2GisDockpaneViewModel - STEP 3
/// Handles MOMRAH 26-field schema population, Land Use / Areas integration, and PARCEL_Approved_Schema output.
/// </summary>
public partial class Cad2GisDockpaneViewModel
{
    public async Task ExecuteStep3PopulateSchemaAsync(bool isPartOfPipeline = false)
    {
        if (!isPartOfPipeline) IsBusy = true;
        ReportProgress(10, "الخطوة 3: تعبئة الاسكيما والبيانات البلدية", "التحقق من مصدر البيانات وتطبيق نموذج MOMRAH (26 حقلاً)...", phase: "المرحلة 3 من 5");

        try
        {
            // 1. Resolve & Validate Step 3 Input from disk
            string step3Input;
            if (GetSource(3) == StepSource.OwnData)
            {
                step3Input = ResolveOwnDataOrFail(3);
                await ValidateInputOrFailAsync(step3Input, GeometryType.Polygon);
            }
            else
            {
                step3Input = PipelineStateManager.GetVerifiedOutputOrFail(_helperManager.PipelineStateFile, step: 2).Path;
                await ValidateInputOrFailAsync(step3Input, GeometryType.Polygon);
            }

            System.Diagnostics.Debug.WriteLine($"[Step3] option={GetSource(3)} source={step3Input}");

            // 2. Read parcels from disk (always load current data from disk)
            await QueuedTask.Run(() =>
            {
                var (loadedParcels, loadedGeoms, _, _) = GdbFeatureClassService.ReadParcelsFromDataset(step3Input, SelectedUtmZone, _helperManager.ScratchDir);
                if (loadedParcels.Count > 0)
                {
                    _parcels.Clear();
                    _parcels.AddRange(loadedParcels);
                    _parcelGeometries.Clear();
                    foreach (var kvp in loadedGeoms)
                        _parcelGeometries[kvp.Key] = kvp.Value;
                }
            });

            if (_parcels.Count == 0)
            {
                StatusMessage = "لم يتم تحميل أي بارسلات صالحة لتطبيق الاسكيما.";
                throw new InvalidOperationException("No parcels loaded to populate schema.");
            }

            await QueuedTask.Run(() =>
            {

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
                    Dictionary<string, LandUseItem>? luMap = null;
                    if (File.Exists(LandUseExcelPath))
                    {
                        ReportProgress(35, "الخطوة 3: مطابقة جدول استخدامات الأراضي", $"قراءة ومطابقة الاستخدامات من '{Path.GetFileName(LandUseExcelPath)}'...", phase: "المرحلة 3 من 5");
                        luMap = LandUseExcelService.ReadLandUseFile(LandUseExcelPath);
                    }
                    var discrepancies = new List<string>();
                    LandUseExcelService.EnrichParcels(_parcels, luMap, discrepancies, MainUsageMode);

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

            // 4. Manual edit check and Commit to single PARCEL_Approved_Schema
            string fcParcelName = IsDemoProject ? "DEMO_PARCEL_Approved_Schema" : "PARCEL_Approved_Schema";
            string verifiedFcPath = Path.Combine(GdbPath, fcParcelName);

            string? editDetails = null;
            if (await GdbFeatureClassService.FeatureClassExistsAsync(verifiedFcPath) &&
                PipelineStateManager.IsEditedSinceWritten(_helperManager.PipelineStateFile, 3, out editDetails))
            {
                var promptResult = ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"تم رصد تعديلات يدوية على طبقة الخطوة 3 ({fcParcelName}): {editDetails}\n\nهل ترغب في استبدال هذه التعديلات وإعادة كتابة الطبقة؟",
                    "تأكيد استبدال التعديلات اليدوية",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (promptResult != System.Windows.MessageBoxResult.Yes)
                {
                    StatusMessage = "تم إلغاء إعادة تشغيل الخطوة 3 للحفاظ على التعديلات اليدوية.";
                    return;
                }
            }

            string templateParcel = Path.Combine(_helperManager.TemplatesDir, "PARCEL.shp");
            await GdbFeatureClassService.EnsureFeatureClassFromTemplateAsync(GdbPath, fcParcelName, templateParcel, SelectedUtmZone);

            await QueuedTask.Run(() =>
            {
                GdbFeatureClassService.WriteParcelsToFeatureClass(GdbPath, fcParcelName, _parcels, _parcelGeometries);
                GdbFeatureClassService.AddOrRefreshMapLayer(MapView.Active?.Map, GdbPath, fcParcelName, fcParcelName);
            });

            // Register Step 3 authoritative single output reference after verification
            var (isVerified, verifyReason) = await GdbFeatureClassService.VerifyPolygonFeatureClassAsync(verifiedFcPath, _parcels.Count, SelectedUtmZone);
            if (!isVerified)
            {
                ReportError($"فشل التحقق من صحة مخرجات الخطوة 3: {verifyReason}", "خطأ في التحقق من الخطوة 3");
                return;
            }

            RegisterStepOutput(3, verifiedFcPath, _parcels.Count, SelectedUtmZone);
            RegisterVerifiedStep3Output(verifiedFcPath, _parcels.Count, SelectedUtmZone);
            ResetProgress($"اكتملت الخطوة 3: تم حفظ وتوثيق {_parcels.Count} قطعة بالاسكيما البلدية داخل '{fcParcelName}' بنجاح.", "اكتملت الخطوة 3");
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


    public Task ExportStep3PopulatedParcelsAsync() =>
        ShowStepOutputOnMapAsync(3, IsDemoProject ? "DEMO_PARCEL_Approved_Schema" : "PARCEL_Approved_Schema");

}
