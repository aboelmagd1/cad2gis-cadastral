using System;
using System.Collections.Generic;
using System.Linq;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Core.Geometry;

public enum CurveType
{
    Straight,
    CircularArc,
    Bezier
}

/// <summary>
/// Represents a boundary segment (straight or true curve) with geometric properties.
/// Curve geometries are preserved without modification.
/// </summary>
public class CoreBoundarySegment
{
    public Point2D Start { get; set; }
    public Point2D End { get; set; }
    public double Length { get; set; }
    public CurveType Type { get; set; } = CurveType.Straight;

    // Curve parameters (arcs)
    public Point2D? Center { get; set; }
    public double? Radius { get; set; }
    public bool? IsCounterClockwise { get; set; }
    public double? StartTangentDeg { get; set; }
    public double? EndTangentDeg { get; set; }

    // Tag to hold native ArcGIS Pro Segment/Polyline without conversion
    public object? NativeGeometryTag { get; set; }

    public Point2D ChordVector => End - Start;
    public double ChordLength => Start.DistanceTo(End);

    public double ChordAzimuthDeg
    {
        get
        {
            var v = ChordVector;
            double deg = Math.Atan2(v.X, v.Y) * (180.0 / Math.PI);
            return (deg + 360.0) % 360.0;
        }
    }

    public CoreBoundarySegment(Point2D start, Point2D end, CurveType type = CurveType.Straight, double? explicitLength = null)
    {
        Start = start;
        End = end;
        Type = type;
        Length = explicitLength ?? start.DistanceTo(end);
    }

    /// <summary>
    /// Splits this segment into two sub-segments at a given distance along the curve.
    /// For a circular arc, both sub-arcs strictly retain the SAME circle (identical center, radius, and orientation).
    /// </summary>
    public (CoreBoundarySegment Sub1, CoreBoundarySegment Sub2) SplitAtDistance(double distanceAlong)
    {
        if (distanceAlong <= 0 || distanceAlong >= Length)
            throw new ArgumentOutOfRangeException(nameof(distanceAlong), "Split distance must be strictly between 0 and Length.");

        if (Type == CurveType.CircularArc && Center.HasValue && Radius.HasValue && Radius.Value > 1e-6)
        {
            var c = Center.Value;
            double r = Radius.Value;
            bool ccw = IsCounterClockwise ?? true;
            double startAngle = Math.Atan2(Start.Y - c.Y, Start.X - c.X);
            double angleDelta = (distanceAlong / r) * (ccw ? 1.0 : -1.0);
            double splitAngle = startAngle + angleDelta;

            var splitPoint = new Point2D(c.X + r * Math.Cos(splitAngle), c.Y + r * Math.Sin(splitAngle));

            var sub1 = new CoreBoundarySegment(Start, splitPoint, CurveType.CircularArc, distanceAlong)
            {
                Center = c,
                Radius = r,
                IsCounterClockwise = ccw,
                NativeGeometryTag = null
            };

            var sub2 = new CoreBoundarySegment(splitPoint, End, CurveType.CircularArc, Length - distanceAlong)
            {
                Center = c,
                Radius = r,
                IsCounterClockwise = ccw,
                NativeGeometryTag = null
            };

            return (sub1, sub2);
        }
        else
        {
            // Straight line
            double t = distanceAlong / Length;
            var splitPoint = new Point2D(Start.X + t * (End.X - Start.X), Start.Y + t * (End.Y - Start.Y));

            var sub1 = new CoreBoundarySegment(Start, splitPoint, CurveType.Straight, distanceAlong);
            var sub2 = new CoreBoundarySegment(splitPoint, End, CurveType.Straight, Length - distanceAlong);
            return (sub1, sub2);
        }
    }
}

/// <summary>
/// Represents a vertex between two consecutive boundary segments on a ring.
/// </summary>
public class CoreBoundaryVertex
{
    public int Index { get; set; }
    public Point2D Point { get; set; }
    public double InteriorAngleDeg { get; set; }
    public bool IsSplitPoint { get; set; }
    public bool IsRedundantArcJoint { get; set; }
    public bool IsSmoothCompoundArcJoint { get; set; }
    public string SplitReason { get; set; } = string.Empty;

    public CoreBoundaryVertex(int index, Point2D point)
    {
        Index = index;
        Point = point;
    }
}

