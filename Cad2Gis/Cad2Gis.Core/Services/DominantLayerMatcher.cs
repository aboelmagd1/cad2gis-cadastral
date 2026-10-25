using System;
using System.Collections.Generic;
using System.Linq;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Lookups;

namespace Cad2Gis.Core.Services;

public class LayerSegment2D
{
    public Point2D Start { get; }
    public Point2D End { get; }
    public string LayerName { get; }
    public double Length => Start.DistanceTo(End);
    public Point2D MidPoint => new((Start.X + End.X) * 0.5, (Start.Y + End.Y) * 0.5);

    public Box2D Envelope => new(
        Math.Min(Start.X, End.X),
        Math.Min(Start.Y, End.Y),
        Math.Max(Start.X, End.X),
        Math.Max(Start.Y, End.Y));

    public LayerSegment2D(Point2D start, Point2D end, string layerName)
    {
        Start = start;
        End = end;
        LayerName = layerName ?? string.Empty;
    }
}

public class LayerAttributionResult
{
    public string DominantLayer { get; set; } = string.Empty;
    public double DominantLength { get; set; }
    public double TotalMatchedLength { get; set; }
    public double DominantShare { get; set; } // 0.0 to 1.0
    public bool IsMixedLayers { get; set; }
    public bool IsTie { get; set; }
    public bool NoSegmentsFound { get; set; }
    public ParcelClass AssignedClass { get; set; } = ParcelClass.Main;
    public string AssignedUsage { get; set; } = string.Empty;
    public string UsageText { get; set; } = string.Empty;
    public Dictionary<string, double> LengthPerLayer { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetSummary()
    {
        if (NoSegmentsFound) return "No boundary segments matched to CAD layers.";
        string mixedStr = IsMixedLayers ? " [MIXED_LAYERS]" : "";
        string tieStr = IsTie ? " [TIE]" : "";
        return $"Dominant: '{DominantLayer}' ({DominantLength:F2}m / {TotalMatchedLength:F2}m, {DominantShare:P1}){mixedStr}{tieStr}";
    }
}

public class DominantLayerMatcherOptions
{
    public double LayerMatchToleranceMeters { get; set; } = 0.05;
    public double DominantLayerMinShare { get; set; } = 0.60;
}

/// <summary>
/// Service implementing Section C.3 of CAD2GIS specifications:
/// Matches split CAD polyline segments to parcel boundaries using a spatial index,
/// calculates dominant layer, determines class (MAIN/SUB), assigns USAGE, and flags MIXED_LAYERS / ties.
/// </summary>
public class DominantLayerMatcher
{
    private readonly SpatialGrid2D<LayerSegment2D> _segmentGrid;
    private readonly List<LayerSegment2D> _segments;
    private readonly DominantLayerMatcherOptions _options;

    public DominantLayerMatcher(IEnumerable<LayerSegment2D> segments, DominantLayerMatcherOptions? options = null, double gridCellSize = 100.0)
    {
        _options = options ?? new DominantLayerMatcherOptions();
        _segmentGrid = new SpatialGrid2D<LayerSegment2D>(gridCellSize);
        _segments = segments.ToList();

        foreach (var seg in _segments)
        {
            _segmentGrid.Insert(seg, seg.Envelope.Expand(_options.LayerMatchToleranceMeters));
        }
    }

    /// <summary>
    /// Attributes a parcel to its dominant CAD layer by examining boundary segments.
    /// </summary>
    /// <param name="boundarySegments">The constituent segments of the parcel boundary.</param>
    /// <param name="subLayerNames">Set of layer names belonging to the Sub-Parcels / Utilities category.</param>
    /// <param name="layerUsageMap">Optional map of layer name to selected USAGE code.</param>
    /// <param name="layerUsageTextMap">Optional map of layer name to Arabic usage description text.</param>
    public LayerAttributionResult AttributeParcel(
        IReadOnlyList<Segment2D> boundarySegments,
        ISet<string>? subLayerNames = null,
        IReadOnlyDictionary<string, string>? layerUsageMap = null,
        IReadOnlyDictionary<string, string>? layerUsageTextMap = null)
    {
        var result = new LayerAttributionResult();
        if (boundarySegments == null || boundarySegments.Count == 0 || _segments.Count == 0)
        {
            result.NoSegmentsFound = true;
            return result;
        }

        // Build parcel boundary bounding box
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var seg in boundarySegments)
        {
            minX = Math.Min(minX, Math.Min(seg.Start.X, seg.End.X));
            minY = Math.Min(minY, Math.Min(seg.Start.Y, seg.End.Y));
            maxX = Math.Max(maxX, Math.Max(seg.Start.X, seg.End.X));
            maxY = Math.Max(maxY, Math.Max(seg.Start.Y, seg.End.Y));
        }
        var parcelBox = new Box2D(minX, minY, maxX, maxY).Expand(_options.LayerMatchToleranceMeters);

