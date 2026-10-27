using System;
using System.Collections.Generic;
using System.Linq;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Lookups;

namespace Cad2Gis.Core.Services;

public class ValidationIssue
{
    public string Level { get; set; } = "Error"; // "Error" or "Warning"
    public string EntityType { get; set; } = "Parcel";
    public string EntityId { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public override string ToString() => $"[{Level}] {EntityType} {EntityId} -> {FieldName}: {Message}";
}

public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public int TotalIssues => Errors.Count + Warnings.Count;
    public List<ValidationIssue> Errors { get; set; } = new();
    public List<ValidationIssue> Warnings { get; set; } = new();

    public int TotalParcels { get; set; }
    public int MainParcelsCount { get; set; }
    public int SubParcelsCount { get; set; }
    public int TotalBorders { get; set; }
    public int InferredBordersCount { get; set; }
    public double TotalAreaSqm { get; set; }

    public List<TextLengthIssue> TextLengthIssues { get; set; } = new();
    public AreaComparisonReport? AreaReport { get; set; }

    public void AddError(string entityType, string entityId, string fieldName, string message) =>
        Errors.Add(new ValidationIssue { Level = "Error", EntityType = entityType, EntityId = entityId, FieldName = fieldName, Message = message });

    public void AddWarning(string entityType, string entityId, string fieldName, string message) =>
        Warnings.Add(new ValidationIssue { Level = "Warning", EntityType = entityType, EntityId = entityId, FieldName = fieldName, Message = message });
}