/// <summary>
/// Represents an unmerged or merged piece of boundary consisting of original segments.
/// </summary>
public class CoreBoundaryPiece
{
    public List<CoreBoundarySegment> Segments { get; set; } = new();
    public double TotalLength => Segments.Sum(s => s.Length);
    public double OutwardAzimuthDeg { get; set; }
    public int CardinalDirection { get; set; } // 1=North, 2=East, 3=South, 4=West
    public int RingIndex { get; set; }

    public CoreBoundarySegment LongestSegment =>
        Segments.OrderByDescending(s => s.Length).FirstOrDefault() ?? Segments[0];

    public Point2D MidpointOfLongestSegment
    {
        get
        {
            var longest = LongestSegment;
            return new Point2D((longest.Start.X + longest.End.X) / 2.0, (longest.Start.Y + longest.End.Y) / 2.0);
        }
    }

    /// <summary>
    /// After merge borders, defines whether this border is "Multi" or "Single".
    /// If the angle between any consecutive segments deviates from 180° by more than toleranceDeg (default 10°),
    /// i.e. angle < (180 - toleranceDeg) or angle > (180 + toleranceDeg), consider this border "Multi"; otherwise "Single".
    /// </summary>
    public string DetermineDimension(double toleranceDeg = 10.0)
    {
        if (Segments == null || Segments.Count == 0)
            return "Single";

        // Check if any circular arc segment has a sweep/curvature turn > toleranceDeg
        foreach (var seg in Segments)
        {
            if (seg.Type == CurveType.CircularArc)
            {
                if (seg.StartTangentDeg.HasValue && seg.EndTangentDeg.HasValue)
                {
                    double sweep = BorderExtractionCore.AngularDifferenceDeg(seg.StartTangentDeg.Value, seg.EndTangentDeg.Value);
                    if (sweep > toleranceDeg)
                        return "Multi";
                }
                else
                {
                    return "Multi";
                }
            }
        }

        if (Segments.Count <= 1)
            return "Single";

        // Check angle between consecutive segments
        for (int i = 0; i < Segments.Count - 1; i++)
        {
            var segIn = Segments[i];
            var segOut = Segments[i + 1];

            double inAzimuthDeg;
            if (segIn.EndTangentDeg.HasValue)
            {
                inAzimuthDeg = segIn.EndTangentDeg.Value;
            }
            else
            {
                var chordIn = segIn.ChordVector;
                inAzimuthDeg = (Math.Atan2(chordIn.X, chordIn.Y) * (180.0 / Math.PI) + 360.0) % 360.0;
            }

            double outAzimuthDeg;
            if (segOut.StartTangentDeg.HasValue)
            {
                outAzimuthDeg = segOut.StartTangentDeg.Value;
            }
            else
            {
                var chordOut = segOut.ChordVector;
                outAzimuthDeg = (Math.Atan2(chordOut.X, chordOut.Y) * (180.0 / Math.PI) + 360.0) % 360.0;
            }

            double deflectionDeg = BorderExtractionCore.AngularDifferenceDeg(inAzimuthDeg, outAzimuthDeg);
            if (deflectionDeg > toleranceDeg)
            {
                return "Multi";
            }
        }

        return "Single";
    }
}