        // Query spatial index for candidate segments
        var candidates = _segmentGrid.Query(parcelBox);
        var matchedSegments = new HashSet<LayerSegment2D>();

        foreach (var candidate in candidates)
        {
            var mid = candidate.MidPoint;

            // Fast rejection against parcel outer bounding box
            if (mid.X < parcelBox.MinX || mid.X > parcelBox.MaxX ||
                mid.Y < parcelBox.MinY || mid.Y > parcelBox.MaxY)
            {
                continue;
            }

            // Find minimum distance from candidate segment midpoint to any boundary segment
            double minDist = double.MaxValue;
            for (int i = 0; i < boundarySegments.Count; i++)
            {
                double d = DistancePointToSegment(mid, boundarySegments[i].Start, boundarySegments[i].End);
                if (d < minDist)
                {
                    minDist = d;
                    if (minDist <= _options.LayerMatchToleranceMeters) break;
                }
            }

            if (minDist <= _options.LayerMatchToleranceMeters + 1e-6)
            {
                matchedSegments.Add(candidate);
            }
        }

        if (matchedSegments.Count == 0)
        {
            result.NoSegmentsFound = true;
            return result;
        }

        // Sum lengths per layer
        foreach (var mSeg in matchedSegments)
        {
            string lyr = mSeg.LayerName;
            if (!result.LengthPerLayer.TryGetValue(lyr, out double currLen))
                currLen = 0.0;
            result.LengthPerLayer[lyr] = currLen + mSeg.Length;
        }

        result.TotalMatchedLength = result.LengthPerLayer.Values.Sum();
        if (result.TotalMatchedLength <= 1e-9)
        {
            result.NoSegmentsFound = true;
            return result;
        }

        // Sort layers deterministically: longest length descending, then layer name ascending
        var rankedLayers = result.LengthPerLayer
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var top = rankedLayers[0];
        result.DominantLayer = top.Key;
        result.DominantLength = top.Value;
        result.DominantShare = top.Value / result.TotalMatchedLength;

        // Check for ties: top 2 have equal length within 1e-4
        if (rankedLayers.Count > 1 && Math.Abs(rankedLayers[0].Value - rankedLayers[1].Value) < 1e-4)
        {
            result.IsTie = true;
        }

        // Check for MIXED_LAYERS: dominant share < threshold (default 60%)
        if (result.DominantShare < _options.DominantLayerMinShare - 1e-6)
        {
            result.IsMixedLayers = true;
        }

        // Class assignment: if dominant layer is in Sub group -> Sub, else Main
        bool isSub = subLayerNames != null && subLayerNames.Contains(result.DominantLayer);
        result.AssignedClass = isSub ? ParcelClass.Sub : ParcelClass.Main;

        // Usage assignment from Step 1 layer dropdown
        if (layerUsageMap != null && layerUsageMap.TryGetValue(result.DominantLayer, out var usageCode))
        {
            if (MunicipalLookups.IsOfficialUsageType(usageCode))
            {
                result.AssignedUsage = usageCode;
            }
        }

        // Usage text description assignment
        if (layerUsageTextMap != null && layerUsageTextMap.TryGetValue(result.DominantLayer, out var uTxt))
        {
            result.UsageText = uTxt;
        }

        return result;
    }

    private static double DistancePointToSegment(Point2D p, Point2D a, Point2D b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-12) return p.DistanceTo(a);

        double t = Math.Max(0.0, Math.Min(1.0, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq));
        var proj = new Point2D(a.X + t * dx, a.Y + t * dy);
        return p.DistanceTo(proj);
    }
}
