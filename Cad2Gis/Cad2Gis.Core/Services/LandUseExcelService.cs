using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Cad2Gis.Core.IO;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Utils;
using Cad2Gis.Core.Lookups;

namespace Cad2Gis.Core.Services;

public class LandUseItem
{
    public string PlotId { get; set; } = string.Empty;
    public string Usage { get; set; } = "RSD";
    public string Remarks { get; set; } = "سكني";
    public string? BlockNo { get; set; }
    public double? AreaSqm { get; set; }
    public int RowIndex { get; set; }
}

public enum MainUsageMode
{
    InferFromText,  // Default temporary: infer from Step 1 chosen text via usage classification; if none, leave empty
    LeaveBlank      // Leave empty pending owner decision
}

public class LandUseExcelService
{
    public static Dictionary<string, LandUseItem> ReadLandUseFile(string filePath)
    {
        var result = new Dictionary<string, LandUseItem>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(filePath))
            return result;

        try
        {
            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Equals("Parcel", StringComparison.OrdinalIgnoreCase))
                            ?? workbook.Worksheets.FirstOrDefault();
            if (worksheet == null) return result;

            var headerRow = worksheet.Row(1);
            int colPlot = -1, colUsage = -1, colRemarks = -1, colBlock = -1, colArea = -1;

