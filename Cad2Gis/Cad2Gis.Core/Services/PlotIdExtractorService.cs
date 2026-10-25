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
/// Service responsible for extracting and resolving PLOT_ID from user-chosen CAD PlotText layers
/// and assigning them to parcel polygons with strict priority and spatial proximity fallback.
/// </summary>
public static class PlotIdExtractorService
{
    /// <summary>
    /// Resolves the clean layer name of the chosen PlotText layer from the UI selection label.
    /// Strips any UI badge suffix like " (★ PlotText)".
    /// </summary>
    public static string? CleanLayerName(string? rawLayerLabel)
    {
        if (string.IsNullOrWhiteSpace(rawLayerLabel)) return null;
        string trimmed = rawLayerLabel.Trim();
        if (trimmed.StartsWith("(") && trimmed.EndsWith(")")) return null;
        if (trimmed.Contains("تلقائي")) return null;

        int starIdx = trimmed.IndexOf(" (★", StringComparison.Ordinal);
        if (starIdx > 0)
        {
            trimmed = trimmed.Substring(0, starIdx).Trim();
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Checks whether a given text string looks like a cadastral plot number (e.g. numeric, fractions like 12/1, or with prefix).
    /// </summary>
    public static bool IsPlotNumberText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim();
        if (t.All(c => char.IsDigit(c) || c == '/' || c == '-' || c == ' ' || c == '.' || c == '\\')) return true;
        if (t.StartsWith("قطعة", StringComparison.OrdinalIgnoreCase) || t.StartsWith("رقم", StringComparison.OrdinalIgnoreCase)) return true;
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
    /// Matches and assigns PLOT_ID to parcels from spatial annotations with strict precedence:
    /// 1. Exact match from user-selected PlotText layer inside the parcel.
    /// 2. If multiple inside the parcel, prefer one matching IsPlotNumberText, or first non-empty.
    /// 3. Match from secondary PlotText layers inside the parcel.
    /// 4. Proximity matching for remaining parcels from the user-selected PlotText layer.
    /// </summary>
    public static PlotIdMatchReport AssignPlotIds(
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyDictionary<long, List<(string Text, string LayerName)>> parcelInsideAnnots,
        IEnumerable<(string Text, double X, double Y, string LayerName)> allAnnots,
        IReadOnlyDictionary<long, (double CenterX, double CenterY)> parcelCenters,
        string? explicitPlotLayer,
        ISet<string>? secondaryPlotLayers = null,
        ISet<string>? usageLayers = null,
        double maxProximityMeters = 20.0)
    {
        var report = new PlotIdMatchReport
        {
            SelectedPlotLayer = explicitPlotLayer ?? "(Auto)",
            TotalParcels = parcels.Count
        };

        if (parcels.Count == 0) return report;

        string? targetExplicit = CleanLayerName(explicitPlotLayer);
        var secondarySet = new HashSet<string>(secondaryPlotLayers ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var usageSet = new HashSet<string>(usageLayers ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        var matchedParcelIds = new HashSet<long>();
        var usedPlotTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Phase 1: Inside Parcel Matching with Strict Priority
        foreach (var p in parcels)
        {
            if (!parcelInsideAnnots.TryGetValue(p.InternalId, out var inside) || inside.Count == 0)
                continue;

            // 1. Priority 1: User's explicitly chosen PlotText layer
            if (!string.IsNullOrEmpty(targetExplicit))
            {
                var explicitAnnots = inside.Where(a => a.LayerName.Equals(targetExplicit, StringComparison.OrdinalIgnoreCase)).ToList();
                if (explicitAnnots.Count > 0)
                {
                    var best = explicitAnnots.FirstOrDefault(a => IsPlotNumberText(a.Text) && !usageSet.Contains(a.LayerName));
                    if (string.IsNullOrWhiteSpace(best.Text))
                    {
                        best = explicitAnnots.FirstOrDefault(a => !usageSet.Contains(a.LayerName));
                    }
                    if (string.IsNullOrWhiteSpace(best.Text))
                    {
                        best = explicitAnnots[0];
                    }

                    if (!string.IsNullOrWhiteSpace(best.Text))
                    {
                        p.PlotId = best.Text.Trim();
                        matchedParcelIds.Add(p.InternalId);
                        usedPlotTexts.Add(p.PlotId);
                        report.MatchedFromSelectedLayer++;
                        continue;
                    }
                }
            }

            // 2. Priority 2: Secondary PlotText layers
            if (secondarySet.Count > 0)
            {
                var secondaryAnnots = inside.Where(a => secondarySet.Contains(a.LayerName)).ToList();
                if (secondaryAnnots.Count > 0)
                {
                    var best = secondaryAnnots.FirstOrDefault(a => IsPlotNumberText(a.Text) && !usageSet.Contains(a.LayerName));
                    if (string.IsNullOrWhiteSpace(best.Text))
                    {
                        best = secondaryAnnots.FirstOrDefault(a => !usageSet.Contains(a.LayerName));
                    }
                    if (string.IsNullOrWhiteSpace(best.Text))
                    {
                        best = secondaryAnnots[0];
                    }

                    if (!string.IsNullOrWhiteSpace(best.Text))
                    {
                        p.PlotId = best.Text.Trim();
                        matchedParcelIds.Add(p.InternalId);
                        usedPlotTexts.Add(p.PlotId);
                        report.MatchedFromSecondaryLayers++;
                        continue;
                    }
                }
            }

            // 3. Priority 3: Any text matching plot number format that is not in a usage layer
            var plotFormatAnnot = inside.FirstOrDefault(a => !usageSet.Contains(a.LayerName) && IsPlotNumberText(a.Text));
            if (!string.IsNullOrWhiteSpace(plotFormatAnnot.Text))
            {
                p.PlotId = plotFormatAnnot.Text.Trim();
                matchedParcelIds.Add(p.InternalId);
                usedPlotTexts.Add(p.PlotId);
                report.MatchedFromSecondaryLayers++;
                continue;
            }

            // 4. Priority 4: Any non-usage text inside the parcel if parcel has a dummy ID
            if (p.PlotId.StartsWith("P", StringComparison.OrdinalIgnoreCase) && int.TryParse(p.PlotId.Substring(1), out _))
            {
                var nonUsage = inside.FirstOrDefault(a => !usageSet.Contains(a.LayerName) && !IsUsageText(a.Text));
                if (!string.IsNullOrWhiteSpace(nonUsage.Text))
                {
                    p.PlotId = nonUsage.Text.Trim();
                    matchedParcelIds.Add(p.InternalId);
                    usedPlotTexts.Add(p.PlotId);
                    report.MatchedFromSecondaryLayers++;
                }
            }
        }

        // Phase 2: Proximity Matching for Unmatched Parcels
        var remainingParcels = parcels.Where(p => !matchedParcelIds.Contains(p.InternalId)).ToList();
        if (remainingParcels.Count > 0 && allAnnots != null)
        {
            var annotList = allAnnots.Where(a => !string.IsNullOrWhiteSpace(a.Text)).ToList();

            // A. Proximity from the user's chosen PlotText layer first!
            if (!string.IsNullOrEmpty(targetExplicit))
            {
                var targetExplicitAnnots = annotList
                    .Where(a => a.LayerName.Equals(targetExplicit, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (targetExplicitAnnots.Count > 0)
                {
                    foreach (var p in remainingParcels)
                    {
                        if (matchedParcelIds.Contains(p.InternalId)) continue;
                        if (!parcelCenters.TryGetValue(p.InternalId, out var center)) continue;

                        var closest = targetExplicitAnnots
                            .Where(a => !usedPlotTexts.Contains(a.Text.Trim()))
                            .Select(a => new
                            {
                                Text = a.Text.Trim(),
                                Dist = Math.Sqrt(Math.Pow(a.X - center.CenterX, 2) + Math.Pow(a.Y - center.CenterY, 2))
                            })
                            .Where(x => x.Dist <= maxProximityMeters)
                            .OrderBy(x => x.Dist)
                            .FirstOrDefault();

                        if (closest != null)
                        {
                            p.PlotId = closest.Text;
                            matchedParcelIds.Add(p.InternalId);
                            usedPlotTexts.Add(p.PlotId);
                            report.MatchedViaProximity++;
                        }
                    }
                }
            }

            // B. Proximity from secondary plot layers if still unmatched
            var stillUnmatched = remainingParcels.Where(p => !matchedParcelIds.Contains(p.InternalId)).ToList();
            if (stillUnmatched.Count > 0 && secondarySet.Count > 0)
            {
                var secondaryCandidates = annotList
                    .Where(a => secondarySet.Contains(a.LayerName) && IsPlotNumberText(a.Text))
                    .ToList();

                if (secondaryCandidates.Count > 0)
                {
                    foreach (var p in stillUnmatched)
                    {
                        if (matchedParcelIds.Contains(p.InternalId)) continue;
                        if (!parcelCenters.TryGetValue(p.InternalId, out var center)) continue;

                        var closest = secondaryCandidates
                            .Where(a => !usedPlotTexts.Contains(a.Text.Trim()))
                            .Select(a => new
                            {
                                Text = a.Text.Trim(),
                                Dist = Math.Sqrt(Math.Pow(a.X - center.CenterX, 2) + Math.Pow(a.Y - center.CenterY, 2))
                            })
                            .Where(x => x.Dist <= maxProximityMeters)
                            .OrderBy(x => x.Dist)
                            .FirstOrDefault();

                        if (closest != null)
                        {
                            p.PlotId = closest.Text;
                            matchedParcelIds.Add(p.InternalId);
                            usedPlotTexts.Add(p.PlotId);
                            report.MatchedViaProximity++;
                        }
                    }
                }
            }
        }

        report.FallbackDummyIds = parcels.Count - matchedParcelIds.Count;
        return report;
    }
}
