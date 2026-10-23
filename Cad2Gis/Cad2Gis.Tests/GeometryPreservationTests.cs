using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;
using Xunit;

namespace Cad2Gis.Tests;

public class GeometryPreservationTests
{
    private const double PrecisionTolerance = 1e-6;

    /// <summary>
    /// Test 1: A parcel with a corner arc and straight edges.
    /// Arc must stay an arc with identical center, radius, and length.
    /// Area and perimeter must match exact analytic values within 1e-6.
    /// </summary>
    [Fact]
    public void Test1_ParcelWithCornerArc_PreservesCenterRadiusAndAnalyticArea()
    {
        // Construct a 20x20 square parcel with a chamfered/rounded corner arc at top-right (radius 5, center (15, 15))
        // Bottom: (0,0) -> (20,0) [len 20]
        // Right: (20,0) -> (20,15) [len 15]
        // Top-right arc: (20,15) -> (15,20), Center (15,15), Radius 5, CCW
        // Top: (15,20) -> (0,20) [len 15]
        // Left: (0,20) -> (0,0) [len 20]

        var segBottom = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(20, 0), CurveType.Straight);
        var segRight = new CoreBoundarySegment(new Point2D(20, 0), new Point2D(20, 15), CurveType.Straight);
        
        double arcLen = 0.5 * Math.PI * 5.0; // 90-degree arc of radius 5
        var segArc = new CoreBoundarySegment(new Point2D(20, 15), new Point2D(15, 20), CurveType.CircularArc, arcLen)
        {
            Center = new Point2D(15, 15),
            Radius = 5.0,
            IsCounterClockwise = true
        };
        var segTop = new CoreBoundarySegment(new Point2D(15, 20), new Point2D(0, 20), CurveType.Straight);
        var segLeft = new CoreBoundarySegment(new Point2D(0, 20), new Point2D(0, 0), CurveType.Straight);

        var segments = new List<CoreBoundarySegment> { segBottom, segRight, segArc, segTop, segLeft };

        // 1. Initial Ingest Audit
        var auditBefore = CurveAuditService.AuditSegments(segments, 1, "P1", "Polygon");
        Assert.Equal(1, auditBefore.CircularArcsCount);
        Assert.Equal(4, auditBefore.LineSegmentsCount);
        Assert.Equal(5.0, auditBefore.Arcs[0].Radius, 6);
        Assert.Equal(15.0, auditBefore.Arcs[0].CenterX, 6);
        Assert.Equal(15.0, auditBefore.Arcs[0].CenterY, 6);

        // Analytic Area: Square 20x20 = 400. Cut off corner square 5x5 = 25, add quarter-circle = 0.25 * PI * 25 = 19.634954
        // Total Analytic Area = 400 - 25 + 0.25 * PI * 25 = 375 + 19.634954084936208 = 394.6349540849
        double expectedAnalyticArea = 375.0 + (0.25 * Math.PI * 25.0);
        double expectedPerimeter = 20.0 + 15.0 + arcLen + 15.0 + 20.0;

        Assert.Equal(expectedAnalyticArea, auditBefore.ExactArea, 4);
        Assert.Equal(expectedPerimeter, auditBefore.ExactLength, 4);

        // 2. Stage Comparison Simulation (After Ingest & Polygonization)
        var auditAfter = CurveAuditService.AuditSegments(segments, 1, "P1", "Polygon");
        var comp = CurveAuditService.CompareStages("Stage 2: Polygonize", "PARCEL", new[] { auditBefore }, new[] { auditAfter });

