using System.Collections.Generic;

namespace Cad2Gis.Core.Models;

public class BorderRecord
{
    public long InternalId { get; set; }
    public string PlotId { get; set; } = string.Empty;        // PLOT_ID
    public string PlanNo { get; set; } = string.Empty;        // PLAN_NO
    public int BorderName { get; set; }                       // BORDERNAME (1 = North / شمال, 2 = East / شرق, 3 = South / جنوب, 4 = West / غرب)
    public string BorderType { get; set; } = "STR";           // BORDERTYPE (STR, PLT, SRV, WCH, LNE, PRK, MSQ, etc.)
    public long BorderDesc { get; set; }                      // BORDERDESC (Integer rounded metric length: Math.Round(SHAPE_Leng))
    public string Dimension { get; set; } = "Single";         // DIMENSION ("Single" or "Multi")
    public double ShapeLength { get; set; }                   // SHAPE_Leng (Exact planar metric length)
    public string Descriptio { get; set; } = string.Empty;    // DESCRIPTIO / DESCRIPTO (Arabic narrative description)

    // Ring & Extraction Metadata (Part A)
    public int RingIndex { get; set; } = 0;                   // 0 = Exterior ring, 1+ = Interior ring (hole)
    public double OutwardNormalAzimuth { get; set; }          // Azimuth of outward normal in degrees
    public int ConstituentPiecesCount { get; set; } = 1;      // Count of merged pieces forming this border line

    // Width Inference & Review Metadata (Section 3)
    public bool IsInferred { get; set; } = false;
    public double? MeasuredWidth { get; set; }
    public double? SuggestedWidth { get; set; }
    public double? RayLength { get; set; }
    public string? HitElementId { get; set; }
    public string? InferenceRule { get; set; }
    public List<double> RayLengths { get; set; } = new();
    public bool IsAccepted { get; set; } = true;

    public string BorderNameArabic => BorderName switch
    {
        1 => "شمال",
        2 => "شرق",
        3 => "جنوب",
        4 => "غرب",
        _ => "غير محدد"
    };

    public string BorderNameEnglish => BorderName switch
    {
        1 => "North",
        2 => "East",
        3 => "South",
        4 => "West",
        _ => "Unknown"
    };

    public override string ToString() => $"Border {BorderNameArabic} ({BorderName}) of Parcel {PlotId}: {Dimension}, {ShapeLength:F2}m, Type: {BorderType}, Desc: {Descriptio}{(IsInferred ? $" [INFERRED W={MeasuredWidth:F1}m]" : "")}";
}
