using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Cad2Gis.Tests;

public class Step2AndStep3FixesTests
{
    [Fact]
    public void Part2_ThreeRectanglesAndTwoTextLayers_AdheresToCadastralTextRules()
    {
        // Setup 3 fictional parcels
        var p1 = new ParcelRecord { InternalId = 1, Class = ParcelClass.Main, CentroidX = 10, CentroidY = 10 };
        var p2 = new ParcelRecord { InternalId = 2, Class = ParcelClass.Main, CentroidX = 30, CentroidY = 10 };
        var p3 = new ParcelRecord { InternalId = 3, Class = ParcelClass.Main, CentroidX = 50, CentroidY = 10 };
        var parcels = new List<ParcelRecord> { p1, p2, p3 };

        // Parcel 1 has:
        // - "101" from PLOT_NO (PlotText)
        // - "سكني" from LAND_USE (UsageText)
        // - "999" from UNSELECTED_LAYER (should be completely ignored)
        var insideP1 = new List<(string Text, string LayerName)>
        {
            ("101", "PLOT_NO"),
            ("سكني", "LAND_USE"),
            ("999", "UNSELECTED_LAYER")
        };

        // Parcel 2 has two candidate numbers on PLOT_NO -> Conflict
        // - "201" (closer to centroid) and "202" (further from centroid)
        var insideP2 = new List<(string Text, string LayerName)>
        {
            ("201", "PLOT_NO"),
            ("202", "PLOT_NO")
        };

        // Parcel 3 has no annotations inside
        var insideP3 = new List<(string Text, string LayerName)>();

        var parcelInsideAnnots = new Dictionary<long, List<(string Text, string LayerName)>>
        {
            { 1, insideP1 },
            { 2, insideP2 },
            { 3, insideP3 }
        };

        var allAnnots = new List<(string Text, double X, double Y, string LayerName)>
        {
            ("101", 10, 10, "PLOT_NO"),
            ("سكني", 10.5, 10.5, "LAND_USE"),
            ("999", 11, 11, "UNSELECTED_LAYER"),
            ("201", 30.1, 10.1, "PLOT_NO"),
            ("202", 30.8, 10.8, "PLOT_NO")
        };

        var parcelCenters = new Dictionary<long, (double CenterX, double CenterY)>
        {
            { 1, (10, 10) },
            { 2, (30, 10) },
            { 3, (50, 10) }
        };

        var activePlotLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PLOT_NO" };
        var activeUsageLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LAND_USE" };

        var report = PlotIdExtractorService.PerformTextJoin(
            parcels,
            parcelInsideAnnots,
            allAnnots,
            parcelCenters,
            explicitPlotLayer: "PLOT_NO",
            secondaryPlotLayers: activePlotLayers,
            usageLayers: activeUsageLayers,
            enableProximityFallback: false);

        // Verification for Parcel 1:
        // PLOT_ID must equal 101 from PLOT_NO only
        Assert.Equal("101", p1.PlotId);
        Assert.Equal("101", p1.PlotNo1);
        Assert.Equal("101", p1.PlotText);
        Assert.Equal("PLOT_NO", p1.PlotLayer);
        Assert.Equal("Contains", p1.MatchMode);

        // Usage text goes to USAGE_TXT / REMARKS and NEVER to PLOT_ID
        Assert.Equal("سكني", p1.UsageText);
        Assert.Equal("سكني", p1.Remarks);
        Assert.Equal("LAND_USE", p1.UsageLayer);
        Assert.NotEqual("999", p1.PlotId); // Unselected layer ignored

        // Verification for Parcel 2:
        // Two candidate plot numbers in one parcel -> Conflict
        Assert.Equal("Conflict", p2.MatchMode);
        Assert.Equal("201", p2.PlotId); // Closest to centroid was chosen deterministically
        Assert.Contains(report.ParcelsWithConflicts, c => c.Contains("Parcel ID 2"));

        // Verification for Parcel 3:
        // Parcel without text -> empty PLOT_ID and listed in report
        Assert.Equal(string.Empty, p3.PlotId);
        Assert.Equal(string.Empty, p3.PlotNo1);
        Assert.Equal("None", p3.MatchMode);
        Assert.Contains(report.ParcelsWithoutPlotText, p => p.Contains("3"));

        // Check report totals
        Assert.Equal(3, report.TotalParcels);
        Assert.Equal(1, report.CountsByMatchMode["Contains"]);
        Assert.Equal(1, report.CountsByMatchMode["Conflict"]);
        Assert.Equal(1, report.CountsByMatchMode["None"]);

        // Layer stats verification
        var plotStats = report.LayerStats.FirstOrDefault(s => s.LayerName == "PLOT_NO");
        Assert.NotNull(plotStats);
        Assert.Equal(3, plotStats.AnnotationsRead); // 101, 201, 202
        Assert.Equal(3, plotStats.AnnotationsInsidePolygon);
        Assert.Equal(2, plotStats.AnnotationsUsed); // 101, 201

        var usageStats = report.LayerStats.FirstOrDefault(s => s.LayerName == "LAND_USE");
        Assert.NotNull(usageStats);
        Assert.Equal(1, usageStats.AnnotationsRead);
        Assert.Equal(1, usageStats.AnnotationsUsed);
    }