public static class QaValidationEngine
{
    public static ValidationResult ValidateAll(
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyList<BorderRecord> borders,
        string? areasExcelPath = null,
        AreaComparisonOptions? areaOptions = null,
        bool verifyExportSchema = false,
        IEnumerable<string>? exportFieldNames = null)
    {
        var result = new ValidationResult
        {
            TotalParcels = parcels.Count,
            MainParcelsCount = parcels.Count(p => p.Class == ParcelClass.Main),
            SubParcelsCount = parcels.Count(p => p.Class != ParcelClass.Main),
            TotalBorders = borders.Count,
            InferredBordersCount = borders.Count(b => b.IsInferred),
            TotalAreaSqm = parcels.Sum(p => p.AreaSqm)
        };

        var mainParcelMap = new Dictionary<string, ParcelRecord>(StringComparer.OrdinalIgnoreCase);

        // 1. Validate Parcels
        foreach (var p in parcels)
        {
            bool isMain = p.Class == ParcelClass.Main;

            if (isMain)
            {
                // A MAIN parcel without a plot number is a QA/QC error
                if (string.IsNullOrWhiteSpace(p.PlotId))
                {
                    result.AddError("Parcel", p.InternalId.ToString(), "PLOT_ID", "MAIN parcel must have an identified PLOT_ID.");
                }
                else
                {
                    if (mainParcelMap.ContainsKey(p.PlotId))
                    {
                        result.AddError("Parcel", p.PlotId, "PLOT_ID", $"Duplicate MAIN PLOT_ID '{p.PlotId}' detected.");
                    }
                    else
                    {
                        mainParcelMap[p.PlotId] = p;
                    }
                }
            }
            else
            {
                // A SUB parcel without a plot number is expected (valid)
                if (string.IsNullOrWhiteSpace(p.PlotId))
                {
                    // Expected, no error
                }
            }

            if (p.AreaSqm <= 0.001)
                result.AddError("Parcel", string.IsNullOrEmpty(p.PlotId) ? p.InternalId.ToString() : p.PlotId, "AREA_SQM", $"Area ({p.AreaSqm}) must be greater than zero.");

            if (p.ShapeLength <= 0.001)
                result.AddError("Parcel", string.IsNullOrEmpty(p.PlotId) ? p.InternalId.ToString() : p.PlotId, "Shape_Leng", $"Perimeter ({p.ShapeLength}) must be greater than zero.");

            if (string.IsNullOrWhiteSpace(p.PlanNo))
                result.AddError("Parcel", string.IsNullOrEmpty(p.PlotId) ? p.InternalId.ToString() : p.PlotId, "PLAN_NO", "PLAN_NO is mandatory.");

            if (string.IsNullOrWhiteSpace(p.Neighbourhood))
                result.AddWarning("Parcel", string.IsNullOrEmpty(p.PlotId) ? p.InternalId.ToString() : p.PlotId, "NEIGHBOURH", "Neighbourhood is empty.");

            if (string.IsNullOrWhiteSpace(p.Address))
                result.AddWarning("Parcel", string.IsNullOrEmpty(p.PlotId) ? p.InternalId.ToString() : p.PlotId, "ADDRESS", "Address is empty.");

            if (!string.IsNullOrWhiteSpace(p.Usage) && !Lookups.MunicipalLookups.IsOfficialUsageType(p.Usage))
                result.AddWarning("Parcel", string.IsNullOrEmpty(p.PlotId) ? p.InternalId.ToString() : p.PlotId, "USAGE", $"Parcel USAGE '{p.Usage}' is not in the approved 'Usage Types' list (RSD, COM, RSC, AGR, IND, UTL).");
            else if (isMain && string.IsNullOrWhiteSpace(p.Usage))
                result.AddWarning("Parcel", p.PlotId, "USAGE", "MAIN parcel USAGE is empty (pending owner decision or unclassified text).");

            if (!string.IsNullOrWhiteSpace(p.Remarks) && p.Remarks.StartsWith("From ", StringComparison.OrdinalIgnoreCase))
                result.AddError("Parcel", string.IsNullOrEmpty(p.PlotId) ? p.InternalId.ToString() : p.PlotId, "REMARKS", $"Internal provenance text '{p.Remarks}' must not be stored in REMARKS field.");
        }

        // 2. Validate Borders: ONLY MAIN parcels get dimensions (sub-parcels never get dimensions)
        var bordersByPlot = borders.GroupBy(b => b.PlotId, StringComparer.OrdinalIgnoreCase)
                                   .ToDictionary(g => g.Key, g => g.ToList());

        // Check that sub-parcels have no borders
        foreach (var sub in parcels.Where(p => p.Class != ParcelClass.Main && !string.IsNullOrWhiteSpace(p.PlotId)))
        {
            if (bordersByPlot.ContainsKey(sub.PlotId))
            {
                result.AddError("SubParcel", sub.PlotId, "Borders", "Sub-parcels must NEVER get dimensions (border features).");
            }
        }

        // Check MAIN parcels: exactly four dimension groups, one per direction (1 = North, 2 = East, 3 = South, 4 = West)
        foreach (var p in parcels.Where(p => p.Class == ParcelClass.Main))
        {
            if (string.IsNullOrWhiteSpace(p.PlotId)) continue;

            if (!bordersByPlot.TryGetValue(p.PlotId, out var plotBorders) || plotBorders.Count == 0)
            {
                result.AddError("Parcel", p.PlotId, "Borders", "MAIN parcel has no associated boundary records.");
                continue;
            }

            // Check MAIN parcels: evaluated per ring (exterior ring 0 and interior holes 1..k)
            var bordersByRing = plotBorders.GroupBy(b => b.RingIndex).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var ringKvp in bordersByRing)
            {
                int ringIdx = ringKvp.Key;
                var ringBorders = ringKvp.Value;

                // Check for fewer than 4 directions in ring
                var distinctDirsInRing = ringBorders.Select(b => b.BorderName).Distinct().ToList();
                if (distinctDirsInRing.Count < 4)
                {
                    result.AddWarning("Parcel", p.PlotId, "FEWER_THAN_4_DIRECTIONS", 
                        $"Ring {ringIdx} of parcel {p.PlotId} has {distinctDirsInRing.Count} cardinal direction(s) instead of 4 (FEWER_THAN_4_DIRECTIONS).");
                }

                // Check for duplicate directions strictly WITHIN this ring
                var ringDirCounts = ringBorders.GroupBy(b => b.BorderName).ToDictionary(g => g.Key, g => g.Count());
                foreach (var dc in ringDirCounts)
                {
                    if (dc.Value > 1)
                    {
                        string dirName = dc.Key switch { 1 => "North (1)", 2 => "East (2)", 3 => "South (3)", 4 => "West (4)", _ => dc.Key.ToString() };
                        result.AddError("Parcel", p.PlotId, "DuplicateCardinalInRing", 
                            $"Duplicate borders found for cardinal direction {dirName} within Ring {ringIdx} of parcel {p.PlotId} ({dc.Value} records).");
                    }
                }
            }

            // Overall parcel check: if parcel has only 1 ring (no holes), exactly 4 borders required
            if (bordersByRing.Count <= 1 && plotBorders.Count != 4)
            {
                result.AddError("Parcel", p.PlotId, "BordersCount", 
                    $"MAIN parcel without holes has {plotBorders.Count} borders; exactly 4 cardinal borders (1=North, 2=East, 3=South, 4=West) are strictly required.");
            }
        }

        // 3. Validate Individual Borders
        foreach (var b in borders)
        {
            if (string.IsNullOrWhiteSpace(b.PlotId))
            {
                result.AddError("Border", b.InternalId.ToString(), "PLOT_ID", "Border has empty PLOT_ID.");
            }
            else if (!mainParcelMap.ContainsKey(b.PlotId))
            {
                result.AddError("Border", b.PlotId, "ReferentialIntegrity", $"Border references PLOT_ID '{b.PlotId}' which does not exist in MAIN parcels.");
            }

            if (b.BorderName < 1 || b.BorderName > 4)
                result.AddError("Border", b.PlotId, "BORDERNAME", $"Invalid BORDERNAME '{b.BorderName}'. Must be 1 (North), 2 (South), 3 (East), or 4 (West).");

            if (b.ShapeLength <= 0.001)
                result.AddError("Border", b.PlotId, "SHAPE_Leng", $"Border length ({b.ShapeLength}) must be greater than zero.");

            if (b.IsUnknown || string.IsNullOrWhiteSpace(b.Descriptio) || b.Descriptio == "UNKNOWN")
            {
                result.AddWarning("Border", b.PlotId, "DESCRIPTIO", "Arabic DESCRIPTIO is UNKNOWN or empty (unresolved neighbor/street width).");
            }

            if (!string.IsNullOrWhiteSpace(b.BorderType) && !Lookups.MunicipalLookups.IsOfficialBorderType(b.BorderType) && !Lookups.MunicipalLookups.IsUserMappedBorderType(b.BorderType))
            {
                result.AddWarning("Border", b.PlotId, "BORDERTYPE", $"Border type '{b.BorderType}' is not in the official 'Border Types' list.");
            }
        }

