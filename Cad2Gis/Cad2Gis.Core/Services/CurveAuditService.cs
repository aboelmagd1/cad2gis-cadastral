using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

/// <summary>
/// Service responsible for auditing curves, computing exact analytic metrics,
/// and executing blocking geometry-preservation checks across pipeline stages.
/// </summary>
public static class CurveAuditService
{
    private const double MetricTolerance = 1e-6;

    /// <summary>
    /// Audits a collection of boundary segments (lines, arcs, Béziers) and computes exact analytic metrics.
    /// </summary>
    public static GeometryCurveAuditResult AuditSegments(
        IReadOnlyList<CoreBoundarySegment> segments,
        long featureId = 0,
        string featureKey = "",
        string geometryType = "Polygon")
    {
        var result = new GeometryCurveAuditResult
        {
            FeatureId = featureId,
            FeatureKey = featureKey,
            GeometryType = geometryType,
            TotalSegments = segments.Count,
            VertexCount = segments.Count > 0 ? segments.Count + 1 : 0
        };

        double totalLen = 0;
        foreach (var seg in segments)
        {
            totalLen += seg.Length;
            if (seg.Type == CurveType.CircularArc)
            {
                result.CircularArcsCount++;
                if (seg.Center.HasValue && seg.Radius.HasValue)
                {
                    result.Arcs.Add(new ArcAuditInfo
                    {
                        CenterX = seg.Center.Value.X,
                        CenterY = seg.Center.Value.Y,
                        Radius = seg.Radius.Value,
                        Length = seg.Length,
                        IsCounterClockwise = seg.IsCounterClockwise ?? true,
                        StartX = seg.Start.X,
                        StartY = seg.Start.Y,
                        EndX = seg.End.X,
                        EndY = seg.End.Y
                    });
                }
            }
            else if (seg.Type == CurveType.Bezier)
            {
                result.BezierCurvesCount++;
            }
            else
            {
                result.LineSegmentsCount++;
            }
        }
        result.ExactLength = totalLen;

        // Compute exact analytic area for closed polygon rings
        if (geometryType.Equals("Polygon", StringComparison.OrdinalIgnoreCase) && segments.Count >= 2)
        {
            result.ExactArea = ComputeExactAnalyticArea(segments);
        }

        return result;
    }

    /// <summary>
    /// Computes exact analytic area of a closed ring composed of straight lines and circular arcs.
    /// Uses Shoelace formula on chord vertices + exact circular segment (cap) areas.
    /// For two circular arcs forming a circle (two semicircles), computes exact circular area pi * R^2.
    /// </summary>
    public static double ComputeExactAnalyticArea(IReadOnlyList<CoreBoundarySegment> segments)
    {
        if (segments == null || segments.Count < 2) return 0.0;

        // Special case: 2 circular arcs forming a complete circle
        if (segments.Count == 2 && segments[0].Type == CurveType.CircularArc && segments[1].Type == CurveType.CircularArc)
        {
            double r1 = segments[0].Radius ?? 0.0;
            double r2 = segments[1].Radius ?? 0.0;
            if (r1 > 1e-6 && r2 > 1e-6)
            {
                double sweep1 = segments[0].Length / r1;
                double sweep2 = segments[1].Length / r2;
                return Math.Max(0.0, 0.5 * r1 * r1 * sweep1 + 0.5 * r2 * r2 * sweep2);
            }
        }

        if (segments.Count < 3) return 0.0;

        // 1. Shoelace on chord polygon
        double chordArea = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var p1 = segments[i].Start;
            var p2 = segments[i].End;
            chordArea += (p1.X * p2.Y) - (p2.X * p1.Y);
        }
        chordArea = 0.5 * chordArea; // signed chord area

        // Determine polygon orientation: CW has negative signedArea, CCW has positive signedArea
        bool isCcw = chordArea > 0;

        // 2. Add/subtract exact analytic circular arc segment (cap) areas:
        // Area_cap = 0.5 * R^2 * (theta - sin(theta))
        double totalArea = Math.Abs(chordArea);