/// <summary>
/// Result of the border extraction for a single ring or entire parcel.
/// </summary>
public class RingExtractionResult
{
    public int RingIndex { get; set; }
    public bool IsHole { get; set; }
    public double RingPerimeter { get; set; }
    public List<CoreBoundaryPiece> FinalLines { get; set; } = new();
    public List<string> AuditLog { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public bool HasFewerThan4Directions { get; set; }
    public bool IntegrityChecksPassed { get; set; } = true;
    public double MeridianConvergenceDeg { get; set; }
}

public static class BorderExtractionCore
{
    /// <summary>
    /// Processes a single ring boundary (exterior ring or interior hole) strictly according to Part A steps.
    /// Never creates new lines; only splits and merges original boundary pieces.
    /// Preserves true curves without modification.
    /// </summary>
    public static RingExtractionResult ProcessRing(
        IReadOnlyList<CoreBoundarySegment> segments,
        int ringIndex,
        bool isHole,
        BorderExtractionOptions options,
        Point2D? parcelCentroid = null,
        int utmZone = 37)
    {
        var result = new RingExtractionResult
        {
            RingIndex = ringIndex,
            IsHole = isHole,
            RingPerimeter = segments.Sum(s => s.Length)
        };

        if (segments == null || segments.Count == 0)
        {
            result.IntegrityChecksPassed = false;
            result.Warnings.Add("Ring has no segments.");
            return result;
        }

        int segCount = segments.Count;

        // Determine polygon winding and inside-orientation
        // Signed Area of polygon ring using chord endpoints + circular arc contributions (P4.2)
        double signedArea = 0;
        for (int i = 0; i < segCount; i++)
        {
            var p1 = segments[i].Start;
            var p2 = segments[i].End;
            signedArea += (p1.X * p2.Y) - (p2.X * p1.Y);

            if (segments[i].Type == CurveType.CircularArc && segments[i].Radius is double r && r > 1e-6)
            {
                double chordLen = p1.DistanceTo(p2);
                double halfAngle = Math.Asin(Math.Min(1.0, Math.Max(0.0, (chordLen / 2.0) / r)));
                double theta = 2.0 * halfAngle;
                double capArea = 0.5 * r * r * (theta - Math.Sin(theta));

                bool isCcwArc = segments[i].IsCounterClockwise ?? true;
                // In shoelace sum, CCW (left-bulging) adds positive area, CW (right-bulging) adds negative area
                signedArea += (isCcwArc ? 2.0 : -2.0) * capArea;
            }
        }

        // In standard GIS:
        // Exterior ring CW (signedArea <= 0) => interior is on the RIGHT
        // Hole CCW (signedArea >= 0) => interior parcel material is on the RIGHT (hole void on left)
        bool insideIsOnRight = isHole ? (signedArea >= 0) : (signedArea <= 0);

        // Compute meridian convergence at parcel centroid if True North correction is enabled
        double convergenceDeg = 0;
        if (options.TrueNorthCorrection && parcelCentroid.HasValue)
        {
            convergenceDeg = CrsProjectionService.ComputeMeridianConvergenceDeg(
                parcelCentroid.Value.X, parcelCentroid.Value.Y, utmZone);
            result.MeridianConvergenceDeg = convergenceDeg;
            result.AuditLog.Add($"Ring {ringIndex}: True north meridian convergence at centroid: {convergenceDeg:F3}°");
        }

        // =========================================================================
        // STEP 1 — Corner (split) points by angle (A2)
        // =========================================================================
        var vertices = DetectCornerVertices(segments, isHole, options, result.AuditLog);

        // Split boundary at all split points to create boundary pieces
        var splitIndices = vertices.Where(v => v.IsSplitPoint).Select(v => v.Index).OrderBy(x => x).ToList();

        // If no split points were found (e.g. circle, smooth ellipse), split at extremal coordinate points
        if (splitIndices.Count == 0 && segCount >= 4)
        {
            splitIndices.AddRange(new[] { 0, segCount / 4, segCount / 2, (3 * segCount) / 4 });
        }
        else if (splitIndices.Count == 0 && segCount > 0)
        {
            for (int i = 0; i < segCount; i++) splitIndices.Add(i);
            result.AuditLog.Add($"Ring {ringIndex}: No corner split points found on {segCount}-segment ring; split at segment boundaries.");
        }

        var pieces = new List<CoreBoundaryPiece>();
        for (int s = 0; s < splitIndices.Count; s++)
        {
            int startVtx = splitIndices[s];
            int endVtx = splitIndices[(s + 1) % splitIndices.Count];

            var piece = new CoreBoundaryPiece { RingIndex = ringIndex };
            int currSeg = startVtx;
            int walked = 0;
            do
            {
                piece.Segments.Add(segments[currSeg]);
                currSeg = (currSeg + 1) % segCount;
                walked++;
            }
            while (walked < segCount && (currSeg != endVtx || splitIndices.Count == 1));

            if (piece.Segments.Count > 0)
            {
                pieces.Add(piece);
            }
        }

        // =========================================================================
        // STEP 2 — Direction of each piece (outward normal) (A3)
        // =========================================================================
        foreach (var piece in pieces)
        {
            // Length-weighted circular mean of segments' outward normals
            double sumSin = 0;
            double sumCos = 0;

            foreach (var seg in piece.Segments)
            {
                var chord = seg.ChordVector;
                double len = chord.Length;
                if (len < 1e-9) continue;

                // Tangent vector
                double tx = chord.X / len;
                double ty = chord.Y / len;

                // Outward normal:
                // If inside is on right => outward is to the LEFT: (-ty, tx)
                // If inside is on left => outward is to the RIGHT: (ty, -tx)
                double nx = insideIsOnRight ? -ty : ty;
                double ny = insideIsOnRight ? tx : -tx;

                // Azimuth in compass degrees (0 = North, 90 = East, 180 = South, 270 = West)
                double segAzimuth = (Math.Atan2(nx, ny) * (180.0 / Math.PI) + 360.0) % 360.0;

                sumSin += seg.Length * Math.Sin(segAzimuth * (Math.PI / 180.0));
                sumCos += seg.Length * Math.Cos(segAzimuth * (Math.PI / 180.0));
            }

            double meanAzimuth = (Math.Atan2(sumSin, sumCos) * (180.0 / Math.PI) + 360.0) % 360.0;

            // Apply True North meridian convergence correction
            if (options.TrueNorthCorrection)
            {
                meanAzimuth = (meanAzimuth + convergenceDeg + 360.0) % 360.0;
            }

            piece.OutwardAzimuthDeg = meanAzimuth;
        }

        // Cardinal direction assignment using 4-direction cross optimization to guarantee
        // that opposite sides (N<->S and E<->W) are kept strictly opposite and tie-breaking is deterministic
        AssignCardinalDirections(pieces, result.AuditLog, ringIndex);

        // =========================================================================
        // STEP 3 — Merge into single-part lines (no multipart) (A4)
        // =========================================================================
        var finalPieces = MergePiecesIntoSinglePartRuns(pieces, options, result.AuditLog, ringIndex);

        result.FinalLines = finalPieces;

        // Check for fewer than 4 directions
        var distinctDirections = finalPieces.Select(p => p.CardinalDirection).Distinct().ToList();
        if (distinctDirections.Count < 4)
        {
            result.HasFewerThan4Directions = true;
            result.Warnings.Add($"FEWER_THAN_4_DIRECTIONS in Ring {ringIndex}: Generated {distinctDirections.Count} directions instead of 4.");
            result.AuditLog.Add($"Ring {ringIndex}: Flagged FEWER_THAN_4_DIRECTIONS ({distinctDirections.Count} directions present). No lines invented.");
        }

        // =========================================================================
        // STEP 4 — Geometry-preservation checks (blocking) (A5)
        // =========================================================================
        double totalOutputLength = finalPieces.Sum(p => p.TotalLength);
        double perimeterDiff = Math.Abs(totalOutputLength - result.RingPerimeter);

        if (perimeterDiff > 1e-4)
        {
            result.IntegrityChecksPassed = false;
            result.Warnings.Add($"Perimeter mismatch in Ring {ringIndex}: original {result.RingPerimeter:F6}m vs output {totalOutputLength:F6}m (diff: {perimeterDiff:E2}m).");
        }

        int origCurveCount = segments.Count(s => s.Type != CurveType.Straight);
        int outputCurveCount = finalPieces.Sum(p => p.Segments.Count(s => s.Type != CurveType.Straight));
        if (origCurveCount != outputCurveCount)
        {
            result.IntegrityChecksPassed = false;
            result.Warnings.Add($"Curve count mismatch in Ring {ringIndex}: original {origCurveCount} vs output {outputCurveCount}.");
        }

        return result;
    }