    [Fact]
    public void Part3_ContentStampResolver_ImmuneToUnrelatedFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Cad2Gis_StampTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string gdbDir = Path.Combine(tempDir, "Test.gdb");
            Directory.CreateDirectory(gdbDir);

            string dataFile = Path.Combine(gdbDir, "a00000001.gdbtable");
            File.WriteAllText(dataFile, "InitialData_100Features");

            // Mock resolver that hashes the target dataset content
            string MockResolver(string path)
            {
                if (!Directory.Exists(path) && !File.Exists(path)) return string.Empty;
                string primaryData = File.Exists(dataFile) ? File.ReadAllText(dataFile) : "";
                return $"{primaryData.Length}_{primaryData.GetHashCode():X8}";
            }

            PipelineStateManager.ContentStampResolver = MockResolver;

            string initialStamp = PipelineStateManager.ComputeContentStamp(gdbDir);
            Assert.NotEmpty(initialStamp);

            // Simulate ArcPro touching the GDB folder (lock file or unrelated file added)
            Thread.Sleep(50);
            string lockFile = Path.Combine(gdbDir, "random.lock");
            File.WriteAllText(lockFile, "lock");
            Directory.SetLastWriteTimeUtc(gdbDir, DateTime.UtcNow.AddMinutes(5));

            string stampAfterUnrelatedTouch = PipelineStateManager.ComputeContentStamp(gdbDir);
            // Must remain identical because data did not change!
            Assert.Equal(initialStamp, stampAfterUnrelatedTouch);

            // Now simulate actual data edit
            File.WriteAllText(dataFile, "ModifiedData_101Features_UpdatedAttrs");
            string stampAfterRealEdit = PipelineStateManager.ComputeContentStamp(gdbDir);

            // Stamp MUST change on real edit
            Assert.NotEqual(initialStamp, stampAfterRealEdit);
        }
        finally
        {
            PipelineStateManager.ContentStampResolver = null;
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Part3_Step3Tracer_LogsEntryExitAndTimes()
    {
        string tempLog = Path.Combine(Path.GetTempPath(), "step3_trace_test_" + Guid.NewGuid().ToString("N") + ".log");

        try
        {
            var tracer = new Step3Tracer(tempLog);
            tracer.LogInfo("=== Start ===");

            using (tracer.BeginPhase("validate input"))
            {
                Thread.Sleep(10);
            }

            using (tracer.BeginPhase("read parcels"))
            {
                Thread.Sleep(15);
            }

            using (tracer.BeginPhase("write"))
            {
                Thread.Sleep(5);
            }

            tracer.LogInfo("=== Complete ===");

            Assert.True(File.Exists(tempLog));
            string content = File.ReadAllText(tempLog);

            // Verify entry and exit lines
            Assert.Contains("[ENTRY] validate input", content);
            Assert.Contains("[EXIT] validate input (Elapsed:", content);
            Assert.Contains("[ENTRY] read parcels", content);
            Assert.Contains("[EXIT] read parcels (Elapsed:", content);
            Assert.Contains("[ENTRY] write", content);
            Assert.Contains("[EXIT] write (Elapsed:", content);
        }
        finally
        {
            try { File.Delete(tempLog); } catch { }
        }
    }
}