            int lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                string h = ArabicTextNormalizer.Normalize(headerRow.Cell(c).GetString().Trim().ToLowerInvariant());
                if (colPlot == -1 && (h.Contains("plot") || h.Contains("قطعه") || h.Contains("القطعه") || h.Contains("رقم") || h == "id"))
                    colPlot = c;
                else if (colUsage == -1 && (h.Contains("usage") || h.Contains("استخدام") || h.Contains("الاستخدام")))
                    colUsage = c;
                else if (colRemarks == -1 && (h.Contains("remark") || h.Contains("بيان") || h.Contains("ملاحظ") || h.Contains("وصف")))
                    colRemarks = c;
                else if (colBlock == -1 && (h.Contains("block") || h.Contains("بلك") || h.Contains("البلك") || h.Contains("مجاوره")))
                    colBlock = c;
                else if (colArea == -1 && (h.Contains("area") || h.Contains("مساح") || h.Contains("المساحه")))
                    colArea = c;
            }

            if (colPlot == -1) colPlot = 1; // Default to first column

            int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            for (int r = 2; r <= lastRow; r++)
            {
                var row = worksheet.Row(r);
                string plotRaw = row.Cell(colPlot).GetString().Trim();
                if (string.IsNullOrWhiteSpace(plotRaw)) continue;

                string plotKey = ArabicTextNormalizer.Normalize(plotRaw);

                var item = new LandUseItem
                {
                    PlotId = plotRaw,
                    RowIndex = r
                };

                if (colUsage > 0)
                {
                    string rawUsage = row.Cell(colUsage).GetString().Trim();
                    // Validate that USAGE must be in the approved Usage Types sheet
                    if (MunicipalLookups.IsOfficialUsageType(rawUsage))
                    {
                        item.Usage = rawUsage;
                    }
                    else
                    {
                        string? classified = MunicipalLookups.ClassifyUsageFromText(rawUsage);
                        item.Usage = classified ?? string.Empty;
                    }
                }

                if (colRemarks > 0)
                    item.Remarks = row.Cell(colRemarks).GetString().Trim();
                if (string.IsNullOrWhiteSpace(item.Remarks) && !string.IsNullOrWhiteSpace(item.Usage))
                    item.Remarks = MunicipalLookups.GetRemarksForUsage(item.Usage);

                if (colBlock > 0)
                {
                    string b = row.Cell(colBlock).GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(b)) item.BlockNo = b;
                }

                if (colArea > 0)
                {
                    if (double.TryParse(row.Cell(colArea).GetString().Trim(), out double parsedArea))
                        item.AreaSqm = parsedArea;
                    else if (row.Cell(colArea).TryGetValue<double>(out double val))
                        item.AreaSqm = val;
                }

                result[plotKey] = item;
                if (!result.ContainsKey(plotRaw))
                {
                    result[plotRaw] = item;
                }
            }
        }
        catch { }

        return result;
    }

    public static void EnrichParcels(
        IEnumerable<ParcelRecord> parcels,
        Dictionary<string, LandUseItem>? landUseLookup,
        List<string>? discrepancies = null,
        MainUsageMode mainUsageMode = MainUsageMode.InferFromText)
    {
        foreach (var p in parcels)
        {
            string key = ArabicTextNormalizer.Normalize(p.PlotId);
            bool matched = false;

            if (landUseLookup != null && (landUseLookup.TryGetValue(key, out var lu) || landUseLookup.TryGetValue(p.PlotId, out lu)))
            {
                if (!string.IsNullOrWhiteSpace(lu.Usage) && MunicipalLookups.IsOfficialUsageType(lu.Usage))
                    p.Usage = lu.Usage;
                if (!string.IsNullOrWhiteSpace(lu.Remarks))
                    p.Remarks = lu.Remarks;
                if (!string.IsNullOrWhiteSpace(lu.BlockNo))
                    p.BlockNo = lu.BlockNo;

                if (lu.AreaSqm.HasValue && Math.Abs(lu.AreaSqm.Value - p.AreaSqm) > 0.05)
                {
                    discrepancies?.Add($"Plot {p.PlotId}: CAD Area ({p.AreaSqm:F2} m²) differs from Excel Area ({lu.AreaSqm.Value:F2} m²) by {Math.Abs(lu.AreaSqm.Value - p.AreaSqm):F2} m².");
                }
                matched = true;
            }

            if (!matched)
            {
                // Fallback rules when PARCEs_landuse.xlsx has no matching data:
                // Section 1.2:
                // SUB Parcel -> UTL (Approved in Usage Types sheet)
                // MAIN Parcel -> Pending owner decision. Never write PLT!
                bool isMain = (p.Class == ParcelClass.Main || p.ParcelTyp == 1);
                if (!isMain)
                {
                    p.Usage = "UTL";
                }
                else
                {
                    if (mainUsageMode == MainUsageMode.InferFromText)
                    {
                        string? classified = MunicipalLookups.ClassifyUsageFromText(p.PlotId);
                        if (!string.IsNullOrWhiteSpace(classified) && MunicipalLookups.IsOfficialUsageType(classified))
                        {
                            p.Usage = classified;
                        }
                        else
                        {
                            p.Usage = string.Empty;
                            discrepancies?.Add($"Plot {p.PlotId}: MAIN parcel USAGE left empty (pending owner decision, no approved usage type found in text).");
                        }
                    }
                    else
                    {
                        p.Usage = string.Empty;
                    }
                }

                // Section 1.3: REMARKS Field
                // 1. Text chosen by user in Step 1 (p.PlotId) if valid
                // 2. Otherwise USAGE value (if USAGE is empty, leave REMARKS empty)
                bool hasChosenText = !string.IsNullOrWhiteSpace(p.PlotId)
                    && !(p.PlotId.StartsWith("P", StringComparison.OrdinalIgnoreCase) && int.TryParse(p.PlotId.Substring(1), out _))
                    && !p.PlotId.Equals("MAIN", StringComparison.OrdinalIgnoreCase)
                    && !p.PlotId.Equals("SUB", StringComparison.OrdinalIgnoreCase);

                if (hasChosenText)
                {
                    p.Remarks = p.PlotId;
                }
                else
                {
                    p.Remarks = !string.IsNullOrWhiteSpace(p.Usage) ? p.Usage : string.Empty;
                }
            }

            // Strict safety check: Never write any USAGE value outside the approved Usage Types sheet
            if (!string.IsNullOrWhiteSpace(p.Usage) && !MunicipalLookups.IsOfficialUsageType(p.Usage))
            {
                p.Usage = string.Empty;
            }
        }
    }

    public static void ExportUpdatedLandUseFile(string targetPath, IEnumerable<ParcelRecord> parcels)
    {
        var writer = new SimpleXlsxWriter();
        string[] headers = { "PLOT_ID", "BLOCK_NO", "USAGE", "REMARKS", "AREA_SQM", "PLAN_NO", "NEIGHBOURHOOD" };
        var ws = writer.AddSheet("LandUse_Register", headers);

        foreach (var p in parcels.OrderBy(x => x.InternalId))
        {
            ws.AddRow(
                p.PlotId,
                p.BlockNo ?? string.Empty,
                p.Usage,
                p.Remarks,
                Math.Round(p.AreaSqm, 2),
                p.PlanNo,
                p.Neighbourhood
            );
        }

        writer.Save(targetPath);
    }
}
