using System;
using System.Collections.Generic;
using System.Linq;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

/// <summary>
/// An entity representing a line, open polyline, closed polyline, arc, circle, or spline
/// composed of one or more true geometric segments (lines, arcs, Béziers).
/// </summary>
public class CurvePolylineEntity
{
    public string LayerName { get; set; } = string.Empty;
    public string Category { get; set; } = "Main Parcels";
    public List<CoreBoundarySegment> Segments { get; set; } = new();
    public object? NativeGeometryTag { get; set; }

    public bool IsClosed
    {
        get
        {
            if (Segments.Count == 0) return false;
            for (int i = 0; i < Segments.Count - 1; i++)
            {
                if (Segments[i].End.DistanceTo(Segments[i + 1].Start) >= 1e-4)
                    return false;
            }
            var start = Segments[0].Start;
            var end = Segments[Segments.Count - 1].End;
            return start.DistanceTo(end) < 1e-4;
        }
    }
}

/// <summary>
/// Resulting closed polygon ring constructed by chaining original segments intact.
/// </summary>
public class CurvePolygonRing
{
    public List<CoreBoundarySegment> Segments { get; set; } = new();
    public string SourceLayer { get; set; } = string.Empty;
    public List<string> LayerNames { get; set; } = new();
    public bool IsMixedLayers { get; set; } = false;
    public string Category { get; set; } = "Main Parcels";
    public double Area { get; set; }
    public double Perimeter { get; set; }
    public bool WasUsedAsIs { get; set; }
    public int MinimalGapClosuresCount { get; set; }

    public Point2D Centroid
    {
        get
        {
            if (Segments.Count == 0) return new Point2D(0, 0);
            double sx = Segments.Sum(s => s.Start.X + s.End.X) / (2.0 * Segments.Count);
            double sy = Segments.Sum(s => s.Start.Y + s.End.Y) / (2.0 * Segments.Count);
            return new Point2D(sx, sy);
        }
    }
}

/// <summary>
/// Advanced curve-preserving line polygonizer.
/// Chains original segments (lines and true arcs) into closed rings without converting curves to vertices,
/// bridges gaps <= tolerance with minimal straight segments without moving vertices,
/// generates an input manifest and coverage check across all user-selected CAD layers.
/// </summary>
public static class CurvePreservingPolygonizerService
{
    public static (List<CurvePolygonRing> Rings, PolygonizationManifestReport Manifest) Polygonize(
        IReadOnlyList<CurvePolylineEntity> inputEntities,
        IReadOnlyList<string> expectedLayerNames,
        PolygonizerOptions options,
        string category = "Main Parcels")
    {
        var manifest = new PolygonizationManifestReport
        {
            Mode = options.GroupLayersTogether ? "Group of all selected layers" : "Per layer"
        };

        var rings = new List<CurvePolygonRing>();

        // 1. Build initial layer manifest items for ALL expected layers
        var manifestByLayer = new Dictionary<string, LayerPolygonizationManifestItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var lName in expectedLayerNames)
        {
            manifestByLayer[lName] = new LayerPolygonizationManifestItem
            {
                LayerName = lName,
                Category = category
            };
        }

        // Account for any layers present in entities but not in initial list
        foreach (var entity in inputEntities)
        {
            if (!manifestByLayer.ContainsKey(entity.LayerName))
            {
                manifestByLayer[entity.LayerName] = new LayerPolygonizationManifestItem
                {
                    LayerName = entity.LayerName,
                    Category = entity.Category
                };
            }
        }

        // 2. Classify input entities: Count entities read, closed as-is, and open lines
        var closedEntities = new List<CurvePolylineEntity>();
        var openEntities = new List<CurvePolylineEntity>();

        foreach (var entity in inputEntities)
        {
            var item = manifestByLayer[entity.LayerName];
            item.EntitiesRead++;

            if (entity.Segments.Count == 0)
            {
                item.IgnoredCount++;
                item.IgnoredReason = "Zero segments in entity";
                continue;
            }

            if (options.KeepAlreadyClosedPolylines && entity.IsClosed)
            {
                closedEntities.Add(entity);
                item.UsedAsIsClosed++;
            }
            else
            {
                openEntities.Add(entity);
                item.EnteredNetworkLines++;
            }
        }

        // Check for any selected layer that contributed ZERO entities (mandatory blocking rule)
        foreach (var kvp in manifestByLayer)
        {
            if (kvp.Value.HasZeroEntitiesError)
            {
                manifest.Errors.Add($"Selected layer '{kvp.Key}' contributed 0 entities to polygonization.");
            }
        }

        // 3. Process closed entities directly into rings (Used As-Is)
        foreach (var closed in closedEntities)
        {
            double area = CurveAuditService.ComputeExactAnalyticArea(closed.Segments);
            double perimeter = closed.Segments.Sum(s => s.Length);

            var ring = new CurvePolygonRing
            {
                Segments = new List<CoreBoundarySegment>(closed.Segments),
                SourceLayer = closed.LayerName,
                LayerNames = new List<string> { closed.LayerName },
                IsMixedLayers = false,
                Category = closed.Category,
                Area = area,
                Perimeter = perimeter,
                WasUsedAsIs = true
            };
            rings.Add(ring);
            manifest.TotalClosedKeptAsIs++;

            if (manifestByLayer.TryGetValue(closed.LayerName, out var mItem))
            {
                mItem.PolygonsContributed++;
            }
        }

