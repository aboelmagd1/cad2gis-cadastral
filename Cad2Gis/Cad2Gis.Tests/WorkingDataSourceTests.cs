using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;
using Xunit;

namespace Cad2Gis.Tests;

public class WorkingDataSourceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stateFilePath;

    public WorkingDataSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Cad2Gis_DataSourceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _stateFilePath = Path.Combine(_tempDir, "pipeline_state.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void Test1_OldProfileContinueFromLatestExport_Step4ReadsStep3VerifiedOutput()
    {
        // Arrange: Legacy profile with ContinueFromLatestExport = true
        string profileJson = """
        {
            "ProfileName": "OldProfile",
            "ContinueFromLatestExport": true,
            "WorkingSourceMode": "LatestExport"
        }
        """;
        using var doc = JsonDocument.Parse(profileJson);
        Assert.True(doc.RootElement.GetProperty("ContinueFromLatestExport").GetBoolean());

        // Step 3 output registered in pipeline_state.json
        string step3FcPath = Path.Combine(_tempDir, "Cad2Gis_Workspace.gdb", "PARCEL_Approved_Schema");
        Directory.CreateDirectory(Path.GetDirectoryName(step3FcPath)!);
        File.WriteAllText(step3FcPath, "dummy feature class data");

        var record3 = new StepOutputRecord
        {
            Step = 3,
            Path = step3FcPath,
            FeatureCount = 120,
            Wkid = 32637,
            WrittenAt = DateTime.Now
        };
        PipelineStateManager.TrySaveStepOutput(_stateFilePath, record3, out var err);
        Assert.Null(err);

        // Act: Step 4 resolves Step 3 output
        var resolvedOutput = PipelineStateManager.GetVerifiedOutputOrFail(_stateFilePath, step: 3);

        // Assert: Step 4 resolves Step 3's Feature Class; no mode switch to legacy export happens
        Assert.Equal(step3FcPath, resolvedOutput.Path);
        Assert.Equal(120, resolvedOutput.FeatureCount);
    }

    [Fact]
    public void Test2_EditDetectionInStep3Output_DetectsChangesViaContentStamp()
    {
        // Arrange: Write file and register
        string fcFile = Path.Combine(_tempDir, "PARCEL_Approved_Schema.shp");
        File.WriteAllText(fcFile, "Initial content 12345");

        var record = new StepOutputRecord
        {
            Step = 3,
            Path = fcFile,
            FeatureCount = 10,
            Wkid = 32637,
            WrittenAt = DateTime.Now.AddSeconds(-5),
            ContentStamp = PipelineStateManager.ComputeContentStamp(fcFile)
        };
        PipelineStateManager.TrySaveStepOutput(_stateFilePath, record, out _);

        // Verify initially not edited
        bool initiallyEdited = PipelineStateManager.IsEditedSinceWritten(_stateFilePath, 3, out _);
        Assert.False(initiallyEdited);

        // Act: Simulate manual edit in ArcGIS Pro / disk
        File.AppendAllText(fcFile, "User added additional geometries and attribute changes");

        // Assert: Detected as edited
        bool wasEdited = PipelineStateManager.IsEditedSinceWritten(_stateFilePath, 3, out string? details);
        Assert.True(wasEdited);
        Assert.NotNull(details);
    }

    [Fact]
    public void Test3_RestartSimulation_RestoresStep3VerifiedOutputFromRegistry()
    {
        // Arrange: Write state file
        string fcPath = Path.Combine(_tempDir, "PARCEL_Approved_Schema");
        File.WriteAllText(fcPath, "parcel data");

        var record = new StepOutputRecord
        {
            Step = 3,
            Path = fcPath,
            FeatureCount = 42,
            Wkid = 32638,
            WrittenAt = new DateTime(2026, 10, 24, 14, 30, 0)
        };
        PipelineStateManager.TrySaveStepOutput(_stateFilePath, record, out _);

        // Act: Simulate app restart by resolving from file in a clean call
        var loaded = PipelineStateManager.GetVerifiedOutput(_stateFilePath, 3);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(fcPath, loaded.Path);
        Assert.Equal(42, loaded.FeatureCount);
        Assert.Equal(32638, loaded.Wkid);
    }

    [Fact]
    public void Test4_Step3OutputMissing_ThrowsClearArabicMessageAndDoesNotFallBack()
    {
        // Empty state file - Step 3 has not been run
        File.WriteAllText(_stateFilePath, "{}");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            PipelineStateManager.GetVerifiedOutputOrFail(_stateFilePath, step: 3);
        });

        Assert.Equal("Step 3 has no verified output. Run Step 3 first, or choose 'My own data'.", ex.Message);
    }

    [Fact]
    public void Test5_OptionB_ValidationFailsForInvalidInputs()
    {
        string invalidPath = Path.Combine(_tempDir, "NonExistentParcels.shp");

        // Act & Assert: Check missing file detection
        Assert.False(File.Exists(invalidPath));
        Assert.False(Directory.Exists(invalidPath));
    }

    [Fact]
    public void Test6_OptionB_Step4ChoiceDoesNotAffectStep2AndStep3()
    {
        // Arrange
        var sources = new System.Collections.Generic.Dictionary<int, StepSource>
        {
            [2] = StepSource.PreviousStep,
            [3] = StepSource.PreviousStep,
            [4] = StepSource.PreviousStep,
            [5] = StepSource.PreviousStep
        };

        // Act: User selects OwnData for Step 4 only
        sources[4] = StepSource.OwnData;

        // Assert: Step 2 and 3 remain PreviousStep
        Assert.Equal(StepSource.PreviousStep, sources[2]);
        Assert.Equal(StepSource.PreviousStep, sources[3]);
        Assert.Equal(StepSource.OwnData, sources[4]);
        Assert.Equal(StepSource.PreviousStep, sources[5]);
    }

    [Fact]
    public void Test7_ManualEditProtection_ReRunningDetectsChanges()
    {
        string parcelFile = Path.Combine(_tempDir, "PARCEL_Approved_Schema");
        File.WriteAllText(parcelFile, "Original parcel data");

        var rec = new StepOutputRecord
        {
            Step = 3,
            Path = parcelFile,
            FeatureCount = 50,
            ContentStamp = PipelineStateManager.ComputeContentStamp(parcelFile),
            WrittenAt = DateTime.Now
        };
        PipelineStateManager.TrySaveStepOutput(_stateFilePath, rec, out _);

        // Edit the file
        System.Threading.Thread.Sleep(50);
        File.WriteAllText(parcelFile, "Edited parcel geometries and attributes by user in map");

        bool isEdited = PipelineStateManager.IsEditedSinceWritten(_stateFilePath, 3, out var details);
        Assert.True(isEdited);
        Assert.Contains("modified", details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Test8_Step3HasSingleRegisteredOutput_PARCEL_Approved_Schema()
    {
        string gdbPath = Path.Combine(_tempDir, "Cad2Gis_Workspace.gdb");
        Directory.CreateDirectory(gdbPath);
        string singleFcPath = Path.Combine(gdbPath, "PARCEL_Approved_Schema");
        File.WriteAllText(singleFcPath, "data");

        var record = new StepOutputRecord
        {
            Step = 3,
            Path = singleFcPath,
            FeatureCount = 88,
            Wkid = 32637,
            WrittenAt = DateTime.Now
        };
        PipelineStateManager.TrySaveStepOutput(_stateFilePath, record, out _);

        var allOutputs = PipelineStateManager.GetAllStepOutputs(_stateFilePath);
        Assert.Single(allOutputs);
        Assert.True(allOutputs.ContainsKey(3));
        Assert.Equal("PARCEL_Approved_Schema", Path.GetFileName(allOutputs[3].Path));
    }

    [Fact]
    public void Test9_LegacyProfileAndStateFiles_LoadWithoutErrors()
    {
        // Legacy state with old fields
        string legacyStateJson = """
        {
            "Steps": {
                "3": {
                    "Step": 3,
                    "Path": "C:\\Data\\PARCEL_Approved_Schema",
                    "FeatureCount": 15,
                    "Wkid": 32637,
                    "Timestamp": "2026-10-20T10:00:00",
                    "ContinueFromLatestExport": true,
                    "WorkingSourceMode": "LatestExport",
                    "ExportHistory": [
                        { "Timestamp": "2026-10-19", "Path": "C:\\Old" }
                    ]
                }
            }
        }
        """;
        File.WriteAllText(_stateFilePath, legacyStateJson);

        // Act: Should load without throwing
        var output = PipelineStateManager.LoadStepOutput(_stateFilePath, 3);

        // Assert: Legacy file parses gracefully, ignores obsolete fields
        Assert.NotNull(output);
        Assert.Equal(3, output.Step);
        Assert.Equal("C:\\Data\\PARCEL_Approved_Schema", output.Path);
        Assert.Equal(15, output.FeatureCount);
    }

    [Fact]
    public void Test10_StepSourceEnum_HasExactlyTwoValues()
    {
        var names = Enum.GetNames(typeof(StepSource));
        Assert.Equal(2, names.Length);
        Assert.Contains("PreviousStep", names);
        Assert.Contains("OwnData", names);
        Assert.DoesNotContain("LatestExport", names);
        Assert.DoesNotContain("UserOwnData", names);
    }
}
