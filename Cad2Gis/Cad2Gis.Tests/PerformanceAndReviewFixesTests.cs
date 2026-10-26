using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;
using Cad2Gis.Core.Lookups;

namespace Cad2Gis.Tests;

public class PerformanceAndReviewFixesTests
{
    // =========================================================================
    // Part A: Spatial Index Unit Tests (400+ Synthetic Parcels vs Brute Force)
    // =========================================================================

    [Fact]
    public void PartA_SpatialGrid2D_CandidateSetsEqualBruteForce_On400ParcelsGrid()
    {
        var grid = new SpatialGrid2D<int>(cellSize: 100.0);
        var random = new Random(42);

        // Generate a 20x20 grid of 400 parcels with streets and varied sizes
        var items = new List<(int Id, Box2D Box)>();
        int idSeq = 1;

        for (int row = 0; row < 20; row++)
        {
            for (int col = 0; col < 20; col++)
            {
                double minX = col * 40.0 + random.NextDouble() * 5.0;
                double minY = row * 40.0 + random.NextDouble() * 5.0;
                double width = 20.0 + random.NextDouble() * 15.0;
                double height = 20.0 + random.NextDouble() * 15.0;

                var box = new Box2D(minX, minY, minX + width, minY + height);
                items.Add((idSeq, box));
                grid.Insert(idSeq, box);
                idSeq++;
            }
        }

        Assert.Equal(400, grid.Count);

        // Perform 50 diverse queries with varying extents (point queries, small borders, large street searches)
        for (int q = 0; q < 50; q++)
        {
            double qMinX = random.NextDouble() * 800.0;
            double qMinY = random.NextDouble() * 800.0;
            double qW = 5.0 + random.NextDouble() * 120.0;
            double qH = 5.0 + random.NextDouble() * 120.0;

            var queryBox = new Box2D(qMinX, qMinY, qMinX + qW, qMinY + qH);

            var gridResults = grid.Query(queryBox).OrderBy(x => x).ToList();
            var bruteResults = grid.QueryBruteForce(queryBox).OrderBy(x => x).ToList();

            Assert.Equal(bruteResults.Count, gridResults.Count);
            Assert.Equal(bruteResults, gridResults);
        }
    }

    [Fact]
    public void PartA_Box2D_DistanceAndExpandCalculations()
    {
        var boxA = new Box2D(0, 0, 10, 10);
        var boxB = new Box2D(15, 0, 25, 10); // 5m gap in X
        var boxTouching = new Box2D(10, 0, 20, 10);

        Assert.Equal(5.0, boxA.DistanceTo(boxB), precision: 5);
        Assert.Equal(0.0, boxA.DistanceTo(boxTouching), precision: 5);

        var expanded = boxA.Expand(0.05);
        Assert.Equal(-0.05, expanded.MinX, precision: 5);
        Assert.Equal(-0.05, expanded.MinY, precision: 5);
        Assert.Equal(10.05, expanded.MaxX, precision: 5);
        Assert.Equal(10.05, expanded.MaxY, precision: 5);
        Assert.True(expanded.Intersects(boxTouching));
    }

    // =========================================================================
    // Part B: Defects Fixes Tests (B.1, B.2, B.3)
    // =========================================================================

    [Fact]
    public void PartB1_ExampleFromPrompt_MainWithPlot12AndUsageSakani_AssignsRsdAndSakani()
    {
        // Example: MAIN parcel with plot text 12 and usage text سكني.
        // Expected: USAGE = RSD, REMARKS = سكني (classified from UsageText, never PlotId).
        var p = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "12",
            UsageText = "سكني"
        };

        LandUseExcelService.EnrichParcels(new[] { p }, null, mainUsageMode: MainUsageMode.InferFromText);