        // 4. Polygonize open entities
        if (openEntities.Count > 0)
        {
            if (options.GroupLayersTogether)
            {
                // Mode A: Group of all selected layers into ONE combined network
                PolygonizeSegmentGroup(openEntities, options, rings, manifest, manifestByLayer);
            }
            else
            {
                // Mode B: Per layer separately
                var groupedByLayer = openEntities.GroupBy(e => e.LayerName, StringComparer.OrdinalIgnoreCase);
                foreach (var group in groupedByLayer)
                {
                    PolygonizeSegmentGroup(group.ToList(), options, rings, manifest, manifestByLayer);
                }
            }
        }

        manifest.TotalPolygonsBuilt = rings.Count;
        manifest.Layers = manifestByLayer.Values.OrderBy(l => l.LayerName).ToList();

        return (rings, manifest);
    }

    private static void PolygonizeSegmentGroup(
        List<CurvePolylineEntity> entities,
        PolygonizerOptions options,
        List<CurvePolygonRing> results,
        PolygonizationManifestReport manifest,
        Dictionary<string, LayerPolygonizationManifestItem> manifestByLayer)
    {
        // 1. Flatten all segments from entities, tagging each with its source layer
        var pool = new List<(CoreBoundarySegment Segment, string LayerName, string Category)>();
        foreach (var e in entities)
        {
            foreach (var s in e.Segments)
            {
                if (s.Length > 1e-5)
                {
                    pool.Add((s, e.LayerName, e.Category));
                }
            }
        }

        if (pool.Count == 0) return;

        // 2. Extend Dangling Lines & Curves (Curve-Preserving Ray & Arc Extension)
        if (options.ExtendLinesAndCurves)
        {
            double extendTol = Math.Max(0.001, options.ExtendToleranceMeters);
            ExtendDanglingLinesAndCurves(pool, extendTol);
        }

        double snapTol = Math.Max(0.01, options.SnapToleranceMeters);

        // 3. Noding Pass: Split segments at T-junctions, crossings, and endpoint projections
        NodeSegments(pool, snapTol);

        // 3. Cluster Nodes: Map segment endpoints to clustered nodes within snap tolerance
        var nodes = new List<Point2D>();
        int FindOrAddNode(Point2D pt)
        {
            for (int n = 0; n < nodes.Count; n++)
            {
                if (nodes[n].DistanceTo(pt) <= snapTol)
                    return n;
            }
            nodes.Add(pt);
            return nodes.Count - 1;
        }

        var segNodes = new (int fromNode, int toNode)[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            segNodes[i] = (FindOrAddNode(pool[i].Segment.Start), FindOrAddNode(pool[i].Segment.End));
        }

        // 4. Build Directed Half-Edges (Forward and Reverse)
        var outgoingByNode = new List<HalfEdge>[nodes.Count];
        for (int n = 0; n < nodes.Count; n++) outgoingByNode[n] = new List<HalfEdge>();

        var allHalfEdges = new List<HalfEdge>();
        for (int i = 0; i < pool.Count; i++)
        {
            var (u, v) = segNodes[i];
            if (u == v) continue; // Skip zero-length degenerate segment

            var (fwdOut, fwdIn, revOut, revIn) = ComputeHalfEdgeAngles(pool[i].Segment);

            var fwd = new HalfEdge
            {
                SegmentIndex = i,
                IsReverse = false,
                FromNode = u,
                ToNode = v,
                OutgoingAngle = fwdOut,
                IncomingAngle = fwdIn
            };
            var rev = new HalfEdge
            {
                SegmentIndex = i,
                IsReverse = true,
                FromNode = v,
                ToNode = u,
                OutgoingAngle = revOut,
                IncomingAngle = revIn
            };

            allHalfEdges.Add(fwd);
            allHalfEdges.Add(rev);
            outgoingByNode[u].Add(fwd);
            outgoingByNode[v].Add(rev);
        }

        // Sort outgoing half-edges at each node CCW by OutgoingAngle (-PI to PI)
        for (int n = 0; n < nodes.Count; n++)
        {
            outgoingByNode[n].Sort((a, b) => a.OutgoingAngle.CompareTo(b.OutgoingAngle));
        }

        // 5. Planar Face Traversal (Sharpest Left Turn)
        var visited = new HashSet<(int segIdx, bool isRev)>();
        var segmentUsageCount = new int[pool.Count];

        foreach (var startEdge in allHalfEdges)
        {
            var edgeKey = (startEdge.SegmentIndex, startEdge.IsReverse);
            if (visited.Contains(edgeKey)) continue;

            var cycle = new List<HalfEdge>();
            var currentEdge = startEdge;
            bool closed = false;

            while (true)
            {
                cycle.Add(currentEdge);
                int nextNode = currentEdge.ToNode;

                // Check if nextNode matches any previous edge's FromNode in cycle
                int matchIdx = cycle.FindIndex(h => h.FromNode == nextNode);
                if (matchIdx >= 0 && (cycle.Count - matchIdx) >= 2)
                {
                    cycle = cycle.Skip(matchIdx).ToList();
                    closed = true;
                    break;
                }

                if (cycle.Count > pool.Count * 2)
                {
                    break;
                }

                var outgoing = outgoingByNode[nextNode];
                if (outgoing.Count == 0) break;

                // Incoming direction arriving at nextNode is currentEdge.IncomingAngle
                // The direction pointing back along currentEdge is reverseAngle = incomingAngle + PI
                double revAngle = NormalizeAngle(currentEdge.IncomingAngle + Math.PI);

                HalfEdge? bestNext = null;
                double minCwDelta = double.MaxValue;

                foreach (var candidate in outgoing)
                {
                    // Avoid immediate U-turn along the exact same segment if other options exist
                    if (candidate.SegmentIndex == currentEdge.SegmentIndex && outgoing.Count > 1)
                        continue;

                    // Sharpest left turn from forward direction corresponds to smallest clockwise angle from reverse direction
                    double cwDelta = revAngle - candidate.OutgoingAngle;
                    while (cwDelta <= 1e-6) cwDelta += 2.0 * Math.PI;
                    while (cwDelta > 2.0 * Math.PI + 1e-6) cwDelta -= 2.0 * Math.PI;

                    if (cwDelta < minCwDelta)
                    {
                        minCwDelta = cwDelta;
                        bestNext = candidate;
                    }
                }

                if (bestNext == null && outgoing.Count > 0)
                {
                    bestNext = outgoing[0];
                }

                if (bestNext == null) break;
                currentEdge = bestNext.Value;
            }

            // Mark all edges in this cycle as visited to prevent duplicate face tracing
            foreach (var h in cycle)
            {
                visited.Add((h.SegmentIndex, h.IsReverse));
            }

            if (closed && cycle.Count >= 2)
            {
                var ringSegments = new List<CoreBoundarySegment>();
                var participatingLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var h in cycle)
                {
                    var baseSeg = pool[h.SegmentIndex].Segment;
                    var segToUse = h.IsReverse ? ReverseSegment(baseSeg) : baseSeg;
                    ringSegments.Add(segToUse);
                    participatingLayers.Add(pool[h.SegmentIndex].LayerName);
                }

                // Bridge small gaps <= snapTol with minimal straight segments without moving existing vertices
                var closedSegments = new List<CoreBoundarySegment>();
                int gapClosures = 0;
                for (int i = 0; i < ringSegments.Count; i++)
                {
                    closedSegments.Add(ringSegments[i]);
                    var next = ringSegments[(i + 1) % ringSegments.Count];
                    double gap = ringSegments[i].End.DistanceTo(next.Start);
                    if (gap > 1e-4 && gap <= snapTol)
                    {
                        closedSegments.Add(new CoreBoundarySegment(ringSegments[i].End, next.Start, CurveType.Straight, gap));
                        gapClosures++;
                    }
                }
                ringSegments = closedSegments;

                // Check orientation via signed chord area (Shoelace)
                double signedChordArea = 0;
                for (int i = 0; i < ringSegments.Count; i++)
                {
                    var p1 = ringSegments[i].Start;
                    var p2 = ringSegments[i].End;
                    signedChordArea += (p1.X * p2.Y) - (p2.X * p1.Y);
                }
                signedChordArea *= 0.5;

                // Positive CCW area indicates an interior polygon face (parcel). Negative is CW exterior world face.
                if (signedChordArea > 0.01)
                {
                    // Compute exact analytic area (with circular arc caps)
                    double area = CurveAuditService.ComputeExactAnalyticArea(ringSegments);
                    if (area > 0.1)
                    {
                        // Check if duplicate ring already added
                        double sx = ringSegments.Sum(s => s.Start.X + s.End.X) / (2.0 * ringSegments.Count);
                        double sy = ringSegments.Sum(s => s.Start.Y + s.End.Y) / (2.0 * ringSegments.Count);
                        bool isDup = results.Any(r => Math.Abs(r.Area - area) < 0.1 && Math.Abs(r.Centroid.X - sx) < 0.1 && Math.Abs(r.Centroid.Y - sy) < 0.1);
                        if (!isDup)
                        {
                            double perim = ringSegments.Sum(s => s.Length);
                            var lengthByLayer = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                            foreach (var h in cycle)
                            {
                                var poolSeg = pool[h.SegmentIndex];
                                lengthByLayer[poolSeg.LayerName] = lengthByLayer.TryGetValue(poolSeg.LayerName, out var cur)
                                    ? cur + poolSeg.Segment.Length
                                    : poolSeg.Segment.Length;
                            }

                            string dominantLayer = lengthByLayer.OrderByDescending(kv => kv.Value).FirstOrDefault().Key ?? "CAD";
                            var contributing = lengthByLayer.Where(kv => kv.Value > 1e-4).Select(kv => kv.Key).ToList();
                            bool isMixed = lengthByLayer.Values.Count(v => perim > 0 && (v / perim) > 0.10) > 1;

                            var ring = new CurvePolygonRing
                            {
                                Segments = ringSegments,
                                SourceLayer = dominantLayer,
                                LayerNames = contributing,
                                IsMixedLayers = isMixed,
                                Category = pool[cycle[0].SegmentIndex].Category,
                                Area = area,
                                Perimeter = perim,
                                WasUsedAsIs = false,
                                MinimalGapClosuresCount = gapClosures
                            };
                            results.Add(ring);

                            foreach (var h in cycle)
                            {
                                segmentUsageCount[h.SegmentIndex]++;
                            }

                            foreach (var lyr in participatingLayers)
                            {
                                if (manifestByLayer.TryGetValue(lyr, out var mItem))
                                {
                                    mItem.PolygonsContributed++;
                                }
                            }
                        }
                    }
                }
            }
        }

        // 6. Accurate Dangle Reporting: Only segments that were never used in any polygon face
        for (int i = 0; i < pool.Count; i++)
        {
            if (segmentUsageCount[i] == 0)
            {
                manifest.TotalDanglesFound++;
                if (manifestByLayer.TryGetValue(pool[i].LayerName, out var mItem))
                {
                    mItem.DanglesOrphansCount++;
                }
            }
        }
    }

    private struct HalfEdge
    {
        public int SegmentIndex;
        public bool IsReverse;
        public int FromNode;
        public int ToNode;
        public double OutgoingAngle;
        public double IncomingAngle;
    }

    private static (double fwdOut, double fwdIn, double revOut, double revIn) ComputeHalfEdgeAngles(CoreBoundarySegment seg)
    {
        if (seg.Type == CurveType.CircularArc && seg.Center.HasValue && seg.Radius.HasValue)
        {
            var c = seg.Center.Value;
            bool ccw = seg.IsCounterClockwise ?? true;

            // Start tangent
            double rsX = seg.Start.X - c.X;
            double rsY = seg.Start.Y - c.Y;
            double tsX = ccw ? -rsY : rsY;
            double tsY = ccw ? rsX : -rsX;
            double fwdOut = Math.Atan2(tsY, tsX);

            // End tangent
            double reX = seg.End.X - c.X;
            double reY = seg.End.Y - c.Y;
            double teX = ccw ? -reY : reY;
            double teY = ccw ? reX : -reX;
            double fwdIn = Math.Atan2(teY, teX);

            double revOut = NormalizeAngle(fwdIn + Math.PI);
            double revIn = NormalizeAngle(fwdOut + Math.PI);
            return (fwdOut, fwdIn, revOut, revIn);
        }
        else
        {
            double dx = seg.End.X - seg.Start.X;
            double dy = seg.End.Y - seg.Start.Y;
            double straightAngle = Math.Atan2(dy, dx);
            double revAngle = NormalizeAngle(straightAngle + Math.PI);
            return (straightAngle, straightAngle, revAngle, revAngle);
        }
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2.0 * Math.PI;
        while (a <= -Math.PI) a += 2.0 * Math.PI;
        return a;
    }

    #region Line and Curve Extension (Curve-Preserving Ray & Arc Extension)

    private static void ExtendDanglingLinesAndCurves(
        List<(CoreBoundarySegment Segment, string LayerName, string Category)> pool,
        double extendTol)
    {
        int maxPasses = 8;
        bool anyExtended = true;

        while (anyExtended && maxPasses-- > 0)
        {
            anyExtended = false;

            for (int i = 0; i < pool.Count; i++)
            {
                var seg = pool[i].Segment;
                if (seg.Length < 1e-4) continue;

                // Check End first
                if (IsEndpointDangling(seg.End, i, false, pool))
                {
                    if (TryExtendEndpoint(i, isStart: false, pool, extendTol))
                    {
                        anyExtended = true;
                        break;
                    }
                }

                // Check Start
                if (IsEndpointDangling(seg.Start, i, true, pool))
                {
                    if (TryExtendEndpoint(i, isStart: true, pool, extendTol))
                    {
                        anyExtended = true;
                        break;
                    }
                }
            }
        }
    }

    private static bool IsEndpointDangling(
        Point2D pt,
        int currentSegIdx,
        bool isCurrentStart,
        List<(CoreBoundarySegment Segment, string LayerName, string Category)> pool)
    {
        for (int j = 0; j < pool.Count; j++)
        {
            if (j == currentSegIdx) continue;
            var s = pool[j].Segment;
            if (pt.DistanceTo(s.Start) < 1e-4 || pt.DistanceTo(s.End) < 1e-4)
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryExtendEndpoint(
        int segIdx,
        bool isStart,
        List<(CoreBoundarySegment Segment, string LayerName, string Category)> pool,
        double extendTol)
    {
        var seg = pool[segIdx].Segment;
        var pOrig = isStart ? seg.Start : seg.End;

        Point2D? bestIntersection = null;
        double minExtendDist = extendTol + 1e-6;
        int targetSegIdx = -1;
        double targetDistAlong = -1;

        for (int j = 0; j < pool.Count; j++)
        {
            if (j == segIdx) continue;
            var target = pool[j].Segment;

            var hits = FindExtensionIntersections(seg, isStart, target, extendTol);
            foreach (var hit in hits)
            {
                if (hit.ExtensionDist > 1e-5 && hit.ExtensionDist < minExtendDist)
                {
                    minExtendDist = hit.ExtensionDist;
                    bestIntersection = hit.HitPoint;
                    targetSegIdx = j;
                    targetDistAlong = hit.DistanceAlongTarget;
                }
            }
        }

        if (bestIntersection.HasValue && targetSegIdx >= 0 && minExtendDist <= extendTol)
        {
            var hitPt = bestIntersection.Value;

            // 1. Extend the dangling segment
            CoreBoundarySegment newSeg;
            if (seg.Type == CurveType.CircularArc && seg.Center.HasValue && seg.Radius.HasValue)
            {
                // Circular arc: strictly preserve center, radius, and orientation!
                double newLen = seg.Length + minExtendDist;
                if (isStart)
                {
                    newSeg = new CoreBoundarySegment(hitPt, seg.End, CurveType.CircularArc, newLen)
                    {
                        Center = seg.Center,
                        Radius = seg.Radius,
                        IsCounterClockwise = seg.IsCounterClockwise,
                        NativeGeometryTag = null
                    };
                }
                else
                {
                    newSeg = new CoreBoundarySegment(seg.Start, hitPt, CurveType.CircularArc, newLen)
                    {
                        Center = seg.Center,
                        Radius = seg.Radius,
                        IsCounterClockwise = seg.IsCounterClockwise,
                        NativeGeometryTag = null
                    };
                }
            }
            else
            {
                // Straight segment
                double newLen = seg.Length + minExtendDist;
                if (isStart)
                {
                    newSeg = new CoreBoundarySegment(hitPt, seg.End, CurveType.Straight, newLen)
                    {
                        NativeGeometryTag = null
                    };
                }
                else
                {
                    newSeg = new CoreBoundarySegment(seg.Start, hitPt, CurveType.Straight, newLen)
                    {
                        NativeGeometryTag = null
                    };
                }
            }

            pool[segIdx] = (newSeg, pool[segIdx].LayerName, pool[segIdx].Category);

            // 2. If hitPt lands on the interior of target segment, split target cleanly
            var target = pool[targetSegIdx].Segment;
            if (targetDistAlong > 1e-4 && (target.Length - targetDistAlong) > 1e-4)
            {
                try
                {
                    var (sub1, sub2) = target.SplitAtDistance(targetDistAlong);
                    var lyr = pool[targetSegIdx].LayerName;
                    var cat = pool[targetSegIdx].Category;
                    pool[targetSegIdx] = (sub1, lyr, cat);
                    pool.Add((sub2, lyr, cat));
                }
                catch { }
            }

            return true;
        }

        return false;
    }

    private static List<(Point2D HitPoint, double ExtensionDist, double DistanceAlongTarget)> FindExtensionIntersections(
        CoreBoundarySegment seg,
        bool isStart,
        CoreBoundarySegment target,
        double extendTol)
    {
        var results = new List<(Point2D HitPoint, double ExtensionDist, double DistanceAlongTarget)>();
        var pOrig = isStart ? seg.Start : seg.End;

        // Case A: seg is Straight
        if (seg.Type == CurveType.Straight)
        {
            double dx = isStart ? (seg.Start.X - seg.End.X) : (seg.End.X - seg.Start.X);
            double dy = isStart ? (seg.Start.Y - seg.End.Y) : (seg.End.Y - seg.Start.Y);
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-8) return results;
            dx /= len; dy /= len; // unit ray direction

            if (target.Type == CurveType.Straight)
            {
                // Straight-straight ray intersection
                double x1 = pOrig.X, y1 = pOrig.Y;
                double x3 = target.Start.X, y3 = target.Start.Y;
                double x4 = target.End.X, y4 = target.End.Y;
                double dBx = x4 - x3, dBy = y4 - y3;

                double cross = dx * dBy - dy * dBx;
                if (Math.Abs(cross) > 1e-8)
                {
                    double t = ((x3 - x1) * dBy - (y3 - y1) * dBx) / cross;
                    double u = ((x3 - x1) * dy - (y3 - y1) * dx) / cross;
                    if (t > 1e-5 && t <= extendTol && u >= -1e-4 && u <= 1.0 + 1e-4)
                    {
                        var hit = new Point2D(x1 + t * dx, y1 + t * dy);
                        double distAlong = Math.Clamp(u, 0.0, 1.0) * target.Length;
                        results.Add((hit, t, distAlong));
                    }
                }
            }
            else if (target.Type == CurveType.CircularArc && target.Center.HasValue && target.Radius.HasValue)
            {
                // Straight ray hitting target circular arc
                var c2 = target.Center.Value;
                double r2 = target.Radius.Value;
                double wx = pOrig.X - c2.X;
                double wy = pOrig.Y - c2.Y;
                double b = 2.0 * (wx * dx + wy * dy);
                double cVal = wx * wx + wy * wy - r2 * r2;
                double disc = b * b - 4.0 * cVal;

                if (disc >= 0)
                {
                    double sqrtDisc = Math.Sqrt(disc);
                    double[] tRoots = { (-b - sqrtDisc) / 2.0, (-b + sqrtDisc) / 2.0 };
                    foreach (var t in tRoots)
                    {
                        if (t > 1e-5 && t <= extendTol)
                        {
                            var hit = new Point2D(pOrig.X + t * dx, pOrig.Y + t * dy);
                            double hitAngle = Math.Atan2(hit.Y - c2.Y, hit.X - c2.X);
                            double startAngle = Math.Atan2(target.Start.Y - c2.Y, target.Start.X - c2.X);
                            double endAngle = Math.Atan2(target.End.Y - c2.Y, target.End.X - c2.X);
                            bool ccw = target.IsCounterClockwise ?? true;

                            if (IsAngleOnArc(hitAngle, startAngle, endAngle, ccw, out double frac))
                            {
                                results.Add((hit, t, frac * target.Length));
                            }
                        }
                    }
                }
            }
        }
        // Case B: seg is Circular Arc
        else if (seg.Type == CurveType.CircularArc && seg.Center.HasValue && seg.Radius.HasValue)
        {
            var c1 = seg.Center.Value;
            double r1 = seg.Radius.Value;
            bool ccw1 = seg.IsCounterClockwise ?? true;
            double angleOrig = Math.Atan2(pOrig.Y - c1.Y, pOrig.X - c1.X);
            double dir = (!isStart) ? (ccw1 ? 1.0 : -1.0) : (ccw1 ? -1.0 : 1.0);
            double maxDeltaAngle = extendTol / r1;

            if (target.Type == CurveType.Straight)
            {
                // Circular arc continuing along (c1, r1) hitting straight target
                double qx = target.Start.X, qy = target.Start.Y;
                double vx = target.End.X - qx, vy = target.End.Y - qy;
                double wx = qx - c1.X, wy = qy - c1.Y;

                double aQuad = vx * vx + vy * vy;
                double bQuad = 2.0 * (wx * vx + wy * vy);
                double cQuad = wx * wx + wy * wy - r1 * r1;
                double disc = bQuad * bQuad - 4.0 * aQuad * cQuad;

                if (disc >= 0 && aQuad > 1e-8)
                {
                    double sqrtDisc = Math.Sqrt(disc);
                    double[] lambdas = { (-bQuad - sqrtDisc) / (2.0 * aQuad), (-bQuad + sqrtDisc) / (2.0 * aQuad) };
                    foreach (var lam in lambdas)
                    {
                        if (lam >= -1e-4 && lam <= 1.0 + 1e-4)
                        {
                            var hit = new Point2D(qx + lam * vx, qy + lam * vy);
                            double hitAngle = Math.Atan2(hit.Y - c1.Y, hit.X - c1.X);
                            double deltaAngle = dir * (hitAngle - angleOrig);
                            while (deltaAngle <= 0) deltaAngle += 2.0 * Math.PI;
                            while (deltaAngle > 2.0 * Math.PI) deltaAngle -= 2.0 * Math.PI;

                            if (deltaAngle > 1e-5 && deltaAngle <= maxDeltaAngle)
                            {
                                double extDist = r1 * deltaAngle;
                                double distAlong = Math.Clamp(lam, 0.0, 1.0) * target.Length;
                                results.Add((hit, extDist, distAlong));
                            }
                        }
                    }
                }
            }
            else if (target.Type == CurveType.CircularArc && target.Center.HasValue && target.Radius.HasValue)
            {
                // Circle-circle intersection
                var c2 = target.Center.Value;
                double r2 = target.Radius.Value;
                double dx = c2.X - c1.X, dy = c2.Y - c1.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);

                if (d > 1e-6 && d <= (r1 + r2 + 1e-5) && d >= Math.Abs(r1 - r2) - 1e-5)
                {
                    double aDist = (r1 * r1 - r2 * r2 + d * d) / (2.0 * d);
                    double hSquared = r1 * r1 - aDist * aDist;
                    double h = Math.Sqrt(Math.Max(0.0, hSquared));

                    double xm = c1.X + (aDist / d) * dx;
                    double ym = c1.Y + (aDist / d) * dy;
                    double rx = -dy * (h / d);
                    double ry = dx * (h / d);

                    Point2D[] candidates = { new Point2D(xm + rx, ym + ry), new Point2D(xm - rx, ym - ry) };
                    foreach (var hit in candidates)
                    {
                        double hitAngle1 = Math.Atan2(hit.Y - c1.Y, hit.X - c1.X);
                        double deltaAngle = dir * (hitAngle1 - angleOrig);
                        while (deltaAngle <= 0) deltaAngle += 2.0 * Math.PI;
                        while (deltaAngle > 2.0 * Math.PI) deltaAngle -= 2.0 * Math.PI;

                        if (deltaAngle > 1e-5 && deltaAngle <= maxDeltaAngle)
                        {
                            double extDist = r1 * deltaAngle;
                            double hitAngle2 = Math.Atan2(hit.Y - c2.Y, hit.X - c2.X);
                            double startAngle2 = Math.Atan2(target.Start.Y - c2.Y, target.Start.X - c2.X);
                            double endAngle2 = Math.Atan2(target.End.Y - c2.Y, target.End.X - c2.X);
                            bool ccw2 = target.IsCounterClockwise ?? true;

                            if (IsAngleOnArc(hitAngle2, startAngle2, endAngle2, ccw2, out double frac2))
                            {
                                results.Add((hit, extDist, frac2 * target.Length));
                            }
                        }
                    }
                }
            }
        }

        return results;
    }

    private static bool IsAngleOnArc(double angle, double startAngle, double endAngle, bool ccw, out double frac)
    {
        double sweep = ccw ? (endAngle - startAngle) : (startAngle - endAngle);
        while (sweep <= 0) sweep += 2.0 * Math.PI;
        while (sweep > 2.0 * Math.PI) sweep -= 2.0 * Math.PI;

        double ptSweep = ccw ? (angle - startAngle) : (startAngle - angle);
        while (ptSweep < 0) ptSweep += 2.0 * Math.PI;
        while (ptSweep >= 2.0 * Math.PI) ptSweep -= 2.0 * Math.PI;

        if (ptSweep >= -1e-5 && ptSweep <= sweep + 1e-5)
        {
            frac = Math.Clamp(ptSweep / Math.Max(1e-6, sweep), 0.0, 1.0);
            return true;
        }

        frac = 0.0;
        return false;
    }

    #endregion

    #region Robust Noding Pass

    private static void NodeSegments(List<(CoreBoundarySegment Segment, string LayerName, string Category)> pool, double snapTol)
    {
        int maxPasses = 16;
        bool splitOccurred = true;

        while (splitOccurred && maxPasses-- > 0)
        {
            splitOccurred = false;
            int initialCount = pool.Count;

            for (int i = 0; i < initialCount; i++)
            {
                var segA = pool[i].Segment;
                if (segA.Length < snapTol * 2.0) continue;

                double bestT = -1.0;
                double bestDist = double.MaxValue;

                for (int j = 0; j < pool.Count; j++)
                {
                    if (i == j) continue;
                    var segB = pool[j].Segment;

                    CheckProjection(segB.Start, segA, snapTol, ref bestT, ref bestDist);
                    CheckProjection(segB.End, segA, snapTol, ref bestT, ref bestDist);
                    CheckSegmentSegmentIntersection(segA, segB, snapTol, ref bestT, ref bestDist);
                }

                if (bestT > 1e-4 && bestT < 1.0 - 1e-4)
                {
                    double splitDist = bestT * segA.Length;
                    if (splitDist > snapTol && (segA.Length - splitDist) > snapTol)
                    {
                        try
                        {
                            var (sub1, sub2) = segA.SplitAtDistance(splitDist);
                            var lyr = pool[i].LayerName;
                            var cat = pool[i].Category;
                            pool[i] = (sub1, lyr, cat);
                            pool.Add((sub2, lyr, cat));
                            splitOccurred = true;
                        }
                        catch { }
                    }
                }
            }
        }
    }

    private static void CheckSegmentSegmentIntersection(
        CoreBoundarySegment segA,
        CoreBoundarySegment segB,
        double snapTol,
        ref double bestT,
        ref double bestDist)
    {
        // 1. Straight - Straight
        if (segA.Type == CurveType.Straight && segB.Type == CurveType.Straight)
        {
            double x1 = segA.Start.X, y1 = segA.Start.Y;
            double x2 = segA.End.X, y2 = segA.End.Y;
            double x3 = segB.Start.X, y3 = segB.Start.Y;
            double x4 = segB.End.X, y4 = segB.End.Y;

            double dAx = x2 - x1, dAy = y2 - y1;
            double dBx = x4 - x3, dBy = y4 - y3;

            double cross = dAx * dBy - dAy * dBx;
            if (Math.Abs(cross) < 1e-8) return;

            double t = ((x3 - x1) * dBy - (y3 - y1) * dBx) / cross;
            double u = ((x3 - x1) * dAy - (y3 - y1) * dAx) / cross;

            double tolT = snapTol / Math.Max(0.01, segA.Length);
            double tolU = snapTol / Math.Max(0.01, segB.Length);

            if (t > tolT && t < 1.0 - tolT && u > tolU && u < 1.0 - tolU)
            {
                if (0.0 < bestDist)
                {
                    bestDist = 0.0;
                    bestT = t;
                }
            }
        }
        // 2. Straight segA - Circular Arc segB
        else if (segA.Type == CurveType.Straight && segB.Type == CurveType.CircularArc && segB.Center.HasValue && segB.Radius.HasValue)
        {
            var c = segB.Center.Value;
            double r = segB.Radius.Value;
            double x1 = segA.Start.X, y1 = segA.Start.Y;
            double dx = segA.End.X - x1, dy = segA.End.Y - y1;
            double wx = x1 - c.X, wy = y1 - c.Y;

            double aQuad = dx * dx + dy * dy;
            double bQuad = 2.0 * (wx * dx + wy * dy);
            double cQuad = wx * wx + wy * wy - r * r;
            double disc = bQuad * bQuad - 4.0 * aQuad * cQuad;

            if (disc >= 0 && aQuad > 1e-8)
            {
                double sqrtDisc = Math.Sqrt(disc);
                double[] tRoots = { (-bQuad - sqrtDisc) / (2.0 * aQuad), (-bQuad + sqrtDisc) / (2.0 * aQuad) };
                double tolT = snapTol / Math.Max(0.01, segA.Length);

                foreach (var t in tRoots)
                {
                    if (t > tolT && t < 1.0 - tolT)
                    {
                        var hit = new Point2D(x1 + t * dx, y1 + t * dy);
                        double hitAngle = Math.Atan2(hit.Y - c.Y, hit.X - c.X);
                        double startAngle = Math.Atan2(segB.Start.Y - c.Y, segB.Start.X - c.X);
                        double endAngle = Math.Atan2(segB.End.Y - c.Y, segB.End.X - c.X);
                        bool ccw = segB.IsCounterClockwise ?? true;

                        if (IsAngleOnArc(hitAngle, startAngle, endAngle, ccw, out _))
                        {
                            if (0.0 < bestDist)
                            {
                                bestDist = 0.0;
                                bestT = t;
                            }
                        }
                    }
                }
            }
        }
        // 3. Circular Arc segA - Straight segB
        else if (segA.Type == CurveType.CircularArc && segB.Type == CurveType.Straight && segA.Center.HasValue && segA.Radius.HasValue)
        {
            var c = segA.Center.Value;
            double r = segA.Radius.Value;
            double qx = segB.Start.X, qy = segB.Start.Y;
            double vx = segB.End.X - qx, vy = segB.End.Y - qy;
            double wx = qx - c.X, wy = qy - c.Y;

            double aQuad = vx * vx + vy * vy;
            double bQuad = 2.0 * (wx * vx + wy * vy);
            double cQuad = wx * wx + wy * wy - r * r;
            double disc = bQuad * bQuad - 4.0 * aQuad * cQuad;

            if (disc >= 0 && aQuad > 1e-8)
            {
                double sqrtDisc = Math.Sqrt(disc);
                double[] lambdas = { (-bQuad - sqrtDisc) / (2.0 * aQuad), (-bQuad + sqrtDisc) / (2.0 * aQuad) };
                double tolU = snapTol / Math.Max(0.01, segB.Length);

                foreach (var lam in lambdas)
                {
                    if (lam > tolU && lam < 1.0 - tolU)
                    {
                        var hit = new Point2D(qx + lam * vx, qy + lam * vy);
                        double hitAngle = Math.Atan2(hit.Y - c.Y, hit.X - c.X);
                        double startAngle = Math.Atan2(segA.Start.Y - c.Y, segA.Start.X - c.X);
                        double endAngle = Math.Atan2(segA.End.Y - c.Y, segA.End.X - c.X);
                        bool ccw = segA.IsCounterClockwise ?? true;

                        if (IsAngleOnArc(hitAngle, startAngle, endAngle, ccw, out double frac))
                        {
                            double tolT = snapTol / Math.Max(0.01, segA.Length);
                            if (frac > tolT && frac < 1.0 - tolT)
                            {
                                if (0.0 < bestDist)
                                {
                                    bestDist = 0.0;
                                    bestT = frac;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private static void CheckProjection(Point2D pt, CoreBoundarySegment seg, double snapTol, ref double bestT, ref double bestDist)
    {
        if (seg.Type == CurveType.Straight)
        {
            double dx = seg.End.X - seg.Start.X;
            double dy = seg.End.Y - seg.Start.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-8) return;

            double t = ((pt.X - seg.Start.X) * dx + (pt.Y - seg.Start.Y) * dy) / lenSq;
            if (t > 1e-3 && t < 1.0 - 1e-3)
            {
                var proj = new Point2D(seg.Start.X + t * dx, seg.Start.Y + t * dy);
                double dist = pt.DistanceTo(proj);
                if (dist <= snapTol && dist < bestDist)
                {
                    bestDist = dist;
                    bestT = t;
                }
            }
        }
        else if (seg.Type == CurveType.CircularArc && seg.Center.HasValue && seg.Radius.HasValue)
        {
            var c = seg.Center.Value;
            double r = seg.Radius.Value;
            double distFromCenter = pt.DistanceTo(c);
            double distToArc = Math.Abs(distFromCenter - r);
            if (distToArc <= snapTol && distToArc < bestDist)
            {
                double anglePt = Math.Atan2(pt.Y - c.Y, pt.X - c.X);
                double startAngle = Math.Atan2(seg.Start.Y - c.Y, seg.Start.X - c.X);
                double endAngle = Math.Atan2(seg.End.Y - c.Y, seg.End.X - c.X);
                bool ccw = seg.IsCounterClockwise ?? true;

                if (IsAngleOnArc(anglePt, startAngle, endAngle, ccw, out double frac))
                {
                    if (frac > 1e-3 && frac < 1.0 - 1e-3)
                    {
                        bestDist = distToArc;
                        bestT = frac;
                    }
                }
            }
        }
    }

    private static CoreBoundarySegment ReverseSegment(CoreBoundarySegment seg)
    {
        var rev = new CoreBoundarySegment(seg.End, seg.Start, seg.Type, seg.Length)
        {
            Center = seg.Center,
            Radius = seg.Radius,
            IsCounterClockwise = seg.IsCounterClockwise.HasValue ? !seg.IsCounterClockwise.Value : (bool?)null,
            StartTangentDeg = seg.EndTangentDeg.HasValue ? (seg.EndTangentDeg.Value + 180.0) % 360.0 : (double?)null,
            EndTangentDeg = seg.StartTangentDeg.HasValue ? (seg.StartTangentDeg.Value + 180.0) % 360.0 : (double?)null,
            NativeGeometryTag = seg.NativeGeometryTag
        };
        return rev;
    }

    #endregion
}
