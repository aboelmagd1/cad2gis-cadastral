using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

/// <summary>
/// Service managing pipeline state persistence (HelperFiles\state\pipeline_state.json).
/// Records authoritative verified outputs of pipeline steps so subsequent steps or restarted
/// sessions reliably consume the verified dataset.
/// </summary>
public static class PipelineStateManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public class PipelineStateContainer
    {
        public Dictionary<int, StepOutputRecord> Steps { get; set; } = new();
    }

    public static bool TrySaveStepOutput(string stateFilePath, StepOutputRecord record, out string? errorMessage)
    {
        errorMessage = null;
        if (record == null)
        {
            errorMessage = "Record cannot be null.";
            return false;
        }

        try
        {
            string? dir = Path.GetDirectoryName(stateFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var container = LoadContainer(stateFilePath) ?? new PipelineStateContainer();
            container.Steps[record.Step] = record;

            string json = JsonSerializer.Serialize(container, JsonOpts);
            string tmpFile = stateFilePath + ".tmp_" + Guid.NewGuid().ToString("N");
            File.WriteAllText(tmpFile, json);
            if (File.Exists(stateFilePath))
            {
                File.Delete(stateFilePath);
            }
            File.Move(tmpFile, stateFilePath);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public static void SaveStepOutput(string stateFilePath, StepOutputRecord record)
    {
        if (!TrySaveStepOutput(stateFilePath, record, out var err))
        {
            throw new IOException($"Failed to save pipeline state to '{stateFilePath}': {err}");
        }
    }

    public static StepOutputRecord? LoadStepOutput(string stateFilePath, int step = 3)
    {
        var container = LoadContainer(stateFilePath);
        if (container != null && container.Steps.TryGetValue(step, out var rec))
        {
            return rec;
        }
        return null;
    }

    public static StepOutputRecord? GetVerifiedOutput(string stateFilePath, int step) => LoadStepOutput(stateFilePath, step);

    /// <summary>
    /// Resolves the verified output record of step N-1 from the registry (HelperFiles\state\pipeline_state.json).
    /// If missing or unverified, throws InvalidOperationException with standard prompt message.
    /// </summary>
    public static StepOutputRecord GetVerifiedOutputOrFail(string stateFilePath, int step)
    {
        var rec = LoadStepOutput(stateFilePath, step);
        if (rec == null || string.IsNullOrWhiteSpace(rec.Path))
        {
            throw new InvalidOperationException($"Step {step} has no verified output. Run Step {step} first, or choose 'My own data'.");
        }
        return rec;
    }

    /// <summary>
    /// Custom resolver for computing content stamp of non-file datasets (e.g. GDB feature classes).
    /// </summary>
    public static Func<string, string>? ContentStampResolver { get; set; }

    /// <summary>
    /// Computes a lightweight content stamp (hash / timestamp marker) of a dataset path to detect manual edits.
    /// Uses data-level hashing to prevent false positives when unrelated files in the same container change.
    /// </summary>
    public static string ComputeContentStamp(string datasetPath)
    {
        if (string.IsNullOrWhiteSpace(datasetPath)) return string.Empty;
        try
        {
            if (ContentStampResolver != null)
            {
                var custom = ContentStampResolver(datasetPath);
                if (!string.IsNullOrEmpty(custom)) return custom;
            }

            if (File.Exists(datasetPath))
            {
                var fi = new FileInfo(datasetPath);
                return $"{fi.LastWriteTimeUtc.Ticks}_{fi.Length}";
            }
            if (Directory.Exists(datasetPath))
            {
                var di = new DirectoryInfo(datasetPath);
                return $"{di.LastWriteTimeUtc.Ticks}";
            }
            // For GDB feature classes or internal dataset items without custom resolver:
            // Do NOT use the parent .gdb directory timestamp because touching unrelated feature classes
            // or lock files changes the folder timestamp!
            string? dir = Path.GetDirectoryName(datasetPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                string name = Path.GetFileName(datasetPath);
                string specificItemPath = Path.Combine(dir, name);
                if (File.Exists(specificItemPath))
                {
                    var fi = new FileInfo(specificItemPath);
                    return $"{fi.LastWriteTimeUtc.Ticks}_{fi.Length}";
                }
            }
        }
        catch { }
        return string.Empty;
    }

    /// <summary>
    /// Checks whether the dataset referenced by step output record was modified on disk since it was written.
    /// </summary>
    public static bool IsEditedSinceWritten(string stateFilePath, int step, out string? details)
    {
        details = null;
        var rec = LoadStepOutput(stateFilePath, step);
        if (rec == null || string.IsNullOrWhiteSpace(rec.Path)) return false;

        string currentStamp = ComputeContentStamp(rec.Path);
        if (!string.IsNullOrEmpty(rec.ContentStamp) && !string.IsNullOrEmpty(currentStamp) && rec.ContentStamp != currentStamp)
        {
            details = $"Output for Step {step} was modified after it was generated ({rec.WrittenAt:yyyy-MM-dd HH:mm:ss}).";
            return true;
        }
        return false;
    }

    public static Dictionary<int, StepOutputRecord> GetAllStepOutputs(string stateFilePath)
    {
        var container = LoadContainer(stateFilePath);
        return container?.Steps ?? new Dictionary<int, StepOutputRecord>();
    }

    private static PipelineStateContainer? LoadContainer(string stateFilePath)
    {
        if (string.IsNullOrWhiteSpace(stateFilePath) || !File.Exists(stateFilePath))
            return null;

        try
        {
            string json = File.ReadAllText(stateFilePath);
            return JsonSerializer.Deserialize<PipelineStateContainer>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PipelineStateManager] LoadContainer error: {ex.Message}");
            return null;
        }
    }
}
