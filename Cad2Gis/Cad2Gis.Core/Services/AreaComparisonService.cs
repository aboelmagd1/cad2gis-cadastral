using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Utils;

namespace Cad2Gis.Core.Services;

public class AreaComparisonEntry
{
    public string PlotId { get; set; } = string.Empty;
    public string NormalizedPlotId { get; set; } = string.Empty;
    public double ComputedAreaSqm { get; set; }
    public double? ExcelAreaSqm { get; set; }
    public double? AreaDifferenceSqm { get; set; }
    public double? PercentDifference { get; set; }
    public string Status { get; set; } = "Matched"; // Matched, Mismatched, InExcelOnly, InDrawingOnly, Duplicate
    public string Notes { get; set; } = string.Empty;
}

public class AreaComparisonReport
{
    public int MatchedCount { get; set; }
    public int MismatchedCount { get; set; }
    public int InDrawingOnlyCount { get; set; }
    public int InExcelOnlyCount { get; set; }
    public int DuplicateCount { get; set; }
    public List<AreaComparisonEntry> Entries { get; set; } = new();
}

public class AreaComparisonOptions
{
    public double AbsoluteToleranceSqm { get; set; } = 0.5; // e.g. 0.5 m²
    public double PercentTolerance { get; set; } = 0.1; // e.g. 0.1%
}

public static class AreaComparisonService
{
    /// <summary>
    /// Normalizes plot number by trimming spaces, converting Arabic-Indic digits (٠١٢٣٤٥٦٧٨٩)
    /// to Western digits (0123456789), and unifying slash/dash styles while keeping formats like "11/3314" intact.
    /// </summary>
    public static string NormalizePlotNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        string s = raw.Trim();

