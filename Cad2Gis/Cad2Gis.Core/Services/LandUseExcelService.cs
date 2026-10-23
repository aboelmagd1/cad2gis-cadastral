using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Cad2Gis.Core.IO;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Utils;

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

public class LandUseExcelService
{
    public static Dictionary<string, LandUseItem> ReadLandUseFile(string filePath)
    {
        var result = new Dictionary<string, LandUseItem>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(filePath))
            return result;

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.FirstOrDefault();
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
                item.Usage = row.Cell(colUsage).GetString().Trim();
            if (string.IsNullOrWhiteSpace(item.Usage)) item.Usage = "RSD";

            if (colRemarks > 0)
                item.Remarks = row.Cell(colRemarks).GetString().Trim();
            if (string.IsNullOrWhiteSpace(item.Remarks)) item.Remarks = Lookups.MunicipalLookups.GetRemarksForUsage(item.Usage);

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

        return result;
    }

    public static void EnrichParcels(IEnumerable<ParcelRecord> parcels, Dictionary<string, LandUseItem> landUseLookup, List<string>? discrepancies = null)
    {
        foreach (var p in parcels)
        {
            string key = ArabicTextNormalizer.Normalize(p.PlotId);
            if (landUseLookup.TryGetValue(key, out var lu) || landUseLookup.TryGetValue(p.PlotId, out lu))
            {
                p.Usage = lu.Usage;
                p.Remarks = lu.Remarks;
                if (!string.IsNullOrWhiteSpace(lu.BlockNo))
                    p.BlockNo = lu.BlockNo;

                if (lu.AreaSqm.HasValue && Math.Abs(lu.AreaSqm.Value - p.AreaSqm) > 0.05)
                {
                    discrepancies?.Add($"Plot {p.PlotId}: CAD Area ({p.AreaSqm:F2} m²) differs from Excel Area ({lu.AreaSqm.Value:F2} m²) by {Math.Abs(lu.AreaSqm.Value - p.AreaSqm):F2} m².");
                }
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
