using System;
using System.Collections.Generic;

namespace Cad2Gis.Core.Models;

public class ExportFeatureMetadata
{
    public string PlotId { get; set; } = string.Empty;
    public int? BorderName { get; set; }
    public int RingIndex { get; set; } = 0;
    public int ParcelTyp { get; set; } = 1; // 1 = MAIN, 2 = SUB

    // Frozen metric values at export time
    public double? AreaSqm { get; set; }
    public double? ShapeArea { get; set; }
    public double? ShapeLength { get; set; }
    public long? BorderDesc { get; set; }

    // User-editable attributes
    public string? BorderType { get; set; }
    public string? Descriptio { get; set; }
    public string? Usage { get; set; }
    public string? Address { get; set; }
    public string? Neighbourhood { get; set; }
    public string? Remarks { get; set; }

    // Geometry signature (e.g. vertex count, centroid X/Y, or coordinate hash)
    public string? GeometrySignature { get; set; }
}

public class ExportDatasetInfo
{
    public string DatasetName { get; set; } = string.Empty; // "PARCEL" or "Border"
    public string FilePath { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty; // SHA-256
    public long FileSize { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public int FeatureCount { get; set; }
    public List<ExportFeatureMetadata> Features { get; set; } = new();
}

public class ExportHistoryItem
{
    public string RunId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string ExportFolder { get; set; } = string.Empty;
    public string ParcelPath { get; set; } = string.Empty;
    public string BorderPath { get; set; } = string.Empty;
    public string SourceCadName { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public int WorkingUtmZone { get; set; }
}

public class ExportStateRecord
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ExportFolder { get; set; } = string.Empty;
    public string ParcelPath { get; set; } = string.Empty;
    public string BorderPath { get; set; } = string.Empty;
    public string ExportCrs { get; set; } = "GCS_WGS_1984";
    public int WorkingUtmZone { get; set; } = 32637;
    public string SourceCadName { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;

    public Dictionary<string, ExportDatasetInfo> Datasets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ExportHistoryItem> History { get; set; } = new();
}

public class FeatureAttributeDiff
{
    public string Key { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
}

public class FeatureGeometryDiff
{
    public string Key { get; set; } = string.Empty;
    public double StoredArea { get; set; }
    public double NewArea { get; set; }
    public double StoredPerimeter { get; set; }
    public double NewPerimeter { get; set; }
    public bool ExceedsTolerance { get; set; }
}

public class ExportDiffReport
{
    public bool WasEditedAfterExport { get; set; }
    public string StatePath { get; set; } = string.Empty;
    public DateTime ExportTimestamp { get; set; }
    public List<string> AddedFeatures { get; set; } = new();
    public List<string> DeletedFeatures { get; set; } = new();
    public List<FeatureGeometryDiff> GeometryChanges { get; set; } = new();
    public List<FeatureAttributeDiff> AttributeChanges { get; set; } = new();
    public bool HasFrozenMetricDiscrepancies => GeometryChanges.Any(g => g.ExceedsTolerance);

    public string GetSummaryText()
    {
        var parts = new List<string>();
        if (AddedFeatures.Count > 0) parts.Add($"Added: {AddedFeatures.Count}");
        if (DeletedFeatures.Count > 0) parts.Add($"Deleted: {DeletedFeatures.Count}");
        if (GeometryChanges.Count > 0) parts.Add($"Geometry changed: {GeometryChanges.Count}");
        if (AttributeChanges.Count > 0) parts.Add($"Attributes changed: {AttributeChanges.Count}");
        return parts.Count > 0 ? string.Join(", ", parts) : "No changes detected.";
    }
}
