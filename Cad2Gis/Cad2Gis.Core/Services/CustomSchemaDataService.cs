using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Utils;

namespace Cad2Gis.Core.Services;

public class TargetFieldDefinition
{
    public string TargetField { get; set; } = string.Empty;
    public string DescriptionArabic { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public string[] Synonyms { get; set; } = Array.Empty<string>();
}

public static class CustomSchemaDataService
{
    public static readonly List<TargetFieldDefinition> MomrahTargetFields = new()
    {
        new TargetFieldDefinition
        {
            TargetField = "PLOT_ID",
            DescriptionArabic = "رقم القطعة (مفتاح الربط الأساسي)",
            IsMandatory = true,
            Synonyms = new[] { "plot_id", "plotid", "plot_no", "plotno", "plot", "parcel_id", "parcelid", "رقم_القطعة", "رقم القطعة", "القطعة", "الرقم", "id", "num" }
        },
        new TargetFieldDefinition
        {
            TargetField = "PLAN_NO",
            DescriptionArabic = "رقم المخطط المعتمد",
            IsMandatory = true,
            Synonyms = new[] { "plan_no", "planno", "plan_num", "plan", "رقم_المخطط", "رقم المخطط", "المخطط", "المخطط_المعتمد" }
        },
        new TargetFieldDefinition
        {
            TargetField = "USAGE",
            DescriptionArabic = "كود/نوع الاستخدام (RSD, COM, etc.)",
            IsMandatory = true,
            Synonyms = new[] { "usage", "land_use", "landuse", "use", "الاستخدام", "نوع_الاستخدام", "استخدام", "نوع الاستخدام" }
        },
        new TargetFieldDefinition
        {
            TargetField = "BLOCK_NO",
            DescriptionArabic = "رقم البلك أو المجاورة",
            IsMandatory = false,
            Synonyms = new[] { "block_no", "blockno", "block", "البلك", "رقم_البلك", "رقم البلك", "بلك", "مجاورة" }
        },
        new TargetFieldDefinition
        {
            TargetField = "NEIGHBOURH",
            DescriptionArabic = "كود الحي البلدي (3 أرقام)",
            IsMandatory = true,
            Synonyms = new[] { "neighbourh", "neighborhood", "neighbourhood", "district", "الحي", "كود_الحي", "كود الحي", "اسم_الحي", "اسم الحي" }
        },
        new TargetFieldDefinition
        {
            TargetField = "ADDRESS",
            DescriptionArabic = "نص العنوان المنظم الكامل",
            IsMandatory = false,
            Synonyms = new[] { "address", "addr", "العنوان", "عنوان_العقار", "العنوان_الوطني" }
        },
        new TargetFieldDefinition
        {
            TargetField = "CITY_ID",
            DescriptionArabic = "كود/اسم المدينة",
            IsMandatory = true,
            Synonyms = new[] { "city_id", "cityid", "city", "المدينة", "كود_المدينة", "كود المدينة", "اسم_المدينة" }
        },
        new TargetFieldDefinition
        {
            TargetField = "REGION_ID",
            DescriptionArabic = "كود/اسم المنطقة",
            IsMandatory = true,
            Synonyms = new[] { "region_id", "regionid", "region", "المنطقة", "كود_المنطقة", "كود المنطقة", "اسم_المنطقة" }
        },
        new TargetFieldDefinition
        {
            TargetField = "PARCEL_TYP",
            DescriptionArabic = "تصنيف القطعة (1=رئيسية، 2=مرفق)",
            IsMandatory = false,
            Synonyms = new[] { "parcel_typ", "parceltyp", "parcel_type", "نوع_القطعة", "تصنيف_القطعة", "النوع" }
        },
        new TargetFieldDefinition
        {
            TargetField = "REALESTA_1",
            DescriptionArabic = "نوع العقار (2=أرض فضاء، 1=مبنى)",
            IsMandatory = false,
            Synonyms = new[] { "realesta_1", "realestate", "realestate_type", "نوع_العقار", "العقار" }
        },
        new TargetFieldDefinition
        {
            TargetField = "ISBUILDING",
            DescriptionArabic = "هل العقار مبني (0=لا، 1=نعم)",
            IsMandatory = false,
            Synonyms = new[] { "isbuilding", "is_building", "building", "مبنى", "بناء", "هل_مبني" }
        },
        new TargetFieldDefinition
        {
            TargetField = "BUILDINGTY",
            DescriptionArabic = "نوع البناء (1=مسلح حديث)",
            IsMandatory = false,
            Synonyms = new[] { "buildingty", "building_type", "buildingtype", "نوع_المبنى", "نوع_البناء" }
        },
        new TargetFieldDefinition
        {
            TargetField = "REMARKS",
            DescriptionArabic = "بيان / ملاحظات الاستخدام بالعربي",
            IsMandatory = false,
            Synonyms = new[] { "remarks", "remark", "notes", "note", "ملاحظات", "ملاحظة", "البيان", "الوصف", "وصف" }
        },
        new TargetFieldDefinition
        {
            TargetField = "LAYOUT",
            DescriptionArabic = "المجاورة التخطيطية",
            IsMandatory = false,
            Synonyms = new[] { "layout", "sub_layout", "المجاورة", "مجاورة" }
        },
        new TargetFieldDefinition
        {
            TargetField = "REQUEST_TY",
            DescriptionArabic = "نوع الطلب البلدي",
            IsMandatory = false,
            Synonyms = new[] { "request_ty", "request_type", "نوع_الطلب", "الطلب" }
        },
        new TargetFieldDefinition
        {
            TargetField = "REQUEST_NO",
            DescriptionArabic = "رقم الطلب البلدي",
            IsMandatory = false,
            Synonyms = new[] { "request_no", "requestno", "رقم_الطلب" }
        },
        new TargetFieldDefinition
        {
            TargetField = "REPORT_NO",
            DescriptionArabic = "رقم تقرير المعاينة/المخطط",
            IsMandatory = false,
            Synonyms = new[] { "report_no", "reportno", "رقم_التقرير", "التقرير" }
        },
        new TargetFieldDefinition
        {
            TargetField = "AREA_SQM",
            DescriptionArabic = "المساحة المعتمدة بالمتر المربع",
            IsMandatory = false,
            Synonyms = new[] { "area_sqm", "areasqm", "area", "shape_area", "المساحة", "مساحة", "المساحة_م2" }
        },
        new TargetFieldDefinition
        {
            TargetField = "PLOT_NO1",
            DescriptionArabic = "رقم القطعة 1",
            IsMandatory = false,
            Synonyms = new[] { "plot_no1", "plotno1", "رقم_القطعة_1" }
        },
        new TargetFieldDefinition
        {
            TargetField = "PLOT_NO2",
            DescriptionArabic = "رقم القطعة 2",
            IsMandatory = false,
            Synonyms = new[] { "plot_no2", "plotno2", "رقم_القطعة_2" }
        },
        new TargetFieldDefinition
        {
            TargetField = "PLOT_NO3",
            DescriptionArabic = "رقم القطعة 3",
            IsMandatory = false,
            Synonyms = new[] { "plot_no3", "plotno3", "رقم_القطعة_3" }
        },
        new TargetFieldDefinition
        {
            TargetField = "PLOT_NO4",
            DescriptionArabic = "رقم القطعة 4",
            IsMandatory = false,
            Synonyms = new[] { "plot_no4", "plotno4", "رقم_القطعة_4" }
        }
    };

