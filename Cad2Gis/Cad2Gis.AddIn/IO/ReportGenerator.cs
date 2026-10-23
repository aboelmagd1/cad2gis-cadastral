using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Cad2Gis.Core.IO;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.AddIn.IO;

public static class ReportGenerator
{
    public static void GenerateAllReports(
        string outputFolder,
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyList<BorderRecord> borders,
        ValidationResult valResult,
        ProjectMetadata meta,
        string sourceCadName = "Drawing.dwg",
        string profileName = "Default_Consultant_Profile",
        int utmEpsg = 32637,
        string? continuedFromExport = null)
    {
        Directory.CreateDirectory(outputFolder);

        // 1. QA_QC_Report.xlsx (With all required sheets)
        string excelReportPath = Path.Combine(outputFolder, "QA_QC_Report.xlsx");
        GenerateExcelReport(excelReportPath, parcels, borders, valResult, meta, utmEpsg);

        // 2. Missing_Data_Report.csv
        string csvReportPath = Path.Combine(outputFolder, "Missing_Data_Report.csv");
        GenerateMissingDataCsv(csvReportPath, valResult);

        // 3. Audit_Log.json
        string jsonReportPath = Path.Combine(outputFolder, "Audit_Log.json");
        GenerateJsonAuditLog(jsonReportPath, parcels, borders, valResult, meta, utmEpsg, continuedFromExport);

        // 4. Mandatory Layers Used Excel (Section 8.3)
        var dummyLayersUsed = new List<LayerMatchResult>
        {
            new() { CadLayerName = "PARCEL_BOUNDARY", EntityCount = parcels.Count(p => p.Class == ParcelClass.Main), AssignedRole = "Main parcel", IsMatchedFromExcel = true, IsNew = false },
            new() { CadLayerName = "SUB_PARCELS", EntityCount = parcels.Count(p => p.Class != ParcelClass.Main), AssignedRole = "Sub-parcel", IsMatchedFromExcel = true, IsNew = false },
            new() { CadLayerName = "PLOT_NUMBERS", EntityCount = parcels.Count, AssignedRole = "Plot-number text", IsMatchedFromExcel = true, IsNew = false }
        };
        ExpectedLayersService.WriteLayersUsedExcel(outputFolder, meta.PlanNo, profileName, sourceCadName, dummyLayersUsed);
    }

