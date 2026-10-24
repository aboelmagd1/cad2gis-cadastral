using System;
using System.Collections.Generic;
using System.Linq;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

public readonly struct Point2D
{
    public double X { get; }
    public double Y { get; }

    public Point2D(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static Point2D operator +(Point2D a, Point2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2D operator *(Point2D a, double scalar) => new(a.X * scalar, a.Y * scalar);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public Point2D Normalize()
    {
        double len = Length;
        return len > 1e-9 ? new Point2D(X / len, Y / len) : new Point2D(0, 0);
    }

    public double DistanceTo(Point2D other) => (this - other).Length;
}

public class Segment2D
{
    public Point2D Start { get; set; }
    public Point2D End { get; set; }
    public string? ElementId { get; set; }
    public string? ElementType { get; set; } // "Parcel", "SubParcel", "Road"

    public Segment2D(Point2D start, Point2D end, string? elementId = null, string? elementType = null)
    {
        Start = start;
        End = end;
        ElementId = elementId;
        ElementType = elementType;
    }

    public double Length => Start.DistanceTo(End);

    public Point2D Direction => (End - Start).Normalize();

    public Point2D PointAt(double fraction) => Start + (End - Start) * fraction;
}

public class RayHitResult
{
    public bool Hit { get; set; }
    public double Distance { get; set; }
    public Point2D HitPoint { get; set; }
    public string? HitElementId { get; set; }
    public string? HitElementType { get; set; }
}

public class WidthInferenceOptions
{
    public double MaxSearchDistance { get; set; } = 100.0;
    public double PassageStreetThreshold { get; set; } = 10.0;
    public int RayCount { get; set; } = 3; // 3 rays (1/4, 1/2, 3/4) or 1 (1/2)
    public List<double> StandardWidths { get; set; } = new() { 6.0, 8.0, 10.0, 12.0, 15.0, 20.0, 25.0, 30.0 };
    public string PassageBorderType { get; set; } = "LNE"; // Walkway code
    public string StreetBorderType { get; set; } = "STR";
}

public static class WidthInferenceService
{
    public static void InferBorderWidth(
        BorderRecord border,
        Segment2D dimensionSegment,
        Point2D outwardNormal,
        IReadOnlyList<Segment2D> obstacleSegments,
        WidthInferenceOptions options)
    {
        var fractions = options.RayCount switch
        {
            1 => new[] { 0.5 },
            _ => new[] { 0.25, 0.5, 0.75 }
        };

        var validHits = new List<RayHitResult>();
        var allRayDistances = new List<double>();

        const double rayOffsetMeters = 0.05;

        foreach (double frac in fractions)
        {
            var origin = dimensionSegment.PointAt(frac);
            // Start ray slightly outside the boundary (0.05m epsilon) to avoid self-hitting
            var rayStart = origin + outwardNormal * rayOffsetMeters;

            var hit = CastRay(rayStart, outwardNormal, options.MaxSearchDistance, obstacleSegments);
            if (hit.Hit)
            {
                // Add the start offset back so width is measured from the actual parcel boundary (P5.3)
                double boundaryDist = hit.Distance + rayOffsetMeters;
                validHits.Add(hit);
                allRayDistances.Add(boundaryDist);
            }
            else
            {
                // Retry with tiny offset along segment (+0.05m or -0.05m) in case corner grazed
                var tangent = dimensionSegment.Direction;
                var retryStart = rayStart + tangent * 0.05;
                var retryHit = CastRay(retryStart, outwardNormal, options.MaxSearchDistance, obstacleSegments);
                if (retryHit.Hit)
                {
                    double boundaryDist = retryHit.Distance + rayOffsetMeters;
                    validHits.Add(retryHit);
                    allRayDistances.Add(boundaryDist);
                }
            }
        }

        border.RayLengths = allRayDistances;

        if (validHits.Count > 0)
        {
            // P5.3: Define median properly (mean of two middle values for an even count)
            var sorted = allRayDistances.OrderBy(d => d).ToList();
            double medianDist;
            if (sorted.Count % 2 == 1)
            {
                medianDist = sorted[sorted.Count / 2];
            }
            else
            {
                medianDist = (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2.0;
            }

            // P5.2: Use MidpointRounding.AwayFromZero instead of Banker's rounding
            double roundedWidth = Math.Round(medianDist, MidpointRounding.AwayFromZero);

            border.IsInferred = true;
            border.IsUnknown = false;
            border.MeasuredWidth = roundedWidth;
            border.RayLength = Math.Round(medianDist, 2, MidpointRounding.AwayFromZero);
            border.SuggestedWidth = FindNearestStandardWidth(roundedWidth, options.StandardWidths);
            border.HitElementId = validHits[0].HitElementId ?? "FacingEdge";

            if (roundedWidth < options.PassageStreetThreshold)
            {
                border.Descriptio = $"ممر بعرض {roundedWidth} م";
                border.BorderType = options.PassageBorderType;
                border.InferenceRule = $"Width < {options.PassageStreetThreshold}m -> Passage (LNE)";
            }
            else
            {
                border.Descriptio = $"شارع بعرض {roundedWidth} م";
                border.BorderType = options.StreetBorderType;
                border.InferenceRule = $"Width >= {options.PassageStreetThreshold}m -> Street (STR)";
            }
        }
        else
        {
            // P5.1: Keep state in flag, not in deliverable data. Export blank for BORDERTYPE and DESCRIPTIO.
            border.IsInferred = true;
            border.IsUnknown = true;
            border.ReviewStatus = "RequiresReview";
            border.MeasuredWidth = null;
            border.SuggestedWidth = null;
            border.RayLength = null;
            border.HitElementId = null;
            border.BorderType = "";
            border.Descriptio = "";
            border.InferenceRule = $"No obstacle hit within {options.MaxSearchDistance}m -> UNKNOWN";
        }
    }

    public static RayHitResult CastRay(
        Point2D rayOrigin,
        Point2D rayDirection,
        double maxDistance,
        IReadOnlyList<Segment2D> obstacleSegments)
    {
        Point2D rayEnd = rayOrigin + rayDirection * maxDistance;
        double closestDist = double.MaxValue;
        Point2D closestPoint = default;
        Segment2D? closestSeg = null;

        foreach (var seg in obstacleSegments)
        {
            if (TryIntersectRaySegment(rayOrigin, rayDirection, maxDistance, seg.Start, seg.End, out double dist, out Point2D intPt))
            {
                if (dist > 0.01 && dist < closestDist)
                {
                    closestDist = dist;
                    closestPoint = intPt;
                    closestSeg = seg;
                }
            }
        }

        if (closestSeg != null)
        {
            return new RayHitResult
            {
                Hit = true,
                Distance = closestDist,
                HitPoint = closestPoint,
                HitElementId = closestSeg.ElementId,
                HitElementType = closestSeg.ElementType
            };
        }

        return new RayHitResult { Hit = false };
    }

    public static bool TryIntersectRaySegment(
        Point2D rayOrigin,
        Point2D rayDir,
        double maxDist,
        Point2D segA,
        Point2D segB,
        out double distance,
        out Point2D intersection)
    {
        distance = 0;
        intersection = default;

        Point2D p = rayOrigin;
        Point2D r = rayDir * maxDist;
        Point2D q = segA;
        Point2D s = segB - segA;

        double rxs = (r.X * s.Y) - (r.Y * s.X);
        Point2D qMinusP = q - p;
        double qpxr = (qMinusP.X * r.Y) - (qMinusP.Y * r.X);

        if (Math.Abs(rxs) < 1e-9)
        {
            return false; // Parallel or collinear
        }

        double t = ((qMinusP.X * s.Y) - (qMinusP.Y * s.X)) / rxs;
        double u = qpxr / rxs;

        if (t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0)
        {
            distance = t * maxDist;
            intersection = p + rayDir * distance;
            return true;
        }

        return false;
    }

    public static double FindNearestStandardWidth(double measuredWidth, IReadOnlyList<double> standardWidths)
    {
        if (standardWidths == null || standardWidths.Count == 0) return measuredWidth;
        return standardWidths.OrderBy(w => Math.Abs(w - measuredWidth)).First();
    }
}
