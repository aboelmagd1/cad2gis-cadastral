using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Cad2Gis.Core.IO;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

public class ExpectedLayerRule
{
    public string Profile { get; set; } = string.Empty;
    public string LayerName { get; set; } = string.Empty;
    public string Role { get; set; } = "Main parcel"; // Main parcel, Sub-parcel, Plot-number text, Usage text, Road, Other
    public string MatchType { get; set; } = "exact"; // exact, wildcard, regex
    public string Notes { get; set; } = string.Empty;
}

public class LayerMatchResult
{
    public string CadLayerName { get; set; } = string.Empty;
    public int EntityCount { get; set; }
    public string AssignedRole { get; set; } = "Other";
    public bool IsMatchedFromExcel { get; set; }
    public bool IsNew { get; set; }
    public string MatchedRuleProfile { get; set; } = string.Empty;
}

public class ExpectedLayersReport
{
    public string BestMatchingProfile { get; set; } = string.Empty;
    public List<LayerMatchResult> CadLayersMatched { get; set; } = new();
    public List<ExpectedLayerRule> ExpectedLayersMissingFromCad { get; set; } = new();
    public List<string> CadLayersNotInExcel { get; set; } = new();
}

public static class ExpectedLayersService
{
    /// <summary>
    /// Parses Excel/MOMRAH role string into strongly typed LayerRole.
    /// Handles forms like "Main parcel", "Sub-parcel", "Plot-number text", "Usage text", "Dimensions", "Road", "Water Channel", "Other"
    /// as well as Arabic equivalents.
    /// </summary>
    public static LayerRole ParseRole(string roleStr)
    {
        if (string.IsNullOrWhiteSpace(roleStr)) return LayerRole.Ignore;
        string r = roleStr.Trim().ToLowerInvariant();
        if (r.Contains("main") || r.Contains("parcel") || r.Contains("boundary") || r.Contains("رئيسي") || r.Contains("قطع"))
            return LayerRole.Boundary;
        if (r.Contains("sub") || r.Contains("service") || r.Contains("مرفق") || r.Contains("خدم") || r.Contains("حدائق") || r.Contains("مساجد"))
            return LayerRole.Service;
        if (r.Contains("plot") || r.Contains("number") || r.Contains("ارقام") || r.Contains("رقم") || r.Contains("ترقيم"))
            return LayerRole.PlotText;
        if (r.Contains("usage") || r.Contains("use") || r.Contains("استعمال") || r.Contains("نشاط"))
            return LayerRole.UsageText;
        if (r.Contains("dim") || r.Contains("dimension") || r.Contains("ابعاد") || r.Contains("أبعاد") || r.Contains("شطف"))
            return LayerRole.Dimensions;
        if (r.Contains("road") || r.Contains("street") || r.Contains("طريق") || r.Contains("شوارع") || r.Contains("ممر"))
            return LayerRole.Street;
        if (r.Contains("water") || r.Contains("canal") || r.Contains("قناة") || r.Contains("مجرى"))
            return LayerRole.WaterChannel;
        return LayerRole.Ignore;
    }