    /// <summary>
    /// Computes interior angle in degrees between incoming chord and outgoing chord,
    /// measured strictly inside the parcel material.
    /// </summary>
    public static double ComputeInteriorAngleDeg(Point2D chordIn, Point2D chordOut, bool insideIsOnRight)
    {
        double angleIn = Math.Atan2(chordIn.Y, chordIn.X);
        double angleOut = Math.Atan2(chordOut.Y, chordOut.X);

        // Turn angle from incoming to outgoing: positive = left turn (CCW), negative = right turn (CW)
        double turnRad = angleOut - angleIn;
        while (turnRad <= -Math.PI) turnRad += 2.0 * Math.PI;
        while (turnRad > Math.PI) turnRad -= 2.0 * Math.PI;

        double turnDeg = turnRad * (180.0 / Math.PI);

        // If inside of parcel is to the right of traversal:
        // A right turn (turnDeg < 0) bends around parcel material => Convex: 180 + turnDeg = 180 - |turnDeg| < 180°
        // A left turn (turnDeg > 0) bends away from parcel material => Reflex: 180 + turnDeg > 180°
        if (insideIsOnRight)
        {
            double interior = 180.0 + turnDeg;
            if (interior <= 0) interior += 360.0;
            if (interior > 360.0) interior -= 360.0;
            return interior;
        }
        else
        {
            // Inside is to the left:
            // A left turn (turnDeg > 0) bends around parcel material => Convex: 180 - turnDeg < 180°
            // A right turn (turnDeg < 0) bends away from parcel material => Reflex: 180 + |turnDeg| > 180°
            double interior = 180.0 - turnDeg;
            if (interior <= 0) interior += 360.0;
            if (interior > 360.0) interior -= 360.0;
            return interior;
        }
    }

