using System;
using System.Collections.Generic;
using System.Linq;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

public class PlotIdMatchReport
{
    public string SelectedPlotLayer { get; set; } = string.Empty;
    public int TotalParcels { get; set; }
    public int MatchedFromSelectedLayer { get; set; }
    public int MatchedFromSecondaryLayers { get; set; }
    public int MatchedViaProximity { get; set; }
    public int FallbackDummyIds { get; set; }

    public string GetSummary() =>
        $"Plot ID matching report: {MatchedFromSelectedLayer} parcels matched from '{SelectedPlotLayer}', " +
        $"{MatchedFromSecondaryLayers} from secondary layers, {MatchedViaProximity} via proximity, {FallbackDummyIds} remaining with dummy IDs.";
}

/// <summary>
/// Service responsible for extracting and resolving PLOT_ID and USAGE_TXT/REMARKS
/// from user-chosen CAD PlotText and UsageText layers, with strict priority and diagnostics.
/// </summary>
public static class PlotIdExtractorService
{
    /// <summary>
    /// Resolves the clean layer name of the chosen PlotText layer from the UI selection label.
    /// Strips any UI badge suffix like " (★ PlotText)" and ensures placeholders are treated as null.
    /// </summary>
    public static string? CleanLayerName(string? rawLayerLabel)
    {
        if (string.IsNullOrWhiteSpace(rawLayerLabel)) return null;
        string trimmed = rawLayerLabel.Trim();
        if (trimmed.StartsWith("(") && trimmed.EndsWith(")")) return null;
        if (trimmed.Contains("تلقائي")) return null;
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("none selected", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("(none selected)", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("(none)", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("(لا يوجد)", StringComparison.OrdinalIgnoreCase)) return null;

        int starIdx = trimmed.IndexOf(" (★", StringComparison.Ordinal);
        if (starIdx > 0)
        {
            trimmed = trimmed.Substring(0, starIdx).Trim();
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Checks whether a given text string looks like a cadastral plot number (e.g. numeric, alphanumeric like 105/A, fractions like 12/1, or with prefix).
    /// </summary>
    public static bool IsPlotNumberText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim();
        if (t.All(c => char.IsDigit(c) || c == '/' || c == '-' || c == ' ' || c == '.' || c == '\\')) return true;
        if (t.StartsWith("قطعة", StringComparison.OrdinalIgnoreCase) || t.StartsWith("رقم", StringComparison.OrdinalIgnoreCase)) return true;
        // Check for subdivided plot notation e.g. "105/A", "105/أ", "105-A", "A/105" (must contain at least one digit and <= 10 chars)
        if (t.Any(char.IsDigit) && t.Length <= 10 && t.All(c => char.IsLetterOrDigit(c) || c == '/' || c == '-' || c == ' ' || c == '.' || c == '\\'))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether a given text string is a land use or activity keyword (not a plot number).
    /// </summary>
    public static bool IsUsageText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim().ToLowerInvariant();
        return t.Contains("سكني") || t.Contains("تجاري") || t.Contains("مسجد") || t.Contains("حديقة") ||
               t.Contains("خدمات") || t.Contains("مرفق") || t.Contains("مدرسة") || t.Contains("res") ||
               t.Contains("comm") || t.Contains("mosque") || t.Contains("park");
    }

    /// <summary>
    /// Performs robust text joining for both PLOT_ID and USAGE_TXT/REMARKS according to strict cadastral rules.
    /// Produces a comprehensive TextJoinReport.
    /// </summary>
    public static TextJoinReport PerformTextJoin(
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyDictionary<long, List<(string Text, string LayerName)>> parcelInsideAnnots,
        IEnumerable<(string Text, double X, double Y, string LayerName)> allAnnots,
        IReadOnlyDictionary<long, (double CenterX, double CenterY)> parcelCenters,
        string? explicitPlotLayer,
        ISet<string>? secondaryPlotLayers = null,
        ISet<string>? usageLayers = null,
        double maxProximityMeters = 1.0,
        bool enableProximityFallback = false,
        Func<long, double, double, double>? distanceToParcelFunc = null)
    {
        string? cleanPlotLayer = CleanLayerName(explicitPlotLayer);
        var activePlotLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(cleanPlotLayer)) activePlotLayers.Add(cleanPlotLayer);
        if (secondaryPlotLayers != null)
        {
            foreach (var s in secondaryPlotLayers)
            {
                string? cs = CleanLayerName(s);
                if (!string.IsNullOrEmpty(cs)) activePlotLayers.Add(cs);
            }
        }

        var activeUsageLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (usageLayers != null)
        {
            foreach (var u in usageLayers)
            {
                string? cu = CleanLayerName(u);
                if (!string.IsNullOrEmpty(cu)) activeUsageLayers.Add(cu);
            }
        }

        var report = new TextJoinReport
        {
            TotalParcels = parcels.Count,
            SelectedPlotTextLayer = cleanPlotLayer ?? string.Empty,
            SelectedUsageTextLayers = activeUsageLayers.OrderBy(x => x).ToList()
        };

        // Initialize layer stats for each active layer
        var layerStatsMap = new Dictionary<string, LayerAnnotationStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in activePlotLayers)
        {
            layerStatsMap[l] = new LayerAnnotationStats { LayerName = l, Role = "PlotText" };
        }
        foreach (var u in activeUsageLayers)
        {
            if (!layerStatsMap.ContainsKey(u))
            {
                layerStatsMap[u] = new LayerAnnotationStats { LayerName = u, Role = "UsageText" };
            }
        }

        var allAnnotList = allAnnots?.Where(a => !string.IsNullOrWhiteSpace(a.Text)).ToList() 
                           ?? new List<(string Text, double X, double Y, string LayerName)>();

        // Count annotations read
        foreach (var a in allAnnotList)
        {
            if (layerStatsMap.TryGetValue(a.LayerName, out var ls))
            {
                ls.AnnotationsRead++;
            }
        }

        // Count annotations inside polygons
        foreach (var kvp in parcelInsideAnnots)
        {
            foreach (var insideItem in kvp.Value)
            {
                if (layerStatsMap.TryGetValue(insideItem.LayerName, out var ls))
                {
                    ls.AnnotationsInsidePolygon++;
                }
            }
        }

        var plotTextToParcelsMap = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);

        // Process each parcel
        foreach (var p in parcels)
        {
            // Reset fields
            p.PlotId = string.Empty;
            p.PlotNo1 = string.Empty;
            p.PlotText = string.Empty;
            p.PlotLayer = string.Empty;
            p.MatchMode = "None";

            var center = parcelCenters.TryGetValue(p.InternalId, out var c) ? c : (CenterX: p.CentroidX, CenterY: p.CentroidY);

            // 1. PLOT_ID Resolution (Only from active PlotText layers!)
            if (activePlotLayers.Count > 0 && parcelInsideAnnots.TryGetValue(p.InternalId, out var inside) && inside.Count > 0)
            {
                // Gather candidates from active PlotText layers that match plot number criteria
                // Exclude any layers that belong to active usage layers
                var plotCandidates = inside
                    .Where(a => activePlotLayers.Contains(a.LayerName) && !activeUsageLayers.Contains(a.LayerName))
                    .Where(a => IsPlotNumberText(a.Text))
                    .ToList();

                // 1. Priority: If explicit layer was specified and has annotations inside, restrict to explicit layer
                if (!string.IsNullOrEmpty(cleanPlotLayer))
                {
                    var explicitCandidates = inside
                        .Where(a => a.LayerName.Equals(cleanPlotLayer, StringComparison.OrdinalIgnoreCase) && !activeUsageLayers.Contains(a.LayerName))
                        .ToList();
                    if (explicitCandidates.Count > 0)
                    {
                        var explicitPlotNums = explicitCandidates.Where(a => IsPlotNumberText(a.Text)).ToList();
                        plotCandidates = explicitPlotNums.Count > 0 ? explicitPlotNums : explicitCandidates;
                    }
                    else
                    {
                        // Explicit layer selected by user had 0 matches inside this parcel
                        // Only fall back to secondary layers if no explicit layer or if secondary layers enabled
                        plotCandidates = new List<(string Text, string LayerName)>();
                    }
                }

                if (plotCandidates.Count == 1)
                {
                    var single = plotCandidates[0];
                    p.PlotId = single.Text.Trim();
                    p.PlotNo1 = p.PlotId;
                    p.PlotText = single.Text.Trim();
                    p.PlotLayer = single.LayerName.Trim();
                    p.MatchMode = "Contains";

                    if (layerStatsMap.TryGetValue(single.LayerName, out var ls)) ls.AnnotationsUsed++;
                }
                else if (plotCandidates.Count > 1)
                {
                    // Conflict: multiple candidate texts inside the parcel polygon
                    // Deterministic choice: closest to parcel centroid, then smaller value
                    var sorted = plotCandidates
                        .Select(cand =>
                        {
                            // Find distance to centroid if coordinate available
                            var annotLoc = allAnnotList.FirstOrDefault(a => a.LayerName.Equals(cand.LayerName, StringComparison.OrdinalIgnoreCase) && a.Text.Trim().Equals(cand.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                            double dist = annotLoc.Text != null
                                ? Math.Sqrt(Math.Pow(annotLoc.X - center.CenterX, 2) + Math.Pow(annotLoc.Y - center.CenterY, 2))
                                : 0.0;
                            return (Candidate: cand, Distance: dist);
                        })
                        .OrderBy(x => x.Distance)
                        .ThenBy(x => x.Candidate.Text.Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var best = sorted[0].Candidate;
                    p.PlotId = best.Text.Trim();
                    p.PlotNo1 = p.PlotId;
                    p.PlotText = best.Text.Trim();
                    p.PlotLayer = best.LayerName.Trim();
                    p.MatchMode = "Conflict";

                    string conflictDesc = $"Parcel ID {p.InternalId}: multiple candidates [{string.Join(", ", plotCandidates.Select(c => $"'{c.Text}'({c.LayerName})"))}] -> chose '{best.Text}'";
                    report.ParcelsWithConflicts.Add(conflictDesc);

                    if (layerStatsMap.TryGetValue(best.LayerName, out var ls)) ls.AnnotationsUsed++;
                }
            }

            // Fallback to Proximity (only if enabled and still unmatched)
            if (enableProximityFallback && string.IsNullOrEmpty(p.PlotId) && activePlotLayers.Count > 0)
            {
                var targetPlotAnnots = allAnnotList
                    .Where(a => activePlotLayers.Contains(a.LayerName) && !activeUsageLayers.Contains(a.LayerName) && IsPlotNumberText(a.Text))
                    .ToList();

                if (!string.IsNullOrEmpty(cleanPlotLayer))
                {
                    var exp = targetPlotAnnots.Where(a => a.LayerName.Equals(cleanPlotLayer, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (exp.Count > 0) targetPlotAnnots = exp;
                }

                if (targetPlotAnnots.Count > 0)
                {
                    var nearest = targetPlotAnnots
                        .Select(a => new
                        {
                            Annot = a,
                            Dist = distanceToParcelFunc != null
                                ? distanceToParcelFunc(p.InternalId, a.X, a.Y)
                                : Math.Sqrt(Math.Pow(a.X - center.CenterX, 2) + Math.Pow(a.Y - center.CenterY, 2))
                        })
                        .Where(x => x.Dist <= maxProximityMeters)
                        .OrderBy(x => x.Dist)
                        .ThenBy(x => x.Annot.Text.Trim(), StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();

                    if (nearest != null)
                    {
                        p.PlotId = nearest.Annot.Text.Trim();
                        p.PlotNo1 = p.PlotId;
                        p.PlotText = nearest.Annot.Text.Trim();
                        p.PlotLayer = nearest.Annot.LayerName.Trim();
                        p.MatchMode = "Nearest";

                        if (layerStatsMap.TryGetValue(nearest.Annot.LayerName, out var ls)) ls.AnnotationsUsed++;
                    }
                }
            }

            // Track duplicate usage of the same plot text across parcels
            if (!string.IsNullOrEmpty(p.PlotId))
            {
                if (!plotTextToParcelsMap.TryGetValue(p.PlotId, out var pList))
                {
                    pList = new List<long>();
                    plotTextToParcelsMap[p.PlotId] = pList;
                }
                pList.Add(p.InternalId);
            }
            else
            {
                report.ParcelsWithoutPlotText.Add($"Parcel ID {p.InternalId} (Class: {p.Class}, Area: {p.AreaSqm:F1} m²)");
            }

            report.CountsByMatchMode[p.MatchMode]++;

            // 2. USAGE_TXT and REMARKS Resolution (from active UsageText layers)
            if (activeUsageLayers.Count > 0)
            {
                if (parcelInsideAnnots.TryGetValue(p.InternalId, out var insideU) && insideU.Count > 0)
                {
                    var usageInside = insideU
                        .Where(a => activeUsageLayers.Contains(a.LayerName) && !IsPlotNumberText(a.Text))
                        .Select(a => (Text: a.Text.Trim(), Layer: a.LayerName.Trim()))
                        .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                        .Distinct()
                        .ToList();

                    if (usageInside.Count > 0)
                    {
                        p.UsageText = string.Join(" - ", usageInside.Select(x => x.Text));
                        p.Remarks = p.UsageText;
                        p.UsageLayer = string.Join(";", usageInside.Select(x => x.Layer).Distinct());

                        foreach (var uItem in usageInside)
                        {
                            if (layerStatsMap.TryGetValue(uItem.Layer, out var ls)) ls.AnnotationsUsed++;
                        }
                    }
                }

                // Proximity fallback for Usage if enabled
                if (enableProximityFallback && string.IsNullOrWhiteSpace(p.UsageText))
                {
                    var targetUsageAnnots = allAnnotList
                        .Where(a => activeUsageLayers.Contains(a.LayerName) && !IsPlotNumberText(a.Text))
                        .ToList();

                    if (targetUsageAnnots.Count > 0)
                    {
                        var nearestU = targetUsageAnnots
                            .Select(a => new
                            {
                                a.Text,
                                a.LayerName,
                                Dist = Math.Sqrt(Math.Pow(a.X - center.CenterX, 2) + Math.Pow(a.Y - center.CenterY, 2))
                            })
                            .Where(x => x.Dist <= 15.0)
                            .OrderBy(x => x.Dist)
                            .FirstOrDefault();

                        if (nearestU != null)
                        {
                            p.UsageText = nearestU.Text.Trim();
                            p.Remarks = nearestU.Text.Trim();
                            p.UsageLayer = nearestU.LayerName.Trim();

                            if (layerStatsMap.TryGetValue(nearestU.LayerName, out var ls)) ls.AnnotationsUsed++;
                        }
                    }
                }
            }
        }

        // Duplicate plot texts check
        foreach (var kvp in plotTextToParcelsMap)
        {
            if (kvp.Value.Count > 1)
            {
                report.DuplicateTextsUsedAcrossParcels.Add($"Plot text '{kvp.Key}' used by {kvp.Value.Count} parcels: [{string.Join(", ", kvp.Value)}]");
            }
        }

        report.LayerStats = layerStatsMap.Values.ToList();
        return report;
    }

    /// <summary>
    /// Backward-compatible overload for AssignPlotIds that delegates to PerformTextJoin.
    /// </summary>
    public static PlotIdMatchReport AssignPlotIds(
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyDictionary<long, List<(string Text, string LayerName)>> parcelInsideAnnots,
        IEnumerable<(string Text, double X, double Y, string LayerName)> allAnnots,
        IReadOnlyDictionary<long, (double CenterX, double CenterY)> parcelCenters,
        string? explicitPlotLayer,
        ISet<string>? secondaryPlotLayers = null,
        ISet<string>? usageLayers = null,
        double maxProximityMeters = 1.0,
        Func<long, double, double, double>? distanceToParcelFunc = null)
    {
        var joinReport = PerformTextJoin(
            parcels,
            parcelInsideAnnots,
            allAnnots,
            parcelCenters,
            explicitPlotLayer,
            secondaryPlotLayers,
            usageLayers,
            maxProximityMeters,
            enableProximityFallback: true,
            distanceToParcelFunc);

        return new PlotIdMatchReport
        {
            SelectedPlotLayer = joinReport.SelectedPlotTextLayer,
            TotalParcels = joinReport.TotalParcels,
            MatchedFromSelectedLayer = joinReport.CountsByMatchMode.GetValueOrDefault("Contains", 0),
            MatchedFromSecondaryLayers = joinReport.CountsByMatchMode.GetValueOrDefault("Conflict", 0),
            MatchedViaProximity = joinReport.CountsByMatchMode.GetValueOrDefault("Nearest", 0),
            FallbackDummyIds = joinReport.CountsByMatchMode.GetValueOrDefault("None", 0)
        };
    }
}
