using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class SyntheticDatasetAndAddendumTests
{
    private readonly string _testScratchDir;

    public SyntheticDatasetAndAddendumTests()
    {
        _testScratchDir = Path.Combine(Path.GetTempPath(), "Cad2Gis_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testScratchDir);
    }

    [Fact]
    public void SyntheticDataset_AssertKnownAnswers()
    {
        // 1. Generate Synthetic Dataset
        var demo = SyntheticDataGenerator.CreateSyntheticProject(_testScratchDir);

        Assert.Equal(2, demo.MainParcels.Count);
        Assert.Equal(2, demo.SubParcels.Count);
        Assert.Equal(4, demo.AllParcels.Count);

        // Check Known Main Parcel Metric: Area = 600.0 m² each (20 x 30m)
        foreach (var main in demo.MainParcels)
        {
            Assert.Equal(600.0, main.AreaSqm);
            Assert.Equal(100.0, main.ShapeLength);
            Assert.Equal("MAIN", main.TemporaryParcelClass);
            Assert.False(string.IsNullOrEmpty(main.PlotId));
        }

        // Sub-parcels: Garden and Corridor have SUB class tag and empty PLOT_ID
        foreach (var sub in demo.SubParcels)
        {
            Assert.Equal("SUB", sub.TemporaryParcelClass);
            Assert.True(string.IsNullOrEmpty(sub.PlotId));
        }
    }

    [Fact]
    public void WidthInference_Under10Meters_InfersPassage_Over10Meters_InfersStreet()
    {
        var demo = SyntheticDataGenerator.CreateSyntheticProject(_testScratchDir);
        var options = new WidthInferenceOptions
        {
            PassageStreetThreshold = 10.0,
            RayCount = 3,
            MaxSearchDistance = 100.0
        };

        // Test South facade of parcel 101 facing 12m street
        // Parcel 101 South edge: Y = 2400000, X: [500000..500020]. Outward normal is (0, -1)
        var southBorder = new BorderRecord { PlotId = "101", BorderName = 3 };
        var southSeg = new Segment2D(new Point2D(500000, 2400000), new Point2D(500020, 2400000));
        var southNormal = new Point2D(0, -1);

        WidthInferenceService.InferBorderWidth(southBorder, southSeg, southNormal, demo.FacingObstacleSegments, options);

        Assert.True(southBorder.IsInferred);
        Assert.Equal(12.0, southBorder.MeasuredWidth);
        Assert.Equal("شارع بعرض 12 م", southBorder.Descriptio);
        Assert.Equal("STR", southBorder.BorderType);
        Assert.Equal(12.0, southBorder.SuggestedWidth);
        Assert.Equal(3, southBorder.RayLengths.Count);

        // Test West facade facing 6m corridor/passage
        // West edge: X = 499994, Y: [2400000..2400030]. Outward normal is (-1, 0)
        var westBorder = new BorderRecord { PlotId = "101", BorderName = 4 };
        var westSeg = new Segment2D(new Point2D(499994, 2400000), new Point2D(499994, 2400030));
        var westNormal = new Point2D(-1, 0);

        WidthInferenceService.InferBorderWidth(westBorder, westSeg, westNormal, demo.FacingObstacleSegments, options);

        Assert.True(westBorder.IsInferred);
        Assert.Equal(6.0, westBorder.MeasuredWidth);
        Assert.Equal("ممر بعرض 6 م", westBorder.Descriptio);
        Assert.Equal("LNE", westBorder.BorderType);
        Assert.Equal(6.0, westBorder.SuggestedWidth);
    }

    [Fact]
    public void WidthInference_NoHitWithin100Meters_LeavesUnknown()
    {
        var emptyObstacles = new List<Segment2D>();
        var options = new WidthInferenceOptions { MaxSearchDistance = 100.0 };

        var border = new BorderRecord { PlotId = "101", BorderName = 1 };
        var seg = new Segment2D(new Point2D(500000, 2400030), new Point2D(500020, 2400030));
        var normal = new Point2D(0, 1);

        WidthInferenceService.InferBorderWidth(border, seg, normal, emptyObstacles, options);

        Assert.True(border.IsInferred);
        Assert.Null(border.MeasuredWidth);
        Assert.Equal("UNKNOWN", border.Descriptio);
    }

    [Fact]
    public void LinePolygonizer_ClosesGapsWithinTolerance_AndReportsDangles()
    {
        var demo = SyntheticDataGenerator.CreateSyntheticProject(_testScratchDir);

        var options = new PolygonizerOptions
        {
            SnapToleranceMeters = 0.05,
            KeepAlreadyClosedPolylines = true,
            GroupLayersTogether = true
        };

        var report = LinePolygonizerService.Polygonize(demo.UnclosedLineGroup, options);

        // 1 quadrilateral ring should be closed successfully from the 4 lines
        Assert.True(report.PolygonsBuilt >= 1);
        Assert.Contains(report.ResultRings, r => Math.Abs(r.Area - 100.0) < 1.0); // 10x10 = 100 m²

        // The dangling line should be detected and reported with its coordinate location
        Assert.NotEmpty(report.DanglesFound);
        Assert.Contains(report.DanglesFound, d => d.Message.Contains("dangle") || d.Message.Contains("gap"));
    }

    [Fact]
    public void AreaComparison_DetectsMatchMismatchAndExcelOnly()
    {
        var demo = SyntheticDataGenerator.CreateSyntheticProject(_testScratchDir);

        var report = AreaComparisonService.CompareAreas(
            demo.MainParcels, 
            demo.AreasExcelPath, 
            new AreaComparisonOptions { AbsoluteToleranceSqm = 0.5 }
        );

        Assert.Equal(1, report.MatchedCount);     // Plot 101 matches: 600m²
        Assert.Equal(1, report.MismatchedCount);  // Plot 102 mismatches: 600m² vs 750m²
        Assert.Equal(1, report.InExcelOnlyCount); // Plot 103 is in Excel only

        var match101 = report.Entries.First(e => e.PlotId == "101");
        Assert.Equal("Matched", match101.Status);

        var mismatch102 = report.Entries.First(e => e.PlotId == "102");
        Assert.Equal("Mismatched", mismatch102.Status);
        Assert.Equal(-150.0, mismatch102.AreaDifferenceSqm);
    }

    [Fact]
    public void NormalizedPlotMatching_HandlesArabicIndicDigitsAndSlashes()
    {
        // Check conversion of Arabic-Indic digits ٠١٢٣٤٥٦٧٨٩ -> 0123456789
        string raw1 = "  ١٠١  ";
        Assert.Equal("101", AreaComparisonService.NormalizePlotNumber(raw1));

        // Format with slashes like 11/3314
        string raw2 = " ١١ / ٣٣١٤ ";
        Assert.Equal("11/3314", AreaComparisonService.NormalizePlotNumber(raw2));

        // Dashes
        string raw3 = " 102 - A ";
        Assert.Equal("102-A", AreaComparisonService.NormalizePlotNumber(raw3));
    }

    [Fact]
    public void DbfTextLength_Utf8ByteCount_ArabicTakes2Bytes_SafelyTruncatesAtCharBoundary()
    {
        // ASCII letter = 1 byte
        Assert.Equal(4, DbfTextLengthService.GetUtf8ByteLength("Plot"));

        // Arabic letter = 2 bytes in UTF-8
        // "شارع" = 4 letters = 8 bytes
        Assert.Equal(8, DbfTextLengthService.GetUtf8ByteLength("شارع"));

        // Safe truncation: If max bytes = 7, "شارع" (8 bytes) must be cut at 3 chars = 6 bytes, NEVER mid-character
        string truncated = DbfTextLengthService.TruncateUtf8Safe("شارع", 7);
        Assert.Equal("شار", truncated);
        Assert.Equal(6, DbfTextLengthService.GetUtf8ByteLength(truncated));
    }

    [Fact]
    public void CrsProjection_UtmZones35To40_RanksPlausibleSaudiZones()
    {
        // Central Saudi Arabia coordinates: Riyadh ~ (Easting 500,000, Northing 2,730,000 in Zone 38N)
        var zones = CrsProjectionService.EvaluateZonesForBbox(490000, 2700000, 510000, 2740000);

        Assert.Equal(6, zones.Count);
        // Zone 38N should be ranked as plausible
        var zone38 = zones.First(z => z.Epsg == 32638);
        Assert.True(zone38.IsPlausibleForSaudi);

        // Reproject to Lat/Lon: Northing ~2,730,000 in Zone 38N gives Lat ~24.6°N, Lon ~45°E
        var (lat, lon) = CrsProjectionService.UtmToLatLon(500000, 2730000, 38);
        Assert.InRange(lat, 24.0, 25.0);
        Assert.InRange(lon, 44.5, 45.5);
    }

    [Fact]
    public void ExpectedLayers_MatchesCadLayersAndFlagsNewLayers()
    {
        var demo = SyntheticDataGenerator.CreateSyntheticProject(_testScratchDir);
        var rules = ExpectedLayersService.ReadExpectedLayersExcel(demo.ExpectedLayersExcelPath);

        Assert.NotEmpty(rules);

        var cadLayers = new List<CadLayerInfo>
        {
            new() { LayerName = "PARCEL_BOUNDARY", EntityCount = 2 },
            new() { LayerName = "NEW_SURVEY_POINTS", EntityCount = 10 } // Not in expected layers
        };

        var matchReport = ExpectedLayersService.MatchCadLayers(cadLayers, rules);

        Assert.Contains(matchReport.CadLayersMatched, l => l.CadLayerName == "PARCEL_BOUNDARY" && !l.IsNew);
        Assert.Contains(matchReport.CadLayersMatched, l => l.CadLayerName == "NEW_SURVEY_POINTS" && l.IsNew);
        Assert.Contains("NEW_SURVEY_POINTS", matchReport.CadLayersNotInExcel);
    }

    [Fact]
    public void LayersUsedExcel_WrittenOnEveryRun()
    {
        var demo = SyntheticDataGenerator.CreateSyntheticProject(_testScratchDir);
        var layers = new List<LayerMatchResult>
        {
            new() { CadLayerName = "PARCEL_BOUNDARY", EntityCount = 2, AssignedRole = "Main parcel", IsNew = false },
            new() { CadLayerName = "SUB_PARCELS", EntityCount = 2, AssignedRole = "Sub-parcel", IsNew = false }
        };

        string filePath = ExpectedLayersService.WriteLayersUsedExcel(
            _testScratchDir, 
            demo.Metadata.PlanNo, 
            "Default_Consultant_Profile", 
            "Sample_Plan.dwg", 
            layers
        );

        Assert.True(File.Exists(filePath));
        Assert.Contains("Layers_Used_", Path.GetFileName(filePath));
    }

    [Fact]
    public void CadExtraction_InferRoleFromName_AccuratelyMapsArabicAndEnglish()
    {
        Assert.Equal(LayerRole.Boundary, CadExtractionService.InferRoleFromName("حدود_القطع"));
        Assert.Equal(LayerRole.Boundary, CadExtractionService.InferRoleFromName("PARCEL_BOUNDARY"));
        Assert.Equal(LayerRole.PlotText, CadExtractionService.InferRoleFromName("ارقام_القطع"));
        Assert.Equal(LayerRole.PlotText, CadExtractionService.InferRoleFromName("PLOT_NUMBERS"));
        Assert.Equal(LayerRole.UsageText, CadExtractionService.InferRoleFromName("استعمال_سكني_تجاري"));
        Assert.Equal(LayerRole.UsageText, CadExtractionService.InferRoleFromName("LAND_USE"));
        Assert.Equal(LayerRole.Street, CadExtractionService.InferRoleFromName("شارع_الملك_فهد"));
        Assert.Equal(LayerRole.Street, CadExtractionService.InferRoleFromName("STREET_NETWORK"));
        Assert.Equal(LayerRole.Dimensions, CadExtractionService.InferRoleFromName("شطفات_الاركان"));
        Assert.Equal(LayerRole.Dimensions, CadExtractionService.InferRoleFromName("DIMENSIONS"));
        Assert.Equal(LayerRole.Service, CadExtractionService.InferRoleFromName("خدمات_ومسجد"));
        Assert.Equal(LayerRole.WaterChannel, CadExtractionService.InferRoleFromName("مجرى_سيل_وادي"));
        Assert.Equal(LayerRole.Ignore, CadExtractionService.InferRoleFromName("RANDOM_UNMATCHED_LAYER"));
    }

    [Fact]
    public void CadExtraction_ExtractLayersFromDxf_ParsesGroupCode8Properly()
    {
        string sampleDxf = Path.Combine(_testScratchDir, "test_sample.dxf");
        string dxfContent = @"0
SECTION
2
ENTITIES
0
LINE
8
PARCEL_BOUNDARY
0
LINE
8
PARCEL_BOUNDARY
0
TEXT
8
PLOT_NUMBERS
0
LWPOLYLINE
8
شارع_رئيسي
0
ENDSEC
0
EOF";
        File.WriteAllText(sampleDxf, dxfContent);

        var extracted = CadExtractionService.ExtractLayersFromDxf(sampleDxf);
        Assert.Equal(3, extracted.Count);

        var parcelLayer = extracted.FirstOrDefault(l => l.LayerName == "PARCEL_BOUNDARY");
        Assert.NotNull(parcelLayer);
        Assert.Equal(2, parcelLayer.EntityCount);
        Assert.Equal(LayerRole.Boundary, parcelLayer.Role);

        var plotTextLayer = extracted.FirstOrDefault(l => l.LayerName == "PLOT_NUMBERS");
        Assert.NotNull(plotTextLayer);
        Assert.Equal(1, plotTextLayer.EntityCount);
        Assert.Equal(LayerRole.PlotText, plotTextLayer.Role);

        var streetLayer = extracted.FirstOrDefault(l => l.LayerName == "شارع_رئيسي");
        Assert.NotNull(streetLayer);
        Assert.Equal(1, streetLayer.EntityCount);
        Assert.Equal(LayerRole.Street, streetLayer.Role);
    }

    private static string FindHelperFilesDir()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "HelperFiles");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HelperFiles");
    }

    [Fact]
    public void ProfileExcelService_ReadsAllSettingsFromExampleProfileXlsx()
    {
        string profileXlsx = Path.Combine(FindHelperFilesDir(), "profiles", "Example_Profile.xlsx");
        Assert.True(File.Exists(profileXlsx), $"Example_Profile.xlsx should exist at {profileXlsx}");
        var profile = ProfileExcelService.LoadProfileFromExcel(profileXlsx);

        Assert.Equal("Default_Consultant_Profile", profile.ProfileName);
        Assert.Equal("PARCEL_BOUNDARY", profile.BoundaryLayer);
        Assert.Equal("PLOT_NUMBERS", profile.PlotNoTextLayer);
        Assert.Equal("LAND_USE", profile.UsageTextLayer);
        Assert.Equal("MOMRAH", profile.DirectionSystem);
        Assert.Equal(10.0, profile.PassageStreetThresholdMeters);
        Assert.Equal(100.0, profile.MaxRaySearchDistanceMeters);
        Assert.Equal(3, profile.RayCount);
        Assert.Equal(32637, profile.UtmEpsg);
        Assert.True(profile.MergeChamfers);
    }

    [Fact]
    public void SchemaComplianceService_DetectsDeviationsAndWritesComplianceReport()
    {
        string reqExcel = Path.Combine(FindHelperFilesDir(), "Required Fields in the Shapefile.xlsx");
        var parcels = new List<ParcelRecord>
        {
            new() { InternalId = 1, Class = ParcelClass.Main, PlotId = "101", Usage = "RSD", AreaSqm = 600.0 },
            new() { InternalId = 2, Class = ParcelClass.Main, PlotId = "", Usage = "INVALID_CODE", AreaSqm = 500.0 }
        };

        var borders = new List<BorderRecord>
        {
            new() { PlotId = "101", BorderName = 1 },
            new() { PlotId = "101", BorderName = 2 },
            new() { PlotId = "101", BorderName = 3 },
            new() { PlotId = "101", BorderName = 4 }
        };

        var compliance = SchemaComplianceService.AuditAndCompare(reqExcel, _testScratchDir, parcels, borders);

        Assert.True(compliance.HasDeviations);
        Assert.Single(compliance.DeviatingParcels);
        Assert.Contains("رقم القطعة PLOT_ID مفقود", compliance.DeviatingParcels[0].DeviationReason);
        Assert.Contains("كود الاستخدام USAGE", compliance.DeviatingParcels[0].DeviationReason);
        Assert.True(File.Exists(compliance.ReportExcelPath));
        Assert.NotEmpty(compliance.FieldComparisons);
    }
}


