using System;
using System.Collections.Generic;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Core.Geometry;

/// <summary>
/// Geometric evaluator for shared boundary segments between adjoining cadastral parcels.
/// Enforces Section 2 rules: A shared segment must have a real geometric contact length >= 0.5m
/// to qualify for BORDERTYPE and non-street DESCRIPTION rules.
/// </summary>
public static class SharedSegmentDetector
{
    public const double DefaultMinSharedSegmentLength = 0.5; // meters

    /// <summary>
    /// Checks if a measured shared segment length meets the minimum eligibility threshold.
    /// </summary>
    public static bool IsEligibleSharedSegment(double sharedLength, double minSharedLength = DefaultMinSharedSegmentLength)
    {
        return sharedLength >= (minSharedLength - 1e-6);
    }

    /// <summary>
    /// Computes the exact geometric 1D overlap length between two 2D boundary segments.
    /// Verifies that segments are collinear (or parallel within tolerance) and distance between lines is within snapTolerance.
    /// </summary>
    public static double CalculateSharedLength(Segment2D segA, Segment2D segB, double snapTolerance = 0.05)
    {
        double lenA = segA.Length;
        double lenB = segB.Length;
        if (lenA < 1e-6 || lenB < 1e-6) return 0.0;

        var dirA = segA.Direction;
        var dirB = segB.Direction;

        // Check if parallel or anti-parallel: dot product absolute value close to 1
        double dot = Math.Abs(dirA.X * dirB.X + dirA.Y * dirB.Y);
        if (dot < 0.985) // angle deviation > ~10 degrees
            return 0.0;

        // Check perpendicular distance from segB.Start and segB.End to line containing segA
        double distStart = PointToLineDistance(segB.Start, segA.Start, dirA);
        double distEnd = PointToLineDistance(segB.End, segA.Start, dirA);

        if (distStart > snapTolerance || distEnd > snapTolerance)
            return 0.0;

        // Project segB start and end onto line segA: t = dot((P - A.Start), dirA)
        double tStartB = (segB.Start.X - segA.Start.X) * dirA.X + (segB.Start.Y - segA.Start.Y) * dirA.Y;
        double tEndB = (segB.End.X - segA.Start.X) * dirA.X + (segB.End.Y - segA.Start.Y) * dirA.Y;

        double minB = Math.Min(tStartB, tEndB);
        double maxB = Math.Max(tStartB, tEndB);

        // segA interval is [0, lenA]
        double overlapStart = Math.Max(0.0, minB);
        double overlapEnd = Math.Min(lenA, maxB);

        double overlap = overlapEnd - overlapStart;
        return overlap > 0.0 ? overlap : 0.0;
    }

    /// <summary>
    /// Calculates the total shared boundary length between a list of border segments and a neighbor polygon's segments.
    /// </summary>
    public static double CalculateTotalSharedLength(
        IReadOnlyList<Segment2D> borderSegments,
        IReadOnlyList<Segment2D> neighborSegments,
        double snapTolerance = 0.05)
    {
        if (borderSegments == null || neighborSegments == null) return 0.0;

        double totalShared = 0.0;
        foreach (var bSeg in borderSegments)
        {
            foreach (var nSeg in neighborSegments)
            {
                double shared = CalculateSharedLength(bSeg, nSeg, snapTolerance);
                totalShared += shared;
            }
        }
        return totalShared;
    }

    private static double PointToLineDistance(Point2D pt, Point2D linePoint, Point2D lineDir)
    {
        // Vector from linePoint to pt
        double dx = pt.X - linePoint.X;
        double dy = pt.Y - linePoint.Y;

        // Cross product with unit lineDir: |dx * lineDir.Y - dy * lineDir.X|
        return Math.Abs(dx * lineDir.Y - dy * lineDir.X);
    }
}