    public static string AutoMatchField(string targetField, IEnumerable<string> sourceFields)
    {
        var targetDef = MomrahTargetFields.FirstOrDefault(d => d.TargetField.Equals(targetField, StringComparison.OrdinalIgnoreCase));
        var sList = sourceFields?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();

        // 1. Exact case-insensitive match
        var exact = sList.FirstOrDefault(s => string.Equals(s, targetField, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 2. Ignore underscores and spaces
        string normTarget = targetField.Replace("_", "").Replace(" ", "").ToLowerInvariant();
        var normMatch = sList.FirstOrDefault(s => s.Replace("_", "").Replace(" ", "").ToLowerInvariant() == normTarget);
        if (normMatch != null) return normMatch;

        // 3. Match against known synonyms
        if (targetDef != null && targetDef.Synonyms.Length > 0)
        {
            foreach (var syn in targetDef.Synonyms)
            {
                string normSyn = syn.Replace("_", "").Replace(" ", "").ToLowerInvariant();
                var synMatch = sList.FirstOrDefault(s => 
                    string.Equals(s, syn, StringComparison.OrdinalIgnoreCase) ||
                    s.Replace("_", "").Replace(" ", "").ToLowerInvariant() == normSyn ||
                    s.ToLowerInvariant().Contains(syn.ToLowerInvariant()));
                if (synMatch != null) return synMatch;
            }
        }

        return string.Empty;
    }

    public static (bool IsIdentical, int MatchedCount, int TotalStandard) CheckSchema(IEnumerable<string> sourceFields)
    {
        var sList = sourceFields?.Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>();
        int matchedCount = 0;
        foreach (var def in MomrahTargetFields)
        {
            if (sList.Any(s => string.Equals(s, def.TargetField, StringComparison.OrdinalIgnoreCase)))
            {
                matchedCount++;
            }
        }

        // Schema is identical if core fields exist with exact names
        bool isIdentical = matchedCount >= MomrahTargetFields.Count;
        return (isIdentical, matchedCount, MomrahTargetFields.Count);
    }

    public static List<string> ReadExcelHeaders(string filePath)
    {
        var headers = new List<string>();
        if (!File.Exists(filePath)) return headers;

        try
        {
            using var workbook = new XLWorkbook(filePath);
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet == null) return headers;

            var firstRow = sheet.Row(1);
            int lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int col = 1; col <= lastCol; col++)
            {
                string val = firstRow.Cell(col).GetString().Trim();
                if (!string.IsNullOrEmpty(val) && !headers.Contains(val, StringComparer.OrdinalIgnoreCase))
                {
                    headers.Add(val);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReadExcelHeaders] Error: {ex.Message}");
        }

        return headers;
    }

    public static List<Dictionary<string, object>> ReadExcelRows(string filePath)
    {
        var rows = new List<Dictionary<string, object>>();
        if (!File.Exists(filePath)) return rows;

        try
        {
            using var workbook = new XLWorkbook(filePath);
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet == null) return rows;

            var headerRow = sheet.Row(1);
            int lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            var colNames = new Dictionary<int, string>();
            for (int col = 1; col <= lastCol; col++)
            {
                string val = headerRow.Cell(col).GetString().Trim();
                if (!string.IsNullOrEmpty(val))
                    colNames[col] = val;
            }

            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            for (int r = 2; r <= lastRow; r++)
            {
                var rowData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var row = sheet.Row(r);
                bool hasData = false;
                foreach (var kvp in colNames)
                {
                    string strVal = row.Cell(kvp.Key).GetString().Trim();
                    rowData[kvp.Value] = strVal;
                    if (!string.IsNullOrEmpty(strVal)) hasData = true;
                }
                if (hasData) rows.Add(rowData);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReadExcelRows] Error: {ex.Message}");
        }

        return rows;
    }
}
