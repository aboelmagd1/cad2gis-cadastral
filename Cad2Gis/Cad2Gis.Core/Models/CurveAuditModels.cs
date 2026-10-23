using System;
using System.Collections.Generic;

namespace Cad2Gis.Core.Models;

/// <summary>
/// Geometric characteristics of an arc segment for curve audit and integrity verification.
/// </summary>
public class ArcAuditInfo
{
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Radius { get; set; }
    public double Length { get; set; }
    public bool IsCounterClockwise { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
}

/// <summary>
/// Detailed segment and metric breakdown for a single geometry.
/// </summary>
public class GeometryCurveAuditResult
{
    public long FeatureId { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public string GeometryType { get; set; } = "Polygon";
    public int TotalSegments { get; set; }
    public int LineSegmentsCount { get; set; }
    public int CircularArcsCount { get; set; }
    public int EllipticArcsCount { get; set; }
    public int BezierCurvesCount { get; set; }
    public int TotalCurveSegmentsCount => CircularArcsCount + EllipticArcsCount + BezierCurvesCount;
    public int VertexCount { get; set; }
    public double ExactLength { get; set; }
    public double ExactArea { get; set; }
    public List<ArcAuditInfo> Arcs { get; set; } = new();
}

/// <summary>
/// Comparison report between stages to verify that curve counts, centers, radii, and metrics
/// remain strictly preserved without accidental flattening or degradation.
/// </summary>
public class StagePreservationComparison
{
    public string StageName { get; set; } = string.Empty;
    public string FeatureClass { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int FeaturesChecked { get; set; }
    public int TotalCurveSegmentsBefore { get; set; }
    public int TotalCurveSegmentsAfter { get; set; }
    public int TotalVerticesBefore { get; set; }
    public int TotalVerticesAfter { get; set; }
    public double MaxLengthDifference { get; set; }
    public double MaxAreaDifference { get; set; }
    public double MaxArcCenterShift { get; set; }
    public double MaxArcRadiusDifference { get; set; }
    public double MaxDeviationWhereConverted { get; set; }
    public int UnavoidableConversionsCount { get; set; }
    public List<string> Violations { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public bool Passed => Violations.Count == 0;
}

/// <summary>
/// Comprehensive geometry preservation report spanning all stages of execution.
/// </summary>
public class GeometryPreservationReport
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int UtmZoneEpsg { get; set; }
    public List<StagePreservationComparison> StageComparisons { get; set; } = new();
    public List<GeometryCurveAuditResult> FeatureAudits { get; set; } = new();
    public bool AllStagesPassed => StageComparisons.TrueForAll(sc => sc.Passed);
}
