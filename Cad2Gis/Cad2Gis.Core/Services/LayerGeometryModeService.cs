namespace Cad2Gis.Core.Services;

/// <summary>
/// Service managing Cad Layer Geometry Selection Modes: Both (Polygons & Lines), Polygon only, or Line only.
/// Both mode (Default): Ingests native CAD polygons and also converts eligible polylines into polygons.
/// Line mode: Ignores CAD polygons and directly converts eligible lines into parcels.
/// Polygon mode: Reads CAD/Dataset polygons and ignores lines unless explicitly forced.
/// </summary>
public static class LayerGeometryModeService
{
    public const string ModeBoth = "Both (مضلعات وخطوط معاً)";
    public const string ModePolygon = "Polygon (مضلعات فقط)";
    public const string ModeLine = "Line (خطوط فقط)";

    public static readonly string[] AvailableModes = new[]
    {
        ModeBoth,
        ModePolygon,
        ModeLine
    };

    public static bool ShouldIngestPolygons(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return true;
        // False only if explicitly "Line (خطوط فقط)"
        if (mode == ModeLine || 
            (mode.StartsWith("Line", StringComparison.OrdinalIgnoreCase) && !mode.Contains("Both") && !mode.Contains("معاً")) || 
            mode.Contains("خطوط فقط"))
            return false;
        return true;
    }

    public static bool ShouldPolygonizeLines(string? mode, bool forceBuildLines = false)
    {
        if (forceBuildLines) return true;
        if (string.IsNullOrWhiteSpace(mode)) return true;
        // False only if explicitly "Polygon (مضلعات فقط)"
        if (mode == ModePolygon || 
            (mode.StartsWith("Polygon", StringComparison.OrdinalIgnoreCase) && !mode.Contains("Both") && !mode.Contains("معاً")) || 
            mode.Contains("مضلعات فقط"))
            return false;
        return true;
    }
}
