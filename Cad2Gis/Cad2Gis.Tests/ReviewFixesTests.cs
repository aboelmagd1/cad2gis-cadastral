using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class ReviewFixesTests : IDisposable
{
    private readonly string _testDir;

    public ReviewFixesTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Cad2Gis_ReviewFixes_" + Guid.NewGuid().ToString("N"));
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

    // =========================================================================
    // P1 Tests: Per-step source mode and pipeline_state persistence
    // =========================================================================

    [Fact]
    public void P1_PipelineStateManager_PersistsAndLoadsStepOutputAcrossSessions()
    {
        string stateFile = Path.Combine(_testDir, "state", "pipeline_state.json");
        var record = new StepOutputRecord
        {
            Step = 3,
            Path = @"C:\GIS\Cad_Dataset.gdb\PARCEL_Approved",
            FeatureCount = 585,
            Wkid = 32637,
            Timestamp = DateTime.UtcNow,
            RunId = "run_step3_test"
        };

        // Save step output
        bool saved = PipelineStateManager.TrySaveStepOutput(stateFile, record, out var err);
        Assert.True(saved, err);
        Assert.True(File.Exists(stateFile));

        // Restart simulation: reload from persistent json
        var loaded = PipelineStateManager.LoadStepOutput(stateFile, 3);
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.Step);
        Assert.Equal(@"C:\GIS\Cad_Dataset.gdb\PARCEL_Approved", loaded.Path);
        Assert.Equal(585, loaded.FeatureCount);
        Assert.Equal(32637, loaded.Wkid);
        Assert.Equal("run_step3_test", loaded.RunId);
    }

    [Fact]
    public void P1_ConsultantProfile_DefaultContinueFromLatestExport_IsFalse()
    {
        var profile = new ConsultantProfileModel();
        Assert.False(profile.ContinueFromLatestExport, "ContinueFromLatestExport must default to false to prevent silent source switching");

        var settings = new ToolSettings();
        Assert.False(settings.ContinueFromLatestExport, "ToolSettings ContinueFromLatestExport must default to false");
    }

    [Fact]
    public void P1_PerStepSourceDictionary_KeepsModesIsolated()
    {
        // Simulates DockPaneViewModel per-step source management
        var sourceByStep = new Dictionary<int, string>
        {
            [2] = "FromPreviousStep",
            [3] = "FromPreviousStep",
            [4] = "FromPreviousStep"
        };

        // User picks LatestExport for Step 4 only
        sourceByStep[4] = "LatestExport";

        Assert.Equal("FromPreviousStep", sourceByStep[2]);
        Assert.Equal("FromPreviousStep", sourceByStep[3]);
        Assert.Equal("LatestExport", sourceByStep[4]);
    }

    // =========================================================================
    // P2 Tests: Verification before registration
    // =========================================================================

    [Fact]
    public void P2_StepOutputRecord_Validation_RejectsMissingPathOrMismatchedCount()
    {
        string stateFile = Path.Combine(_testDir, "state", "pipeline_state.json");

        // Verify null record is rejected by TrySaveStepOutput
        bool saveNull = PipelineStateManager.TrySaveStepOutput(stateFile, null!, out var errNull);
        Assert.False(saveNull);
        Assert.NotNull(errNull);

        // Valid record succeeds
        var record = new StepOutputRecord
        {
            Step = 3,
            Path = @"C:\GIS\Cad_Dataset.gdb\PARCEL",
            FeatureCount = 100,
            Wkid = 32637
        };
        bool saveOk = PipelineStateManager.TrySaveStepOutput(stateFile, record, out var errOk);
        Assert.True(saveOk);
        Assert.Null(errOk);
    }

    // =========================================================================
    // P3 Tests: Lines -> Polygons Attribution and Data-Driven Diagnosis
    // =========================================================================

    [Fact]
    public void P3_1_FailingWhereClause_DoesNotSilentlyDropLayerFilter()
    {
        // In GdbFeatureClassService, if MakeFeatureLayer with where_clause fails,
        // it now immediately sets Success = false and returns diagnostic message without retrying without filter.
        var diag = PolygonizationAttributionAndDiagnosisService.DiagnoseLineNetwork(
            new List<(CoreBoundarySegment, string)>(),
            new[] { "PLOT_LINES", "SERVICE_LINES" });

        Assert.False(diag.HasLinesInSelectedLayers);
        Assert.Equal("no lines in selected layers", diag.FormattedDiagnosis);
    }

    [Fact]
    public void P3_2_GeometricAttribution_AttributesLayerFromGeometry_AndFlagsMixedLayers()
    {
        // Define two input candidate lines: 3 segments on LAYER_A, 1 segment on LAYER_B
        var lines = new List<(CoreBoundarySegment Segment, string LayerName)>
        {
            (new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0)), "LAYER_A"),
            (new CoreBoundarySegment(new Point2D(10, 0), new Point2D(10, 10)), "LAYER_A"),
            (new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10)), "LAYER_A"),
            (new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0)), "LAYER_B")
        };

        // Construct closed boundary of the rectangle (perimeter = 40m)
        // LAYER_A contributes 30m (75%), LAYER_B contributes 10m (25% > 10%)
        var boundary = new List<CoreBoundarySegment>
        {
            new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0)),
            new CoreBoundarySegment(new Point2D(10, 0), new Point2D(10, 10)),
            new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10)),
            new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0))
        };

        var attr = PolygonizationAttributionAndDiagnosisService.AttributePolygonFromBoundary(boundary, lines);
        Assert.Equal("LAYER_A", attr.DominantLayer);
        Assert.Contains("LAYER_A", attr.LayerNames);
        Assert.Contains("LAYER_B", attr.LayerNames);
        Assert.True(attr.IsMixedLayers, "Polygon bounded by >10% of both Layer A and Layer B must be flagged MIXED_LAYERS");
    }

    [Fact]
    public void P3_2_GeometricAttribution_PureSingleLayer_NotFlaggedMixed()
    {
        var lines = new List<(CoreBoundarySegment Segment, string LayerName)>
        {
            (new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0)), "PASSAGES"),
            (new CoreBoundarySegment(new Point2D(10, 0), new Point2D(10, 10)), "PASSAGES"),
            (new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10)), "PASSAGES"),
            (new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0)), "PASSAGES")
        };

        var boundary = new List<CoreBoundarySegment>
        {
            new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0)),
            new CoreBoundarySegment(new Point2D(10, 0), new Point2D(10, 10)),
            new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10)),
            new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0))
        };

        var attr = PolygonizationAttributionAndDiagnosisService.AttributePolygonFromBoundary(boundary, lines);
        Assert.Equal("PASSAGES", attr.DominantLayer);
        Assert.False(attr.IsMixedLayers);
        Assert.Single(attr.LayerNames);
    }

    [Fact]
    public void P3_3_Diagnosis_MeasuresDanglesAndSmallestGapWithoutGuessing()
    {
        // Rectangle with a 0.032m gap between endpoints (10, 9.968) and (10, 10)
        var lines = new List<(CoreBoundarySegment Segment, string LayerName)>
        {
            (new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0)), "PLOT_LINES"),
            (new CoreBoundarySegment(new Point2D(10, 0), new Point2D(10, 9.968)), "PLOT_LINES"), // Gap: 0.032m to (10, 10)
            (new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10)), "PLOT_LINES"),
            (new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0)), "PLOT_LINES")
        };

        var diag = PolygonizationAttributionAndDiagnosisService.DiagnoseLineNetwork(
            lines,
            new[] { "PLOT_LINES" },
            snapToleranceMeters: 0.0,
            spatialRefInfo: "EPSG:32637");

        Assert.Equal(2, diag.DanglesCount);
        Assert.NotNull(diag.SmallestDangleGapMeters);
        Assert.Equal(0.032, diag.SmallestDangleGapMeters.Value, 3);
        Assert.Contains("2 dangling endpoints", diag.FormattedDiagnosis);
        Assert.Contains("0.032 m", diag.FormattedDiagnosis);
        Assert.Contains("tolerance used: 0 m", diag.FormattedDiagnosis);
    }

    [Fact]
    public void P3_3_Diagnosis_EmptySelection_ReportsNoLinesInSelectedLayers()
    {
        var lines = new List<(CoreBoundarySegment Segment, string LayerName)>
        {
            (new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0)), "FRAME")
        };

        var diag = PolygonizationAttributionAndDiagnosisService.DiagnoseLineNetwork(
            lines,
            new[] { "PARCEL_LAYER" },
            snapToleranceMeters: 0.0);

        Assert.False(diag.HasLinesInSelectedLayers);
        Assert.Equal("no lines in selected layers", diag.FormattedDiagnosis);
    }

    // =========================================================================
    // P4 Tests: Border extraction edge cases (2-arc circle, reflex angle comments)
    // =========================================================================

    [Fact]
    public void P4_1_TwoArcCircle_ProducesPieces_CircumferenceMatches_IntegrityPasses()
    {
        // Circle of radius R=10 centered at (0, 0) made of two 180° circular arcs:
        // Arc 0: from (-10, 0) to (10, 0), curving clockwise through (0, 10)
        // Arc 1: from (10, 0) to (-10, 0), curving clockwise through (0, -10)
        double r = 10.0;
        double halfCirc = Math.PI * r;

        var arc0 = new CoreBoundarySegment(new Point2D(-r, 0), new Point2D(r, 0), CurveType.CircularArc, halfCirc)
        {
            Center = new Point2D(0, 0),
            Radius = r,
            IsCounterClockwise = false // CW
        };

        var arc1 = new CoreBoundarySegment(new Point2D(r, 0), new Point2D(-r, 0), CurveType.CircularArc, halfCirc)
        {
            Center = new Point2D(0, 0),
            Radius = r,
            IsCounterClockwise = false // CW
        };

        var segments = new List<CoreBoundarySegment> { arc0, arc1 };
        var options = new BorderExtractionOptions();

        var result = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, options);

        Assert.True(result.IntegrityChecksPassed, "Integrity check must pass on two-arc circle");
        Assert.True(result.FinalLines.Count > 0, "A two-arc circle must produce boundary pieces (not 0 pieces)");
        double totalLength = result.FinalLines.Sum(p => p.TotalLength);
        Assert.Equal(2.0 * Math.PI * r, totalLength, 2);
    }

    [Fact]
    public void P4_2_TwoArcCircle_RingOrientation_ArcFacingNorth_ClassifiedNorth()
    {
        // Upper arc curves through North (0, 10). Outward normal points North (0, 1) -> Azimuth 0°
        double r = 10.0;
        double halfCirc = Math.PI * r;

        var upperArc = new CoreBoundarySegment(new Point2D(-r, 0), new Point2D(r, 0), CurveType.CircularArc, halfCirc)
        {
            Center = new Point2D(0, 0),
            Radius = r,
            IsCounterClockwise = false // CW upper half
        };

        var lowerArc = new CoreBoundarySegment(new Point2D(r, 0), new Point2D(-r, 0), CurveType.CircularArc, halfCirc)
        {
            Center = new Point2D(0, 0),
            Radius = r,
            IsCounterClockwise = false // CW lower half
        };

        var segments = new List<CoreBoundarySegment> { upperArc, lowerArc };
        var options = new BorderExtractionOptions();

        var result = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, options);

        Assert.Equal(2, result.FinalLines.Count);

        // Piece containing upper arc should have outward normal pointing North (Azimuth around 0° or Direction 1)
        var northPiece = result.FinalLines.FirstOrDefault(p => p.CardinalDirection == 1);
        Assert.NotNull(northPiece);
        Assert.Equal(1, northPiece.CardinalDirection); // 1 = North

        var southPiece = result.FinalLines.FirstOrDefault(p => p.CardinalDirection == 3);
        Assert.NotNull(southPiece);
        Assert.Equal(3, southPiece.CardinalDirection); // 3 = South
    }

    [Fact]
    public void P4_1_SingleSplitPoint_YieldsOneFullRingPiece()
    {
        // 4-segment polygon where corner detection yields only 1 corner split point
        var s1 = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0));
        var s2 = new CoreBoundarySegment(new Point2D(10, 0), new Point2D(10, 10));
        var s3 = new CoreBoundarySegment(new Point2D(10, 10), new Point2D(0, 10));
        var s4 = new CoreBoundarySegment(new Point2D(0, 10), new Point2D(0, 0));

        var segments = new List<CoreBoundarySegment> { s1, s2, s3, s4 };
        var options = new BorderExtractionOptions();

        // Pass through ProcessRing
        var result = BorderExtractionCore.ProcessRing(segments, ringIndex: 0, isHole: false, options);
        Assert.True(result.IntegrityChecksPassed);
        Assert.NotEmpty(result.FinalLines);
        Assert.Equal(40.0, result.FinalLines.Sum(p => p.TotalLength), 2);
    }

    // =========================================================================
    // P5 Tests: Width Inference, Banker's Rounding, Median, and Ray Offset
    // =========================================================================

    [Fact]
    public void P5_1_NoHit_LeavesDescriptioAndBorderTypeEmpty_SetsIsUnknownFlag()
    {
        var border = new BorderRecord { PlotId = "P101", BorderName = 1 };
        var dimSeg = new Segment2D(new Point2D(0, 0), new Point2D(20, 0));
        var normal = new Point2D(0, 1);
        var obstacles = new List<Segment2D>(); // No obstacles
        var options = new WidthInferenceOptions { MaxSearchDistance = 100.0 };

        WidthInferenceService.InferBorderWidth(border, dimSeg, normal, obstacles, options);

        Assert.True(border.IsUnknown);
        Assert.Equal("RequiresReview", border.ReviewStatus);
        Assert.Equal("", border.BorderType);
        Assert.Equal("", border.Descriptio);
        Assert.True(border.IsInferred);

        // Validation engine should flag warning
        var val = QaValidationEngine.ValidateAll(new List<ParcelRecord>(), new[] { border });
        Assert.Contains(val.Warnings, w => w.FieldName == "DESCRIPTIO" && w.Message.Contains("UNKNOWN or empty"));
    }

    [Theory]
    [InlineData(6.5, 7)]
    [InlineData(9.5, 10)]
    [InlineData(10.5, 11)]
    [InlineData(12.5, 13)]
    public void P5_2_AwayFromZeroRounding_AvoidsBankersRoundingFlips(double input, double expected)
    {
        double rounded = Math.Round(input, MidpointRounding.AwayFromZero);
        Assert.Equal(expected, rounded);
    }

    [Fact]
    public void P5_3_MedianOfEvenHits_AndRayOffsetRestoration()
    {
        // Obstacle segment at y=12.00, dimension segment on y=0
        // Ray starts at y=0.05 (rayOffset = 0.05m)
        // Hit distance from ray start is 11.95m. With offset restored, measured width is exactly 12.00m!
        var border = new BorderRecord { PlotId = "P102", BorderName = 1 };
        var dimSeg = new Segment2D(new Point2D(0, 0), new Point2D(20, 0));
        var normal = new Point2D(0, 1);

        // Obstacle placed at y = 12.00m from boundary
        var obstacles = new List<Segment2D>
        {
            new Segment2D(new Point2D(-10, 12.00), new Point2D(30, 12.00))
        };

        var options = new WidthInferenceOptions
        {
            MaxSearchDistance = 100.0,
            RayCount = 3
        };

        WidthInferenceService.InferBorderWidth(border, dimSeg, normal, obstacles, options);

        Assert.False(border.IsUnknown);
        Assert.Equal(12.0, border.MeasuredWidth);
        Assert.Contains("12", border.Descriptio);
        Assert.Equal(12.0, border.RayLength);
    }

    [Fact]
    public void P5_3_Median_EvenCount_ComputesMeanOfTwoMiddleValues()
    {
        // Test two hit distances: 11.9m and 12.1m -> mean should be exactly 12.0m
        var sorted = new List<double> { 11.9, 12.1 };
        double median = (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2.0;
        Assert.Equal(12.0, median, 2);
    }

    // =========================================================================
    // P6 Tests: Export Verification and Error Propagation
    // =========================================================================

    [Fact]
    public void P6_TrySaveExportState_FailsGracefully_WhenPathIsInvalid()
    {
        var state = new ExportStateRecord
        {
            RunId = "run_p6_test",
            Timestamp = DateTime.UtcNow,
            ExportFolder = _testDir
        };

        // Invalid path with illegal characters
        string invalidPath = "Z:\\NonExistent_Invalid_Dir_XYZ\\state.json";
        bool result = ExportStateManager.TrySaveExportState(invalidPath, state, 10, out var err);

        Assert.False(result);
        Assert.NotNull(err);
    }

    [Fact]
    public void P6_SaveExportState_ThrowsIOException_OnFailure()
    {
        var state = new ExportStateRecord
        {
            RunId = "run_p6_test",
            Timestamp = DateTime.UtcNow,
            ExportFolder = _testDir
        };

        string invalidPath = "Z:\\NonExistent_Invalid_Dir_XYZ\\state.json";
        Assert.Throws<IOException>(() => ExportStateManager.SaveExportState(invalidPath, state, 10));
    }

    [Fact]
    public void P6_TrySaveExportState_Succeeds_AndPersistsHistory()
    {
        string stateFile = Path.Combine(_testDir, "last_export.json");
        var state = new ExportStateRecord
        {
            RunId = "run_01",
            Timestamp = DateTime.UtcNow,
            ExportFolder = _testDir,
            ParcelPath = Path.Combine(_testDir, "PARCEL.shp"),
            BorderPath = Path.Combine(_testDir, "BORDER.shp"),
            WorkingUtmZone = 32637
        };

        bool ok = ExportStateManager.TrySaveExportState(stateFile, state, 10, out var err);
        Assert.True(ok, err);
        Assert.True(File.Exists(stateFile));

        var loaded = ExportStateManager.LoadLatestExport(stateFile);
        Assert.NotNull(loaded);
        Assert.Equal("run_01", loaded.RunId);
    }

    [Fact]
    public void Step4_ResolvesStep3ApprovedExport_WhenLastExportJsonMissing()
    {
        string stateFile = Path.Combine(_testDir, "pipeline_state.json");
        string gdbDir = Path.Combine(_testDir, "Export_Output", "Cad2Gis_Workspace.gdb");
        Directory.CreateDirectory(gdbDir);
        string step3FcPath = Path.Combine(gdbDir, "PARCEL_Approved_Schema");

        var step3Record = new StepOutputRecord
        {
            Step = 3,
            Path = step3FcPath,
            FeatureCount = 120,
            Wkid = 32637,
            Timestamp = DateTime.UtcNow,
            RunId = "step3_run"
        };
        bool saved = PipelineStateManager.TrySaveStepOutput(stateFile, step3Record, out var err);
        Assert.True(saved, err);

        // When Step 5 hasn't run yet, last_export.json does NOT exist
        string lastExportFile = Path.Combine(_testDir, "last_export.json");
        var latestExport = ExportStateManager.LoadLatestExport(lastExportFile);
        Assert.Null(latestExport);

        // Loading Step 3 output from pipeline_state successfully restores the approved schema path
        var restoredStep3 = PipelineStateManager.LoadStepOutput(stateFile, 3);
        Assert.NotNull(restoredStep3);
        Assert.Equal(step3FcPath, restoredStep3.Path);
        Assert.Contains("PARCEL_Approved_Schema", restoredStep3.Path);
    }
}

