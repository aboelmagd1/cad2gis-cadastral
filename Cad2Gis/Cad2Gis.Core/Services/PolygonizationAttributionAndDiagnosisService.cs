using System;
using System.Collections.Generic;
using System.Linq;
using Cad2Gis.Core.Geometry;

namespace Cad2Gis.Core.Services;

public class PolygonAttributionResult
{
    public string DominantLayer { get; set; } = string.Empty;
    public List<string> LayerNames { get; set; } = new();
    public bool IsMixedLayers { get; set; }
    public Dictionary<string, double> LengthByLayer { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class NetworkDiagnosisResult
{
    public bool HasLinesInSelectedLayers { get; set; }
    public int TotalLines { get; set; }
    public int EligibleLines { get; set; }
    public int ClosedRingsCount { get; set; }
    public int DanglesCount { get; set; }
    public double? SmallestDangleGapMeters { get; set; }
    public double ToleranceUsedMeters { get; set; }
    public string FormattedDiagnosis { get; set; } = string.Empty;
    public Dictionary<string, int> LineCountByLayer { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ClosedRingCountByLayer { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Service implementing P3 requirements:
/// - P3.2: Geometric attribution of polygons to candidate layers (dominant layer, contributing layers, MIXED_LAYERS flag).
/// - P3.3: Data-driven network diagnosis when 0 polygons are built (no guessing, measured dangles, smallest gaps).
/// </summary>
public static class PolygonizationAttributionAndDiagnosisService
{
    /// <summary>
    /// Measures boundary length share coincident with each layer's lines and returns dominant layer,
    /// all contributing layers, and sets IsMixedLayers if more than one layer contributes > 10% of perimeter.
    /// </summary>
    public static PolygonAttributionResult AttributePolygonFromBoundary(
        IReadOnlyList<CoreBoundarySegment> boundarySegments,
        IReadOnlyList<(CoreBoundarySegment Segment, string LayerName)> inputLines,
        double coincidenceTolerance = 0.05)
    {
        var result = new PolygonAttributionResult();
        if (boundarySegments == null || boundarySegments.Count == 0)
        {
            return result;
        }

        double totalPerimeter = boundarySegments.Sum(s => s.Length);
        if (totalPerimeter <= 1e-6)
        {
            return result;
        }

        if (inputLines == null || inputLines.Count == 0)
        {
            result.DominantLayer = "UNKNOWN_LAYER";
            return result;
        }

        // For each boundary segment, find matching input line(s)
        foreach (var bSeg in boundarySegments)
        {
            double bLen = bSeg.Length;
            if (bLen < 1e-6) continue;

            string bestLayer = "";
            double bestMatchScore = double.MaxValue;

            foreach (var inLine in inputLines)
            {
                if (inLine.Segment.Length < 1e-6) continue;

                // Check endpoints match in forward or reverse
                double dStartStart = bSeg.Start.DistanceTo(inLine.Segment.Start);
                double dEndEnd = bSeg.End.DistanceTo(inLine.Segment.End);
                double dStartEnd = bSeg.Start.DistanceTo(inLine.Segment.End);
                double dEndStart = bSeg.End.DistanceTo(inLine.Segment.Start);

                double endDist = Math.Min(dStartStart + dEndEnd, dStartEnd + dEndStart);
                if (endDist <= coincidenceTolerance * 2.0 && endDist < bestMatchScore)
                {
                    bestMatchScore = endDist;
                    bestLayer = inLine.LayerName;
                }
                else
                {
                    // Check if midpoint of boundary segment lies within tolerance of input segment
                    var midPt = new Point2D((bSeg.Start.X + bSeg.End.X) / 2.0, (bSeg.Start.Y + bSeg.End.Y) / 2.0);
                    double distToInLine = DistancePointToSegment(midPt, inLine.Segment.Start, inLine.Segment.End);
                    if (distToInLine <= coincidenceTolerance && distToInLine < bestMatchScore)
                    {
                        bestMatchScore = distToInLine;
                        bestLayer = inLine.LayerName;
                    }
                }
            }

            if (!string.IsNullOrEmpty(bestLayer))
            {
                result.LengthByLayer[bestLayer] = result.LengthByLayer.TryGetValue(bestLayer, out var cur)
                    ? cur + bLen
                    : bLen;
            }
        }

        if (result.LengthByLayer.Count == 0)
        {
            // If no coincident segments detected, find closest layer
            var mid = boundarySegments[0].Start;
            var nearest = inputLines.OrderBy(l => Math.Min(mid.DistanceTo(l.Segment.Start), mid.DistanceTo(l.Segment.End))).FirstOrDefault();
            result.DominantLayer = !string.IsNullOrEmpty(nearest.LayerName) ? nearest.LayerName : "UNKNOWN_LAYER";
            result.LayerNames.Add(result.DominantLayer);
            return result;
        }

        // Dominant layer is the one with the maximum length share
        result.DominantLayer = result.LengthByLayer.OrderByDescending(kv => kv.Value).First().Key;
        result.LayerNames = result.LengthByLayer.Keys.OrderBy(k => k).ToList();

        // Flag MIXED_LAYERS when more than one layer contributes more than 10%
        int significantLayersCount = result.LengthByLayer.Values.Count(len => (len / totalPerimeter) > 0.10);
        result.IsMixedLayers = significantLayersCount > 1;

        return result;
    }

    /// <summary>
    /// Computes data-driven network diagnosis:
    /// line count and closed-ring count per layer, dangling endpoints (degree 1),
    /// and smallest gap distance between dangle pairs.
    /// </summary>
    public static NetworkDiagnosisResult DiagnoseLineNetwork(
        IReadOnlyList<(CoreBoundarySegment Segment, string LayerName)> lines,
        IReadOnlyList<string> selectedLayers,
        double snapToleranceMeters = 0.0,
        string spatialRefInfo = "")
    {
        var diag = new NetworkDiagnosisResult
        {
            TotalLines = lines?.Count ?? 0,
            ToleranceUsedMeters = snapToleranceMeters
        };

        var selectedSet = new HashSet<string>(selectedLayers?.Where(s => !string.IsNullOrWhiteSpace(s)) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        var eligibleLines = lines?.Where(l => selectedSet.Count == 0 || selectedSet.Contains(l.LayerName)).ToList() ?? new List<(CoreBoundarySegment, string)>();
        diag.EligibleLines = eligibleLines.Count;
        diag.HasLinesInSelectedLayers = eligibleLines.Count > 0;

        if (eligibleLines.Count == 0)
        {
            diag.FormattedDiagnosis = "no lines in selected layers";
            return diag;
        }

        // 1. Counts per layer and closed rings count
        foreach (var l in eligibleLines)
        {
            diag.LineCountByLayer[l.LayerName] = diag.LineCountByLayer.TryGetValue(l.LayerName, out var c) ? c + 1 : 1;
            bool isClosed = l.Segment.Start.DistanceTo(l.Segment.End) <= Math.Max(0.001, snapToleranceMeters);
            if (isClosed)
            {
                diag.ClosedRingsCount++;
                diag.ClosedRingCountByLayer[l.LayerName] = diag.ClosedRingCountByLayer.TryGetValue(l.LayerName, out var cc) ? cc + 1 : 1;
            }
        }

        // 2. Cluster endpoints to find degrees
        double nodeClusterTol = Math.Max(0.001, snapToleranceMeters);
        var endpoints = new List<Point2D>();
        var endpointLineIndices = new List<int>();

        foreach (var l in eligibleLines)
        {
            endpoints.Add(l.Segment.Start);
            endpoints.Add(l.Segment.End);
        }

        var nodeClusters = new List<List<Point2D>>();
        foreach (var pt in endpoints)
        {
            var cluster = nodeClusters.FirstOrDefault(c => c.Any(node => node.DistanceTo(pt) <= nodeClusterTol));
            if (cluster != null)
            {
                cluster.Add(pt);
            }
            else
            {
                nodeClusters.Add(new List<Point2D> { pt });
            }
        }

        // Dangles are clustered nodes with degree 1
        var dangleNodes = new List<Point2D>();
        foreach (var c in nodeClusters)
        {
            if (c.Count == 1)
            {
                dangleNodes.Add(c[0]);
            }
        }

        diag.DanglesCount = dangleNodes.Count;

        // Smallest gap between dangle pairs
        if (dangleNodes.Count >= 2)
        {
            double minGap = double.MaxValue;
            for (int i = 0; i < dangleNodes.Count; i++)
            {
                for (int j = i + 1; j < dangleNodes.Count; j++)
                {
                    double d = dangleNodes[i].DistanceTo(dangleNodes[j]);
                    if (d > 1e-4 && d < minGap)
                    {
                        minGap = d;
                    }
                }
            }
            if (minGap < double.MaxValue)
            {
                diag.SmallestDangleGapMeters = minGap;
            }
        }

        // Build data-driven formatted diagnostic message (measured facts only)
        if (diag.DanglesCount > 0)
        {
            string gapStr = diag.SmallestDangleGapMeters.HasValue
                ? $"{diag.SmallestDangleGapMeters.Value:F3} m"
                : "none detected";
            diag.FormattedDiagnosis = $"{diag.DanglesCount} dangling endpoints; smallest gap between two endpoints: {gapStr}; tolerance used: {snapToleranceMeters} m";
            if (!string.IsNullOrEmpty(spatialRefInfo))
            {
                diag.FormattedDiagnosis += $"; Spatial Reference: {spatialRefInfo}";
            }
        }
        else if (diag.ClosedRingsCount > 0)
        {
            diag.FormattedDiagnosis = $"{diag.ClosedRingsCount} closed rings detected across {diag.EligibleLines} lines; tolerance used: {snapToleranceMeters} m";
        }
        else
        {
            diag.FormattedDiagnosis = "cause not determined";
        }

        return diag;
    }

    private static double DistancePointToSegment(Point2D pt, Point2D a, Point2D b)
    {
        double l2 = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
        if (l2 < 1e-9) return pt.DistanceTo(a);

        double t = ((pt.X - a.X) * (b.X - a.X) + (pt.Y - a.Y) * (b.Y - a.Y)) / l2;
        t = Math.Max(0.0, Math.Min(1.0, t));
        var projection = new Point2D(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
        return pt.DistanceTo(projection);
    }
}