    /// <summary>
    /// Assigns cardinal directions (1=North, 2=East, 3=South, 4=West) to pieces,
    /// optimizing a 4-direction cross for rotated/irregular parcels and resolving ties deterministically.
    /// </summary>
    private static void AssignCardinalDirections(List<CoreBoundaryPiece> pieces, List<string> auditLog, int ringIndex)
    {
        if (pieces.Count == 0) return;

        // Try standard cardinal assignment first
        foreach (var p in pieces)
        {
            p.CardinalDirection = ClassifyAzimuthToCardinal(p.OutwardAzimuthDeg);
        }

        // Check if opposite directions are reasonably distributed or if parcel is rotated ~45°
        // Test cross offsets from 0° to 89° with 1° step to find minimum length-weighted angular deviation
        double bestScore = double.MaxValue;
        double bestOffset = 0;

        for (double offset = 0; offset < 90.0; offset += 1.0)
        {
            double score = 0;
            foreach (var p in pieces)
            {
                double d0 = AngularDifferenceDeg(p.OutwardAzimuthDeg, offset);
                double d1 = AngularDifferenceDeg(p.OutwardAzimuthDeg, offset + 90.0);
                double d2 = AngularDifferenceDeg(p.OutwardAzimuthDeg, offset + 180.0);
                double d3 = AngularDifferenceDeg(p.OutwardAzimuthDeg, offset + 270.0);

                double minDev = Math.Min(Math.Min(d0, d1), Math.Min(d2, d3));
                score += p.TotalLength * (minDev * minDev);
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestOffset = offset;
            }
        }

        // If bestOffset is significantly non-zero (rotated parcel > 5°), align by the optimal cross
        if (bestOffset > 5.0 && bestOffset < 85.0)
        {
            auditLog.Add($"Ring {ringIndex}: Rotated parcel cross optimization applied (optimal rotation: {bestOffset:F1}°).");
            foreach (var p in pieces)
            {
                double dN = AngularDifferenceDeg(p.OutwardAzimuthDeg, bestOffset);
                double dE = AngularDifferenceDeg(p.OutwardAzimuthDeg, bestOffset + 90.0);
                double dS = AngularDifferenceDeg(p.OutwardAzimuthDeg, bestOffset + 180.0);
                double dW = AngularDifferenceDeg(p.OutwardAzimuthDeg, bestOffset + 270.0);

                double minDev = Math.Min(Math.Min(dN, dE), Math.Min(dS, dW));

                // Check for ties within 1.0°
                var candidates = new List<(int dir, double diff)>
                {
                    (1, dN), (2, dE), (3, dS), (4, dW)
                }.Where(c => Math.Abs(c.diff - minDev) < 1.0).ToList();

                if (candidates.Count > 1)
                {
                    // Tie-break: order N (1), E (2), S (3), W (4)
                    int chosenDir = candidates.OrderBy(c => c.dir).First().dir;
                    auditLog.Add($"Ring {ringIndex}: Near-tie on piece (azimuth {p.OutwardAzimuthDeg:F1}°) between directions [{string.Join(", ", candidates.Select(c => c.dir))}]. Deterministically assigned to {chosenDir}.");
                    p.CardinalDirection = chosenDir;
                }
                else
                {
                    if (Math.Abs(minDev - dN) < 1e-6) p.CardinalDirection = 1;
                    else if (Math.Abs(minDev - dE) < 1e-6) p.CardinalDirection = 2;
                    else if (Math.Abs(minDev - dS) < 1e-6) p.CardinalDirection = 3;
                    else p.CardinalDirection = 4;
                }
            }
        }
    }

    /// <summary>
    /// Classifies an azimuth in compass degrees to the nearest cardinal direction:
    /// 1 = North [315°..360°) U [0°..45°)
    /// 2 = East  [45°..135°)
    /// 3 = South [135°..225°)
    /// 4 = West  [225°..315°)
    /// </summary>
    public static int ClassifyAzimuthToCardinal(double azimuth)
    {
        double az = (azimuth % 360.0 + 360.0) % 360.0;
        if (az >= 315.0 || az < 45.0) return 1; // North
        if (az >= 45.0 && az < 135.0) return 2; // East
        if (az >= 135.0 && az < 225.0) return 3; // South
        return 4; // West
    }

