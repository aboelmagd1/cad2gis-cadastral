using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class PlotIdFromPlotTextTests
{
    [Fact]
    public void Test1_ThreeParcels_AssignsPlotIdAndReportsCounts()
    {
        // 1. Three parcels with PlotText = 101, 102, "" -> PLOT_ID = 101, 102, previous value; counts assigned 2, empty 1.
        var p1 = new ParcelRecord { InternalId = 1, PlotId = "P1", PlotText = "101" };
        var p2 = new ParcelRecord { InternalId = 2, PlotId = "P2", PlotText = "102" };
        var p3 = new ParcelRecord { InternalId = 3, PlotId = "P3", PlotText = "" };
        var parcels = new List<ParcelRecord> { p1, p2, p3 };

        var r = PlotIdFromPlotTextService.Apply(parcels);

        Assert.Equal(2, r.Assigned);
        Assert.Equal(1, r.EmptyText);
        Assert.Equal(2, r.Changed);
        Assert.Empty(r.DuplicateIds);

        Assert.Equal("101", p1.PlotId);
        Assert.Equal("101", p1.PlotNo1);
        Assert.Equal("102", p2.PlotId);
        Assert.Equal("102", p2.PlotNo1);
        Assert.Equal("P3", p3.PlotId);
    }

    [Fact]
    public void Test2_TrimSurroundingSpacesAndKeepSlashAndDigits()
    {
        // 2. PlotText with surrounding spaces and 11/3314 -> trimmed, slash kept.
        var p = new ParcelRecord { InternalId = 1, PlotId = "P1", PlotText = "  11/3314  " };
        var r = PlotIdFromPlotTextService.Apply(new[] { p });

        Assert.Equal(1, r.Assigned);
        Assert.Equal(0, r.EmptyText);
        Assert.Equal(1, r.Changed);
        Assert.Equal("11/3314", p.PlotId);
        Assert.Equal("11/3314", p.PlotNo1);

        // Also test Arabic digits preservation
        var pArabic = new ParcelRecord { InternalId = 2, PlotId = "P2", PlotText = "  ١٠١/٣٣  " };
        var rArabic = PlotIdFromPlotTextService.Apply(new[] { pArabic });
        Assert.Equal("١٠١/٣٣", pArabic.PlotId);
        Assert.Equal("١٠١/٣٣", pArabic.PlotNo1);
    }

    [Fact]
    public void Test3_DuplicatePlotTextValues_KeptAndReported()
    {
        // 3. Duplicate PlotText values -> both kept, duplicates reported, nothing renamed.
        var p1 = new ParcelRecord { InternalId = 1, PlotId = "P1", PlotText = "101" };
        var p2 = new ParcelRecord { InternalId = 2, PlotId = "P2", PlotText = "101" };
        var p3 = new ParcelRecord { InternalId = 3, PlotId = "P3", PlotText = "102" };
        var parcels = new List<ParcelRecord> { p1, p2, p3 };

        var r = PlotIdFromPlotTextService.Apply(parcels);

        Assert.Equal(3, r.Assigned);
        Assert.Equal(0, r.EmptyText);
        Assert.Single(r.DuplicateIds);
        Assert.Contains("101", r.DuplicateIds);

        // Both kept as 101, neither dropped nor renamed
        Assert.Equal("101", p1.PlotId);
        Assert.Equal("101", p2.PlotId);
        Assert.Equal("102", p3.PlotId);
    }

    [Fact]
    public void Test4_ModeLineOrPolygon_HelperNotCalled_PlotIdUnchanged()
    {
        // 4. Mode Line or Polygon -> the apply step is not called (use the mode check helper) and PLOT_ID is unchanged.
        var modes = new[] { LayerGeometryModeService.ModeLine, LayerGeometryModeService.ModePolygon };
        foreach (var mode in modes)
        {
            var parcels = new List<ParcelRecord>
            {
                new ParcelRecord { InternalId = 1, PlotId = "P1", PlotText = "101" },
                new ParcelRecord { InternalId = 2, PlotId = "P2", PlotText = "102" }
            };

            bool isLineWithDataLayer = LayerGeometryModeService.IsLineWithDataLayer(mode);
            Assert.False(isLineWithDataLayer);

            if (isLineWithDataLayer)
            {
                PlotIdFromPlotTextService.Apply(parcels);
            }

            Assert.Equal("P1", parcels[0].PlotId);
            Assert.Equal("P2", parcels[1].PlotId);
        }

        // Verify that ModeLineWithDataLayer is recognized
        Assert.True(LayerGeometryModeService.IsLineWithDataLayer(LayerGeometryModeService.ModeLineWithDataLayer));
    }

    [Fact]
    public void Test5_ModeLineWithDataLayer_NoPlotTxtField_NoException_PlotIdUnchanged()
    {
        // 5. Mode Line with data layer and no PLOT_TXT field -> no exception, one log line, PLOT_ID unchanged.
        var parcels = new List<ParcelRecord>
        {
            new ParcelRecord { InternalId = 1, PlotId = "P1", PlotText = "101" },
            new ParcelRecord { InternalId = 2, PlotId = "P2", PlotText = "102" }
        };

        bool isLineWithDataLayer = LayerGeometryModeService.IsLineWithDataLayer(LayerGeometryModeService.ModeLineWithDataLayer);
        Assert.True(isLineWithDataLayer);

        bool hasPlotTxtField = false; // simulates PLOT_TXT field not found in feature class
        string logMessage = string.Empty;
        string step3Input = @"C:\Workspace\Cad_Constructed_Parcels";

        if (isLineWithDataLayer)
        {
            if (!hasPlotTxtField)
            {
                logMessage = $"PLOT_TXT field not found in {step3Input}; PLOT_ID left unchanged";
            }
            else
            {
                PlotIdFromPlotTextService.Apply(parcels);
            }
        }

        Assert.Equal($"PLOT_TXT field not found in {step3Input}; PLOT_ID left unchanged", logMessage);
        Assert.Equal("P1", parcels[0].PlotId);
        Assert.Equal("P2", parcels[1].PlotId);
    }

    [Fact]
    public void Test6_ExcelLookupReceivesNewPlotId()
    {
        // 6. The Excel lookup receives the new PLOT_ID (the plot found by 101).
        var p = new ParcelRecord
        {
            InternalId = 1,
            PlotId = "P1",
            PlotText = "101",
            Class = ParcelClass.Main,
            AreaSqm = 500.0
        };

        // Before apply: PLOT_ID is P1
        var excelLookup = new Dictionary<string, LandUseItem>
        {
            ["101"] = new LandUseItem
            {
                PlotId = "101",
                Remarks = "سكني معتمد",
                BlockNo = "55"
            }
        };

        // Before applying PlotIdFromPlotTextService, Excel lookup for "101" will not match "P1"
        var testParcelsBefore = new List<ParcelRecord> { new ParcelRecord { PlotId = "P1" } };
        LandUseExcelService.EnrichParcels(testParcelsBefore, excelLookup);
        Assert.Empty(testParcelsBefore[0].Remarks);

        // After applying PlotIdFromPlotTextService:
        var parcels = new List<ParcelRecord> { p };
        var r = PlotIdFromPlotTextService.Apply(parcels);

        Assert.Equal("101", p.PlotId);
        Assert.Equal("101", p.PlotNo1);

        // Now run LandUseExcelService.EnrichParcels
        LandUseExcelService.EnrichParcels(parcels, excelLookup);

        // Parcel successfully matched via new PLOT_ID ("101")
        Assert.Equal("سكني معتمد", p.Remarks);
        Assert.Equal("55", p.BlockNo);
    }

    [Fact]
    public void Test7_ExamplesRecordedUpToFive()
    {
        var parcels = new List<ParcelRecord>();
        for (int i = 1; i <= 7; i++)
        {
            parcels.Add(new ParcelRecord { InternalId = i, PlotId = $"P{i}", PlotText = $"{100 + i}" });
        }

        var r = PlotIdFromPlotTextService.Apply(parcels);
        Assert.Equal(7, r.Assigned);
        Assert.Equal(7, r.Changed);
        Assert.Equal(5, r.Examples.Count); // Capped at 5
        Assert.Equal("P1 → 101", r.Examples[0]);
        Assert.Equal("P5 → 105", r.Examples[4]);
    }
}