        // 4. Verify Export Schema Check (Temporary class field must be dropped in final export)
        if (verifyExportSchema && exportFieldNames != null)
        {
            var exportSet = exportFieldNames.Select(f => f.Trim().ToUpperInvariant()).ToHashSet();
            if (exportSet.Contains("PARCEL_CLASS"))
            {
                result.AddError("ExportSchema", "PARCEL", "PARCEL_CLASS", "Temporary field 'PARCEL_CLASS' must be dropped from the final export dataset.");
            }
        }

        // 5. DBF Text Length Limits Check
        result.TextLengthIssues = DbfTextLengthService.CheckTextLengths(parcels, borders);
        foreach (var issue in result.TextLengthIssues)
        {
            result.AddError(issue.EntityType, issue.EntityId, issue.FieldName,
                $"Text '{issue.OriginalValue}' is {issue.Utf8ByteLength} bytes in UTF-8, exceeding maximum template width of {issue.MaxAllowedBytes} bytes.");
        }

        // 6. Optional Areas Excel Check
        if (!string.IsNullOrWhiteSpace(areasExcelPath) && System.IO.File.Exists(areasExcelPath))
        {
            result.AreaReport = AreaComparisonService.CompareAreas(parcels, areasExcelPath, areaOptions ?? new AreaComparisonOptions());
            foreach (var e in result.AreaReport.Entries.Where(e => e.Status == "Mismatched"))
            {
                result.AddWarning("AreaCheck", e.PlotId, "AREA_SQM", $"Area mismatch: Computed {e.ComputedAreaSqm} m² vs Excel {e.ExcelAreaSqm} m² (Diff: {e.AreaDifferenceSqm} m²).");
            }
        }

        return result;
    }

    /// <summary>
    /// QA check: compares exported metric fields with frozen UTM values and flags errors on any mismatch.
    /// In a file geodatabase, Shape_Area and Shape_Length are system-managed and derive from geometry;
    /// they must not be reported as frozen sources.
    /// </summary>
    public static void ValidateFrozenMetrics(
        ParcelRecord parcel,
        double? exportedAreaSqm,
        double? exportedShapeArea,
        double? exportedShapeLength,
        ValidationResult result,
        double toleranceSqm = 0.5)
    {
        if (exportedAreaSqm.HasValue && Math.Abs(exportedAreaSqm.Value - parcel.AreaSqm) > toleranceSqm)
        {
            result.AddError("Parcel", parcel.PlotId, "AREA_SQM",
                $"Exported AREA_SQM ({exportedAreaSqm.Value:F2} m²) differs from frozen UTM planar area ({parcel.AreaSqm:F2} m²).");
        }
        if (exportedShapeArea.HasValue && Math.Abs(exportedShapeArea.Value - parcel.AreaSqm) > toleranceSqm)
        {
            result.AddError("Parcel", parcel.PlotId, "Shape_Area",
                $"Exported Shape_Area ({exportedShapeArea.Value:F2} m²) differs from frozen UTM planar area ({parcel.AreaSqm:F2} m²). System geodatabase shape fields must never overwrite frozen metrics.");
        }
        if (exportedShapeLength.HasValue && Math.Abs(exportedShapeLength.Value - parcel.ShapeLength) > 0.05)
        {
            result.AddError("Parcel", parcel.PlotId, "Shape_Leng",
                $"Exported Shape_Leng ({exportedShapeLength.Value:F2} m) differs from frozen UTM planar perimeter ({parcel.ShapeLength:F2} m).");
        }
    }

    public static void ValidateFrozenMetrics(
        BorderRecord border,
        double? exportedShapeLength,
        long? exportedBorderDesc,
        ValidationResult result,
        double toleranceMeters = 0.05)
    {
        if (exportedShapeLength.HasValue && Math.Abs(exportedShapeLength.Value - border.ShapeLength) > toleranceMeters)
        {
            result.AddError("Border", border.PlotId, "SHAPE_Leng",
                $"Exported SHAPE_Leng ({exportedShapeLength.Value:F2} m) differs from frozen UTM planar length ({border.ShapeLength:F2} m).");
        }
        if (exportedBorderDesc.HasValue && exportedBorderDesc.Value != border.BorderDesc)
        {
            result.AddError("Border", border.PlotId, "BORDERDESC",
                $"Exported BORDERDESC ({exportedBorderDesc.Value}) differs from frozen rounded length ({border.BorderDesc}).");
        }
    }
}
