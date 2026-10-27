using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Cad2Gis.Core.Models;

public class LayerAnnotationStats
{
    public string LayerName { get; set; } = string.Empty;
    public string Role { get; set; } = "PlotText"; // "PlotText" or "UsageText"
    public int AnnotationsRead { get; set; }
    public int AnnotationsInsidePolygon { get; set; }
    public int AnnotationsUsed { get; set; }
    public int AnnotationsUnused => Math.Max(0, AnnotationsRead - AnnotationsUsed);
}

public class TextJoinReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public int TotalParcels { get; set; }
    public string SelectedPlotTextLayer { get; set; } = string.Empty;
    public List<string> SelectedUsageTextLayers { get; set; } = new();

    public List<LayerAnnotationStats> LayerStats { get; set; } = new();

    public List<string> ParcelsWithoutPlotText { get; set; } = new();
    public List<string> ParcelsWithConflicts { get; set; } = new();
    public List<string> DuplicateTextsUsedAcrossParcels { get; set; } = new();

    public Dictionary<string, int> CountsByMatchMode { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Contains", 0 },
        { "Nearest", 0 },
        { "Conflict", 0 },
        { "None", 0 }
    };

    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }

    public bool HasWarning { get; set; }
    public string? WarningMessage { get; set; }

    // Spatial Reference & Extent Diagnostics (used when annotations don't match)
    public string AnnotationSpatialReference { get; set; } = string.Empty;
    public string ParcelSpatialReference { get; set; } = string.Empty;
    public string AnnotationExtent { get; set; } = string.Empty;
    public string ParcelExtent { get; set; } = string.Empty;

    public string GetSummary()
    {
        int matched = CountsByMatchMode.GetValueOrDefault("Contains", 0) +
                      CountsByMatchMode.GetValueOrDefault("Nearest", 0) +
                      CountsByMatchMode.GetValueOrDefault("Conflict", 0);
        int missing = CountsByMatchMode.GetValueOrDefault("None", 0);
        return $"Text Join: {matched}/{TotalParcels} parcels matched (Contains: {CountsByMatchMode.GetValueOrDefault("Contains")}, " +
               $"Nearest: {CountsByMatchMode.GetValueOrDefault("Nearest")}, Conflict: {CountsByMatchMode.GetValueOrDefault("Conflict")}, " +
               $"Missing: {missing}).";
    }

    public string GenerateFormattedReport() => GenerateFullReportText();

    public string GenerateFullReportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine($"CAD2GIS TEXT JOIN REPORT — {GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Total Parcels Processed: {TotalParcels}");
        sb.AppendLine($"Selected PlotText Layer: {(string.IsNullOrEmpty(SelectedPlotTextLayer) ? "(None Selected)" : SelectedPlotTextLayer)}");
        sb.AppendLine($"Selected UsageText Layers: {(SelectedUsageTextLayers.Count > 0 ? string.Join(", ", SelectedUsageTextLayers) : "(None Selected)")}");
        sb.AppendLine();

        sb.AppendLine("--- LAYER ANNOTATION STATISTICS ---");
        if (LayerStats.Count == 0)
        {
            sb.AppendLine("  (No PlotText or UsageText layers were active for join)");
        }
        else
        {
            foreach (var ls in LayerStats)
            {
                sb.AppendLine($"  • Layer: '{ls.LayerName}' [{ls.Role}]");
                sb.AppendLine($"      Read: {ls.AnnotationsRead} | Inside Polygons: {ls.AnnotationsInsidePolygon} | Used: {ls.AnnotationsUsed} | Unused: {ls.AnnotationsUnused}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("--- MATCH MODES SUMMARY ---");
        foreach (var kvp in CountsByMatchMode)
        {
            sb.AppendLine($"  • {kvp.Key}: {kvp.Value}");
        }
        sb.AppendLine();

        if (ParcelsWithConflicts.Count > 0)
        {
            sb.AppendLine($"--- CONFLICTS (Parcels with multiple candidate texts) [{ParcelsWithConflicts.Count}] ---");
            foreach (var c in ParcelsWithConflicts)
            {
                sb.AppendLine($"  ! {c}");
            }
            sb.AppendLine();
        }

        if (DuplicateTextsUsedAcrossParcels.Count > 0)
        {
            sb.AppendLine($"--- DUPLICATE USAGE (Same text mapped to multiple parcels) [{DuplicateTextsUsedAcrossParcels.Count}] ---");
            foreach (var d in DuplicateTextsUsedAcrossParcels)
            {
                sb.AppendLine($"  ! {d}");
            }
            sb.AppendLine();
        }

        if (ParcelsWithoutPlotText.Count > 0)
        {
            sb.AppendLine($"--- PARCELS WITHOUT PLOT TEXT [{ParcelsWithoutPlotText.Count}] ---");
            foreach (var p in ParcelsWithoutPlotText)
            {
                sb.AppendLine($"  - {p}");
            }
            sb.AppendLine();
        }

        if (HasWarning && !string.IsNullOrEmpty(WarningMessage))
        {
            sb.AppendLine("--- DIAGNOSTIC WARNING ---");
            sb.AppendLine(WarningMessage);
            if (!string.IsNullOrEmpty(AnnotationSpatialReference) || !string.IsNullOrEmpty(ParcelSpatialReference))
            {
                sb.AppendLine($"  Annotation Spatial Reference : {AnnotationSpatialReference}");
                sb.AppendLine($"  Parcel Spatial Reference     : {ParcelSpatialReference}");
                sb.AppendLine($"  Annotation Extent            : {AnnotationExtent}");
                sb.AppendLine($"  Parcel Extent                : {ParcelExtent}");
            }
            sb.AppendLine();
        }

        if (HasError && !string.IsNullOrEmpty(ErrorMessage))
        {
            sb.AppendLine("--- ERROR ---");
            sb.AppendLine(ErrorMessage);
            sb.AppendLine();
        }

        sb.AppendLine("================================================================================");
        return sb.ToString();
    }
}