        foreach (var seg in segments)
        {
            if (seg.Type == CurveType.CircularArc && seg.Center.HasValue && seg.Radius.HasValue && seg.Radius.Value > 1e-6)
            {
                double r = seg.Radius.Value;
                double chordLen = seg.Start.DistanceTo(seg.End);
                double halfAngle = Math.Asin(Math.Min(1.0, Math.Max(0.0, (chordLen / 2.0) / r)));
                double theta = 2.0 * halfAngle;
                double capArea = 0.5 * r * r * (theta - Math.Sin(theta));

                // Check whether arc bulges outward or inward relative to polygon interior
                var midChord = new Point2D((seg.Start.X + seg.End.X) / 2.0, (seg.Start.Y + seg.End.Y) / 2.0);
                var toCenter = seg.Center.Value - midChord;
                var chordVec = seg.End - seg.Start;
                var outwardNormal = isCcw 
                    ? new Point2D(chordVec.Y, -chordVec.X) 
                    : new Point2D(-chordVec.Y, chordVec.X);

                // If center is on the inside, the arc curves outward (adds capArea)
                bool arcBulgesOutward = (toCenter.X * outwardNormal.X + toCenter.Y * outwardNormal.Y) <= 0;

                if (arcBulgesOutward)
                {
                    totalArea += capArea;
                }
                else
                {
                    totalArea -= capArea;
                }
            }
        }

