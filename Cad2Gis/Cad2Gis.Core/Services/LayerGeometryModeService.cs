using System;

namespace Cad2Gis.Core.Services;

/// <summary>
/// Service managing Cad Layer Geometry Selection Modes:
/// 1. Line (خطوط فقط): Directly converts eligible lines into parcels via Feature To Polygon.
/// 2. Polygon (مضلعات فقط): Reads CAD/Dataset polygons and ignores lines unless explicitly forced.
/// 3. Line with data layer (خطوط مع طبقة البيانات): Builds parcels like Line mode, then determines which CAD layer created each parcel boundary to assign its USAGE and class.
/// </summary>
public static class LayerGeometryModeService
{
    public const string ModeLine = "Line (خطوط فقط)";
    public const string ModePolygon = "Polygon (مضلعات فقط)";
    public const string ModeLineWithDataLayer = "Line with data layer (خطوط مع طبقة البيانات)";

    // Backward-compatibility aliases for legacy profiles / code
    public const string ModeBoth = "Both (مضلعات وخطوط معاً)";
    public const string ModePolygonLine = "Polygon line";

    public static readonly string[] AvailableModes = new[]
    {
        ModeLine,
        ModePolygon,
        ModeLineWithDataLayer
    };

    public static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return ModeLineWithDataLayer;
        string m = mode.Trim();

        // 1. Line mode (pure lines without data layer attribution)
        if (m.Equals(ModeLine, StringComparison.OrdinalIgnoreCase) ||
            m.Equals("Line", StringComparison.OrdinalIgnoreCase) ||
            m.Equals("Lines", StringComparison.OrdinalIgnoreCase) ||
            (m.StartsWith("Line", StringComparison.OrdinalIgnoreCase) && !m.Contains("data", StringComparison.OrdinalIgnoreCase) && !m.Contains("طبقة", StringComparison.OrdinalIgnoreCase) && !m.Contains("Both") && !m.Contains("معاً")))
        {
            return ModeLine;
        }

        // 2. Polygon mode
        if (m.Equals(ModePolygon, StringComparison.OrdinalIgnoreCase) ||
            m.Equals("Polygon", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("مضلعات فقط"))
        {
            return ModePolygon;
        }

        // 3. Line with data layer (including legacy "Both" and "Polygon line")
        return ModeLineWithDataLayer;
    }

    public static bool IsLineWithDataLayer(string? mode) =>
        NormalizeMode(mode) == ModeLineWithDataLayer;

    public static bool ShouldIngestPolygons(string? mode)
    {
        string norm = NormalizeMode(mode);
        // Only Polygon mode reads CAD polygon features; Line and Line with data layer build from lines
        return norm == ModePolygon;
    }

    public static bool ShouldPolygonizeLines(string? mode, bool forceBuildLines = false)
    {
        if (forceBuildLines) return true;
        string norm = NormalizeMode(mode);
        return norm == ModeLine || norm == ModeLineWithDataLayer;
    }
}
