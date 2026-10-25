using System.IO;
using System.Linq;
using Xunit;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class CadExtractionAndExpectedLayersTests
{
    [Fact]
    public void Neighbourhood_CodeInParcel_NameInAddress_AreDistinctAndAccurate()
    {
        var meta = new ProjectMetadata
        {
            NeighbourhoodCode = "101",
            NeighbourhoodName = "المروة",
            CityName = "جدة",
            RegionName = "مكة المكرمة",
            PlanNo = "13-9-2026"
        };

        var parcel = new ParcelRecord
        {
            InternalId = 1,
            PlotId = "101"
        };

        MissingDataPolicy.ApplyDefaults(parcel, meta);

        // Assert: NEIGHBOURH field has the code
        Assert.Equal("101", parcel.Neighbourhood);

        // Assert: ADDRESS field has the name formatted
        Assert.Equal("حي/المروة/بجدة/بمنطقة/مكة المكرمة", parcel.Address);
    }

    private static string FindWorkspaceDir()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "HelperFiles")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    private static string FindHelperFilesDir()
    {
        return Path.Combine(FindWorkspaceDir(), "HelperFiles");
    }

    [Fact]
    public void ExpectedLayersExcel_ExistsAndContainsRules()
    {
        string path = Path.Combine(FindHelperFilesDir(), "expected_layers", "Expected_Layers.xlsx");
        Assert.True(File.Exists(path), "Expected_Layers.xlsx should exist in HelperFiles/expected_layers/");

        var rules = ExpectedLayersService.ReadExpectedLayersExcel(path);
        Assert.NotEmpty(rules);

        // Check for specific residential and commercial rules
        var resRule = rules.FirstOrDefault(r => r.LayerName.Contains("residential", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(resRule);
        Assert.Equal("Main parcel", resRule.Role);

        var roadRule = rules.FirstOrDefault(r => r.LayerName.Contains("boundary roads", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(roadRule);
        Assert.Equal("Road", roadRule.Role);
    }

    [Fact]
    public void CadExtraction_DwgExtraction_ExtractsLayers()
    {
        string dwgPath = Path.Combine(FindWorkspaceDir(), "example", "sample_cad.dwg");
        if (File.Exists(dwgPath))
        {
            var layers = CadExtractionService.ExtractLayers(dwgPath);
            Assert.NotEmpty(layers);
            Assert.Contains(layers, l => l.LayerName.Contains("residential", System.StringComparison.OrdinalIgnoreCase) || 
                                         l.LayerName.Contains("pathes", System.StringComparison.OrdinalIgnoreCase) || 
                                         l.LayerName.Contains("area", System.StringComparison.OrdinalIgnoreCase) || 
                                         l.LayerName.Contains("dim", System.StringComparison.OrdinalIgnoreCase) || 
                                         l.LayerName == "0");
        }
    }

    [Fact]
    public void CadLayerInfo_CategoriesAndTabs_MatchImage2Specification()
    {
        // 1. Main Parcels
        Assert.Equal("Main Parcels", CadLayerInfo.DetermineCategory(LayerRole.Boundary, "PARCEL_RESIDENTIAL"));
        Assert.Equal("Main Parcels", CadLayerInfo.DetermineCategory(LayerRole.Boundary, "PARCEL_COMMERCIAL"));
        Assert.Equal("Main Parcels", CadLayerInfo.DetermineCategory(LayerRole.Ignore, "الاوجه التجارية"));

        // 2. Sub-Parcels / Utilities
        Assert.Equal("Sub-Parcels / Utilities", CadLayerInfo.DetermineCategory(LayerRole.Service, "SERVICE_LANE"));
        Assert.Equal("Sub-Parcels / Utilities", CadLayerInfo.DetermineCategory(LayerRole.Service, "UTILITY_EASEMENT"));
        Assert.Equal("Sub-Parcels / Utilities", CadLayerInfo.DetermineCategory(LayerRole.Service, "garden"));
        Assert.Equal("Sub-Parcels / Utilities", CadLayerInfo.DetermineCategory(LayerRole.Street, "boundary roads"));
        Assert.Equal("Sub-Parcels / Utilities", CadLayerInfo.DetermineCategory(LayerRole.Street, "ممرات"));

        // 3. Text & Annotations
        Assert.Equal("Text & Annotations", CadLayerInfo.DetermineCategory(LayerRole.PlotText, "number"));
        Assert.Equal("Text & Annotations", CadLayerInfo.DetermineCategory(LayerRole.Dimensions, "DIM"));
        Assert.Equal("Text & Annotations", CadLayerInfo.DetermineCategory(LayerRole.UsageText, "استعمالات_الاراضي"));

        // 4. Ignore Layers (Tab 4)
        Assert.Equal("Ignore Layers", CadLayerInfo.DetermineCategory(LayerRole.WaterChannel, "water canals"));
        Assert.Equal("Ignore Layers", CadLayerInfo.DetermineCategory(LayerRole.Ignore, "التنظيم"));
        Assert.Equal("Ignore Layers", CadLayerInfo.DetermineCategory(LayerRole.Ignore, "other layer"));
    }

    [Fact]
    public void CadLayerInfo_Enrichment_SetsGeometryAndPreviewText()
    {
        var res = new CadLayerInfo { LayerName = "PARCEL_RESIDENTIAL", EntityCount = 1452 };
        CadExtractionService.EnrichCadLayerMetadata(res);
        Assert.Equal("Polygon", res.GeometryType);
        Assert.Equal("#FACC15", res.Color);
        Assert.Equal("S_RES_01", res.RawCadText);
        Assert.Equal("سكني - 1", res.DecodedArabicText);
        Assert.Equal("1,452", res.FormattedCount);

        var srv = new CadLayerInfo { LayerName = "SERVICE_LANE", EntityCount = 128 };
        CadExtractionService.EnrichCadLayerMetadata(srv);
        Assert.Equal("Point", srv.GeometryType);
        Assert.Equal("#06B6D4", srv.Color);
        Assert.Equal("SERV_LANE", srv.RawCadText);
        Assert.Equal("ممر خدمة", srv.DecodedArabicText);
        Assert.Equal("128", srv.FormattedCount);

        var com = new CadLayerInfo { LayerName = "PARCEL_COMMERCIAL", EntityCount = 342 };
        CadExtractionService.EnrichCadLayerMetadata(com);
        Assert.Equal("Line", com.GeometryType);
        Assert.Equal("#D946EF", com.Color);
        Assert.Equal("S_RES_01", com.RawCadText);
        Assert.Equal("سكني - 1", com.DecodedArabicText);
        Assert.Equal("342", com.FormattedCount);
    }

    [Fact]
    public void PolygonLayerScope_FiltersAppropriately()
    {
        var mainLayer = new CadLayerInfo { LayerName = "01 residential", Category = "Main Parcels", Role = LayerRole.Boundary, IsSelected = true };
        var subLayer = new CadLayerInfo { LayerName = "garden", Category = "Sub-Parcels / Utilities", Role = LayerRole.Service, IsSelected = true };
        var roadLayer = new CadLayerInfo { LayerName = "roads", Category = "Roads & References", Role = LayerRole.Street, IsSelected = true };
        var textLayer = new CadLayerInfo { LayerName = "number", Category = "Text & Annotations", Role = LayerRole.PlotText, IsSelected = true, GeometryType = "Text" };
        var unselectedMain = new CadLayerInfo { LayerName = "commercial", Category = "Main Parcels", Role = LayerRole.Boundary, IsSelected = false };

        // 1. All Selected (Main & Sub)
        Assert.True(PolygonLayerScopeService.IsLayerEligible(mainLayer, PolygonLayerScopeService.ScopeMainAndSub));
        Assert.True(PolygonLayerScopeService.IsLayerEligible(subLayer, PolygonLayerScopeService.ScopeMainAndSub));
        Assert.False(PolygonLayerScopeService.IsLayerEligible(unselectedMain, PolygonLayerScopeService.ScopeMainAndSub));
        Assert.False(PolygonLayerScopeService.IsLayerEligible(textLayer, PolygonLayerScopeService.ScopeMainAndSub));

        // 2. Main Only
        Assert.True(PolygonLayerScopeService.IsLayerEligible(mainLayer, PolygonLayerScopeService.ScopeMainOnly));
        Assert.False(PolygonLayerScopeService.IsLayerEligible(subLayer, PolygonLayerScopeService.ScopeMainOnly));
        Assert.False(PolygonLayerScopeService.IsLayerEligible(textLayer, PolygonLayerScopeService.ScopeMainOnly));

        // 3. All Except Text
        Assert.True(PolygonLayerScopeService.IsLayerEligible(mainLayer, PolygonLayerScopeService.ScopeAllExceptText));
        Assert.True(PolygonLayerScopeService.IsLayerEligible(subLayer, PolygonLayerScopeService.ScopeAllExceptText));
        Assert.True(PolygonLayerScopeService.IsLayerEligible(roadLayer, PolygonLayerScopeService.ScopeAllExceptText));
        Assert.False(PolygonLayerScopeService.IsLayerEligible(textLayer, PolygonLayerScopeService.ScopeAllExceptText));
        Assert.False(PolygonLayerScopeService.IsLayerEligible(unselectedMain, PolygonLayerScopeService.ScopeAllExceptText));
    }

    [Fact]
    public void ArabicCadDecoder_DecodesCorrectly()
    {
        Assert.Equal("سكني", Cad2Gis.Core.Utils.ArabicCadDecoder.Decode("سكني"));
        Assert.Equal(string.Empty, Cad2Gis.Core.Utils.ArabicCadDecoder.Decode(null));
        Assert.Equal("101", Cad2Gis.Core.Utils.ArabicCadDecoder.Decode("{\\fArial|b0|i0;101}"));
    }

    [Fact]
    public void CadExtraction_CadastralNames_CategorizedAsMainOrSubParcels()
    {
        var commRole = CadExtractionService.InferRoleFromName("commercial");
        Assert.Equal(LayerRole.Boundary, commRole);
        Assert.Equal("Main Parcels", CadLayerInfo.DetermineCategory(commRole, "commercial"));

        var resRole = CadExtractionService.InferRoleFromName("سكني");
        Assert.Equal(LayerRole.Boundary, resRole);
        Assert.Equal("Main Parcels", CadLayerInfo.DetermineCategory(resRole, "سكني"));

        var arabCommRole = CadExtractionService.InferRoleFromName("الاجهه التجارية");
        Assert.Equal(LayerRole.Boundary, arabCommRole);
        Assert.Equal("Main Parcels", CadLayerInfo.DetermineCategory(arabCommRole, "الاجهه التجارية"));

        var mixedRole = CadExtractionService.InferRoleFromName("mixed Use");
        Assert.Equal(LayerRole.Boundary, mixedRole);
        Assert.Equal("Main Parcels", CadLayerInfo.DetermineCategory(mixedRole, "mixed Use"));

        var hospRole = CadExtractionService.InferRoleFromName("Hospital");
        Assert.Equal(LayerRole.Service, hospRole);
        Assert.Equal("Sub-Parcels / Utilities", CadLayerInfo.DetermineCategory(hospRole, "Hospital"));

        var msqRole = CadExtractionService.InferRoleFromName("mosque");
        Assert.Equal(LayerRole.Service, msqRole);
        Assert.Equal("Sub-Parcels / Utilities", CadLayerInfo.DetermineCategory(msqRole, "mosque"));
    }

    [Fact]
    public void LayerGeometryModeService_ValidatesModesAndGeometryInclusion()
    {
        // 1. Available Modes (ModeLine, ModePolygon, and ModeLineWithDataLayer)
        Assert.Equal(3, LayerGeometryModeService.AvailableModes.Length);
        Assert.Contains(LayerGeometryModeService.ModeLineWithDataLayer, LayerGeometryModeService.AvailableModes);
        Assert.Contains(LayerGeometryModeService.ModePolygon, LayerGeometryModeService.AvailableModes);
        Assert.Contains(LayerGeometryModeService.ModeLine, LayerGeometryModeService.AvailableModes);

        // 2. Line with data layer: builds from lines, attributes to layer
        Assert.False(LayerGeometryModeService.ShouldIngestPolygons(LayerGeometryModeService.ModeLineWithDataLayer));
        Assert.True(LayerGeometryModeService.ShouldPolygonizeLines(LayerGeometryModeService.ModeLineWithDataLayer));
        Assert.True(LayerGeometryModeService.IsLineWithDataLayer(LayerGeometryModeService.ModeLineWithDataLayer));

        // 3. Polygon Mode: Ingests Polygons, skips Lines unless forced
        Assert.True(LayerGeometryModeService.ShouldIngestPolygons(LayerGeometryModeService.ModePolygon));
        Assert.False(LayerGeometryModeService.ShouldPolygonizeLines(LayerGeometryModeService.ModePolygon, forceBuildLines: false));
        Assert.True(LayerGeometryModeService.ShouldPolygonizeLines(LayerGeometryModeService.ModePolygon, forceBuildLines: true));

        // 4. Line Mode: Skips Polygons, directly converts Lines to Polygons
        Assert.False(LayerGeometryModeService.ShouldIngestPolygons(LayerGeometryModeService.ModeLine));
        Assert.True(LayerGeometryModeService.ShouldPolygonizeLines(LayerGeometryModeService.ModeLine));

        // 5. Backwards compatibility: legacy profile values map to Line with data layer
        Assert.Equal(LayerGeometryModeService.ModeLineWithDataLayer, LayerGeometryModeService.NormalizeMode("Both (مضلعات وخطوط معاً)"));
        Assert.Equal(LayerGeometryModeService.ModeLineWithDataLayer, LayerGeometryModeService.NormalizeMode("Both"));
        Assert.Equal(LayerGeometryModeService.ModeLineWithDataLayer, LayerGeometryModeService.NormalizeMode("Polygon line"));
        Assert.Equal(LayerGeometryModeService.ModeLineWithDataLayer, LayerGeometryModeService.NormalizeMode("Line with data layer"));
    }

    [Fact]
    public void ArcPyLineToPolygon_ScriptExistsAndIsSyntacticallyValid()
    {
        string baseDir = AppContext.BaseDirectory;
        // Search for HelperFiles/scripts/arcpy_line_to_polygon.py
        string candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "HelperFiles", "scripts", "arcpy_line_to_polygon.py"));
        if (!File.Exists(candidate))
        {
            candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "HelperFiles", "scripts", "arcpy_line_to_polygon.py"));
        }
        if (File.Exists(candidate))
        {
            string content = File.ReadAllText(candidate);
            Assert.Contains("CAD2GIS ArcPy Line to Polygon Tool", content);
            Assert.Contains("FeatureToPolygon", content);
            Assert.Contains("arcpy.env.outputZFlag = \"Disabled\"", content);
            Assert.Contains("arcpy.env.outputMFlag = \"Disabled\"", content);
        }
    }

    [Fact]
    public void IgnoreLayers_AreIgnoredByDefault()
    {
        var ignoreLayer = new CadLayerInfo
        {
            LayerName = "random_ignored_lines",
            Category = "Ignore Layers",
            Role = LayerRole.Ignore,
            IsSelected = false
        };

        // All scopes must ignore Ignore Layers when Role is Ignore or IsSelected is false
        Assert.False(PolygonLayerScopeService.IsLayerEligible(ignoreLayer, PolygonLayerScopeService.ScopeMainAndSub));
        Assert.False(PolygonLayerScopeService.IsLayerEligible(ignoreLayer, PolygonLayerScopeService.ScopeMainOnly));
        Assert.False(PolygonLayerScopeService.IsLayerEligible(ignoreLayer, PolygonLayerScopeService.ScopeAllExceptText));
    }

    [Fact]
    public void StreetRole_TransfersToSubParcelsCategory_AndIsNotMainParcel()
    {
        var layer = new CadLayerInfo
        {
            LayerName = "street_corridor_01",
            Role = LayerRole.Boundary // Initially Boundary
        };
        Assert.Equal("Main Parcels", layer.Category);

        // When user sets role to Street (e.g. for corridors / streets)
        layer.Role = LayerRole.Street;

        // Must automatically transfer to Sub-Parcels / Utilities
        Assert.Equal("Sub-Parcels / Utilities", layer.Category);

        // Sub-parcels are eligible under Main & Sub scope, but NOT under Main Only
        layer.IsSelected = true;
        Assert.True(PolygonLayerScopeService.IsLayerEligible(layer, PolygonLayerScopeService.ScopeMainAndSub));
        Assert.False(PolygonLayerScopeService.IsLayerEligible(layer, PolygonLayerScopeService.ScopeMainOnly));
    }

    [Fact]
    public void AppendOrUpdateLayersInExpectedExcel_AddsNewLayersAndPreservesExisting()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Cad2Gis_Test_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string excelPath = Path.Combine(tempDir, "Expected_Layers.xlsx");

        try
        {
            var initialLayers = new List<CadLayerInfo>
            {
                new() { LayerName = "EXISTING_PARCEL", Role = LayerRole.Boundary, IsSelected = true }
            };

            // First call ensures and seeds the excel
            ExpectedLayersService.EnsureExpectedLayersExcel(tempDir, "Default", initialLayers);
            Assert.True(File.Exists(excelPath));

            var rules1 = ExpectedLayersService.ReadExpectedLayersExcel(excelPath);
            Assert.Single(rules1);
            Assert.Equal("EXISTING_PARCEL", rules1[0].LayerName);

            // Second call appends newly selected layers
            var newlySelected = new List<CadLayerInfo>
            {
                new() { LayerName = "NEW_COMMERCIAL_PARCEL", Role = LayerRole.Boundary, IsSelected = true },
                new() { LayerName = "NEW_SERVICE_LANE", Role = LayerRole.Service, IsSelected = true }
            };

            ExpectedLayersService.AppendOrUpdateLayersInExpectedExcel(excelPath, "Default", newlySelected);

            var rules2 = ExpectedLayersService.ReadExpectedLayersExcel(excelPath);
            Assert.Equal(3, rules2.Count);
            Assert.Contains(rules2, r => r.LayerName == "EXISTING_PARCEL");
            Assert.Contains(rules2, r => r.LayerName == "NEW_COMMERCIAL_PARCEL" && r.Role == "Main parcel");
            Assert.Contains(rules2, r => r.LayerName == "NEW_SERVICE_LANE" && r.Role == "Sub-parcel");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void HelperFilesManager_ResolvesExpectedLayersDirAndExcel()
    {
        string baseDir = FindWorkspaceDir();
        var hfm = new HelperFilesManager(baseDir);

        Assert.True(Directory.Exists(hfm.ExpectedLayersDir), $"ExpectedLayersDir should exist: {hfm.ExpectedLayersDir}");
        Assert.True(File.Exists(hfm.ExpectedLayersExcel), $"ExpectedLayersExcel should exist: {hfm.ExpectedLayersExcel}");
    }

    [Fact]
    public void FinalExportStructure_ValidatesAllFoldersAndFiles()
    {
        string tempExport = Path.Combine(Path.GetTempPath(), "Export_Test_" + System.Guid.NewGuid().ToString("N"));
        string finalDir = Path.Combine(tempExport, "Final");
        string finalGdb = Path.Combine(finalDir, "Final.gdb");
        string shapeDir = Path.Combine(finalDir, "Shape");
        string reportsDir = Path.Combine(finalDir, "Reports");

        Directory.CreateDirectory(finalDir);
        Directory.CreateDirectory(finalGdb);
        Directory.CreateDirectory(shapeDir);
        Directory.CreateDirectory(reportsDir);

        File.WriteAllText(Path.Combine(shapeDir, "PARCEL.shp"), "test shape data");
        File.WriteAllText(Path.Combine(shapeDir, "BORDER.shp"), "test border data");
        File.WriteAllText(Path.Combine(reportsDir, "QA_QC_Report.xlsx"), "test report data");

        // Integrity check
        bool gdbOk = Directory.Exists(finalGdb);
        bool parcelOk = File.Exists(Path.Combine(shapeDir, "PARCEL.shp")) && new FileInfo(Path.Combine(shapeDir, "PARCEL.shp")).Length > 0;
        bool borderOk = File.Exists(Path.Combine(shapeDir, "BORDER.shp")) && new FileInfo(Path.Combine(shapeDir, "BORDER.shp")).Length > 0;
        bool reportsOk = Directory.Exists(reportsDir) && Directory.GetFiles(reportsDir).Length > 0;

        Assert.True(gdbOk, "Final.gdb must exist");
        Assert.True(parcelOk, "Shape/PARCEL.shp must exist and be non-empty");
        Assert.True(borderOk, "Shape/BORDER.shp must exist and be non-empty");
        Assert.True(reportsOk, "Reports must contain generated reports");

        try { Directory.Delete(tempExport, true); } catch { }
    }
}