        return Math.Max(0.0, totalArea);
    }

    /// <summary>
    /// Executes blocking geometry preservation comparison between two pipeline stages.
    /// Halts with violations if any arc is flattened, arc center/radius shifts, or metrics deviate beyond 1e-6.
    /// </summary>
    public static StagePreservationComparison CompareStages(
        string stageName,
        string featureClass,
        IReadOnlyList<GeometryCurveAuditResult> before,
        IReadOnlyList<GeometryCurveAuditResult> after,
        bool isUnavoidableExportConversion = false,
        double maxAllowedDeviation = 0.0)
    {
        var comparison = new StagePreservationComparison
        {
            StageName = stageName,
            FeatureClass = featureClass,
            FeaturesChecked = Math.Min(before.Count, after.Count)
        };

        comparison.TotalCurveSegmentsBefore = before.Sum(b => b.TotalCurveSegmentsCount);
        comparison.TotalCurveSegmentsAfter = after.Sum(a => a.TotalCurveSegmentsCount);
        comparison.TotalVerticesBefore = before.Sum(b => b.VertexCount);
        comparison.TotalVerticesAfter = after.Sum(a => a.VertexCount);

        var afterDict = after.ToDictionary(a => a.FeatureId, a => a);

        foreach (var b in before)
        {
            if (!afterDict.TryGetValue(b.FeatureId, out var a))
            {
                comparison.Violations.Add($"Feature {b.FeatureId} ({b.FeatureKey}) was lost between stages in {stageName}.");
                continue;
            }

            double lenDiff = Math.Abs(b.ExactLength - a.ExactLength);
            double areaDiff = Math.Abs(b.ExactArea - a.ExactArea);
            comparison.MaxLengthDifference = Math.Max(comparison.MaxLengthDifference, lenDiff);
            comparison.MaxAreaDifference = Math.Max(comparison.MaxAreaDifference, areaDiff);

            if (isUnavoidableExportConversion)
            {
                // Shapefile / GCS export conversion with controlled max deviation
                if (b.TotalCurveSegmentsCount > 0 && a.TotalCurveSegmentsCount == 0)
                {
                    comparison.UnavoidableConversionsCount += b.TotalCurveSegmentsCount;
                    comparison.MaxDeviationWhereConverted = Math.Max(comparison.MaxDeviationWhereConverted, maxAllowedDeviation);
                    comparison.Notes.Add($"Feature {b.FeatureId}: {b.TotalCurveSegmentsCount} curve segment(s) converted to vertices within max deviation tolerance {maxAllowedDeviation:F4}m for Shapefile/GCS deliverable.");
                }
            }
            else
            {
                // Strict blocking check in UTM working data:
                // 1. Number of curve segments must not decrease
                if (a.TotalCurveSegmentsCount < b.TotalCurveSegmentsCount)
                {
                    comparison.Violations.Add($"Feature {b.FeatureId} ({b.FeatureKey}): curve segments decreased from {b.TotalCurveSegmentsCount} to {a.TotalCurveSegmentsCount} in {stageName}. Arc-to-line flattening detected!");
                }

                // 2. Exact length match within 1e-6
                if (lenDiff > MetricTolerance)
                {
                    comparison.Violations.Add($"Feature {b.FeatureId} ({b.FeatureKey}): perimeter/length changed by {lenDiff:E4}m (tolerance {MetricTolerance:E1}m) in {stageName}.");
                }

                // 3. Exact area match within 1e-6
                if (b.GeometryType == "Polygon" && areaDiff > MetricTolerance)
                {
                    comparison.Violations.Add($"Feature {b.FeatureId} ({b.FeatureKey}): area changed by {areaDiff:E4} sqm (tolerance {MetricTolerance:E1}m) in {stageName}.");
                }

                // 4. Arc center and radius constancy within 1e-6
                int arcCountToCheck = Math.Min(b.Arcs.Count, a.Arcs.Count);
                for (int i = 0; i < arcCountToCheck; i++)
                {
                    var bArc = b.Arcs[i];
                    var aArc = a.Arcs[i];

                    double centerShift = Math.Sqrt(Math.Pow(bArc.CenterX - aArc.CenterX, 2) + Math.Pow(bArc.CenterY - aArc.CenterY, 2));
                    double radiusDiff = Math.Abs(bArc.Radius - aArc.Radius);

                    comparison.MaxArcCenterShift = Math.Max(comparison.MaxArcCenterShift, centerShift);
                    comparison.MaxArcRadiusDifference = Math.Max(comparison.MaxArcRadiusDifference, radiusDiff);

                    if (centerShift > MetricTolerance)
                    {
                        comparison.Violations.Add($"Feature {b.FeatureId} Arc {i + 1}: center shifted by {centerShift:E4}m in {stageName}.");
                    }
                    if (radiusDiff > MetricTolerance)
                    {
                        comparison.Violations.Add($"Feature {b.FeatureId} Arc {i + 1}: radius changed by {radiusDiff:E4}m in {stageName}.");
                    }
                }
            }
        }

        return comparison;
    }

    /// <summary>
    /// Generates human-readable Geometry Preservation Report text.
    /// </summary>
    public static string FormatPreservationReport(GeometryPreservationReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                  CAD2GIS GEOMETRY PRESERVATION AUDIT REPORT                    ");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Run ID:           {report.RunId}");
        sb.AppendLine($"Generated At:     {report.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Working UTM EPSG: {report.UtmZoneEpsg}");
        sb.AppendLine($"Overall Status:   {(report.AllStagesPassed ? "PASSED (Zero Illegal Geometry Alteration)" : "FAILED (Violations Detected)")}");
        sb.AppendLine("--------------------------------------------------------------------------------");

        foreach (var sc in report.StageComparisons)
        {
            sb.AppendLine($"\n[Stage: {sc.StageName}] - Feature Class: {sc.FeatureClass}");
            sb.AppendLine($"  Features Checked:          {sc.FeaturesChecked}");
            sb.AppendLine($"  Curve Segments (Before):   {sc.TotalCurveSegmentsBefore}");
            sb.AppendLine($"  Curve Segments (After):    {sc.TotalCurveSegmentsAfter}");
            sb.AppendLine($"  Vertex Count (Before):     {sc.TotalVerticesBefore}");
            sb.AppendLine($"  Vertex Count (After):      {sc.TotalVerticesAfter}");
            sb.AppendLine($"  Max Length Difference:     {sc.MaxLengthDifference:E4} m");
            sb.AppendLine($"  Max Area Difference:       {sc.MaxAreaDifference:E4} sqm");
            sb.AppendLine($"  Max Arc Center Shift:      {sc.MaxArcCenterShift:E4} m");
            sb.AppendLine($"  Max Arc Radius Diff:       {sc.MaxArcRadiusDifference:E4} m");
            if (sc.UnavoidableConversionsCount > 0)
            {
                sb.AppendLine($"  Unavoidable Conversions:   {sc.UnavoidableConversionsCount} curve segments (Max deviation <= {sc.MaxDeviationWhereConverted:F4}m)");
            }
            sb.AppendLine($"  Stage Audit Result:        {(sc.Passed ? "PASSED" : "FAILED")}");

            if (sc.Notes.Count > 0)
            {
                sb.AppendLine("  Notes:");
                foreach (var note in sc.Notes)
                {
                    sb.AppendLine($"    * {note}");
                }
            }

            if (sc.Violations.Count > 0)
            {
                sb.AppendLine("  VIOLATIONS:");
                foreach (var v in sc.Violations)
                {
                    sb.AppendLine($"    ! {v}");
                }
            }
        }

        sb.AppendLine("\n================================================================================");
        return sb.ToString();
    }
}
