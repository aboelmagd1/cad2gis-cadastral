using System.Collections.Generic;

namespace Cad2Gis.Core.Models;

public enum ParcelClass
{
    Main,
    Sub,
    Divided,
    PublicService,
    Park,
    Mosque,
    Utility
}

public class ParcelRecord
{
    public long InternalId { get; set; }
    public ParcelClass Class { get; set; } = ParcelClass.Main;

    /// <summary>
    /// Temporary text field tag (MAIN / SUB) on the working dataset only.
    /// Dropped at final export to strictly match template schemas.
    /// </summary>
    public string TemporaryParcelClass => Class == ParcelClass.Sub ? "SUB" : "MAIN";

    /// <summary>
    /// Official PARCEL_TYP field: 1 = MAIN (القطع الرئيسية), 2 = SUB (المرافق والخدمات).
    /// </summary>
    public short ParcelTyp => (short)(Class == ParcelClass.Sub ? 2 : 1);

    /// <summary>
    /// Optional sub-parcel classification type (e.g. GARDEN, CORRIDOR, UTILITY)
    /// </summary>
    public string? SubType { get; set; }

    // MOMRAH / Balady Mandatory Attributes (matching templates/PARCEL.shp)
    public int RealestateType { get; set; } = 2;              // REALESTA_1 (2 = أرض فضاء)
    public string Neighbourhood { get; set; } = string.Empty; // NEIGHBOURH
    public string RegionId { get; set; } = string.Empty;      // REGION_ID (No silent default)
    public string CityId { get; set; } = string.Empty;        // CITY_ID (No silent default)
    public string Address { get; set; } = string.Empty;       // ADDRESS
    public string Usage { get; set; } = string.Empty;         // USAGE (No silent default)
    public int IsBuilding { get; set; } = 0;                  // ISBUILDING (0 = لا, 1 = نعم)
    public int BuildingType { get; set; } = 1;                // BUILDINGTY (1 = مسلح حديث)
    public string Remarks { get; set; } = string.Empty;       // REMARKS (No silent default)
    public string? Layout { get; set; }                       // LAYOUT
    public string PlotId { get; set; } = string.Empty;        // PLOT_ID (Unique Parcel ID, e.g. "101")
    public string? BlockNo { get; set; }                      // BLOCK_NO (Block identifier, e.g. "1")
    public string PlanNo { get; set; } = string.Empty;        // PLAN_NO
    public string PlotNo1 { get; set; } = string.Empty;       // PLOT_NO1
    public string? PlotNo2 { get; set; }                      // PLOT_NO2
    public string? PlotNo3 { get; set; }                      // PLOT_NO3
    public string? PlotNo4 { get; set; }                      // PLOT_NO4
    public string? PlotNo5 { get; set; }                      // PLOT_NO5
    public string? PlotNo6 { get; set; }                      // PLOT_NO6
    public string RequestType { get; set; } = string.Empty;   // REQUEST_TY (No silent default)
    public string RequestNo { get; set; } = string.Empty;     // REQUEST_NO
    public string ReportNo { get; set; } = string.Empty;      // REPORT_NO

    // Frozen Planar UTM Metric Values
    public double AreaSqm { get; set; }                       // AREA_SQM
    public double ShapeArea { get; set; }                     // Shape_Area
    public double ShapeLength { get; set; }                   // Shape_Leng

    public double CentroidX { get; set; }
    public double CentroidY { get; set; }

    // Associated 4-cardinal borders (Only for MAIN parcels)
    public List<BorderRecord> Borders { get; set; } = new();

    /// <summary>
    /// Raw usage/description annotation or layer text captured during ingestion.
    /// Internal field, never exported to shapefiles.
    /// </summary>
    public string UsageText { get; set; } = string.Empty;

    /// <summary>
    /// Layer-level default USAGE code assigned from Step 1 layer selection or dominant layer.
    /// Internal field, used during Step 3 precedence resolution.
    /// </summary>
    public string LayerUsage { get; set; } = string.Empty;

    /// <summary>
    /// Records which source supplied the final USAGE value ("Manual", "Excel", "Layer", "Fallback").
    /// </summary>
    public string UsageSource { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if USAGE was manually entered/edited by user, which must be protected.
    /// </summary>
    public bool HasUserAssignedUsage { get; set; } = false;

    /// <summary>
    /// Internal tracking of feature provenance (e.g. source layer or construction method).
    /// Kept out of REMARKS so it never leaks into exported data.
    /// </summary>
    public string Provenance { get; set; } = string.Empty;

    /// <summary>
    /// Dominant CAD layer that created the parcel (working field SRC_LAYER).
    /// </summary>
    public string SourceLayer { get; set; } = string.Empty;

    /// <summary>
    /// All contributing layers with boundary meters, e.g. "A:120.5;B:3.2" (working field SRC_LAYERS).
    /// </summary>
    public string SourceLayers { get; set; } = string.Empty;

    /// <summary>
    /// Raw matched plot text (working field PLOT_TXT).
    /// </summary>
    public string PlotText { get; set; } = string.Empty;

    /// <summary>
    /// Layer from which raw matched plot text originated (working field PLOT_LYR).
    /// </summary>
    public string PlotLayer { get; set; } = string.Empty;

    /// <summary>
    /// Layer from which raw matched usage text originated (working field USAGE_LYR).
    /// </summary>
    public string UsageLayer { get; set; } = string.Empty;

    /// <summary>
    /// Spatial match mode ("Contains", "Nearest", "Conflict", "None") (working field MATCH_MODE).
    /// </summary>
    public string MatchMode { get; set; } = "None";

    public override string ToString() => $"Parcel {PlotId} [{TemporaryParcelClass}] (Plan: {PlanNo}, Usage: {Usage}, Area: {AreaSqm:F2} m²)";
}