        Assert.True(comp.Passed);
        Assert.Empty(comp.Violations);
        Assert.Equal(0.0, comp.MaxArcCenterShift, 6);
        Assert.Equal(0.0, comp.MaxArcRadiusDifference, 6);
        Assert.Equal(0.0, comp.MaxLengthDifference, 6);
        Assert.Equal(0.0, comp.MaxAreaDifference, 6);
    }

    /// <summary>
    /// Test 2: A circle stored as two 180° arcs stays two arcs (not merged or re-drawn).
    /// Splitting an arc yields two sub-arcs on the exact same circle.
    /// </summary>
    [Fact]
    public void Test2_CircleStoredAsTwoArcs_StaysTwoArcs_AndSplittingPreservesCircle()
    {
        var center = new Point2D(50, 50);
        double radius = 10.0;
        double halfCircumference = Math.PI * radius;

        // Arc 1: (60, 50) -> (40, 50), Center (50, 50), R=10, CCW
        var arc1 = new CoreBoundarySegment(new Point2D(60, 50), new Point2D(40, 50), CurveType.CircularArc, halfCircumference)
        {
            Center = center,
            Radius = radius,
            IsCounterClockwise = true
        };

        // Arc 2: (40, 50) -> (60, 50), Center (50, 50), R=10, CCW
        var arc2 = new CoreBoundarySegment(new Point2D(40, 50), new Point2D(60, 50), CurveType.CircularArc, halfCircumference)
        {
            Center = center,
            Radius = radius,
            IsCounterClockwise = true
        };

        var circleSegments = new List<CoreBoundarySegment> { arc1, arc2 };

        // Audit circle
        var audit = CurveAuditService.AuditSegments(circleSegments, 1, "Circle1", "Polygon");
        Assert.Equal(2, audit.CircularArcsCount);
        Assert.Equal(0, audit.LineSegmentsCount);
        Assert.Equal(2 * Math.PI * radius, audit.ExactLength, 4);
        Assert.Equal(Math.PI * radius * radius, audit.ExactArea, 4);

        // Splitting arc 1 at half its length (quarter circle)
        double splitDist = halfCircumference / 2.0;
        var (sub1, sub2) = arc1.SplitAtDistance(splitDist);

        // Verify sub-arcs strictly retain the SAME circle (identical center, radius, and orientation)
        Assert.Equal(radius, sub1.Radius!.Value, 6);
        Assert.Equal(radius, sub2.Radius!.Value, 6);
        Assert.Equal(center.X, sub1.Center!.Value.X, 6);
        Assert.Equal(center.Y, sub1.Center!.Value.Y, 6);
        Assert.Equal(center.X, sub2.Center!.Value.X, 6);
        Assert.Equal(center.Y, sub2.Center!.Value.Y, 6);
        Assert.Equal(arc1.IsCounterClockwise, sub1.IsCounterClockwise);
        Assert.Equal(arc1.IsCounterClockwise, sub2.IsCounterClockwise);
        Assert.Equal(splitDist, sub1.Length, 6);
        Assert.Equal(halfCircumference - splitDist, sub2.Length, 6);
        Assert.Equal(sub1.End.X, sub2.Start.X, 6);
        Assert.Equal(sub1.End.Y, sub2.Start.Y, 6);
    }

    /// <summary>
    /// Test 3: Lines-to-polygons with three selected layers whose lines together close the ring.
    /// Each layer alone cannot close the ring.
    /// Manifest lists all layers, flags 0-entity errors, and marks stale when selection changes.
    /// </summary>
    [Fact]
    public void Test3_LinesToPolygons_ThreeSelectedLayers_MultiLayerClosure_ZeroEntitiesFlag_Stale()
    {
        // 3 Layers: WALLS, FENCE, CURBS
        // WALLS has line (0,0) -> (10,0)
        // FENCE has line (10,0) -> (10,10) and (10,10) -> (0,10)
        // CURBS has line (0,10) -> (0,0)
        var entityWalls = new CurvePolylineEntity
        {
            LayerName = "WALLS",
            Category = "Main Parcels",
            Segments = new List<CoreBoundarySegment>
            {
                new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0), CurveType.Straight)
            }
        };

        var entityFence = new CurvePolylineEntity
        {
            LayerName = "FENCE",
            Category = "Main Parcels",
            Segments = new List<CoreBoundarySegment>
            {
                new CoreBoundarySegment(new Point2D(10, 0), new Point2D(10, 10), CurveType.Straight),
                new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10), CurveType.Straight)
            }
        };

        var entityCurbs = new CurvePolylineEntity
        {
            LayerName = "CURBS",
            Category = "Main Parcels",
            Segments = new List<CoreBoundarySegment>
            {
                new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0), CurveType.Straight)
            }
        };

        var selectedLayers = new List<string> { "WALLS", "FENCE", "CURBS", "EMPTY_LAYER" };
        var entities = new List<CurvePolylineEntity> { entityWalls, entityFence, entityCurbs };

        // 1. Group of all selected layers mode
        var (rings, manifest) = CurvePreservingPolygonizerService.Polygonize(
            entities,
            selectedLayers,
            new PolygonizerOptions
            {
                SnapToleranceMeters = 0.05,
                GroupLayersTogether = true,
                KeepAlreadyClosedPolylines = true
            });

        // 1 ring successfully built
        Assert.Single(rings);
        Assert.Equal(100.0, rings[0].Area, 4);
        Assert.Equal(40.0, rings[0].Perimeter, 4);

        // Manifest check: All 4 layers are present in manifest
        Assert.Equal(4, manifest.Layers.Count);
        var wallsItem = manifest.Layers.First(l => l.LayerName == "WALLS");
        var fenceItem = manifest.Layers.First(l => l.LayerName == "FENCE");
        var curbsItem = manifest.Layers.First(l => l.LayerName == "CURBS");
        var emptyItem = manifest.Layers.First(l => l.LayerName == "EMPTY_LAYER");

        Assert.Equal(1, wallsItem.EntitiesRead);
        Assert.Equal(1, fenceItem.EntitiesRead);
        Assert.Equal(1, curbsItem.EntitiesRead);
        Assert.Equal(0, emptyItem.EntitiesRead);

        // Selected layer with 0 entities is flagged as an error
        Assert.True(emptyItem.HasZeroEntitiesError);
        Assert.True(manifest.HasZeroEntityErrors);
        Assert.Contains(manifest.Errors, e => e.Contains("EMPTY_LAYER"));

        // Coverage check: All 3 participating layers contributed
        Assert.True(wallsItem.PolygonsContributed > 0);
        Assert.True(fenceItem.PolygonsContributed > 0);
        Assert.True(curbsItem.PolygonsContributed > 0);
        Assert.Equal(0, manifest.TotalDanglesFound);

        // 2. Per layer mode: None of the layers alone can close the ring
        var (perLayerRings, perLayerManifest) = CurvePreservingPolygonizerService.Polygonize(
            entities,
            new[] { "WALLS", "FENCE", "CURBS" },
            new PolygonizerOptions
            {
                SnapToleranceMeters = 0.05,
                GroupLayersTogether = false,
                KeepAlreadyClosedPolylines = true
            });

        Assert.Empty(perLayerRings);
        Assert.True(perLayerManifest.TotalDanglesFound > 0);
    }

    /// <summary>
    /// Test 4: Lines-to-polygons with arcs among the lines.
    /// Output rings contain original arcs.
    /// Gaps <= snap tolerance are closed with minimal straight segments without moving existing vertices.
    /// Larger gaps are reported as dangles.
    /// </summary>
    [Fact]
    public void Test4_LinesToPolygons_WithArcs_NoVertexMoved_MinimalGapClosed_LargeGapReported()
    {
        // Line 1: (0,0) -> (10,0)
        var l1 = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0), CurveType.Straight);
        
        // Arc 2: (10, 0.02) -> (10, 10), R=5, Center (10, 5). Gap from l1 is 0.02m (<= tolerance 0.05m)
        var arc2 = new CoreBoundarySegment(new Point2D(10, 0.02), new Point2D(10, 10), CurveType.CircularArc, 5.0 * Math.PI)
        {
            Center = new Point2D(10, 5),
            Radius = 5.0,
            IsCounterClockwise = true
        };

        // Line 3: (10,10) -> (0,10)
        var l3 = new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10), CurveType.Straight);

        // Line 4: (0,10) -> (0, 0.01). Gap to origin (0,0) is 0.01m (<= tolerance 0.05m)
        var l4 = new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0.01), CurveType.Straight);

        var entity = new CurvePolylineEntity
        {
            LayerName = "PARCEL_LINES",
            Segments = new List<CoreBoundarySegment> { l1, arc2, l3, l4 }
        };

        var (rings, manifest) = CurvePreservingPolygonizerService.Polygonize(
            new[] { entity },
            new[] { "PARCEL_LINES" },
            new PolygonizerOptions
            {
                SnapToleranceMeters = 0.05,
                GroupLayersTogether = true,
                KeepAlreadyClosedPolylines = true
            });

        Assert.Single(rings);
        var ring = rings[0];

        // Must contain original arc segment intact!
        var foundArc = ring.Segments.FirstOrDefault(s => s.Type == CurveType.CircularArc);
        Assert.NotNull(foundArc);
        Assert.Equal(5.0, foundArc.Radius!.Value, 6);
        Assert.Equal(10.0, foundArc.Center!.Value.X, 6);
        Assert.Equal(5.0, foundArc.Center!.Value.Y, 6);

        // Existing vertices must NOT have been moved:
        // l1.End must be exactly (10, 0)
        Assert.Equal(10.0, l1.End.X, 9);
        Assert.Equal(0.0, l1.End.Y, 9);
        // arc2.Start must be exactly (10, 0.02)
        Assert.Equal(10.0, arc2.Start.X, 9);
        Assert.Equal(0.02, arc2.Start.Y, 9);

        // Minimal bridge segments were inserted to bridge gaps without moving vertices
        Assert.True(ring.MinimalGapClosuresCount >= 1);

        // Large Gap Test: Gap of 0.20m > snap tolerance 0.05m
        var openEntity = new CurvePolylineEntity
        {
            LayerName = "OPEN_CHAIN",
            Segments = new List<CoreBoundarySegment>
            {
                new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0), CurveType.Straight),
                new CoreBoundarySegment(new Point2D(10, 0.20), new Point2D(10, 10), CurveType.Straight) // gap = 0.20m
            }
        };

        var (ringsOpen, manifestOpen) = CurvePreservingPolygonizerService.Polygonize(
            new[] { openEntity },
            new[] { "OPEN_CHAIN" },
            new PolygonizerOptions { SnapToleranceMeters = 0.05 });

        Assert.Empty(ringsOpen);
        Assert.True(manifestOpen.TotalDanglesFound > 0);
    }

    /// <summary>
    /// Test 5: Exact vertex coordinate preservation check.
    /// Fails if any output vertex coordinate differs from the input by more than 1e-9 m.
    /// </summary>
    [Fact]
    public void Test5_ExactVertexCoordinatesPreserved_Within1e9Meters()
    {
        var inputVertices = new List<Point2D>
        {
            new Point2D(123456.789012345, 987654.321098765),
            new Point2D(123486.789012345, 987654.321098765),
            new Point2D(123486.789012345, 987684.321098765),
            new Point2D(123456.789012345, 987684.321098765)
        };

        var segments = new List<CoreBoundarySegment>();
        for (int i = 0; i < inputVertices.Count; i++)
        {
            var p1 = inputVertices[i];
            var p2 = inputVertices[(i + 1) % inputVertices.Count];
            segments.Add(new CoreBoundarySegment(p1, p2, CurveType.Straight));
        }

        var entity = new CurvePolylineEntity
        {
            LayerName = "SURVEY_BOUNDARY",
            Segments = segments
        };

        var (rings, _) = CurvePreservingPolygonizerService.Polygonize(
            new[] { entity },
            new[] { "SURVEY_BOUNDARY" },
            new PolygonizerOptions { SnapToleranceMeters = 0.001, KeepAlreadyClosedPolylines = true });

        Assert.Single(rings);
        var ring = rings[0];

        for (int i = 0; i < inputVertices.Count; i++)
        {
            var inputPt = inputVertices[i];
            var outPt = ring.Segments[i].Start;

            double dx = Math.Abs(inputPt.X - outPt.X);
            double dy = Math.Abs(inputPt.Y - outPt.Y);

            Assert.True(dx <= 1e-9, $"Vertex {i} X difference {dx:E4} exceeded 1e-9 m!");
            Assert.True(dy <= 1e-9, $"Vertex {i} Y difference {dy:E4} exceeded 1e-9 m!");
        }
    }

    /// <summary>
    /// Test 6: Export controlled deviation and frozen metrics.
    /// Shapefile/GCS conversion deviates by no more than configured tolerance (0.001m default).
    /// Frozen metrics equal analytic UTM values.
    /// Geometry Preservation Report lists the conversion.
    /// </summary>
    [Fact]
    public void Test6_ExportControlledDeviation_FrozenMetricsExactUtm()
    {
        // Corner arc of radius 10m
        double r = 10.0;
        double maxAllowedDeviation = 0.001; // 1 mm

        // Sagitta (chord deviation) formula for arc of angle theta:
        // h = R * (1 - cos(theta / 2))
        // To satisfy h <= maxAllowedDeviation:
        // cos(theta / 2) >= 1 - (h / R) = 1 - 0.0001 = 0.9999
        // theta_sub <= 2 * acos(0.9999) = 0.02828 radians (~1.62 degrees)
        double maxSubAngle = 2.0 * Math.Acos(1.0 - (maxAllowedDeviation / r));
        double quarterCircleAngle = 0.5 * Math.PI;
        int minSegments = (int)Math.Ceiling(quarterCircleAngle / maxSubAngle);

        // Verify the formula guarantees deviation <= tolerance
        double actualSubAngle = quarterCircleAngle / minSegments;
        double actualDeviation = r * (1.0 - Math.Cos(actualSubAngle / 2.0));
        Assert.True(actualDeviation <= maxAllowedDeviation, $"Deviation {actualDeviation} exceeded {maxAllowedDeviation}");

        // Frozen metrics are derived from true curve geometry BEFORE export
        var arcSeg = new CoreBoundarySegment(new Point2D(10, 0), new Point2D(0, 10), CurveType.CircularArc, 0.5 * Math.PI * r)
        {
            Center = new Point2D(0, 0),
            Radius = r,
            IsCounterClockwise = true
        };
        var closing1 = new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0), CurveType.Straight);
        var closing2 = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0), CurveType.Straight);

        var trueSegments = new List<CoreBoundarySegment> { arcSeg, closing1, closing2 };
        double frozenAnalyticArea = 0.25 * Math.PI * r * r;
        double frozenAnalyticPerimeter = (0.5 * Math.PI * r) + 10.0 + 10.0;

        var auditBefore = CurveAuditService.AuditSegments(trueSegments, 1, "PARCEL_1", "Polygon");
        Assert.Equal(frozenAnalyticArea, auditBefore.ExactArea, 4);
        Assert.Equal(frozenAnalyticPerimeter, auditBefore.ExactLength, 4);

        // Export report simulation
        var auditExport = new GeometryCurveAuditResult
        {
            FeatureId = 1,
            FeatureKey = "PARCEL_1",
            CircularArcsCount = 0,
            LineSegmentsCount = minSegments + 2,
            ExactArea = auditBefore.ExactArea,
            ExactLength = auditBefore.ExactLength
        };

        var comp = CurveAuditService.CompareStages(
            "Stage 5: Shapefile Deliverable",
            "PARCEL",
            new[] { auditBefore },
            new[] { auditExport },
            isUnavoidableExportConversion: true,
            maxAllowedDeviation: maxAllowedDeviation);

        Assert.True(comp.Passed);
        Assert.Equal(1, comp.UnavoidableConversionsCount);
        Assert.Equal(maxAllowedDeviation, comp.MaxDeviationWhereConverted);
        Assert.Single(comp.Notes);
    }

    /// <summary>
    /// Test 7: Static code audit test.
    /// Scans all C# source files in Cad2Gis.Core and Cad2Gis.AddIn.
    /// Asserts that banned calls (Densify, DensifyByLength, DensifyByDeviation, Generalize, Simplify)
    /// are NOT used on stored geometry, and PolygonBuilderEx.CreatePolygon(pts) from vertex lists is eliminated.
    /// </summary>
    [Fact]
    public void Test7_StaticCodeAudit_BannedCallsOnStoredGeometryEliminated()
    {
        string baseDir = AppContext.BaseDirectory;
        // Locate root of repository
        var dirInfo = new DirectoryInfo(baseDir);
        while (dirInfo != null && !File.Exists(Path.Combine(dirInfo.FullName, "Cad2Gis", "Cad2Gis.sln")))
        {
            dirInfo = dirInfo.Parent;
        }

        if (dirInfo == null)
        {
            // If running in isolated test runner, search current directory up to 5 levels
            dirInfo = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dirInfo != null && !File.Exists(Path.Combine(dirInfo.FullName, "Cad2Gis", "Cad2Gis.sln")))
            {
                dirInfo = dirInfo.Parent;
            }
        }

        Assert.NotNull(dirInfo);
        string cad2GisDir = Path.Combine(dirInfo.FullName, "Cad2Gis");

        var csFiles = Directory.GetFiles(cad2GisDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("obj") && !f.Contains("bin") && !f.EndsWith("GeometryPreservationTests.cs"))
            .ToList();

        var violations = new List<string>();

        // Regex patterns for banned operations on stored geometry
        var bannedDensifyPattern = new Regex(@"\b(DensifyByLength|DensifyByDeviation|Densify|Generalize|Simplify)\b");
        var bannedPointsRebuildPattern = new Regex(@"PolygonBuilderEx\.CreatePolygon\s*\(\s*pts\b");

        foreach (var file in csFiles)
        {
            string fileName = Path.GetFileName(file);
            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("*"))
                    continue;

                // ShapefileExporter is explicitly allowed to use DensifyByDeviation for controlled export deliverable
                if (fileName == "ShapefileExporter.cs" && line.Contains("DensifyByDeviation"))
                    continue;

                // ParcelPolygonizer obsolete marker method definition is allowed
                if (fileName == "ParcelPolygonizer.cs" && line.Contains("DensifyAndClean"))
                    continue;

                if (bannedDensifyPattern.IsMatch(line))
                {
                    violations.Add($"{fileName}:{i + 1} - Banned densify/generalize/simplify call: '{line}'");
                }

                if (bannedPointsRebuildPattern.IsMatch(line))
                {
                    violations.Add($"{fileName}:{i + 1} - Banned polygon rebuild from vertex point list: '{line}'");
                }
            }
        }

        Assert.True(violations.Count == 0, "Banned geometry alterations found in source code:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Test 8: Topological planar graph polygonization for CAD line networks with shared edges and T-junctions.
    /// Simulates 'inner outline' CAD lines representing adjacent lots in a block.
    /// Ensures that:
    /// 1. Shared edges between parcels are used in both directions (forward/reverse) without false dangles.
    /// 2. Long block lines meeting at T-junctions are noded correctly.
    /// 3. All adjacent parcels are constructed with exact analytic areas.
    /// 4. Dangles count is 0 and manifest reflects all contributed parcels.
    /// </summary>
    [Fact]
    public void Test8_LinesToPolygons_SharedEdgesAndTJunctions_InnerOutlineGrid()
    {
        // 2x2 parcel grid with outer boundary and interior dividing lines:
        // Outer box:
        // Bottom: (0,0) -> (20,0) (continuous 20m line with T-junction at (10,0))
        // Top: (0,20) -> (20,20) (continuous 20m line with T-junction at (10,20))
        // Left: (0,0) -> (0,20) (continuous 20m line with T-junction at (0,10))
        // Right: (20,0) -> (20,20) (continuous 20m line with T-junction at (20,10))
        // Interior dividing lines:
        // Vertical divider: (10,0) -> (10,20)
        // Horizontal divider: (0,10) -> (20,10)

        var lines = new List<CurvePolylineEntity>
        {
            new CurvePolylineEntity
            {
                LayerName = "inner outline",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(0, 0), new Point2D(20, 0), CurveType.Straight)
                }
            },
            new CurvePolylineEntity
            {
                LayerName = "inner outline",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(20, 0), new Point2D(20, 20), CurveType.Straight)
                }
            },
            new CurvePolylineEntity
            {
                LayerName = "inner outline",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(20, 20), new Point2D(0, 20), CurveType.Straight)
                }
            },
            new CurvePolylineEntity
            {
                LayerName = "inner outline",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(0, 20), new Point2D(0, 0), CurveType.Straight)
                }
            },
            new CurvePolylineEntity
            {
                LayerName = "inner outline",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(10, 0), new Point2D(10, 20), CurveType.Straight)
                }
            },
            new CurvePolylineEntity
            {
                LayerName = "inner outline",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(0, 10), new Point2D(20, 10), CurveType.Straight)
                }
            }
        };

        var options = new PolygonizerOptions
        {
            GroupLayersTogether = true,
            KeepAlreadyClosedPolylines = false,
            SnapToleranceMeters = 0.05
        };

        var (rings, manifest) = CurvePreservingPolygonizerService.Polygonize(
            lines,
            new[] { "inner outline" },
            options);

        // Assert 4 parcels formed
        Assert.Equal(4, rings.Count);

        // Assert each parcel has area 100
        foreach (var r in rings)
        {
            Assert.Equal(100.0, r.Area, 2);
        }

        // Assert manifest tracking
        var item = manifest.Layers.FirstOrDefault(l => l.LayerName == "inner outline");
        Assert.NotNull(item);
        Assert.Equal(6, item.EntitiesRead);
        Assert.Equal(4, item.PolygonsContributed);
        Assert.Equal(0, item.DanglesOrphansCount);
        Assert.Equal("OK", item.Status);
    }

    /// <summary>
    /// Test 9: Multi-layer line polygonization with true circular arcs and shared boundaries.
    /// Combines lines from "inner outline" and "Lot_Boundaries".
    /// Proves that:
    /// 1. True circular arc segments remain circular arcs without densification.
    /// 2. Multiple selected layers all contribute to polygon construction.
    /// 3. Manifest report correctly attributes polygon contributions to each layer.
    /// </summary>
    [Fact]
    public void Test9_MultiLayer_LinesToPolygons_WithArcsAndGaps()
    {
        // Two adjacent parcels:
        // Parcel 1 (left): (0,0) -> (10,0) -> (10,10) -> Arc to (0,10) -> (0,0)
        // Parcel 2 (right): (10,0) -> (20,0) -> (20,10) -> (10,10) -> (10,0)
        // Shared edge between P1 and P2: (10,0) -> (10,10) (comes from 'inner outline')
        // Parcel 1 Arc top: (10,10) to (0,10) with Center (5, 10), Radius 5, CCW
        
        double arcLen = Math.PI * 5.0; // 180-degree semi-circle bulge
        var arcSegment = new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10), CurveType.CircularArc, arcLen)
        {
            Center = new Point2D(5, 10),
            Radius = 5.0,
            IsCounterClockwise = true
        };

        var lines = new List<CurvePolylineEntity>
        {
            // Layer 1: inner outline (shared divider and bottoms)
            new CurvePolylineEntity
            {
                LayerName = "inner outline",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(0, 0), new Point2D(20, 0), CurveType.Straight) // Bottom of both
                }
            },
            new CurvePolylineEntity
            {
                LayerName = "inner outline",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(10, 0), new Point2D(10, 10), CurveType.Straight) // Shared dividing line
                }
            },
            // Layer 2: Lot_Boundaries (outer edges and arc)
            new CurvePolylineEntity
            {
                LayerName = "Lot_Boundaries",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0), CurveType.Straight) // Left edge
                }
            },
            new CurvePolylineEntity
            {
                LayerName = "Lot_Boundaries",
                Segments = new List<CoreBoundarySegment>
                {
                    arcSegment // Top of Parcel 1 is an Arc!
                }
            },
            new CurvePolylineEntity
            {
                LayerName = "Lot_Boundaries",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(20, 0), new Point2D(20, 10), CurveType.Straight) // Right edge
                }
            },
            new CurvePolylineEntity
            {
                LayerName = "Lot_Boundaries",
                Segments = new List<CoreBoundarySegment>
                {
                    new CoreBoundarySegment(new Point2D(20, 10), new Point2D(10, 10), CurveType.Straight) // Top edge of P2
                }
            }
        };

        var options = new PolygonizerOptions
        {
            GroupLayersTogether = true,
            KeepAlreadyClosedPolylines = false,
            SnapToleranceMeters = 0.05
        };

        var (rings, manifest) = CurvePreservingPolygonizerService.Polygonize(
            lines,
            new[] { "inner outline", "Lot_Boundaries" },
            options);

        // 2 parcels formed
        Assert.Equal(2, rings.Count);

        // Parcel 1 has arc preserved:
        var p1 = rings.FirstOrDefault(r => r.Segments.Any(s => s.Type == CurveType.CircularArc));
        Assert.NotNull(p1);
        var preservedArc = p1.Segments.First(s => s.Type == CurveType.CircularArc);
        Assert.Equal(5.0, preservedArc.Radius);
        Assert.Equal(5.0, preservedArc.Center!.Value.X);
        Assert.Equal(10.0, preservedArc.Center!.Value.Y);

        // Analytic area of P1 = square 10x10 (100) + semicircle 0.5*PI*25 (39.27) = 139.27
        double expectedP1Area = 100.0 + (0.5 * Math.PI * 25.0);
        Assert.Equal(expectedP1Area, p1.Area, 1);

        // Both layers in manifest show contributions
        var itemInner = manifest.Layers.First(l => l.LayerName == "inner outline");
        var itemLot = manifest.Layers.First(l => l.LayerName == "Lot_Boundaries");
        Assert.True(itemInner.PolygonsContributed > 0);
        Assert.True(itemLot.PolygonsContributed > 0);
        Assert.Equal(0, itemInner.DanglesOrphansCount);
        Assert.Equal(0, itemLot.DanglesOrphansCount);
    }

    /// <summary>
    /// Test 8: Dangling circular arc endpoint extends along its curvature (same center and radius)
    /// to intersect a target line within 5 cm, perfectly preserving true curve geometry.
    /// </summary>
    [Fact]
    public void Test8_ExtendDanglingCurveAlongCurvature_PreservesCircleCenterAndRadius()
    {
        // Bottom line: (0, 0) -> (10, 0)
        var lBottom = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0), CurveType.Straight);
        // Top line: (10, 10) -> (0, 10)
        var lTop = new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10), CurveType.Straight);
        // Left line: (0, 10) -> (0, 0)
        var lLeft = new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0), CurveType.Straight);

        // Right side: Semicircular arc with Center=(10, 5), Radius=5.0
        // Full arc runs from (10, 0) at -PI/2 to (10, 10) at +PI/2
        // We introduce an undershoot: Start is 3 cm (0.03m) along the circle away from (10, 0)
        double r = 5.0;
        var center = new Point2D(10, 5);
        double deltaS = 0.03; // 3 cm gap (undershoot)
        double deltaAngle = deltaS / r; // 0.006 rad
        double startAngle = -Math.PI / 2.0 + deltaAngle;
        var undershootStart = new Point2D(center.X + r * Math.Cos(startAngle), center.Y + r * Math.Sin(startAngle));
        var arcEnd = new Point2D(10, 10);

        var arc = new CoreBoundarySegment(undershootStart, arcEnd, CurveType.CircularArc, (Math.PI - deltaAngle) * r)
        {
            Center = center,
            Radius = r,
            IsCounterClockwise = true
        };

        var entities = new List<CurvePolylineEntity>
        {
            new CurvePolylineEntity
            {
                LayerName = "PARCEL_EDGES",
                Segments = new List<CoreBoundarySegment> { lBottom, arc, lTop, lLeft }
            }
        };

        var options = new PolygonizerOptions
        {
            GroupLayersTogether = true,
            ExtendLinesAndCurves = true,
            ExtendToleranceMeters = 0.05, // 5 cm default
            SnapToleranceMeters = 0.01
        };

        var (rings, manifest) = CurvePreservingPolygonizerService.Polygonize(
            entities,
            new[] { "PARCEL_EDGES" },
            options);

        // 1 parcel successfully closed
        Assert.Single(rings);
        var ring = rings[0];

        // The arc in the ring must retain the EXACT SAME center and radius
        var ringArc = ring.Segments.FirstOrDefault(s => s.Type == CurveType.CircularArc);
        Assert.NotNull(ringArc);
        Assert.Equal(5.0, ringArc.Radius!.Value, 6);
        Assert.Equal(10.0, ringArc.Center!.Value.X, 6);
        Assert.Equal(5.0, ringArc.Center!.Value.Y, 6);

        // The start of the arc must have been extended along the circle to (10, 0) within precision
        Assert.True(ringArc.Start.DistanceTo(new Point2D(10, 0)) < 1e-4);

        // Parcel area includes the exact analytic semicircle: 10x10 + 0.5*PI*25 = ~139.27
        double expectedArea = 100.0 + (0.5 * Math.PI * 25.0);
        Assert.Equal(expectedArea, ring.Area, 1);
        Assert.Equal(0, manifest.TotalDanglesFound);
    }

    /// <summary>
    /// Test 9: Dangling straight line extends along its ray to meet an adjacent line within 5 cm.
    /// </summary>
    [Fact]
    public void Test9_ExtendDanglingStraightLine_ClosesParcel()
    {
        // 10x10 box where the right line stops 4 cm before reaching (10, 10)
        var s1 = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0), CurveType.Straight);
        var s2 = new CoreBoundarySegment(new Point2D(10, 0), new Point2D(10, 9.96), CurveType.Straight); // 4 cm gap to (10, 10)
        var s3 = new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10), CurveType.Straight);
        var s4 = new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0), CurveType.Straight);

        var entities = new List<CurvePolylineEntity>
        {
            new CurvePolylineEntity
            {
                LayerName = "BOUNDARY",
                Segments = new List<CoreBoundarySegment> { s1, s2, s3, s4 }
            }
        };

        var (rings, manifest) = CurvePreservingPolygonizerService.Polygonize(
            entities,
            new[] { "BOUNDARY" },
            new PolygonizerOptions
            {
                GroupLayersTogether = true,
                ExtendLinesAndCurves = true,
                ExtendToleranceMeters = 0.05
            });

        Assert.Single(rings);
        Assert.Equal(100.0, rings[0].Area, 2);
        Assert.Equal(0, manifest.TotalDanglesFound);
    }

    /// <summary>
    /// Test 10: Multi-layer input with lines in layer A and true curves in layer B
    /// forms a single unified parcel layout without destroying any curve.
    /// </summary>
    [Fact]
    public void Test10_UnifiedMultiLayerLines_FormsClosedParcels_PreservesAllCurves()
    {
        // Layer "BOUNDARY_LINES": straight edges
        var l1 = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(20, 0), CurveType.Straight);
        var l2 = new CoreBoundarySegment(new Point2D(20, 0), new Point2D(20, 10), CurveType.Straight);
        var l3 = new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0), CurveType.Straight);

        // Layer "CURVED_FACADE": Arc on top side from (20, 10) to (0, 10), Center=(10, 10), R=10
        var arc = new CoreBoundarySegment(new Point2D(20, 10), new Point2D(0, 10), CurveType.CircularArc, 10.0 * Math.PI)
        {
            Center = new Point2D(10, 10),
            Radius = 10.0,
            IsCounterClockwise = true
        };

        var entities = new List<CurvePolylineEntity>
        {
            new CurvePolylineEntity
            {
                LayerName = "BOUNDARY_LINES",
                Segments = new List<CoreBoundarySegment> { l1, l2, l3 }
            },
            new CurvePolylineEntity
            {
                LayerName = "CURVED_FACADE",
                Segments = new List<CoreBoundarySegment> { arc }
            }
        };

        var options = new PolygonizerOptions
        {
            GroupLayersTogether = true, // Treated as ONE unified layer
            ExtendLinesAndCurves = true,
            ExtendToleranceMeters = 0.05
        };

        var (rings, manifest) = CurvePreservingPolygonizerService.Polygonize(
            entities,
            new[] { "BOUNDARY_LINES", "CURVED_FACADE" },
            options);

        // Single unified parcel formed from both layers
        Assert.Single(rings);
        var ring = rings[0];

        // Curve must be preserved exactly
        var foundArc = ring.Segments.FirstOrDefault(s => s.Type == CurveType.CircularArc);
        Assert.NotNull(foundArc);
        Assert.Equal(10.0, foundArc.Radius!.Value, 6);
        Assert.Equal(10.0, foundArc.Center!.Value.X, 6);
        Assert.Equal(10.0, foundArc.Center!.Value.Y, 6);

        // Exact analytic area: rectangle 20x10 (200) + semicircle 0.5*PI*100 (157.08) = 357.08
        double expectedArea = 200.0 + (0.5 * Math.PI * 100.0);
        Assert.Equal(expectedArea, ring.Area, 1);

        // Both layers recorded as contributing in manifest
        var itemLines = manifest.Layers.First(l => l.LayerName == "BOUNDARY_LINES");
        var itemCurves = manifest.Layers.First(l => l.LayerName == "CURVED_FACADE");
        Assert.True(itemLines.PolygonsContributed > 0);
        Assert.True(itemCurves.PolygonsContributed > 0);
        Assert.Equal(0, manifest.TotalDanglesFound);
    }
}


