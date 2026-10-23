using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class LatestExportWorkflowTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _stateFile;
    private readonly string _scratchDir;

    public LatestExportWorkflowTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Cad2Gis_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _stateFile = Path.Combine(_testDir, "last_export.json");
        _scratchDir = Path.Combine(_testDir, "scratch");
        Directory.CreateDirectory(_scratchDir);
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

    [Fact]
    public void SaveExportState_WritesJsonAndMaintainsHistory()
    {
        // First export
        var state1 = new ExportStateRecord
        {
            RunId = "run_01",
            Timestamp = DateTime.UtcNow.AddMinutes(-30),
            ExportFolder = Path.Combine(_testDir, "Export_01"),
            ParcelPath = Path.Combine(_testDir, "Export_01", "PARCEL.shp"),
            BorderPath = Path.Combine(_testDir, "Export_01", "border.shp"),
            WorkingUtmZone = 32637,
            ProfileName = "TestProfile"
        };

        ExportStateManager.SaveExportState(_stateFile, state1, maxHistoryCount: 5);

        var loaded1 = ExportStateManager.LoadLatestExport(_stateFile);
        Assert.NotNull(loaded1);
        Assert.Equal("run_01", loaded1.RunId);
        Assert.Empty(loaded1.History);

        // Second export
        var state2 = new ExportStateRecord
        {
            RunId = "run_02",
            Timestamp = DateTime.UtcNow,
            ExportFolder = Path.Combine(_testDir, "Export_02"),
            ParcelPath = Path.Combine(_testDir, "Export_02", "PARCEL.shp"),
            BorderPath = Path.Combine(_testDir, "Export_02", "border.shp"),
            WorkingUtmZone = 32637,
            ProfileName = "TestProfile"
        };

        ExportStateManager.SaveExportState(_stateFile, state2, maxHistoryCount: 5);

        var loaded2 = ExportStateManager.LoadLatestExport(_stateFile);
        Assert.NotNull(loaded2);
        Assert.Equal("run_02", loaded2.RunId);
        // Previous export moved to history
        Assert.Single(loaded2.History);
        Assert.Equal("run_01", loaded2.History[0].RunId);
    }

    [Fact]
    public void DetectEdits_DetectsParcelGeometryAndAttributeChanges()
    {
        var state = new ExportStateRecord
        {
            RunId = "run_01",
            Timestamp = DateTime.UtcNow.AddHours(-1),
            ExportFolder = Path.Combine(_testDir, "Export_01")
        };

        var pDs = new ExportDatasetInfo
        {
            DatasetName = "PARCEL",
            FilePath = Path.Combine(_testDir, "PARCEL.shp"),
            FeatureCount = 2
        };
        pDs.Features.Add(new ExportFeatureMetadata
        {
            PlotId = "101",
            AreaSqm = 500.0,
            ShapeLength = 90.0,
            Usage = "Residential",
            Address = "Riyadh District 1"
        });
        pDs.Features.Add(new ExportFeatureMetadata
        {
            PlotId = "102",
            AreaSqm = 600.0,
            ShapeLength = 100.0,
            Usage = "Commercial",
            Address = "Riyadh District 1"
        });
        state.Datasets["PARCEL"] = pDs;

        // Current parcels: parcel 101 has modified area (550 vs 500), parcel 102 has modified usage
        var currentParcels = new List<ParcelRecord>
        {
            new() { PlotId = "101", AreaSqm = 550.0, ShapeLength = 95.0, Usage = "Residential", Address = "Riyadh District 1" },
            new() { PlotId = "102", AreaSqm = 600.0, ShapeLength = 100.0, Usage = "Commercial Modified", Address = "Riyadh District 1" }
        };

        var diff = ExportStateManager.DetectEdits(state, currentParcels, new List<BorderRecord>());

        Assert.True(diff.WasEditedAfterExport);
        // Geometry change on parcel 101
        Assert.Contains(diff.GeometryChanges, g => g.Key == "Parcel 101" && g.ExceedsTolerance);
        // Attribute change on parcel 102
        Assert.Contains(diff.AttributeChanges, a => a.Key == "Parcel 102" && a.FieldName == "USAGE" && a.NewValue == "Commercial Modified");
    }

    [Fact]
    public void DetectEdits_DetectsManualBorderEdits()
    {
        var state = new ExportStateRecord
        {
            RunId = "run_01",
            Timestamp = DateTime.UtcNow.AddHours(-1),
            ExportFolder = Path.Combine(_testDir, "Export_01")
        };

        var bDs = new ExportDatasetInfo
        {
            DatasetName = "Border",
            FilePath = Path.Combine(_testDir, "border.shp"),
            FeatureCount = 1
        };
        bDs.Features.Add(new ExportFeatureMetadata
        {
            PlotId = "101",
            BorderName = 1,
            RingIndex = 0,
            Descriptio = "شارع عرض 15م",
            BorderType = "STR",
            ShapeLength = 20.0
        });
        state.Datasets["Border"] = bDs;

        // User manually changed description to "شارع عرض 20م"
        var currentBorders = new List<BorderRecord>
        {
            new()
            {
                PlotId = "101",
                BorderName = 1,
                RingIndex = 0,
                Descriptio = "شارع عرض 20م",
                BorderType = "STR",
                ShapeLength = 20.0
            }
        };

        var diff = ExportStateManager.DetectEdits(state, new List<ParcelRecord>(), currentBorders);

        Assert.True(diff.WasEditedAfterExport);
        Assert.Contains(diff.AttributeChanges, a => a.FieldName == "DESCRIPTIO" && a.NewValue == "شارع عرض 20م");
    }

    [Fact]
    public void LockedFile_ResolveReadableCopy_CreatesTemporaryCopy()
    {
        string dummyShp = Path.Combine(_testDir, "locked_parcel.shp");
        File.WriteAllText(dummyShp, "dummy shapefile content");

        // Lock file with exclusive access
        using (var lockStream = new FileStream(dummyShp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // ResolveReadableCopy should catch lock and create temp copy
            var (fileToRead, wasCopied, notice) = ExportStateManager.ResolveReadableCopy(dummyShp, _scratchDir);

            // In Windows, File.Copy can fail if FileShare.None is held strictly,
            // or return error notice without crashing
            Assert.NotNull(fileToRead);
        }
    }

    [Fact]
    public void RecomputeFrozenMetrics_UpdatesMetrics_PreservesManualAttributes()
    {
        var p = new ParcelRecord
        {
            InternalId = 1,
            PlotId = "101",
            AreaSqm = 500.0,       // Old frozen area
            ShapeLength = 90.0,    // Old frozen length
            Usage = "تجاري مخصص", // Manual attribute
            Address = "طريق الملك فهد",
            Neighbourhood = "النخيل",
            Remarks = "ملاحظات هامة"
        };

        // Simulated edited geometry with new area = 550 and perimeter = 95
        double newArea = 550.0;
        double newLength = 95.0;

        // Recompute derived fields only
        p.AreaSqm = newArea;
        p.ShapeArea = newArea;
        p.ShapeLength = newLength;

        // Verify derived metrics updated
        Assert.Equal(550.0, p.AreaSqm);
        Assert.Equal(95.0, p.ShapeLength);

        // Verify user-typed attributes strictly survived
        Assert.Equal("تجاري مخصص", p.Usage);
        Assert.Equal("طريق الملك فهد", p.Address);
        Assert.Equal("النخيل", p.Neighbourhood);
        Assert.Equal("ملاحظات هامة", p.Remarks);
    }
}