        // Convert Arabic-Indic digits to ASCII digits
        char[] chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '\u0660' && chars[i] <= '\u0669') // ٠..٩
            {
                chars[i] = (char)('0' + (chars[i] - '\u0660'));
            }
            else if (chars[i] >= '\u06F0' && chars[i] <= '\u06F9') // Persian ۰..۹
            {
                chars[i] = (char)('0' + (chars[i] - '\u06F0'));
            }
            else if (chars[i] == '\\' || chars[i] == '／')
            {
                chars[i] = '/';
            }
            else if (chars[i] == '－' || chars[i] == '—')
            {
                chars[i] = '-';
            }
        }

        // Remove redundant interior whitespace around slashes/dashes
        string result = new string(chars);
        result = result.Replace(" / ", "/").Replace("/ ", "/").Replace(" /", "/");
        result = result.Replace(" - ", "-").Replace("- ", "-").Replace(" -", "-");

        return result.Trim();
    }

    public static Dictionary<string, (double Area, int Row)> ReadAreasExcel(
        string excelPath,
        out List<string> duplicatePlots)
    {
        var result = new Dictionary<string, (double Area, int Row)>(StringComparer.OrdinalIgnoreCase);
        duplicatePlots = new List<string>();

        if (!File.Exists(excelPath)) return result;

        using var workbook = new XLWorkbook(excelPath);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null) return result;

        var headerRow = worksheet.Row(1);
        int colPlot = -1;
        int colArea = -1;

        int lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int c = 1; c <= lastCol; c++)
        {
            string h = ArabicTextNormalizer.Normalize(headerRow.Cell(c).GetString().Trim().ToLowerInvariant());
            if (colPlot == -1 && (h.Contains("plot") || h.Contains("قطعه") || h.Contains("رقم") || h == "id"))
                colPlot = c;
            else if (colArea == -1 && (h.Contains("area") || h.Contains("مساح") || h.Contains("المساحه")))
                colArea = c;
        }

        if (colPlot == -1) colPlot = 1;
        if (colArea == -1) colArea = 2;

        int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = 2; r <= lastRow; r++)
        {
            var row = worksheet.Row(r);
            string rawPlot = row.Cell(colPlot).GetString().Trim();
            if (string.IsNullOrWhiteSpace(rawPlot)) continue;

            string normPlot = NormalizePlotNumber(rawPlot);
            double areaVal = 0;

            if (row.Cell(colArea).TryGetValue<double>(out double dVal))
            {
                areaVal = dVal;
            }
            else if (double.TryParse(row.Cell(colArea).GetString().Trim(), out double pVal))
            {
                areaVal = pVal;
            }

            if (result.ContainsKey(normPlot))
            {
                duplicatePlots.Add(normPlot);
            }
            else
            {
                result[normPlot] = (areaVal, r);
            }
        }

        return result;
    }

    public static AreaComparisonReport CompareAreas(
        IReadOnlyList<ParcelRecord> parcels,
        string excelPath,
        AreaComparisonOptions options)
    {
        var report = new AreaComparisonReport();
        var excelMap = ReadAreasExcel(excelPath, out var excelDuplicates);

        var drawingMap = new Dictionary<string, ParcelRecord>(StringComparer.OrdinalIgnoreCase);
        var drawingDuplicates = new List<string>();

        foreach (var p in parcels)
        {
            string norm = NormalizePlotNumber(p.PlotId);
            if (string.IsNullOrWhiteSpace(norm)) continue;

            if (drawingMap.ContainsKey(norm))
            {
                drawingDuplicates.Add(norm);
            }
            else
            {
                drawingMap[norm] = p;
            }
        }

        // Report duplicates
        foreach (var dup in excelDuplicates.Distinct())
        {
            report.Entries.Add(new AreaComparisonEntry
            {
                PlotId = dup,
                NormalizedPlotId = dup,
                Status = "Duplicate",
                Notes = "Duplicate plot number in Excel table"
            });
            report.DuplicateCount++;
        }
        foreach (var dup in drawingDuplicates.Distinct())
        {
            report.Entries.Add(new AreaComparisonEntry
            {
                PlotId = dup,
                NormalizedPlotId = dup,
                Status = "Duplicate",
                Notes = "Duplicate plot number in CAD/drawing parcels"
            });
            report.DuplicateCount++;
        }

        // Check each parcel in drawing
        var processedExcelKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in drawingMap)
        {
            string normPlot = kvp.Key;
            var parcel = kvp.Value;

            if (excelMap.TryGetValue(normPlot, out var excelData))
            {
                processedExcelKeys.Add(normPlot);
                double diff = Math.Abs(parcel.AreaSqm - excelData.Area);
                double pctDiff = excelData.Area > 0 ? (diff / excelData.Area) * 100.0 : 0.0;

                bool isMatch = diff <= options.AbsoluteToleranceSqm || pctDiff <= options.PercentTolerance;

                var entry = new AreaComparisonEntry
                {
                    PlotId = parcel.PlotId,
                    NormalizedPlotId = normPlot,
                    ComputedAreaSqm = Math.Round(parcel.AreaSqm, 2),
                    ExcelAreaSqm = Math.Round(excelData.Area, 2),
                    AreaDifferenceSqm = Math.Round(parcel.AreaSqm - excelData.Area, 2),
                    PercentDifference = Math.Round(pctDiff, 2),
                    Status = isMatch ? "Matched" : "Mismatched",
                    Notes = isMatch
                        ? "Within tolerance"
                        : $"Exceeds tolerance (Diff: {diff:F2} m², {pctDiff:F2}%)"
                };

                if (isMatch) report.MatchedCount++;
                else report.MismatchedCount++;

                report.Entries.Add(entry);
            }
            else
            {
                report.Entries.Add(new AreaComparisonEntry
                {
                    PlotId = parcel.PlotId,
                    NormalizedPlotId = normPlot,
                    ComputedAreaSqm = Math.Round(parcel.AreaSqm, 2),
                    Status = "InDrawingOnly",
                    Notes = "Present in drawing but not in Excel"
                });
                report.InDrawingOnlyCount++;
            }
        }

        // Check parcels in Excel not in drawing
        foreach (var kvp in excelMap)
        {
            if (!processedExcelKeys.Contains(kvp.Key))
            {
                report.Entries.Add(new AreaComparisonEntry
                {
                    PlotId = kvp.Key,
                    NormalizedPlotId = kvp.Key,
                    ExcelAreaSqm = Math.Round(kvp.Value.Area, 2),
                    Status = "InExcelOnly",
                    Notes = "Present in Excel but not in drawing"
                });
                report.InExcelOnlyCount++;
            }
        }

        return report;
    }
}
