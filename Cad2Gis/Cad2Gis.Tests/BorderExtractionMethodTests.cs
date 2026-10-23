using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class BorderExtractionMethodTests
{
    private readonly BorderExtractionOptions _defaultOptions = new()
    {
        AMinDeg = 10.0,
        AMaxDeg = 160.0,
        MultiSegmentAngleDeviationDeg = 10.0,
        IncludeReflexCorners = true,
        ArcCenterToleranceMeters = 0.05,
        ArcRadiusToleranceMeters = 0.05,
        ArcTangentToleranceDeg = 2.0,
        ClusterLengthMeters = 7.0,
        Rule1LengthRatioGuard = 1.0,
        TrueNorthCorrection = false
    };

    [Fact]
    public void AxisAlignedRectangle_ProducesExactly4Lines()
    {
        // 20m (W-E) by 30m (S-N) rectangle
        // Vertices CW: (0,0) -> (0,30) -> (20,30) -> (20,0) -> (0,0)
        var segments = new List<CoreBoundarySegment>
        {
            new(new Point2D(0, 0), new Point2D(0, 30)),   // West side, outward normal points West (4)
            new(new Point2D(0, 30), new Point2D(20, 30)), // North side, outward normal points North (1)
            new(new Point2D(20, 30), new Point2D(20, 0)), // East side, outward normal points East (2)
            new(new Point2D(20, 0), new Point2D(0, 0))    // South side, outward normal points South (3)
        };

        var result = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, _defaultOptions);

        Assert.Equal(4, result.FinalLines.Count);
        Assert.True(result.IntegrityChecksPassed);
        Assert.False(result.HasFewerThan4Directions);

        // Verify perimeter preservation (tolerance 1e-6)
        double totalLength = result.FinalLines.Sum(l => l.TotalLength);
        Assert.Equal(100.0, totalLength, 6);

        // Verify one of each cardinal direction
        var directions = result.FinalLines.Select(l => l.CardinalDirection).OrderBy(d => d).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4 }, directions);
    }

    [Fact]
    public void RectangleRotated30Degrees_DirectionsFollowTrueNorth()
    {
        // Rectangle rotated 30 degrees CW
        double angleRad = 30.0 * Math.PI / 180.0;
        Point2D Rotate(double x, double y) => new(
            x * Math.Cos(angleRad) + y * Math.Sin(angleRad),
            -x * Math.Sin(angleRad) + y * Math.Cos(angleRad));

        var p0 = Rotate(0, 0);
        var p1 = Rotate(0, 30);
        var p2 = Rotate(20, 30);
        var p3 = Rotate(20, 0);

        var segments = new List<CoreBoundarySegment>
        {
            new(p0, p1),
            new(p1, p2),
            new(p2, p3),
            new(p3, p0)
        };

        var result = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, _defaultOptions);

        Assert.Equal(4, result.FinalLines.Count);
        Assert.True(result.IntegrityChecksPassed);

        var dirs = result.FinalLines.Select(l => l.CardinalDirection).Distinct().ToList();
        Assert.Equal(4, dirs.Count);
    }

    [Fact]
    public void RectangleRotated45Degrees_DeterministicTieBreak()
    {
        // 45 degrees rotated square
        double angleRad = 45.0 * Math.PI / 180.0;
        Point2D Rotate(double x, double y) => new(
            x * Math.Cos(angleRad) + y * Math.Sin(angleRad),
            -x * Math.Sin(angleRad) + y * Math.Cos(angleRad));

        var p0 = Rotate(0, 0);
        var p1 = Rotate(0, 20);
        var p2 = Rotate(20, 20);
        var p3 = Rotate(20, 0);

        var segments = new List<CoreBoundarySegment>
        {
            new(p0, p1),
            new(p1, p2),
            new(p2, p3),
            new(p3, p0)
        };

        var result1 = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, _defaultOptions);
        var result2 = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, _defaultOptions);

        Assert.Equal(4, result1.FinalLines.Count);
        Assert.Equal(result1.FinalLines.Select(l => l.CardinalDirection), result2.FinalLines.Select(l => l.CardinalDirection));
    }

    [Fact]
    public void ChamferedCorner_MergedIntoNeighbour_Produces4Lines()
    {
        // 20x30 rectangle with a 3m chamfer at corner (20, 30):
        // (0,0) -> (0,30) -> (17,30) -> [chamfer 3m] -> (20,27) -> (20,0) -> (0,0)
        var segments = new List<CoreBoundarySegment>
        {
            new(new Point2D(0, 0), new Point2D(0, 30)),
            new(new Point2D(0, 30), new Point2D(17, 30)),
            new(new Point2D(17, 30), new Point2D(20, 27)), // chamfer length ~4.24m <= 7m
            new(new Point2D(20, 27), new Point2D(20, 0)),
            new(new Point2D(20, 0), new Point2D(0, 0))
        };

        var result = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, _defaultOptions);

        Assert.Equal(4, result.FinalLines.Count);
        Assert.True(result.IntegrityChecksPassed);

        // Sum of lengths must match original 5 segments
        double expectedPerimeter = segments.Sum(s => s.Length);
        Assert.Equal(expectedPerimeter, result.FinalLines.Sum(l => l.TotalLength), 5);
    }

    [Fact]
    public void RoundedCornerWithArc_PreservedAndSplitThroughChord()
    {
        // Rectangle with an arc at corner (20, 30) of radius 3m
        var arc = new CoreBoundarySegment(new Point2D(17, 30), new Point2D(20, 27), CurveType.CircularArc, explicitLength: 3.0 * Math.PI / 2.0)
        {
            Center = new Point2D(17, 27),
            Radius = 3.0,
            StartTangentDeg = 90.0,
            EndTangentDeg = 180.0
        };

        var segments = new List<CoreBoundarySegment>
        {
            new(new Point2D(0, 0), new Point2D(0, 30)),
            new(new Point2D(0, 30), new Point2D(17, 30)),
            arc,
            new(new Point2D(20, 27), new Point2D(20, 0)),
            new(new Point2D(20, 0), new Point2D(0, 0))
        };

        var result = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, _defaultOptions);

        Assert.Equal(4, result.FinalLines.Count);
        // Verify true curve preserved
        int curveCount = result.FinalLines.SelectMany(l => l.Segments).Count(s => s.Type == CurveType.CircularArc);
        Assert.Equal(1, curveCount);
    }

    [Fact]
    public void CircleStoredAsTwoArcs_SameCenterRadius_RedundantJoint_NoSplit()
    {
        // Circle split into two semicircular arcs of same center (10, 10) and radius 5
        var arc1 = new CoreBoundarySegment(new Point2D(5, 10), new Point2D(15, 10), CurveType.CircularArc, explicitLength: 5.0 * Math.PI)
        {
            Center = new Point2D(10, 10),
            Radius = 5.0,
            IsCounterClockwise = false,
            StartTangentDeg = 0.0,
            EndTangentDeg = 180.0
        };
        var arc2 = new CoreBoundarySegment(new Point2D(15, 10), new Point2D(5, 10), CurveType.CircularArc, explicitLength: 5.0 * Math.PI)
        {
            Center = new Point2D(10, 10),
            Radius = 5.0,
            IsCounterClockwise = false,
            StartTangentDeg = 180.0,
            EndTangentDeg = 360.0
        };

        var vertices = BorderExtractionCore.DetectCornerVertices(new[] { arc1, arc2 }, isHole: false, _defaultOptions);

        // Neither vertex should be treated as a corner split point
        Assert.All(vertices, v => Assert.False(v.IsSplitPoint));
        Assert.Contains(vertices, v => v.IsRedundantArcJoint);
    }

    [Fact]
    public void TwoTangentContinuousArcs_DifferentRadii_NoSplit()
    {
        // Smooth compound curve: arc1 tangent at end equals arc2 tangent at start
        var arc1 = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 10), CurveType.CircularArc, explicitLength: 15.0)
        {
            Center = new Point2D(0, 10),
            Radius = 10.0,
            EndTangentDeg = 90.0
        };
        var arc2 = new CoreBoundarySegment(new Point2D(10, 10), new Point2D(25, 20), CurveType.CircularArc, explicitLength: 18.0)
        {
            Center = new Point2D(10, 25),
            Radius = 15.0,
            StartTangentDeg = 90.5 // Within 2.0 deg tolerance
        };
        var closing = new CoreBoundarySegment(new Point2D(25, 20), new Point2D(0, 0));

        var vertices = BorderExtractionCore.DetectCornerVertices(new[] { arc1, arc2, closing }, isHole: false, _defaultOptions);

        var jointVertex = vertices.First(v => v.Point.DistanceTo(new Point2D(10, 10)) < 0.01);
        Assert.False(jointVertex.IsSplitPoint);
        Assert.True(jointVertex.IsSmoothCompoundArcJoint);
    }

    [Fact]
    public void TwoArcsMeetingWithTangentDiscontinuity_SplitEvaluatedByChord()
    {
        // Two arcs meeting at sharp 90 deg corner
        var arc1 = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 10), CurveType.CircularArc)
        {
            Center = new Point2D(0, 10),
            Radius = 10.0,
            EndTangentDeg = 90.0
        };
        var arc2 = new CoreBoundarySegment(new Point2D(10, 10), new Point2D(20, 0), CurveType.CircularArc)
        {
            Center = new Point2D(20, 10),
            Radius = 10.0,
            StartTangentDeg = 180.0 // 90 deg tangent break
        };
        var closing = new CoreBoundarySegment(new Point2D(20, 0), new Point2D(0, 0));

        var vertices = BorderExtractionCore.DetectCornerVertices(new[] { arc1, arc2, closing }, isHole: false, _defaultOptions);

        var jointVertex = vertices.First(v => v.Point.DistanceTo(new Point2D(10, 10)) < 0.01);
        Assert.True(jointVertex.IsSplitPoint);
    }

    [Fact]
    public void InteriorAngleMeasuredFromInside_ConvexAndReflexCorners()
    {
        // L-shaped polygon CW:
        // (0,0) -> (0,30) -> (15,30) -> (15,15) -> (30,15) -> (30,0) -> (0,0)
        // Corner at (15,15) is concave (reflex): interior angle = 270°
        var segments = new List<CoreBoundarySegment>
        {
            new(new Point2D(0, 0), new Point2D(0, 30)),
            new(new Point2D(0, 30), new Point2D(15, 30)),
            new(new Point2D(15, 30), new Point2D(15, 15)),
            new(new Point2D(15, 15), new Point2D(30, 15)),
            new(new Point2D(30, 15), new Point2D(30, 0)),
            new(new Point2D(30, 0), new Point2D(0, 0))
        };

        var vertices = BorderExtractionCore.DetectCornerVertices(segments, isHole: false, _defaultOptions);

        var reflexVertex = vertices.First(v => v.Point.DistanceTo(new Point2D(15, 15)) < 0.01);
        Assert.True(reflexVertex.IsSplitPoint);
        Assert.True(reflexVertex.InteriorAngleDeg > 180.0);
        Assert.Equal(270.0, reflexVertex.InteriorAngleDeg, 1);
    }

    [Fact]
    public void ConcaveLShapedParcel_ResolvedByRule1OrRule2_AtMost4LinesPerRing()
    {
        // L-shaped parcel has 6 sides (North occurs twice: (0,30)->(15,30) and (15,15)->(30,15))
        var segments = new List<CoreBoundarySegment>
        {
            new(new Point2D(0, 0), new Point2D(0, 30)),
            new(new Point2D(0, 30), new Point2D(15, 30)),
            new(new Point2D(15, 30), new Point2D(15, 15)),
            new(new Point2D(15, 15), new Point2D(30, 15)),
            new(new Point2D(30, 15), new Point2D(30, 0)),
            new(new Point2D(30, 0), new Point2D(0, 0))
        };

        var result = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, _defaultOptions);

        // Must produce at most 4 single-part lines
        Assert.True(result.FinalLines.Count <= 4);
        var dirCounts = result.FinalLines.GroupBy(l => l.CardinalDirection).ToDictionary(g => g.Key, g => g.Count());
        Assert.All(dirCounts.Values, c => Assert.Equal(1, c)); // No repeated directions within this ring
        Assert.True(result.IntegrityChecksPassed);
    }

    [Fact]
    public void Triangle_FlaggedFewerThan4Directions_NoInventedLines()
    {
        // 3-sided polygon (triangle): (0,0) -> (10,20) -> (20,0) -> (0,0)
        var segments = new List<CoreBoundarySegment>
        {
            new(new Point2D(0, 0), new Point2D(10, 20)),
            new(new Point2D(10, 20), new Point2D(20, 0)),
            new(new Point2D(20, 0), new Point2D(0, 0))
        };

        var result = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, _defaultOptions);

        Assert.True(result.HasFewerThan4Directions);
        Assert.Contains(result.Warnings, w => w.Contains("FEWER_THAN_4_DIRECTIONS"));
        Assert.Equal(3, result.FinalLines.Count); // Exactly 3 lines, none invented!
    }

    [Fact]
    public void ParcelWithOneHole_4Plus4Lines_DirectionsInwardIntoHole()
    {
        // Exterior rectangle 40x40 CW
        var extSegments = new List<CoreBoundarySegment>
        {
            new(new Point2D(0, 0), new Point2D(0, 40)),
            new(new Point2D(0, 40), new Point2D(40, 40)),
            new(new Point2D(40, 40), new Point2D(40, 0)),
            new(new Point2D(40, 0), new Point2D(0, 0))
        };

        // Interior courtyard hole 10x10 CCW: (15,15) -> (25,15) -> (25,25) -> (15,25) -> (15,15)
        var holeSegments = new List<CoreBoundarySegment>
        {
            new(new Point2D(15, 15), new Point2D(25, 15)), // South boundary of hole
            new(new Point2D(25, 15), new Point2D(25, 25)), // East boundary of hole
            new(new Point2D(25, 25), new Point2D(15, 25)), // North boundary of hole
            new(new Point2D(15, 25), new Point2D(15, 15))  // West boundary of hole
        };

        var extResult = BorderExtractionCore.ProcessRing(extSegments, ringIndex: 0, isHole: false, _defaultOptions);
        var holeResult = BorderExtractionCore.ProcessRing(holeSegments, ringIndex: 1, isHole: true, _defaultOptions);

        Assert.Equal(4, extResult.FinalLines.Count);
        Assert.Equal(4, holeResult.FinalLines.Count);

        // Combined parcel lines = 8 lines (4 exterior + 4 hole)
        var allLines = extResult.FinalLines.Concat(holeResult.FinalLines).ToList();
        Assert.Equal(8, allLines.Count);

        // In the parcel with one hole, each cardinal direction appears exactly twice (once per ring)
        var dirCounts = allLines.GroupBy(l => l.CardinalDirection).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(4, dirCounts.Count);
        Assert.All(dirCounts.Values, count => Assert.Equal(2, count));
    }

    [Fact]
    public void RingStartVertexRotationInvariance()
    {
        // Rotate the ring starting vertex around the cycle
        var seg0 = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(0, 30));
        var seg1 = new CoreBoundarySegment(new Point2D(0, 30), new Point2D(20, 30));
        var seg2 = new CoreBoundarySegment(new Point2D(20, 30), new Point2D(20, 0));
        var seg3 = new CoreBoundarySegment(new Point2D(20, 0), new Point2D(0, 0));

        var orderA = new[] { seg0, seg1, seg2, seg3 };
        var orderB = new[] { seg1, seg2, seg3, seg0 };
        var orderC = new[] { seg2, seg3, seg0, seg1 };

        var resA = BorderExtractionCore.ProcessRing(orderA, 0, false, _defaultOptions);
        var resB = BorderExtractionCore.ProcessRing(orderB, 0, false, _defaultOptions);
        var resC = BorderExtractionCore.ProcessRing(orderC, 0, false, _defaultOptions);

        Assert.Equal(resA.FinalLines.Count, resB.FinalLines.Count);
        Assert.Equal(resA.FinalLines.Count, resC.FinalLines.Count);

        var lengthsA = resA.FinalLines.OrderBy(l => l.CardinalDirection).Select(l => l.TotalLength).ToList();
        var lengthsB = resB.FinalLines.OrderBy(l => l.CardinalDirection).Select(l => l.TotalLength).ToList();
        var lengthsC = resC.FinalLines.OrderBy(l => l.CardinalDirection).Select(l => l.TotalLength).ToList();

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(lengthsA[i], lengthsB[i], 5);
            Assert.Equal(lengthsA[i], lengthsC[i], 5);
        }
    }

    [Fact]
    public void InteriorAngleSplitPoints_ConvexAndReflexRange()
    {
        // Test interior angle calculation for convex (< 180°) and concave/reflex (> 180°)
        // CW traversal of 90-degree corner: incoming North (0,1) -> outgoing East (1,0)
        // chordIn = (0, 10), chordOut = (10, 0)
        // Inside is on right for CW traversal
        double angleConvex = BorderExtractionCore.ComputeInteriorAngleDeg(new Point2D(0, 10), new Point2D(10, 0), insideIsOnRight: true);
        Assert.Equal(90.0, angleConvex, 1);
        Assert.True(angleConvex < 180.0, "Convex corner must have interior angle < 180°");

        // Concave/reflex corner: incoming North (0,1) -> outgoing West (-1,0)
        // chordIn = (0, 10), chordOut = (-10, 0)
        double angleReflex = BorderExtractionCore.ComputeInteriorAngleDeg(new Point2D(0, 10), new Point2D(-10, 0), insideIsOnRight: true);
        Assert.Equal(270.0, angleReflex, 1);
        Assert.True(angleReflex > 180.0, "Concave/reflex corner must have interior angle > 180°");

        // Verify split points for angles in [10°, 160°]
        var opts = new BorderExtractionOptions { AMinDeg = 10.0, AMaxDeg = 160.0, IncludeReflexCorners = true };
        
        // 90° is in [10°, 160°] -> should be split point
        Assert.True(angleConvex >= opts.AMinDeg && angleConvex <= opts.AMaxDeg);

        // 270° is in [360 - 160, 360 - 10] = [200°, 350°] -> reflex split point
        Assert.True(angleReflex >= (360.0 - opts.AMaxDeg) && angleReflex <= (360.0 - opts.AMinDeg));

        // 175° is almost straight line (deflection only 5°) -> not a split point
        double angleAlmostStraight = BorderExtractionCore.ComputeInteriorAngleDeg(new Point2D(0, 10), new Point2D(0.87, 9.96), insideIsOnRight: true);
        Assert.False(angleAlmostStraight >= opts.AMinDeg && angleAlmostStraight <= opts.AMaxDeg);
    }

    [Fact]
    public void DetermineDimension_MultiVsSingle_BasedOnAngleDeviationTolerance()
    {
        // Single straight segment
        var line1 = new CoreBoundaryPiece
        {
            CardinalDirection = 1,
            Segments = new List<CoreBoundarySegment>
            {
                new(new Point2D(0, 0), new Point2D(0, 10))
            }
        };
        Assert.Equal("Single", line1.DetermineDimension(10.0));

        // Two collinear segments (angle = 180°, deflection = 0°)
        var line2 = new CoreBoundaryPiece
        {
            CardinalDirection = 1,
            Segments = new List<CoreBoundarySegment>
            {
                new(new Point2D(0, 0), new Point2D(0, 10)),
                new(new Point2D(0, 10), new Point2D(0, 20))
            }
        };
        Assert.Equal("Single", line2.DetermineDimension(10.0));

        // Two segments with deflection 5° (angle between them = 175°):
        // Since deviation <= 10° tolerance, it is considered "Single"
        var lineSmallDeflection = new CoreBoundaryPiece
        {
            CardinalDirection = 1,
            Segments = new List<CoreBoundarySegment>
            {
                new(new Point2D(0, 0), new Point2D(0, 10)),
                new(new Point2D(0, 10), new Point2D(0.872, 19.962)) // ~5° turn
            }
        };
        Assert.Equal("Single", lineSmallDeflection.DetermineDimension(10.0));

        // Two segments with deflection 15° (angle between them = 165°):
        // Deviation > 10° tolerance -> it is NOT one segment, returns "Multi"
        var lineLargeDeflection = new CoreBoundaryPiece
        {
            CardinalDirection = 1,
            Segments = new List<CoreBoundarySegment>
            {
                new(new Point2D(0, 0), new Point2D(0, 10)),
                new(new Point2D(0, 10), new Point2D(2.588, 19.659)) // ~15° turn
            }
        };
        Assert.Equal("Multi", lineLargeDeflection.DetermineDimension(10.0));

        // If tolerance is relaxed to 20°, then 15° deflection is within tolerance -> "Single"
        Assert.Equal("Single", lineLargeDeflection.DetermineDimension(20.0));

        // If tolerance is tightened to 3°, even 5° deflection is outside tolerance -> "Multi"
        Assert.Equal("Multi", lineSmallDeflection.DetermineDimension(3.0));
    }
}
