using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cad2Gis.Core.Services;

public static class ExportNamingService
{
    public static string SanitizeSegment(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(s.Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray()).Trim();
        return cleaned.Replace(' ', '_');
    }

    public static string BuildExportFolderName(string? planNo, string? cityName, string? districtName, DateTime? exportDate = null)
    {
        var parts = new List<string>();

        string cleanPlan = SanitizeSegment(planNo);
        if (!string.IsNullOrWhiteSpace(cleanPlan) && cleanPlan != "0000" && cleanPlan != "0000_0")
        {
            if (!cleanPlan.StartsWith("Plan", StringComparison.OrdinalIgnoreCase) &&
                !cleanPlan.StartsWith("مخطط", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"Plan_{cleanPlan}");
            }
            else
            {
                parts.Add(cleanPlan);
            }
        }
        else
        {
            parts.Add("Plan");
        }

        string cleanCity = SanitizeSegment(cityName);
        if (!string.IsNullOrWhiteSpace(cleanCity) && cleanCity != "000")
        {
            parts.Add(cleanCity);
        }

        string cleanDistrict = SanitizeSegment(districtName);
        if (!string.IsNullOrWhiteSpace(cleanDistrict) && cleanDistrict != "000")
        {
            parts.Add(cleanDistrict);
        }

        DateTime date = exportDate ?? DateTime.Now;
        parts.Add(date.ToString("yyyyMMdd"));

        return string.Join("_", parts);
    }

    public static string ResolveUniqueExportFolder(string baseOutputDir, string? planNo, string? cityName, string? districtName, DateTime? exportDate = null)
    {
        string cleanBase = string.IsNullOrWhiteSpace(baseOutputDir) ? Directory.GetCurrentDirectory() : baseOutputDir;
        Directory.CreateDirectory(cleanBase);

        string baseFolderName = BuildExportFolderName(planNo, cityName, districtName, exportDate);
        string candidatePath = Path.Combine(cleanBase, baseFolderName);

        // If folder does not exist, create and return it
        if (!Directory.Exists(candidatePath))
        {
            Directory.CreateDirectory(candidatePath);
            return candidatePath;
        }

        // If directory exists and has no entries, reuse it
        if (Directory.GetFileSystemEntries(candidatePath).Length == 0)
        {
            return candidatePath;
        }

        // If data already exists in this folder, append incrementing number (_1, _2, ...)
        int index = 1;
        while (true)
        {
            string numberedFolder = Path.Combine(cleanBase, $"{baseFolderName}_{index}");
            if (!Directory.Exists(numberedFolder) || Directory.GetFileSystemEntries(numberedFolder).Length == 0)
            {
                Directory.CreateDirectory(numberedFolder);
                return numberedFolder;
            }
            index++;
        }
    }
}