    /// <summary>
    /// Reads Expected_Layers.xlsx rules and converts them into CadLayerInfo items for previewing or matching.
    /// </summary>
    public static List<CadLayerInfo> LoadLayersFromExpectedExcel(string excelPath, string profileName = "Default")
    {
        var list = new List<CadLayerInfo>();
        var rules = ReadExpectedLayersExcel(excelPath);
        if (rules.Count == 0) return list;

        var targetRules = rules.Where(r => string.Equals(r.Profile, profileName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (targetRules.Count == 0) targetRules = rules;

        foreach (var r in targetRules)
        {
            if (string.IsNullOrWhiteSpace(r.LayerName) || r.LayerName.Contains("*")) continue;
            var role = ParseRole(r.Role);
            var cat = CadLayerInfo.DetermineCategory(role, r.LayerName);
            var item = new CadLayerInfo
            {
                LayerName = r.LayerName,
                EntityCount = 0,
                Role = role,
                Category = cat,
                IsSelected = true
            };
            CadExtractionService.EnrichCadLayerMetadata(item);
            list.Add(item);
        }
        return list;
    }

    public static List<ExpectedLayerRule> ReadExpectedLayersExcel(string excelPath)
    {
        var rules = new List<ExpectedLayerRule>();
        if (!File.Exists(excelPath)) return rules;

        try
        {
            using var stream = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheets.FirstOrDefault();
            if (ws == null) return rules;

            var header = ws.Row(1);
            int colProfile = -1, colLayer = -1, colRole = -1, colMatch = -1, colNotes = -1;

            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                string text = (header.Cell(c).Value.ToString() ?? "").Trim().ToLowerInvariant();
                if (colProfile == -1 && text.Contains("profile")) colProfile = c;
                else if (colLayer == -1 && (text.Contains("layer") || text.Contains("name"))) colLayer = c;
                else if (colRole == -1 && text.Contains("role")) colRole = c;
                else if (colMatch == -1 && text.Contains("match")) colMatch = c;
                else if (colNotes == -1 && text.Contains("note")) colNotes = c;
            }

            if (colLayer == -1) colLayer = 2;
            if (colProfile == -1) colProfile = 1;
            if (colRole == -1) colRole = 3;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                string layerName = (row.Cell(colLayer).Value.ToString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(layerName)) continue;

                var rule = new ExpectedLayerRule
                {
                    Profile = colProfile > 0 ? (row.Cell(colProfile).Value.ToString() ?? "").Trim() : "Default",
                    LayerName = layerName,
                    Role = colRole > 0 ? (row.Cell(colRole).Value.ToString() ?? "").Trim() : "Main parcel",
                    MatchType = colMatch > 0 ? (row.Cell(colMatch).Value.ToString() ?? "").Trim() : "exact",
                    Notes = colNotes > 0 ? (row.Cell(colNotes).Value.ToString() ?? "").Trim() : string.Empty
                };
                if (string.IsNullOrWhiteSpace(rule.MatchType)) rule.MatchType = "exact";
                rules.Add(rule);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReadExpectedLayersExcel error: {ex.Message}");
        }

        return rules;
    }

    public static ExpectedLayersReport MatchCadLayers(
        IEnumerable<CadLayerInfo> cadLayers,
        IReadOnlyList<ExpectedLayerRule> rules,
        string? activeProfileName = null)
    {
        var report = new ExpectedLayersReport();
        var cadList = cadLayers.ToList();

        // If active profile not specified, suggest profile with most matches
        var profileScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules)
        {
            string prof = string.IsNullOrWhiteSpace(r.Profile) ? "Default" : r.Profile;
            profileScores[prof] = profileScores.GetValueOrDefault(prof) + 0;
        }

        foreach (var cad in cadList)
        {
            foreach (var r in rules)
            {
                if (IsMatch(cad.LayerName, r))
                {
                    string prof = string.IsNullOrWhiteSpace(r.Profile) ? "Default" : r.Profile;
                    profileScores[prof] = profileScores.GetValueOrDefault(prof) + 1;
                }
            }
        }

        report.BestMatchingProfile = profileScores.OrderByDescending(kv => kv.Value).FirstOrDefault().Key ?? "Default";
        string targetProfile = !string.IsNullOrWhiteSpace(activeProfileName) ? activeProfileName : report.BestMatchingProfile;

        var targetRules = rules.Where(r => string.Equals(r.Profile, targetProfile, StringComparison.OrdinalIgnoreCase)).ToList();
        if (targetRules.Count == 0) targetRules = rules.ToList();

        var matchedRuleSet = new HashSet<ExpectedLayerRule>();

        foreach (var cad in cadList)
        {
            var match = targetRules.FirstOrDefault(r => IsMatch(cad.LayerName, r));
            if (match != null)
            {
                matchedRuleSet.Add(match);
                report.CadLayersMatched.Add(new LayerMatchResult
                {
                    CadLayerName = cad.LayerName,
                    EntityCount = cad.EntityCount,
                    AssignedRole = match.Role,
                    IsMatchedFromExcel = true,
                    IsNew = false,
                    MatchedRuleProfile = match.Profile
                });
            }
            else
            {
                report.CadLayersMatched.Add(new LayerMatchResult
                {
                    CadLayerName = cad.LayerName,
                    EntityCount = cad.EntityCount,
                    AssignedRole = cad.Role.ToString(),
                    IsMatchedFromExcel = false,
                    IsNew = true,
                    MatchedRuleProfile = targetProfile
                });
                report.CadLayersNotInExcel.Add(cad.LayerName);
            }
        }

        foreach (var r in targetRules)
        {
            if (!matchedRuleSet.Contains(r))
            {
                report.ExpectedLayersMissingFromCad.Add(r);
            }
        }

        return report;
    }

