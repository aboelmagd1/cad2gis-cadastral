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
/// Partial class for Cad2GisDockpaneViewModel - STEP 5
/// Handles final QA validation, Final.gdb, Final Shapefiles (PARCEL.shp, BORDER.shp), and QA Reports export.
/// </summary>
public partial class Cad2GisDockpaneViewModel
{
    public async Task ExecuteStep5ExportQaAsync(bool isPartOfPipeline = false, CancellationToken token = default)
    {
        if (!isPartOfPipeline)
        {
            IsBusy = true;
            token = StartNewOperationToken();
        }
        ReportProgress(10, "الخطوة 5: التدقيق الشامل والتصدير النهائي", "بدء تدقيق الجودة وتجهيز مجلد المخرجات...", phase: "المرحلة 5 من 5");

        try
        {
            token.ThrowIfCancellationRequested();
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

            string step5ParcelSource = GetSource(5) == StepSource.OwnData
                ? ResolveOwnDataOrFail(5)
                : PipelineStateManager.GetVerifiedOutputOrFail(_helperManager.PipelineStateFile, step: 3).Path;
            await ValidateInputOrFailAsync(step5ParcelSource, GeometryType.Polygon);

            var step4Output = PipelineStateManager.LoadStepOutput(_helperManager.PipelineStateFile, 4);

            await QueuedTask.Run(() =>
            {
                var (pLoaded, pGeoms, _, _) = GdbFeatureClassService.ReadParcelsFromDataset(step5ParcelSource, SelectedUtmZone, _helperManager.ScratchDir);
                if (pLoaded.Count > 0)
                {
                    _parcels.Clear();
                    _parcels.AddRange(pLoaded);
                    _parcelGeometries.Clear();
                    foreach (var kvp in pGeoms) _parcelGeometries[kvp.Key] = kvp.Value;
                }

                string borderPath = step4Output?.Path ?? Path.Combine(GdbPath, "BORDER_Cardinal_Approved");
                if (GdbFeatureClassService.FeatureClassExists(borderPath) || File.Exists(borderPath))
                {
                    var (bLoaded, bGeoms, _, _) = GdbFeatureClassService.ReadBordersFromDataset(borderPath, SelectedUtmZone, _helperManager.ScratchDir);
                    if (bLoaded.Count > 0)
                    {
                        _borders.Clear();
                        _borders.AddRange(bLoaded);
                        _borderGeometries.Clear();
                        foreach (var kvp in bGeoms) _borderGeometries[kvp.Key] = kvp.Value;
                    }
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
                if (true) // Final shapefiles are a mandatory deliverable of Step 5
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
                            exportWarnings.Add($"[Deviations] {complianceResult.DeviatingParcels.Count} parcels with discrepancies are listed in the Reports folder (no extra output layer is created).");
                        }
                    }
                    catch (Exception compEx)
                    {
                        exportWarnings.Add($"[Compliance Notice] {compEx.Message}");
                    }
                }
            });

            // 5. Geodatabase Export into Final\Final.gdb (GP tools safely outside QueuedTask)
            // Final.gdb (PARCEL + BORDER) is a mandatory deliverable of Step 5
            if (true)
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

                }
                catch (Exception gdbEx)
                {
                    ReportError($"Final.gdb export failed: {gdbEx.Message}", "Final Export Error");
                    return;
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

                // P6 Verification: Verify all deliverables before updating latest export
                var v = GdbFeatureClassService.VerifyFinalExport(finalDir, _parcels.Count, _borders.Count, SelectedUtmZone);
                if (!v.Ok)
                {
                    ReportError("Final export failed verification:\n" + v.Report, "Final Export Verification Error");
                    return;
                }

                int maxHist = _profileModel?.MaxExportHistoryCount ?? 10;
                if (!ExportStateManager.TrySaveExportState(_helperManager.LastExportStateFile, state, maxHist, out var saveErr))
                {
                    ReportError("Could not record the latest export: " + saveErr, "State Error");
                    return;
                }

                LatestExportPath = finalDir;
                LatestExportTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                HasLatestExport = true;
                RefreshLatestExportStatus();
            }
            catch (Exception stateEx)
            {
                ReportError($"[Step 5 Error] Failed finalizing export state: {stateEx.Message}", "State Error");
                return;
            }

            // Step 5 outputs on the map: PARCEL and BORDER from Final.gdb + PARCEL and BORDER from the Final shapefiles
            try
            {
                await QueuedTask.Run(() =>
                {
                    GdbFeatureClassService.AddFinalOutputsToMap(MapView.Active?.Map, finalGdbPath, parcelExportName, borderExportName, expectedParcelShp, expectedBorderShp);
                });
            }
            catch (Exception mapEx)
            {
                exportWarnings.Add($"[Map Notice] Could not add the final outputs to the map: {mapEx.Message}");
            }

            // Informative ExportStatusSummary
            ExportStatusSummary = $"تم تصدير المخرجات النهائية والتحقق منها بنجاح إلى هيكل Final الموحد:\n📁 {finalDir}";
            ExportStatusSummary += $"\n✔ Final.gdb: PARCEL ({_parcels.Count}), BORDER ({_borders.Count})";
            ExportStatusSummary += $"\n✔ Shape: PARCEL.shp, BORDER.shp (2D Planar, UTF-8 .cpg, all sidecars verified)";
            ExportStatusSummary += $"\n✔ Reports: كافة تقارير الجودة تم التحقق منها داخل مجلد Reports";

            if (valResult != null && valResult.Errors.Count > 0)
            {
                ExportStatusSummary += $"\n⚠️ ملاحظة: يحتوي تقرير التدقيق على {valResult.Errors.Count} ملاحظة (مفصلة داخل Reports).";
            }
            if (complianceResult != null && complianceResult.HasDeviations)
            {
                ExportStatusSummary += $"\n⚠️ {complianceResult.DeviatingParcels.Count} قطعة بها فروقات مسجلة في ملفات Reports.";
            }

            ResetProgress($"الخطوة 5 اكتملت: تم تصدير مجلد Final بكافة محتوياته بنجاح.", "اكتمل التصدير والتدقيق");
        }
        catch (OperationCanceledException)
        {
            ReportProgress(0, "تم إلغاء الخطوة 5", "تم إيقاف التصدير والتدقيق النهائي بناءً على طلبك.", status: "تم إلغاء الخطوة 5 بواسطة المستخدم.", phase: "ملغاة");
            if (isPartOfPipeline) throw;
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

}
