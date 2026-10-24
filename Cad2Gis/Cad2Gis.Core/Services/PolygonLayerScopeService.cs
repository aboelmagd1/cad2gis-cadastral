using System;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

public static class PolygonLayerScopeService
{
    public const string ScopeMainAndSub = "كل الطبقات المختارة (Main & Sub)";
    public const string ScopeMainOnly = "القطع الرئيسية فقط (Main Only)";
    public const string ScopeAllExceptText = "كل المختار ما عدا النصوص (All Except Text)";

    public static readonly string[] AvailableScopes = new[]
    {
        ScopeMainAndSub,
        ScopeMainOnly,
        ScopeAllExceptText
    };

    public static bool IsLayerEligible(CadLayerInfo? layer, string? scope, bool isLineMode = false)
    {
        if (layer == null || !layer.IsSelected) return false;

        // When building polygons from lines (or in Line mode), all user-checked layers that are not text annotations are eligible!
        if (isLineMode)
        {
            return layer.Category != "Text & Annotations" 
                && layer.Role != LayerRole.PlotText 
                && layer.Role != LayerRole.UsageText 
                && layer.Role != LayerRole.Dimensions 
                && layer.GeometryType != "Text";
        }

        string s = scope ?? ScopeMainAndSub;

        if (s.Contains("Main Only") || s.Contains("الرئيسية فقط"))
        {
            return layer.Category == "Main Parcels" || layer.Role == LayerRole.Boundary;
        }
        else if (s.Contains("Except Text") || s.Contains("ما عدا النصوص"))
        {
            return layer.Category != "Text & Annotations" 
                && layer.Role != LayerRole.PlotText 
                && layer.Role != LayerRole.UsageText 
                && layer.Role != LayerRole.Dimensions 
                && layer.GeometryType != "Text";
        }
        else // Default: Main and Sub
        {
            // If the user explicitly checked it, accept unless it's text
            if (layer.Category != "Text & Annotations" && layer.GeometryType != "Text" && layer.Role != LayerRole.PlotText && layer.Role != LayerRole.UsageText)
            {
                return true;
            }

            return layer.Category == "Main Parcels" 
                || layer.Category == "Sub-Parcels / Utilities" 
                || layer.Role == LayerRole.Boundary 
                || layer.Role == LayerRole.Service;
        }
    }

    public static bool IsLayerEligible(CadLayerInfo? layer, string? scope) => IsLayerEligible(layer, scope, false);

    /// <summary>
    /// Builds a sanitized, case-insensitive SQL IN expression for the CAD Layer field:
    /// UPPER(Layer) IN ('LAYER1','LAYER2')
    /// Single quotes in layer names are escaped by doubling them (' -> '').
    /// </summary>
    public static string BuildLayerWhere(IEnumerable<string> layers)
    {
        if (layers == null) return "1=0";
        var escaped = layers
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => "'" + l.Trim().Replace("'", "''").ToUpperInvariant() + "'")
            .Distinct()
            .ToList();
        if (escaped.Count == 0) return "1=0";
        return $"UPPER(Layer) IN ({string.Join(",", escaped)})";
    }
}
