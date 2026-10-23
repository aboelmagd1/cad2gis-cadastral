using System;
using System.Collections.Generic;

namespace Cad2Gis.Core.Models;

/// <summary>
/// Status and accounting for each selected CAD layer participating in polygonization.
/// </summary>
public class LayerPolygonizationManifestItem
{
    public string LayerName { get; set; } = string.Empty;
    public string Category { get; set; } = "Main Parcels";
    public int EntitiesRead { get; set; }
    public int UsedAsIsClosed { get; set; }
    public int EnteredNetworkLines { get; set; }
    public int IgnoredCount { get; set; }
    public string IgnoredReason { get; set; } = string.Empty;
    public int DanglesOrphansCount { get; set; }
    public int PolygonsContributed { get; set; }
    public bool HasZeroEntitiesError => EntitiesRead == 0;
    public string Status => HasZeroEntitiesError 
        ? "ERROR (0 Entities)" 
        : (PolygonsContributed > 0 
            ? (DanglesOrphansCount > 0 ? "OK (With Dangles)" : "OK") 
            : (DanglesOrphansCount > 0 ? "Warning (Dangles)" : "OK"));
}

/// <summary>
/// Complete manifest and coverage report generated before and after line polygonization.
/// </summary>
public class PolygonizationManifestReport
{
    public string Mode { get; set; } = "Group of all selected layers"; // or "Per layer"
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsStale { get; set; } = false;
    public string StaleReason { get; set; } = string.Empty;
    public List<LayerPolygonizationManifestItem> Layers { get; set; } = new();
    public int TotalPolygonsBuilt { get; set; }
    public int TotalDanglesFound { get; set; }
    public int TotalClosedKeptAsIs { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> AuditLogs { get; set; } = new();

    public bool HasZeroEntityErrors => Layers.Exists(l => l.HasZeroEntitiesError);
}