    /// <summary>
    /// Merges cyclic boundary pieces into single-part lines using Rule 1 and Rule 2.
    /// Never creates multipart geometries. Output per ring has at most 4 lines.
    /// </summary>
    private static List<CoreBoundaryPiece> MergePiecesIntoSinglePartRuns(
        List<CoreBoundaryPiece> pieces,
        BorderExtractionOptions options,
        List<string> auditLog,
        int ringIndex)
    {
        if (pieces.Count <= 1) return pieces;

        // Form initial cyclic runs of adjacent pieces sharing the same direction
        var runs = GroupAdjacentPieces(pieces);

        // Repeat until every direction appears in at most one run
        int maxIterations = pieces.Count * 2;
        int iter = 0;

        while (iter++ < maxIterations)
        {
            runs = GroupAdjacentRuns(runs);

            // Count occurrences of each direction
            var dirCounts = runs.GroupBy(r => r.CardinalDirection).ToDictionary(g => g.Key, g => g.Count());
            var multiDir = dirCounts.FirstOrDefault(kvp => kvp.Value > 1);

            if (multiDir.Key == 0)
            {
                // Every direction has at most 1 run!
                break;
            }

            // Rule 1: Absorb in-between piece (X - Y - X)
            bool rule1Fired = false;
            int rCount = runs.Count;

            for (int i = 0; i < rCount; i++)
            {
                int prev = (i - 1 + rCount) % rCount;
                int next = (i + 1) % rCount;

                int dirX = runs[prev].CardinalDirection;
                int dirY = runs[i].CardinalDirection;
                int dirNext = runs[next].CardinalDirection;

                if (dirX == dirNext && dirX != dirY)
                {
                    // Guard 1: Y must be shorter than both flanking X runs * guard ratio
                    double lenY = runs[i].TotalLength;
                    double lenXPrev = runs[prev].TotalLength;
                    double lenXNext = runs[next].TotalLength;

                    bool shorterThanFlanks = lenY <= (lenXPrev * options.Rule1LengthRatioGuard) &&
                                             lenY <= (lenXNext * options.Rule1LengthRatioGuard);

                    // Guard 2: Y must still keep at least one other run in the ring
                    bool yKeepsAnotherRun = dirCounts.TryGetValue(dirY, out int cY) && cY > 1;

                    if (shorterThanFlanks && yKeepsAnotherRun)
                    {
                        auditLog.Add($"Ring {ringIndex}: Rule 1 fired: Absorbed run of direction {dirY} (len: {lenY:F2}m) into flanking direction {dirX}.");
                        runs[i].CardinalDirection = dirX;
                        rule1Fired = true;
                        break;
                    }
                }
            }

            if (rule1Fired) continue;

            // Rule 2: Longest wins
            // If Rule 1 cannot fire, the longest run of direction X keeps direction X.
            // Each shorter run of X is merged into an adjacent line and takes that neighbor's direction.
            int targetDir = multiDir.Key;
            var sameDirRuns = runs.Where(r => r.CardinalDirection == targetDir).ToList();
            var longestRun = sameDirRuns.OrderByDescending(r => r.TotalLength).First();

            // Find the shortest run of targetDir to merge into an adjacent neighbor
            var shortestRun = sameDirRuns.Where(r => r != longestRun).OrderBy(r => r.TotalLength).First();
            int shortIdx = runs.IndexOf(shortestRun);

            int prevIdx = (shortIdx - 1 + runs.Count) % runs.Count;
            int nextIdx = (shortIdx + 1) % runs.Count;

            var prevNeighbor = runs[prevIdx];
            var nextNeighbor = runs[nextIdx];

            // Choose neighbor whose direction is angularly closest to short run's outward azimuth
            double prevDirAzimuth = CardinalToAzimuth(prevNeighbor.CardinalDirection);
            double nextDirAzimuth = CardinalToAzimuth(nextNeighbor.CardinalDirection);

            double diffPrev = AngularDifferenceDeg(shortestRun.OutwardAzimuthDeg, prevDirAzimuth);
            double diffNext = AngularDifferenceDeg(shortestRun.OutwardAzimuthDeg, nextDirAzimuth);

            int chosenNeighborIdx;
            if (Math.Abs(diffPrev - diffNext) > 1e-4)
            {
                chosenNeighborIdx = diffPrev < diffNext ? prevIdx : nextIdx;
            }
            else
            {
                // Tie -> choose longer neighbor
                chosenNeighborIdx = prevNeighbor.TotalLength >= nextNeighbor.TotalLength ? prevIdx : nextIdx;
            }

            int absorbedDir = runs[chosenNeighborIdx].CardinalDirection;
            auditLog.Add($"Ring {ringIndex}: Rule 2 fired: Shorter run of direction {targetDir} (len: {shortestRun.TotalLength:F2}m) merged into adjacent direction {absorbedDir}.");
            shortestRun.CardinalDirection = absorbedDir;
        }

        return GroupAdjacentRuns(runs);
    }

