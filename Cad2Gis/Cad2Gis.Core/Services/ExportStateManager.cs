using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

/// <summary>
/// Service managing the state file (HelperFiles\state\last_export.json) and diff detection
/// for Part B: "Continue from the latest export (keep my manual edits)".
/// </summary>
public static class ExportStateManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Writes or updates last_export.json with the new export record, preserving a history of the last N exports.
    /// Returns true on success, or false with an errorMessage if saving fails.
    /// </summary>
    public static bool TrySaveExportState(string stateFilePath, ExportStateRecord newState, int maxHistoryCount, out string? errorMessage)
    {
        errorMessage = null;
        if (newState == null)
        {
            errorMessage = "New state record cannot be null.";
            return false;
        }

        try
        {
            string? dir = Path.GetDirectoryName(stateFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Load existing state to extract history
            var existing = LoadLatestExport(stateFilePath);
            var history = new List<ExportHistoryItem>();

            if (existing != null)
            {
                // Add existing latest export as the first history item
                history.Add(new ExportHistoryItem
                {
                    RunId = existing.RunId,
                    Timestamp = existing.Timestamp,
                    ExportFolder = existing.ExportFolder,
                    ParcelPath = existing.ParcelPath,
                    BorderPath = existing.BorderPath,
                    SourceCadName = existing.SourceCadName,
                    ProfileName = existing.ProfileName,
                    WorkingUtmZone = existing.WorkingUtmZone
                });

                if (existing.History != null)
                {
                    history.AddRange(existing.History);
                }
            }

            // Cap history to maxHistoryCount
            newState.History = history.Take(Math.Max(1, maxHistoryCount)).ToList();

            string json = JsonSerializer.Serialize(newState, JsonOpts);
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
            System.Diagnostics.Debug.WriteLine($"[ExportStateManager] TrySaveExportState failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes or updates last_export.json with the new export record, preserving a history of the last N exports.
    /// Throws an IOException if saving fails.
    /// </summary>
    public static void SaveExportState(string stateFilePath, ExportStateRecord newState, int maxHistoryCount = 10)
    {
        if (!TrySaveExportState(stateFilePath, newState, maxHistoryCount, out var err))
        {
            throw new IOException($"Failed saving export state to '{stateFilePath}': {err}");
        }
    }

    /// <summary>
    /// Loads the latest export state record from last_export.json.
    /// </summary>
    public static ExportStateRecord? LoadLatestExport(string stateFilePath)
    {
        if (string.IsNullOrWhiteSpace(stateFilePath) || !File.Exists(stateFilePath))
            return null;

        try
        {
            string json = File.ReadAllText(stateFilePath);
            return JsonSerializer.Deserialize<ExportStateRecord>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExportStateManager] LoadLatestExport failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Computes SHA-256 hash of a file safely, reading with shared access if locked.
    /// </summary>
    public static string ComputeFileSha256(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;

        try
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Compares current parcels and borders against the stored state record.
    /// Returns a structured diff report (added, deleted, geometry changed, attributes changed).
    /// </summary>
    public static ExportDiffReport DetectEdits(
        ExportStateRecord stateRecord,
        IReadOnlyList<ParcelRecord> currentParcels,
        IReadOnlyList<BorderRecord> currentBorders,
        double areaToleranceSqm = 0.5,
        double lengthToleranceMeters = 0.05)
    {
        var report = new ExportDiffReport
        {
            StatePath = stateRecord.ExportFolder,
            ExportTimestamp = stateRecord.Timestamp
        };

        // 1. Detect Parcel Changes
        if (stateRecord.Datasets.TryGetValue("PARCEL", out var parcelDs) && parcelDs.Features.Count > 0)
        {
            var storedParcels = parcelDs.Features.ToDictionary(f => f.PlotId, StringComparer.OrdinalIgnoreCase);
            var currentPlotIds = new HashSet<string>(currentParcels.Select(p => p.PlotId), StringComparer.OrdinalIgnoreCase);

            // Added
            foreach (var p in currentParcels)
            {
                if (!storedParcels.ContainsKey(p.PlotId))
                {
                    report.AddedFeatures.Add($"Parcel {p.PlotId}");
                    report.WasEditedAfterExport = true;
                }
            }

            // Deleted
            foreach (var sp in storedParcels.Keys)
            {
                if (!currentPlotIds.Contains(sp))
                {
                    report.DeletedFeatures.Add($"Parcel {sp}");
                    report.WasEditedAfterExport = true;
                }
            }

            // Modified geometry and attributes
            foreach (var p in currentParcels)
            {
                if (!storedParcels.TryGetValue(p.PlotId, out var sp)) continue;

                // Check geometry metric differences
                double storedArea = sp.AreaSqm ?? 0;
                double storedLen = sp.ShapeLength ?? 0;

                double dArea = Math.Abs(p.AreaSqm - storedArea);
                double dLen = Math.Abs(p.ShapeLength - storedLen);

                if (dArea > areaToleranceSqm || dLen > lengthToleranceMeters)
                {
                    report.GeometryChanges.Add(new FeatureGeometryDiff
                    {
                        Key = $"Parcel {p.PlotId}",
                        StoredArea = storedArea,
                        NewArea = p.AreaSqm,
                        StoredPerimeter = storedLen,
                        NewPerimeter = p.ShapeLength,
                        ExceedsTolerance = true
                    });
                    report.WasEditedAfterExport = true;
                }

                // Check manual attribute changes
                CheckAttrChange(report, $"Parcel {p.PlotId}", "USAGE", sp.Usage, p.Usage);
                CheckAttrChange(report, $"Parcel {p.PlotId}", "ADDRESS", sp.Address, p.Address);
                CheckAttrChange(report, $"Parcel {p.PlotId}", "NEIGHBOURH", sp.Neighbourhood, p.Neighbourhood);
                CheckAttrChange(report, $"Parcel {p.PlotId}", "REMARKS", sp.Remarks, p.Remarks);
            }
        }

        // 2. Detect Border Changes
        if (stateRecord.Datasets.TryGetValue("Border", out var borderDs) && borderDs.Features.Count > 0)
        {
            string MakeKey(string plotId, int? dir, int ring) => $"{plotId}_Dir{dir ?? 0}_Ring{ring}";

            var storedBorders = borderDs.Features.ToDictionary(f => MakeKey(f.PlotId, f.BorderName, f.RingIndex), StringComparer.OrdinalIgnoreCase);
            var currentBorderKeys = new HashSet<string>(
                currentBorders.Select(b => MakeKey(b.PlotId, b.BorderName, b.RingIndex)), StringComparer.OrdinalIgnoreCase);

            // Added borders
            foreach (var b in currentBorders)
            {
                string key = MakeKey(b.PlotId, b.BorderName, b.RingIndex);
                if (!storedBorders.ContainsKey(key))
                {
                    report.AddedFeatures.Add($"Border {b.PlotId} ({b.BorderNameEnglish})");
                    report.WasEditedAfterExport = true;
                }
            }

            // Deleted borders
            foreach (var sbKey in storedBorders.Keys)
            {
                if (!currentBorderKeys.Contains(sbKey))
                {
                    var sb = storedBorders[sbKey];
                    report.DeletedFeatures.Add($"Border {sb.PlotId} (Dir {sb.BorderName})");
                    report.WasEditedAfterExport = true;
                }
            }

            // Modified border attributes and geometry
            foreach (var b in currentBorders)
            {
                string key = MakeKey(b.PlotId, b.BorderName, b.RingIndex);
                if (!storedBorders.TryGetValue(key, out var sb)) continue;

                double storedLen = sb.ShapeLength ?? 0;
                if (Math.Abs(b.ShapeLength - storedLen) > lengthToleranceMeters)
                {
                    report.GeometryChanges.Add(new FeatureGeometryDiff
                    {
                        Key = $"Border {b.PlotId} ({b.BorderNameEnglish})",
                        StoredPerimeter = storedLen,
                        NewPerimeter = b.ShapeLength,
                        ExceedsTolerance = true
                    });
                    report.WasEditedAfterExport = true;
                }

                // Attribute edits
                CheckAttrChange(report, $"Border {b.PlotId} ({b.BorderNameEnglish})", "BORDERTYPE", sb.BorderType, b.BorderType);
                CheckAttrChange(report, $"Border {b.PlotId} ({b.BorderNameEnglish})", "DESCRIPTIO", sb.Descriptio, b.Descriptio);
            }
        }

        return report;
    }

    private static void CheckAttrChange(ExportDiffReport report, string key, string field, string? oldVal, string? newVal)
    {
        string o = oldVal?.Trim() ?? string.Empty;
        string n = newVal?.Trim() ?? string.Empty;

        if (!string.Equals(o, n, StringComparison.OrdinalIgnoreCase))
        {
            report.AttributeChanges.Add(new FeatureAttributeDiff
            {
                Key = key,
                FieldName = field,
                OldValue = o,
                NewValue = n
            });
            report.WasEditedAfterExport = true;
        }
    }

    /// <summary>
    /// If a file is locked by another process, copies it to a temporary location to allow read-only access.
    /// </summary>
    public static (string FileToRead, bool WasCopiedDueToLock, string? NoticeMessage) ResolveReadableCopy(string originalPath, string scratchDir)
    {
        if (!File.Exists(originalPath))
            return (originalPath, false, null);

        try
        {
            // Test exclusive read access
            using var fs = new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.None);
            return (originalPath, false, null);
        }
        catch (IOException)
        {
            // File is locked! Create a read-only copy in scratch directory
            try
            {
                Directory.CreateDirectory(scratchDir);
                string tempCopy = Path.Combine(scratchDir, $"locked_copy_{Guid.NewGuid():N}_{Path.GetFileName(originalPath)}");
                File.Copy(originalPath, tempCopy, true);
                return (tempCopy, true, $"الملف '{Path.GetFileName(originalPath)}' كان قيد الاستخدام في برنامج آخر. تم نسخ نسخة مؤقتة للقراءة فقط.");
            }
            catch (Exception ex)
            {
                return (originalPath, false, $"تعذر نسخ الملف المقفل: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Checks if a dataset file has changed in size, modification time, or SHA-256 hash compared to stored state.
    /// </summary>
    public static bool HasPhysicalDatasetChanged(string filePath, ExportDatasetInfo baseline)
    {
        if (!File.Exists(filePath)) return true;
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Length != baseline.FileSize) return true;
            if (Math.Abs((fi.LastWriteTimeUtc - baseline.LastModifiedUtc).TotalSeconds) > 2.0)
            {
                string curHash = ComputeFileSha256(filePath);
                if (!string.Equals(curHash, baseline.FileHash, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