    private static void GenerateExcelReport(
        string path,
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyList<BorderRecord> borders,
        ValidationResult valResult,
        ProjectMetadata meta,
        int utmEpsg)
    {
        var writer = new SimpleXlsxWriter();

        // Sheet 1: Summary
        var wsSum = writer.AddSheet("Audit Summary", new[] { "Metric / Parameter", "Value" });
        wsSum.AddRow("Title", "MOMRAH / Balady Cadastral Quality Assurance Report");
        wsSum.AddRow("Plan No (PLAN_NO):", meta.PlanNo);
        wsSum.AddRow("Neighbourhood:", meta.Neighbourhood);
        wsSum.AddRow("Region / City:", $"{meta.RegionName} ({meta.RegionId}) / {meta.CityName} ({meta.CityId})");
        wsSum.AddRow("Total Parcels:", parcels.Count);
        wsSum.AddRow("Main Parcels:", valResult.MainParcelsCount);
        wsSum.AddRow("Sub-Parcels:", valResult.SubParcelsCount);
        wsSum.AddRow("Total Borders:", borders.Count);
        wsSum.AddRow("Inferred Borders:", valResult.InferredBordersCount);
        wsSum.AddRow("Total Area (m²):", Math.Round(parcels.Sum(p => p.AreaSqm), 2));
        wsSum.AddRow("Working CRS / Export CRS:", $"EPSG:{utmEpsg} (UTM Metric) -> EPSG:4326 (WGS 84 GCS)");
        wsSum.AddRow("Compliance Status:", valResult.IsValid ? "PASSED (Compliant)" : $"FAILED ({valResult.Errors.Count} Errors)");

        // Sheet 2: Issues & Warnings
        var wsIssues = writer.AddSheet("Issues & Warnings", new[] { "Severity", "Entity Type", "Entity ID", "Field", "Description" });
        foreach (var err in valResult.Errors)
        {
            wsIssues.AddRow("Error", err.EntityType, err.EntityId, err.FieldName, err.Message);
        }
        foreach (var warn in valResult.Warnings)
        {
            wsIssues.AddRow("Warning", warn.EntityType, warn.EntityId, warn.FieldName, warn.Message);
        }

        // Sheet 3: Text Length Check
        var wsText = writer.AddSheet("Text Length Check", new[] { "Entity Type", "Entity ID", "Field", "Value", "UTF-8 Bytes", "Max Allowed", "Safe Truncated", "Suggested Shorter" });
        foreach (var issue in valResult.TextLengthIssues)
        {
            wsText.AddRow(issue.EntityType, issue.EntityId, issue.FieldName, issue.OriginalValue, issue.Utf8ByteLength, issue.MaxAllowedBytes, issue.SafeTruncatedValue, issue.SuggestedShorterValue ?? "");
        }

        // Sheet 4: Area Comparison
        if (valResult.AreaReport != null)
        {
            var wsArea = writer.AddSheet("Area Comparison", new[] { "Plot ID", "Status", "Computed Area (m²)", "Excel Area (m²)", "Difference (m²)", "Difference (%)", "Notes" });
            foreach (var e in valResult.AreaReport.Entries)
            {
                wsArea.AddRow(e.PlotId, e.Status, e.ComputedAreaSqm, e.ExcelAreaSqm?.ToString() ?? "-", e.AreaDifferenceSqm?.ToString() ?? "-", e.PercentDifference?.ToString() ?? "-", e.Notes);
            }
        }

        // Sheet 5: Inferred Borders Review
        var wsInferred = writer.AddSheet("Inferred Borders", new[] { "Plot ID", "Direction", "Measured Width (m)", "Suggested Width (m)", "Description", "Border Type", "Rule Used", "Rays Count" });
        foreach (var b in borders.Where(b => b.IsInferred))
        {
            wsInferred.AddRow(b.PlotId, b.BorderNameArabic, b.MeasuredWidth?.ToString("F1") ?? "UNKNOWN", b.SuggestedWidth?.ToString("F1") ?? "-", b.Descriptio, b.BorderType, b.InferenceRule ?? "Ray Sensing", b.RayLengths.Count);
        }

        writer.Save(path);
    }

    private static void GenerateMissingDataCsv(string path, ValidationResult valResult)
    {
        var sb = new StringBuilder();
        sb.AppendLine("EntityType,EntityId,FieldName,Severity,Message");
        foreach (var err in valResult.Errors)
            sb.AppendLine($"\"{err.EntityType}\",\"{err.EntityId}\",\"{err.FieldName}\",\"Error\",\"{err.Message.Replace("\"", "\"\"")}\"");
        foreach (var warn in valResult.Warnings)
            sb.AppendLine($"\"{warn.EntityType}\",\"{warn.EntityId}\",\"{warn.FieldName}\",\"Warning\",\"{warn.Message.Replace("\"", "\"\"")}\"");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void GenerateJsonAuditLog(
        string path,
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyList<BorderRecord> borders,
        ValidationResult valResult,
        ProjectMetadata meta,
        int utmEpsg,
        string? continuedFromExport = null)
    {
        var audit = new
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            ContinuedFromExport = continuedFromExport ?? "Fresh Session (CAD)",
            Project = meta,
            CrsTransformation = $"EPSG:{utmEpsg} -> EPSG:4326",
            MetricsFrozen = true,
            ParcelsSummary = new
            {
                Total = parcels.Count,
                MainParcels = valResult.MainParcelsCount,
                SubParcels = valResult.SubParcelsCount,
                TotalAreaSqm = parcels.Sum(p => p.AreaSqm)
            },
            BordersSummary = new
            {
                Total = borders.Count,
                InferredCount = valResult.InferredBordersCount
            },
            InferredBorders = borders.Where(b => b.IsInferred).Select(b => new
            {
                b.PlotId,
                b.BorderName,
                Direction = b.BorderNameArabic,
                b.MeasuredWidth,
                b.SuggestedWidth,
                b.Descriptio,
                b.BorderType,
                b.InferenceRule,
                b.RayLength,
                AllRays = b.RayLengths
            }).ToList(),
            Validation = new
            {
                valResult.IsValid,
                ErrorsCount = valResult.Errors.Count,
                WarningsCount = valResult.Warnings.Count
            }
        };

        string json = JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }
}