    private static List<CoreBoundaryPiece> GroupAdjacentPieces(List<CoreBoundaryPiece> pieces)
    {
        var runs = new List<CoreBoundaryPiece>();
        foreach (var p in pieces)
        {
            if (runs.Count > 0 && runs.Last().CardinalDirection == p.CardinalDirection)
            {
                runs.Last().Segments.AddRange(p.Segments);
            }
            else
            {
                var newPiece = new CoreBoundaryPiece
                {
                    RingIndex = p.RingIndex,
                    CardinalDirection = p.CardinalDirection,
                    OutwardAzimuthDeg = p.OutwardAzimuthDeg
                };
                newPiece.Segments.AddRange(p.Segments);
                runs.Add(newPiece);
            }
        }

        // Cyclic wrap-around merge
        if (runs.Count > 1 && runs.First().CardinalDirection == runs.Last().CardinalDirection)
        {
            runs.First().Segments.InsertRange(0, runs.Last().Segments);
            runs.RemoveAt(runs.Count - 1);
        }

        return runs;
    }

    private static List<CoreBoundaryPiece> GroupAdjacentRuns(List<CoreBoundaryPiece> runs)
    {
        var merged = new List<CoreBoundaryPiece>();
        foreach (var r in runs)
        {
            if (merged.Count > 0 && merged.Last().CardinalDirection == r.CardinalDirection)
            {
                merged.Last().Segments.AddRange(r.Segments);
            }
            else
            {
                var copy = new CoreBoundaryPiece
                {
                    RingIndex = r.RingIndex,
                    CardinalDirection = r.CardinalDirection,
                    OutwardAzimuthDeg = r.OutwardAzimuthDeg
                };
                copy.Segments.AddRange(r.Segments);
                merged.Add(copy);
            }
        }

        // Cyclic wrap-around
        if (merged.Count > 1 && merged.First().CardinalDirection == merged.Last().CardinalDirection)
        {
            merged.First().Segments.InsertRange(0, merged.Last().Segments);
            merged.RemoveAt(merged.Count - 1);
        }

        return merged;
    }

    public static double CardinalToAzimuth(int cardinal) => cardinal switch
    {
        1 => 0.0,   // North
        2 => 90.0,  // East
        3 => 180.0, // South
        4 => 270.0, // West
        _ => 0.0
    };

    public static double AngularDifferenceDeg(double a1, double a2)
    {
        double diff = Math.Abs((a1 - a2 + 360.0) % 360.0);
        return diff > 180.0 ? 360.0 - diff : diff;
    }