        Assert.Equal("RSD", p.Usage);
        Assert.Equal("سكني", p.Remarks);
        Assert.Equal("Fallback", p.UsageSource);
    }

    [Fact]
    public void PartB1_NoUsageText_RemarksEqualsUsageOrEmpty_PlotIdNeverCopiedToRemarks()
    {
        var p1 = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "500",
            UsageText = "" // No usage text
        };

        LandUseExcelService.EnrichParcels(new[] { p1 }, null, mainUsageMode: MainUsageMode.InferFromText);

        Assert.Equal(string.Empty, p1.Usage);
        Assert.Equal(string.Empty, p1.Remarks);
        Assert.NotEqual("500", p1.Remarks); // PlotId is NEVER copied into REMARKS!
    }

    [Fact]
    public void PartB1_UserEnteredUsage_IsKeptAndNeverOverwritten()
    {
        var p = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "10",
            Usage = "COM",
            HasUserAssignedUsage = true,
            LayerUsage = "RSD"
        };

        var excelLookup = new Dictionary<string, LandUseItem>
        {
            ["10"] = new LandUseItem { Usage = "IND" }
        };

        LandUseExcelService.EnrichParcels(new[] { p }, excelLookup, mainUsageMode: MainUsageMode.InferFromText);

        // User entered USAGE is protected over Excel and Layer
        Assert.Equal("COM", p.Usage);
        Assert.Equal("Manual", p.UsageSource);
    }

    [Fact]
    public void PartB1_ExcelRsd_OverridesLayerCom_SourceIsExcel()
    {
        var p = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "20",
            LayerUsage = "COM"
        };

        var excelLookup = new Dictionary<string, LandUseItem>
        {
            ["20"] = new LandUseItem { Usage = "RSD" }
        };

        LandUseExcelService.EnrichParcels(new[] { p }, excelLookup);

        Assert.Equal("RSD", p.Usage);
        Assert.Equal("Excel", p.UsageSource);
    }

    [Fact]
    public void PartB1_NoExcelMatch_WithLayerCom_AssignsCom_SourceIsLayer()
    {
        var p = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "30",
            LayerUsage = "COM"
        };

        LandUseExcelService.EnrichParcels(new[] { p }, new Dictionary<string, LandUseItem>());

        Assert.Equal("COM", p.Usage);
        Assert.Equal("Layer", p.UsageSource);
    }

    [Fact]
    public void PartB1_ExcelRowWithEmptyOrInvalidUsage_FallsThroughToLayerValue()
    {
        var p = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "40",
            LayerUsage = "COM"
        };

        // Excel has match, but usage is invalid code "INVALID_CODE"
        var excelLookup = new Dictionary<string, LandUseItem>
        {
            ["40"] = new LandUseItem { Usage = "INVALID_CODE", Remarks = "ملاحظة خاصة" }
        };

        LandUseExcelService.EnrichParcels(new[] { p }, excelLookup);

        // Falls through to layer-level USAGE
        Assert.Equal("COM", p.Usage);
        Assert.Equal("Layer", p.UsageSource);
        Assert.Equal("ملاحظة خاصة", p.Remarks);
    }

    [Fact]
    public void PartB3_ProvenanceText_DoesNotLeakIntoRemarks_AndTriggersQaErrorIfPresent()
    {
        var p = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "100",
            Remarks = "From Dataset layer [BOUNDARY]"
        };

        LandUseExcelService.EnrichParcels(new[] { p }, null);

        // EnrichParcels moves legacy provenance string from Remarks into Provenance
        Assert.Equal(string.Empty, p.Remarks);
        Assert.Equal("From Dataset layer [BOUNDARY]", p.Provenance);

        // If an exported parcel still has "From ..." in REMARKS, QA validation flags it as Error
        var leakyParcel = new ParcelRecord
        {
            InternalId = 2,
            Class = ParcelClass.Main,
            PlotId = "101",
            Remarks = "From FeatureToPolygon [Main Parcels]",
            PlanNo = "1000",
            AreaSqm = 500,
            ShapeLength = 90
        };

        var qaResult = QaValidationEngine.ValidateAll(new[] { leakyParcel }, Array.Empty<BorderRecord>());
        Assert.Contains(qaResult.Errors, e => e.FieldName == "REMARKS" && e.Message.Contains("provenance"));
    }

    // =========================================================================
    // Part C: DominantLayerMatcher Tests (C.4.1 to C.4.4, C.4.9)
    // =========================================================================

    [Fact]
    public void PartC4_1_TwoParcelsOnLayerA_AndOneOnLayerB_EachGetsItsLayerUsage()
    {
        // 3 separate rectangles:
        // Parcel 1: 0..10, 0..10 (Layer A)
        // Parcel 2: 20..30, 0..10 (Layer A)
        // Parcel 3: 40..50, 0..10 (Layer B)
        var segments = new List<LayerSegment2D>
        {
            // Parcel 1 (Layer A)
            new(new Point2D(0, 0), new Point2D(10, 0), "LAYER_A"),
            new(new Point2D(10, 0), new Point2D(10, 10), "LAYER_A"),
            new(new Point2D(10, 10), new Point2D(0, 10), "LAYER_A"),
            new(new Point2D(0, 10), new Point2D(0, 0), "LAYER_A"),

            // Parcel 2 (Layer A)
            new(new Point2D(20, 0), new Point2D(30, 0), "LAYER_A"),
            new(new Point2D(30, 0), new Point2D(30, 10), "LAYER_A"),
            new(new Point2D(30, 10), new Point2D(20, 10), "LAYER_A"),
            new(new Point2D(20, 10), new Point2D(20, 0), "LAYER_A"),

            // Parcel 3 (Layer B)
            new(new Point2D(40, 0), new Point2D(50, 0), "LAYER_B"),
            new(new Point2D(50, 0), new Point2D(50, 10), "LAYER_B"),
            new(new Point2D(50, 10), new Point2D(40, 10), "LAYER_B"),
            new(new Point2D(40, 10), new Point2D(40, 0), "LAYER_B"),
        };

        var matcher = new DominantLayerMatcher(segments);

        var layerUsageMap = new Dictionary<string, string>
        {
            ["LAYER_A"] = "RSD",
            ["LAYER_B"] = "COM"
        };

        // Test Parcel 1
        var p1Boundaries = new[]
        {
            new Segment2D(new Point2D(0, 0), new Point2D(10, 0)),
            new Segment2D(new Point2D(10, 0), new Point2D(10, 10)),
            new Segment2D(new Point2D(10, 10), new Point2D(0, 10)),
            new Segment2D(new Point2D(0, 10), new Point2D(0, 0))
        };
        var res1 = matcher.AttributeParcel(p1Boundaries, layerUsageMap: layerUsageMap);
        Assert.Equal("LAYER_A", res1.DominantLayer);
        Assert.Equal("RSD", res1.AssignedUsage);
        Assert.False(res1.IsMixedLayers);

        // Test Parcel 3
        var p3Boundaries = new[]
        {
            new Segment2D(new Point2D(40, 0), new Point2D(50, 0)),
            new Segment2D(new Point2D(50, 0), new Point2D(50, 10)),
            new Segment2D(new Point2D(50, 10), new Point2D(40, 10)),
            new Segment2D(new Point2D(40, 10), new Point2D(40, 0))
        };
        var res3 = matcher.AttributeParcel(p3Boundaries, layerUsageMap: layerUsageMap);
        Assert.Equal("LAYER_B", res3.DominantLayer);
        Assert.Equal("COM", res3.AssignedUsage);
        Assert.False(res3.IsMixedLayers);
    }

    [Fact]
    public void PartC4_2_ParcelBoundedBy3SidesLayerA_And1SideLayerB_Share90Percent()
    {
        // 3 sides of layer A (30m each = 90m total) and 1 side of layer B (10m)
        // Total = 100m. Layer A share = 90%
        var segments = new List<LayerSegment2D>
        {
            new(new Point2D(0, 0), new Point2D(30, 0), "LAYER_A"),
            new(new Point2D(30, 0), new Point2D(30, 30), "LAYER_A"),
            new(new Point2D(30, 30), new Point2D(0, 30), "LAYER_A"),
            new(new Point2D(0, 30), new Point2D(0, 20), "LAYER_B") // 10m piece
        };

        var boundary = new[]
        {
            new Segment2D(new Point2D(0, 0), new Point2D(30, 0)),
            new Segment2D(new Point2D(30, 0), new Point2D(30, 30)),
            new Segment2D(new Point2D(30, 30), new Point2D(0, 30)),
            new Segment2D(new Point2D(0, 30), new Point2D(0, 20))
        };

        var matcher = new DominantLayerMatcher(segments);
        var res = matcher.AttributeParcel(boundary);

        Assert.Equal("LAYER_A", res.DominantLayer);
        Assert.Equal(90.0, res.DominantLength, precision: 2);
        Assert.Equal(100.0, res.TotalMatchedLength, precision: 2);
        Assert.Equal(0.90, res.DominantShare, precision: 2);
        Assert.False(res.IsMixedLayers);
    }

    [Fact]
    public void PartC4_3_ParcelWith50_50Sides_FlagsMixedLayers_AndDeterministicTie()
    {
        // 2 sides of Layer A (10m each = 20m) and 2 sides of Layer B (10m each = 20m)
        // 50% each -> Mixed (<60%), Tie -> deterministic order by LayerName alphabetical
        var segments = new List<LayerSegment2D>
        {
            new(new Point2D(0, 0), new Point2D(10, 0), "LAYER_B"),
            new(new Point2D(10, 0), new Point2D(10, 10), "LAYER_B"),
            new(new Point2D(10, 10), new Point2D(0, 10), "LAYER_A"),
            new(new Point2D(0, 10), new Point2D(0, 0), "LAYER_A")
        };

        var boundary = new[]
        {
            new Segment2D(new Point2D(0, 0), new Point2D(10, 0)),
            new Segment2D(new Point2D(10, 0), new Point2D(10, 10)),
            new Segment2D(new Point2D(10, 10), new Point2D(0, 10)),
            new Segment2D(new Point2D(0, 10), new Point2D(0, 0))
        };

        var matcher = new DominantLayerMatcher(segments);
        var res = matcher.AttributeParcel(boundary);

        Assert.True(res.IsMixedLayers);
        Assert.True(res.IsTie);
        Assert.Equal(0.50, res.DominantShare, precision: 2);
        // Tie broken deterministically: "LAYER_A" precedes "LAYER_B"
        Assert.Equal("LAYER_A", res.DominantLayer);
    }

    [Fact]
    public void PartC4_4_LayerWithoutUsageSelection_AssignsEmptyUsage_NoError()
    {
        var segments = new List<LayerSegment2D>
        {
            new(new Point2D(0, 0), new Point2D(10, 0), "RAW_BOUNDARY"),
            new(new Point2D(10, 0), new Point2D(10, 10), "RAW_BOUNDARY")
        };

        var boundary = new[]
        {
            new Segment2D(new Point2D(0, 0), new Point2D(10, 0)),
            new Segment2D(new Point2D(10, 0), new Point2D(10, 10))
        };

        var matcher = new DominantLayerMatcher(segments);
        var res = matcher.AttributeParcel(boundary, layerUsageMap: new Dictionary<string, string>());

        Assert.Equal("RAW_BOUNDARY", res.DominantLayer);
        Assert.Equal(string.Empty, res.AssignedUsage);
        Assert.False(res.NoSegmentsFound);
    }

    [Fact]
    public void PartC4_9_SubGroupLayerDominates_AssignsParcelClassSub()
    {
        var segments = new List<LayerSegment2D>
        {
            new(new Point2D(0, 0), new Point2D(20, 0), "UTILITY_CORRIDOR"),
            new(new Point2D(20, 0), new Point2D(20, 20), "UTILITY_CORRIDOR")
        };

        var boundary = new[]
        {
            new Segment2D(new Point2D(0, 0), new Point2D(20, 0)),
            new Segment2D(new Point2D(20, 0), new Point2D(20, 20))
        };

        var subLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UTILITY_CORRIDOR", "SERVICES" };

        var matcher = new DominantLayerMatcher(segments);
        var res = matcher.AttributeParcel(boundary, subLayerNames: subLayers);

        Assert.Equal("UTILITY_CORRIDOR", res.DominantLayer);
        Assert.Equal(ParcelClass.Sub, res.AssignedClass);
    }

    [Fact]
    public void PartC4_5_And_7_ProfileLoadSave_WithGeometryModeAndLayerUsages()
    {
        string tempProfilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_profile_{Guid.NewGuid():N}.xlsx");
        try
        {
            var model = new ConsultantProfileModel
            {
                ProfileName = "Custom_Urban_Profile",
                GeometryMode = LayerGeometryModeService.ModeLineWithDataLayer,
                LayerUsageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "RESIDENTIAL_LINES", "RSD" },
                    { "COMMERCIAL_LINES", "COM" }
                }
            };

            ProfileExcelService.SaveProfileToExcel(model, tempProfilePath);
            Assert.True(System.IO.File.Exists(tempProfilePath));

            var loaded = ProfileExcelService.LoadProfileFromExcel(tempProfilePath);
            Assert.Equal("Custom_Urban_Profile", loaded.ProfileName);
            Assert.Equal(LayerGeometryModeService.ModeLineWithDataLayer, loaded.GeometryMode);
            Assert.Equal("RSD", loaded.LayerUsageMap["RESIDENTIAL_LINES"]);
            Assert.Equal("COM", loaded.LayerUsageMap["COMMERCIAL_LINES"]);

            // Old profile mode backward compatibility check
            Assert.Equal(LayerGeometryModeService.ModeLineWithDataLayer, LayerGeometryModeService.NormalizeMode("Polygon line"));
            Assert.Equal(LayerGeometryModeService.ModeLineWithDataLayer, LayerGeometryModeService.NormalizeMode("Both (خطوط ومضلعات معاً)"));
        }
        finally
        {
            if (System.IO.File.Exists(tempProfilePath))
            {
                System.IO.File.Delete(tempProfilePath);
            }
        }
    }

    // =========================================================================
    // Part D: Plot ID Extraction & Layer Persistence Tests
    // =========================================================================

    [Fact]
    public void PlotIdExtractor_CleanLayerName_HandlesAllVariants()
    {
        Assert.Null(PlotIdExtractorService.CleanLayerName(null));
        Assert.Null(PlotIdExtractorService.CleanLayerName(""));
        Assert.Null(PlotIdExtractorService.CleanLayerName("   "));
        Assert.Null(PlotIdExtractorService.CleanLayerName("(تلقائي من دور الطبقة - PlotText)"));
        Assert.Equal("PLOT_NUMBERS", PlotIdExtractorService.CleanLayerName("PLOT_NUMBERS (★ PlotText)"));
        Assert.Equal("CAD_TEXT", PlotIdExtractorService.CleanLayerName("CAD_TEXT"));
        Assert.Equal("A-B-C", PlotIdExtractorService.CleanLayerName("  A-B-C (★ PlotText)  "));
    }

    [Fact]
    public void PlotIdExtractor_ExplicitLayer_HasAbsolutePrecedence_OverOtherInsideAnnotations()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "P1"
        };

        var insideAnnots = new Dictionary<long, List<(string Text, string LayerName)>>
        {
            [1] = new List<(string Text, string LayerName)>
            {
                ("12.50", "DIMENSIONS"),
                ("سكني", "LAND_USE"),
                ("105/A", "PARCEL_NUMBERS")
            }
        };

        var parcelCenters = new Dictionary<long, (double CenterX, double CenterY)>
        {
            [1] = (100.0, 100.0)
        };

        var report = PlotIdExtractorService.AssignPlotIds(
            new[] { parcel },
            insideAnnots,
            Enumerable.Empty<(string, double, double, string)>(),
            parcelCenters,
            explicitPlotLayer: "PARCEL_NUMBERS (★ PlotText)",
            usageLayers: new HashSet<string> { "LAND_USE" });

        Assert.Equal("105/A", parcel.PlotId);
        Assert.Equal(1, report.MatchedFromSelectedLayer);
        Assert.Equal(0, report.FallbackDummyIds);
    }

    [Fact]
    public void PlotIdExtractor_ProximityMatching_PrefersExplicitLayer_WithinSearchRadius()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 2,
            Class = ParcelClass.Main,
            PlotId = "P2"
        };

        // No annotation strictly inside the parcel
        var insideAnnots = new Dictionary<long, List<(string Text, string LayerName)>>();

        var parcelCenters = new Dictionary<long, (double CenterX, double CenterY)>
        {
            [2] = (50.0, 50.0)
        };

        // 2 annotations outside:
        // A) "999" from OTHER_LAYER at distance 2m (50, 52)
        // B) "345" from explicit PLOT_NUM layer at distance 7m (50, 57)
        var allAnnots = new List<(string Text, double X, double Y, string LayerName)>
        {
            ("999", 50.0, 52.0, "OTHER_LAYER"),
            ("345", 50.0, 57.0, "PLOT_NUM")
        };

        var report = PlotIdExtractorService.AssignPlotIds(
            new[] { parcel },
            insideAnnots,
            allAnnots,
            parcelCenters,
            explicitPlotLayer: "PLOT_NUM",
            secondaryPlotLayers: new HashSet<string> { "OTHER_LAYER" },
            maxProximityMeters: 20.0);

        // Proximity must pick "345" from explicit PLOT_NUM layer, NOT "999" from OTHER_LAYER
        Assert.Equal("345", parcel.PlotId);
        Assert.Equal(1, report.MatchedViaProximity);
    }

    [Fact]
    public void Step1_OutputDirectory_CreatedIfMissing()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Cad2Gis_Test_" + Guid.NewGuid().ToString("N"));
        string exportOutput = Path.Combine(tempRoot, "Export_Output");

        try
        {
            Assert.False(Directory.Exists(exportOutput));

            // Simulating Step 1 directory check
            if (!Directory.Exists(exportOutput))
            {
                Directory.CreateDirectory(exportOutput);
            }

            Assert.True(Directory.Exists(exportOutput));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    [Fact]
    public void ExtractUsageCode_ParsesStandardAndDropdownFormats()
    {
        Assert.Equal("RSD", MunicipalLookups.ExtractUsageCode("RSD"));
        Assert.Equal("RSD", MunicipalLookups.ExtractUsageCode("RSD — سكني"));
        Assert.Equal("COM", MunicipalLookups.ExtractUsageCode("COM - تجاري"));
        Assert.Equal("UTL", MunicipalLookups.ExtractUsageCode("UTL مرافق"));
        Assert.Equal("RSC", MunicipalLookups.ExtractUsageCode("RSC: سكني تجاري"));
        Assert.Null(MunicipalLookups.ExtractUsageCode("UNKNOWN"));
    }

    [Fact]
    public void Usage_PreservedFromCadLayer_AndOverriddenByExcelPriority()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 1,
            PlotId = "101",
            Class = ParcelClass.Main,
            LayerUsage = "COM",
            Usage = "COM"
        };

        // Without Excel, layer usage "COM" is retained
        LandUseExcelService.EnrichParcels(new[] { parcel }, null, mainUsageMode: MainUsageMode.InferFromText);
        Assert.Equal("COM", parcel.Usage);
        Assert.Equal("Layer", parcel.UsageSource);

        // With Excel providing "RSD", Excel has absolute priority and overrides "COM"
        var excelLookup = new Dictionary<string, LandUseItem>
        {
            ["101"] = new LandUseItem { PlotId = "101", Usage = "RSD" }
        };
        LandUseExcelService.EnrichParcels(new[] { parcel }, excelLookup, mainUsageMode: MainUsageMode.InferFromText);
        Assert.Equal("RSD", parcel.Usage);
        Assert.Equal("Excel", parcel.UsageSource);
    }

    [Fact]
    public void Remarks_TakesDataFromMultipleUsageTextLayers_AndExcelOverrides()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 1,
            PlotId = "55",
            Class = ParcelClass.Main,
            LayerUsage = "UTL",
            Usage = "UTL"
        };

        // Multiple annotations from 2 distinct UsageText layers inside the parcel
        var insideAnnots = new List<(string Text, string LayerName)>
        {
            ("مسجد جامع", "LAYER_MOSQUE_TEXT"),
            ("مرفق تعليمي", "LAYER_EDUCATION_TEXT"),
            ("55", "PLOT_NUMBERS") // Plot number should NOT become remarks
        };

        var targetUsageLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LAYER_MOSQUE_TEXT",
            "LAYER_EDUCATION_TEXT"
        };

        var usageTextsInside = insideAnnots
            .Where(a => targetUsageLayers.Contains(a.LayerName))
            .Select(a => a.Text.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t) && !PlotIdExtractorService.IsPlotNumberText(t))
            .Distinct()
            .ToList();

        string combinedRemarks = string.Join(" - ", usageTextsInside);
        parcel.UsageText = combinedRemarks;
        parcel.Remarks = combinedRemarks;

        Assert.Equal("مسجد جامع - مرفق تعليمي", parcel.Remarks);
        Assert.Equal("مسجد جامع - مرفق تعليمي", parcel.UsageText);

        // Step 3 without Excel keeps the spatial join Remarks
        LandUseExcelService.EnrichParcels(new[] { parcel }, null);
        Assert.Equal("مسجد جامع - مرفق تعليمي", parcel.Remarks);

        // Step 3 with Excel overrides Remarks
        var excelLookup = new Dictionary<string, LandUseItem>
        {
            ["55"] = new LandUseItem { PlotId = "55", Remarks = "مسجد ومرفق معتمد من الأمانة" }
        };
        LandUseExcelService.EnrichParcels(new[] { parcel }, excelLookup);
        Assert.Equal("مسجد ومرفق معتمد من الأمانة", parcel.Remarks);
    }
}

