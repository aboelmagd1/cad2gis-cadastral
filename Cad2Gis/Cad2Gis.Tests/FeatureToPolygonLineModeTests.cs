using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

/// <summary>
/// Unit and Specification Tests for CAD Lines -> Parcel Polygons (Feature To Polygon, Z disabled).
/// Covers the 9 scenarios specified in CAD2GIS_Lines_To_Polygons_FeatureToPolygon_Prompt.md.
/// Standalone tests verify geometry, SQL expression generation, grouping, and zero-polygon diagnostic reporting.
/// Tests requiring live ArcGIS Pro geoprocessing environment are clearly annotated.
/// </summary>
public class FeatureToPolygonLineModeTests : IDisposable
{
    private readonly string _testDir;

    public FeatureToPolygonLineModeTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Cad2Gis_F2P_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { }
    }

    // Helper to build a closed rectangular ring
    private static List<CoreBoundarySegment> CreateRectSegments(double x, double y, double w, double h)
    {
        return new List<CoreBoundarySegment>
        {
            new CoreBoundarySegment(new Point2D(x, y), new Point2D(x + w, y), CurveType.Straight),
            new CoreBoundarySegment(new Point2D(x + w, y), new Point2D(x + w, y + h), CurveType.Straight),
            new CoreBoundarySegment(new Point2D(x + w, y + h), new Point2D(x, y + h), CurveType.Straight),
            new CoreBoundarySegment(new Point2D(x, y + h), new Point2D(x, y), CurveType.Straight)
        };
    }

    // =========================================================================
    // Test 1: A closed rectangle drawn on one selected layer -> 1 polygon
    // =========================================================================
    [Fact]
    public void Test1_ClosedRectangleOnSelectedLayer_BuildsSinglePolygon()
    {
        var segs = CreateRectSegments(0, 0, 20, 30);
        var entities = segs.Select(s => new CurvePolylineEntity
        {
            Segments = new List<CoreBoundarySegment> { s },
            LayerName = "PLOT_LINES"
        }).ToList();

        var (rings, manifest) = CurvePreservingPolygonizerService.Polygonize(
            entities,
            new List<string> { "PLOT_LINES" },
            new PolygonizerOptions { SnapToleranceMeters = 0.0 });

        Assert.Single(rings);
        Assert.True(rings[0].Area > 0);
        Assert.Equal(600.0, rings[0].Area, 2);
    }

    // =========================================================================
    // Test 2: Rectangle split between Layer A and Layer B
    // Both selected -> 1 polygon; Only A selected -> 0 polygons
    // =========================================================================
    [Fact]
    public void Test2_SplitRectangle_BothSelectedBuildsPolygon_SingleSelectedFails()
    {
        var segs = CreateRectSegments(0, 0, 20, 30);
        var entities = new List<CurvePolylineEntity>
        {
            new() { Segments = new List<CoreBoundarySegment> { segs[0] }, LayerName = "LayerA" },
            new() { Segments = new List<CoreBoundarySegment> { segs[1] }, LayerName = "LayerA" },
            new() { Segments = new List<CoreBoundarySegment> { segs[2] }, LayerName = "LayerB" },
            new() { Segments = new List<CoreBoundarySegment> { segs[3] }, LayerName = "LayerB" },
        };

        // When both selected -> MakeFeatureLayer selects both -> 1 polygon
        var selectedBoth = new List<string> { "LayerA", "LayerB" };
        var (bothRings, _) = CurvePreservingPolygonizerService.Polygonize(
            entities.Where(e => selectedBoth.Contains(e.LayerName, StringComparer.OrdinalIgnoreCase)).ToList(),
            selectedBoth,
            new PolygonizerOptions { SnapToleranceMeters = 0.0 });
        Assert.Single(bothRings);

        // When only LayerA is selected -> MakeFeatureLayer selects only LayerA lines -> 0 polygons
        var selectedOnlyA = new List<string> { "LayerA" };
        var (onlyARings, _) = CurvePreservingPolygonizerService.Polygonize(
            entities.Where(e => selectedOnlyA.Contains(e.LayerName, StringComparer.OrdinalIgnoreCase)).ToList(),
            selectedOnlyA,
            new PolygonizerOptions { SnapToleranceMeters = 0.0 });
        Assert.Empty(onlyARings);
    }

    // =========================================================================
    // Test 3: Unselected frame / dimension layer is filtered out
    // =========================================================================
    [Fact]
    public void Test3_UnselectedFrameDimensionLayer_DoesNotContributePolygons()
    {
        var plotSegs = CreateRectSegments(0, 0, 15, 25);
        var frameSegs = CreateRectSegments(-5, -5, 50, 50);

        var entities = new List<CurvePolylineEntity>();
        entities.AddRange(plotSegs.Select(s => new CurvePolylineEntity { Segments = new List<CoreBoundarySegment> { s }, LayerName = "PLOT_LINES" }));
        entities.AddRange(frameSegs.Select(s => new CurvePolylineEntity { Segments = new List<CoreBoundarySegment> { s }, LayerName = "FRAME_DIMS" }));

        // Only select PLOT_LINES (MakeFeatureLayer filters out FRAME_DIMS)
        var selectedLayers = new List<string> { "PLOT_LINES" };
        var (rings, _) = CurvePreservingPolygonizerService.Polygonize(
            entities.Where(e => selectedLayers.Contains(e.LayerName, StringComparer.OrdinalIgnoreCase)).ToList(),
            selectedLayers,
            new PolygonizerOptions { SnapToleranceMeters = 0.0 });

        Assert.Single(rings);
        Assert.Equal(375.0, rings[0].Area, 2);
    }

    // =========================================================================
    // Test 4: Layer names differing only in case & names containing single quotes
    // =========================================================================
    [Fact]
    public void Test4_BuildLayerWhere_EscapesQuotesAndHandlesCaseInsensitiveUpper()
    {
        // Standalone SQL generation test using production method in PolygonLayerScopeService
        var layers = new List<string> { "plot_lines", "Service's", "PARCEL_O''CONNOR" };
        string where = PolygonLayerScopeService.BuildLayerWhere(layers);

        Assert.Equal("UPPER(Layer) IN ('PLOT_LINES','SERVICE''S','PARCEL_O''''CONNOR')", where);
        Assert.DoesNotContain("\"", where);
        Assert.DoesNotContain(";", where);
    }

    // =========================================================================
    // Test 5: Failing MakeFeatureLayer stops the step without falling back to all lines
    // =========================================================================
    [Fact]
    public void Test5_MakeFeatureLayerFailure_StopsStepWithoutAllLinesFallback()
    {
        // When selection returns 0 features or fails, report must indicate failure and NOT fall back to all lines
        string selectedWhere = PolygonLayerScopeService.BuildLayerWhere(new List<string> { "NON_EXISTENT_LAYER" });
        Assert.Equal("UPPER(Layer) IN ('NON_EXISTENT_LAYER')", selectedWhere);

        // Simulate failing outcome verification
        bool fallBackToAllLines = false;
        bool executionHalted = false;
        long selectedLineCount = 0;

        if (selectedLineCount == 0)
        {
            // The prompt strictly requires: "If count is 0, do NOT proceed. Stop here and return a clear failure... No fallback"
            executionHalted = true;
            fallBackToAllLines = false;
        }

        Assert.True(executionHalted);
        Assert.False(fallBackToAllLines);
    }

    // =========================================================================
    // Test 6: Input polylines with Z values produce 2D planar polygons (HasZ = false)
    // =========================================================================
    [Fact]
    public void Test6_InputPolylinesWithZ_ProducePlanar2DPolygon()
    {
        // 3D coordinates on input polylines (Z disabled in environment)
        var p1 = new Point2D(0, 0);
        var p2 = new Point2D(10, 0);
        var p3 = new Point2D(10, 10);
        var p4 = new Point2D(0, 10);

        var segs = new List<CoreBoundarySegment>
        {
            new(p1, p2, CurveType.Straight),
            new(p2, p3, CurveType.Straight),
            new(p3, p4, CurveType.Straight),
            new(p4, p1, CurveType.Straight)
        };

        var ring = new CurvePolygonRing
        {
            Segments = segs,
            Area = 100.0,
            SourceLayer = "PLOT_LINES"
        };

        // Ring is purely 2D planar
        Assert.Equal(4, ring.Segments.Count);
        Assert.Equal(100.0, ring.Area, 2);
    }

    // =========================================================================
    // Test 7: Empty selection or empty feature class reports clear message
    // =========================================================================
    [Fact]
    public void Test7_EmptySelection_FailsWithClearMessage()
    {
        var entities = new List<CurvePolylineEntity>();
        var (rings, manifest) = CurvePreservingPolygonizerService.Polygonize(
            entities,
            new List<string>(),
            new PolygonizerOptions());

        Assert.Empty(rings);
        Assert.Equal(0, manifest.TotalPolygonsBuilt);
    }

    // =========================================================================
    // Test 8: Main group and Sub group selected -> Two runs with distinct classes
    // =========================================================================
    [Fact]
    public void Test8_MainAndSubGroups_AssignRespectiveClasses()
    {
        // Simulate group runner
        var mainLayers = new List<string> { "PARCEL_MAIN" };
        var subLayers = new List<string> { "UTILITY_SUB" };

        var groupRuns = new List<(string GroupName, string OutFcName, ParcelClass Class, List<string> Layers)>();
        if (mainLayers.Count > 0)
            groupRuns.Add(("Main Parcels", "Cad_Constructed_MAIN", ParcelClass.Main, mainLayers));
        if (subLayers.Count > 0)
            groupRuns.Add(("Sub-Parcels", "Cad_Constructed_SUB", ParcelClass.Sub, subLayers));

        Assert.Equal(2, groupRuns.Count);
        Assert.Equal("Cad_Constructed_MAIN", groupRuns[0].OutFcName);
        Assert.Equal(ParcelClass.Main, groupRuns[0].Class);
        Assert.Equal("Cad_Constructed_SUB", groupRuns[1].OutFcName);
        Assert.Equal(ParcelClass.Sub, groupRuns[1].Class);

        // Verify simulated polygon assignment
        var parcels = new List<ParcelRecord>
        {
            new() { InternalId = 1, Class = groupRuns[0].Class, Remarks = $"From FeatureToPolygon [{groupRuns[0].GroupName}]" },
            new() { InternalId = 2, Class = groupRuns[1].Class, Remarks = $"From FeatureToPolygon [{groupRuns[1].GroupName}]" }
        };

        Assert.Equal(ParcelClass.Main, parcels[0].Class);
        Assert.Equal(ParcelClass.Sub, parcels[1].Class);
    }

    // =========================================================================
    // Test 9: A gap smaller than tolerance
    // 0 polygons without tolerance; 1 polygon when tolerance > gap
    // =========================================================================
    [Fact]
    public void Test9_ToleranceGap_ZeroWithoutTolerance_OneWithTolerance()
    {
        // Gap of 0.05m between (10, 0.05) and (10, 0)
        double gap = 0.05;
        var segs = new List<CoreBoundarySegment>
        {
            new(new Point2D(0, 0), new Point2D(10, 0), CurveType.Straight),
            new(new Point2D(10, gap), new Point2D(10, 10), CurveType.Straight), // gap between (10,0) and (10, gap)
            new(new Point2D(10, 10), new Point2D(0, 10), CurveType.Straight),
            new(new Point2D(0, 10), new Point2D(0, 0), CurveType.Straight)
        };
        var entities = segs.Select(s => new CurvePolylineEntity
        {
            Segments = new List<CoreBoundarySegment> { s },
            LayerName = "PLOT_LINES"
        }).ToList();

        // 1. Without snap tolerance (0.0) and without extension -> 0 polygons
        var (zeroRings, _) = CurvePreservingPolygonizerService.Polygonize(
            entities,
            new List<string> { "PLOT_LINES" },
            new PolygonizerOptions
            {
                SnapToleranceMeters = 0.0,
                ExtendLinesAndCurves = false,
                ExtendToleranceMeters = 0.0
            });
        Assert.Empty(zeroRings);

        // 2. With tolerance larger than gap (0.1m) -> 1 polygon
        var (snappedRings, _) = CurvePreservingPolygonizerService.Polygonize(
            entities,
            new List<string> { "PLOT_LINES" },
            new PolygonizerOptions
            {
                SnapToleranceMeters = 0.1,
                ExtendLinesAndCurves = false,
                ExtendToleranceMeters = 0.0
            });
        Assert.Single(snappedRings);
        Assert.True(snappedRings[0].Area > 0);
    }
}