    public static List<CoreBoundaryVertex> DetectCornerVertices(
        IReadOnlyList<CoreBoundarySegment> segments,
        bool isHole,
        BorderExtractionOptions options,
        List<string>? auditLog = null)
    {
        var vertices = new List<CoreBoundaryVertex>();
        if (segments == null || segments.Count == 0) return vertices;

        int segCount = segments.Count;
        double signedArea = 0;
        for (int i = 0; i < segCount; i++)
        {
            var p1 = segments[i].Start;
            var p2 = segments[i].End;
            signedArea += (p1.X * p2.Y) - (p2.X * p1.Y);
        }
        bool insideIsOnRight = isHole ? (signedArea >= 0) : (signedArea <= 0);

        for (int i = 0; i < segCount; i++)
        {
            int prevIdx = (i - 1 + segCount) % segCount;
            var segIn = segments[prevIdx];
            var segOut = segments[i];

            var vtx = new CoreBoundaryVertex(i, segOut.Start);

            // Arc-to-arc joints: check whether vertex is real
            if (segIn.Type == CurveType.CircularArc && segOut.Type == CurveType.CircularArc)
            {
                // Check 1: Same circle
                bool sameCenter = segIn.Center.HasValue && segOut.Center.HasValue &&
                    segIn.Center.Value.DistanceTo(segOut.Center.Value) <= options.ArcCenterToleranceMeters;
                bool sameRadius = segIn.Radius.HasValue && segOut.Radius.HasValue &&
                    Math.Abs(segIn.Radius.Value - segOut.Radius.Value) <= options.ArcRadiusToleranceMeters;
                bool sameTurning = segIn.IsCounterClockwise == segOut.IsCounterClockwise;

                if (sameCenter && sameRadius && sameTurning)
                {
                    vtx.IsRedundantArcJoint = true;
                    vtx.IsSplitPoint = false;
                    auditLog?.Add($"Redundant arc joint skipped at vertex {i} ({vtx.Point.X:F2}, {vtx.Point.Y:F2}) [Same Circle].");
                    vertices.Add(vtx);
                    continue;
                }

                // Check 2: Different circles but tangent-continuous
                if (segIn.EndTangentDeg.HasValue && segOut.StartTangentDeg.HasValue)
                {
                    double tangentDiff = AngularDifferenceDeg(segIn.EndTangentDeg.Value, segOut.StartTangentDeg.Value);
                    if (tangentDiff <= options.ArcTangentToleranceDeg)
                    {
                        vtx.IsSmoothCompoundArcJoint = true;
                        vtx.IsSplitPoint = false;
                        auditLog?.Add($"Tangent-continuous arc joint skipped at vertex {i} ({vtx.Point.X:F2}, {vtx.Point.Y:F2}) [Diff: {tangentDiff:F2}°].");
                        vertices.Add(vtx);
                        continue;
                    }
                }
            }

            // Virtual chord angle test
            var chordIn = segIn.ChordVector;
            var chordOut = segOut.ChordVector;

            double interiorAngle = ComputeInteriorAngleDeg(chordIn, chordOut, insideIsOnRight);
            vtx.InteriorAngleDeg = interiorAngle;

            // Check convex range [AMin, AMax]
            bool isConvexCorner = interiorAngle >= options.AMinDeg && interiorAngle <= options.AMaxDeg;

            // Check reflex range [360 - AMax, 360 - AMin]
            double reflexMin = 360.0 - options.AMaxDeg;
            double reflexMax = 360.0 - options.AMinDeg;
            bool isReflexCorner = options.IncludeReflexCorners && (interiorAngle >= reflexMin && interiorAngle <= reflexMax);

            if (isConvexCorner)
            {
                vtx.IsSplitPoint = true;
                vtx.SplitReason = $"Convex angle {interiorAngle:F1}° in [{options.AMinDeg}..{options.AMaxDeg}]";
            }
            else if (isReflexCorner)
            {
                vtx.IsSplitPoint = true;
                vtx.SplitReason = $"Reflex angle {interiorAngle:F1}° in [{reflexMin}..{reflexMax}]";
            }

            vertices.Add(vtx);
        }

        // Corner clusters (chamfers and rounded corners closer than L)
        for (int i = 0; i < segCount; i++)
        {
            if (vertices[i].IsSplitPoint) continue;

            int nextIdx = (i + 1) % segCount;
            double chordDist = segments[i].Length;

            if (chordDist <= options.ClusterLengthMeters)
            {
                int prevSegIdx = (i - 1 + segCount) % segCount;
                var inChord = segments[prevSegIdx].ChordVector;
                var outChord = segments[nextIdx].ChordVector;

                double combinedAngle = ComputeInteriorAngleDeg(inChord, outChord, insideIsOnRight);

                bool clusterConvex = combinedAngle >= options.AMinDeg && combinedAngle <= options.AMaxDeg;
                bool clusterReflex = options.IncludeReflexCorners &&
                    (combinedAngle >= (360.0 - options.AMaxDeg) && combinedAngle <= (360.0 - options.AMinDeg));

                if (clusterConvex || clusterReflex)
                {
                    vertices[i].IsSplitPoint = true;
                    vertices[i].SplitReason = $"Cluster start (combined angle {combinedAngle:F1}°)";
                    vertices[nextIdx].IsSplitPoint = true;
                    vertices[nextIdx].SplitReason = $"Cluster end (combined angle {combinedAngle:F1}°)";
                    auditLog?.Add($"Corner cluster detected between vertices {i} and {nextIdx} (length: {chordDist:F2}m, combined angle: {combinedAngle:F1}°). Split at both ends.");
                }
            }
        }

        return vertices;
    }
}