    public static bool IsMatch(string cadLayer, ExpectedLayerRule rule)
    {
        string normCad = cadLayer.Trim();
        string normPattern = rule.LayerName.Trim();

        if (string.Equals(rule.MatchType, "exact", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(normCad, normPattern, StringComparison.OrdinalIgnoreCase);
        }
        else if (string.Equals(rule.MatchType, "wildcard", StringComparison.OrdinalIgnoreCase))
        {
            string regexPattern = "^" + Regex.Escape(normPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(normCad, regexPattern, RegexOptions.IgnoreCase);
        }
        else if (string.Equals(rule.MatchType, "regex", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Regex.IsMatch(normCad, normPattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        return string.Equals(normCad, normPattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes the mandatory "Layers Used" Excel for every run.
    /// Format: Layers_Used_&lt;project&gt;_&lt;timestamp&gt;.xlsx
    /// </summary>
    public static string WriteLayersUsedExcel(
        string outputFolder,
        string projectName,
        string profileName,
        string sourceCadFileName,
        IReadOnlyList<LayerMatchResult> layersUsed)
    {
        Directory.CreateDirectory(outputFolder);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string safeProject = string.IsNullOrWhiteSpace(projectName) ? "Project" : projectName.Replace('/', '_').Replace('\\', '_');
        string fileName = $"Layers_Used_{safeProject}_{timestamp}.xlsx";
        string filePath = Path.Combine(outputFolder, fileName);

        var writer = new SimpleXlsxWriter();
        string[] headers = { "Profile", "Layer name", "Role", "Match type", "Notes", "Entities", "Source CAD", "Run Date", "New?" };
        var ws = writer.AddSheet("Layers Used", headers);

        foreach (var l in layersUsed)
        {
            ws.AddRow(
                profileName,
                l.CadLayerName,
                l.AssignedRole,
                "exact",
                l.IsMatchedFromExcel ? "Matched from Expected Layers" : "User Confirmed in CAD",
                l.EntityCount,
                Path.GetFileName(sourceCadFileName),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                l.IsNew ? "Yes" : "No"
            );
        }

        writer.Save(filePath);
        return filePath;
    }

    /// <summary>
    /// Auto-creates Expected_Layers.xlsx in expected_layers\ directory if missing.
    /// Never overwrites if already present.
    /// </summary>
    public static void EnsureExpectedLayersExcel(
        string expectedLayersDir,
        string profileName,
        IEnumerable<CadLayerInfo> confirmedLayers)
    {
        Directory.CreateDirectory(expectedLayersDir);
        string expectedFilePath = Path.Combine(expectedLayersDir, "Expected_Layers.xlsx");
        if (File.Exists(expectedFilePath))
            return; // Never overwrite user's existing file

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Expected Layers");

        string[] headers = { "Profile", "Layer name", "Role", "Match type", "Notes" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int row = 2;
        foreach (var l in confirmedLayers)
        {
            ws.Cell(row, 1).Value = profileName;
            ws.Cell(row, 2).Value = l.LayerName;
            ws.Cell(row, 3).Value = l.Role.ToString();
            ws.Cell(row, 4).Value = "exact";
            ws.Cell(row, 5).Value = "Auto-seeded on first run";
            row++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(expectedFilePath);
    }

    public static string GetRoleFriendlyName(LayerRole role) => role switch
    {
        LayerRole.Boundary => "Main parcel",
        LayerRole.Service => "Sub-parcel",
        LayerRole.PlotText => "Plot-number text",
        LayerRole.UsageText => "Usage text",
        LayerRole.Dimensions => "Dimensions",
        LayerRole.Street => "Road",
        LayerRole.WaterChannel => "Water Channel",
        _ => "Other"
    };

    /// <summary>
    /// Appends or updates newly selected/confirmed CAD layers into Expected_Layers.xlsx.
    /// Preserves existing rules and appends new rows for unmapped layers under the active profile.
    /// </summary>
    public static void AppendOrUpdateLayersInExpectedExcel(
        string excelPath,
        string profileName,
        IEnumerable<CadLayerInfo> layers)
    {
        if (string.IsNullOrWhiteSpace(excelPath) || layers == null) return;

        string targetProf = string.IsNullOrWhiteSpace(profileName) ? "Default" : profileName.Trim();

        try
        {
            string? dir = Path.GetDirectoryName(excelPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(excelPath))
            {
                EnsureExpectedLayersExcel(dir ?? "", targetProf, layers);
                return;
            }

            using var workbook = new XLWorkbook(excelPath);
            var ws = workbook.Worksheets.FirstOrDefault();
            if (ws == null) ws = workbook.Worksheets.Add("Expected Layers");

            var header = ws.Row(1);
            int colProfile = -1, colLayer = -1, colRole = -1, colMatch = -1, colNotes = -1;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                string text = (header.Cell(c).Value.ToString() ?? "").Trim().ToLowerInvariant();
                if (colProfile == -1 && text.Contains("profile")) colProfile = c;
                else if (colLayer == -1 && (text.Contains("layer") || text.Contains("name"))) colLayer = c;
                else if (colRole == -1 && text.Contains("role")) colRole = c;
                else if (colMatch == -1 && text.Contains("match")) colMatch = c;
                else if (colNotes == -1 && text.Contains("note")) colNotes = c;
            }

            if (colProfile == -1) colProfile = 1;
            if (colLayer == -1) colLayer = 2;
            if (colRole == -1) colRole = 3;
            if (colMatch == -1) colMatch = 4;
            if (colNotes == -1) colNotes = 5;

            // Header labels if empty
            if (string.IsNullOrWhiteSpace(header.Cell(colProfile).Value.ToString())) header.Cell(colProfile).Value = "Profile";
            if (string.IsNullOrWhiteSpace(header.Cell(colLayer).Value.ToString())) header.Cell(colLayer).Value = "LayerName";
            if (string.IsNullOrWhiteSpace(header.Cell(colRole).Value.ToString())) header.Cell(colRole).Value = "Role";
            if (string.IsNullOrWhiteSpace(header.Cell(colMatch).Value.ToString())) header.Cell(colMatch).Value = "MatchType";
            if (string.IsNullOrWhiteSpace(header.Cell(colNotes).Value.ToString())) header.Cell(colNotes).Value = "Notes";

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var existingLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                string prof = (row.Cell(colProfile).Value.ToString() ?? "").Trim();
                string lName = (row.Cell(colLayer).Value.ToString() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(lName) && (string.Equals(prof, targetProf, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(prof)))
                {
                    existingLayers.Add(lName);
                }
            }

            int nextRow = lastRow + 1;
            bool addedAny = false;

            foreach (var l in layers)
            {
                if (string.IsNullOrWhiteSpace(l.LayerName)) continue;
                if (!existingLayers.Contains(l.LayerName))
                {
                    ws.Cell(nextRow, colProfile).Value = targetProf;
                    ws.Cell(nextRow, colLayer).Value = l.LayerName;
                    ws.Cell(nextRow, colRole).Value = GetRoleFriendlyName(l.Role);
                    ws.Cell(nextRow, colMatch).Value = "exact";
                    ws.Cell(nextRow, colNotes).Value = "User confirmed layer selection";
                    existingLayers.Add(l.LayerName);
                    nextRow++;
                    addedAny = true;
                }
            }

            if (addedAny)
            {
                ws.Columns().AdjustToContents();
                workbook.Save();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppendOrUpdateLayersInExpectedExcel error: {ex.Message}");
        }
    }
}
