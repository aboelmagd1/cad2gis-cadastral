using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace Cad2Gis.Core.Lookups;

public static class MunicipalLookups
{
    // Official 67 MOMRAH Border Types (Required Fields in the Shapefile.xlsx, Border Types sheet)
    public static readonly Dictionary<string, string> OfficialBorderTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NA"] = "لا ينطبق",
        ["SBK"] = "ارتداد",
        ["STR"] = "شارع",
        ["STT"] = "شارع",
        ["UNT"] = "الوحدة",
        ["PLT"] = "القطعة",
        ["DCT"] = "منور",
        ["STW"] = "الدرج",
        ["HLW"] = "ممر",
        ["HOM"] = "بيت",
        ["REL"] = "العقار",
        ["PRS"] = "الشقق",
        ["ROF"] = "السطح",
        ["DSB"] = "موزع",
        ["ELV"] = "مصعد",
        ["NGB"] = "جار",
        ["PRT"] = "شقة",
        ["MEN"] = "مدخل رئيسي",
        ["EVS"] = "مصاعد",
        ["SRV"] = "خدمات",
        ["LNE"] = "ممر مشاه",
        ["ROM"] = "غرفة",
        ["PRV"] = "جزء خاص",
        ["YRD"] = "ساحة",
        ["PAK"] = "حديقة",
        ["BLD"] = "عمارة",
        ["BUL"] = "مبنى",
        ["ECR"] = "غرفة كهرباء",
        ["LAN"] = "أرض فضاء",
        ["ROD"] = "طريق",
        ["PNT"] = "نافذ",
        ["HAL"] = "بهو",
        ["TUB"] = "دكت",
        ["COM"] = "محل",
        ["DTS"] = "مناور",
        ["ENT"] = "مدخل",
        ["LDR"] = "سلم",
        ["GAP"] = "فراغ",
        ["MSQ"] = "مسجد",
        ["REC"] = "غرفة ترفيه",
        ["LNG"] = "صالة",
        ["LDM"] = "أرض جبلية",
        ["VCR"] = "غرفة مفرغة",
        ["BRD"] = "حوش",
        ["ANN"] = "ملحق",
        ["PRK"] = "موقف",
        ["UTL"] = "مرفق",
        ["CRB"] = "رصيف",
        ["PMR"] = "غرفة مضخات",
        ["NRS"] = "حضانة",
        ["WST"] = "غرفة نفايات",
        ["WRH"] = "مستودع",
        ["TNK"] = "غرفة خزان",
        ["TFR"] = "غرفة المحولات",
        ["SWM"] = "مسبح",
        ["SMR"] = "غرفة إدارة الأنظمة",
        ["SDM"] = "ملعب",
        ["PST"] = "محطة توليد",
        ["PNR"] = "غرفة لوحات كهرباء",
        ["OFC"] = "مكتب",
        ["MTR"] = "غرفة صيانة",
        ["MRP"] = "غرفة متعددة الاستخدامات",
        ["MNR"] = "غرفة ميكانيكا",
        ["LBR"] = "مكتبة",
        ["GYM"] = "نادي رياضي",
        ["GRR"] = "غرفة مولدات",
        ["CMR"] = "غرفة اتصالات"
    };

    // Non-official codes mapped by user or domain (e.g. WCH for water channel / stream)
    public static readonly Dictionary<string, string> UserMappedBorderTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WCH"] = "قناة مائية / مجرى سيل"
    };

    // Active lookup dictionary (initialized with official codes + user mapped)
    public static readonly Dictionary<string, string> BorderTypeDescriptions = new(OfficialBorderTypes, StringComparer.OrdinalIgnoreCase);

    // Official 4 Building Types (BUILDINGTY)
    public static readonly Dictionary<int, string> BuildingTypes = new()
    {
        [1] = "مسلح حديث",
        [2] = "مسلح قديم",
        [3] = "شعبي",
        [4] = "مهدم"
    };

    // Standard Land Use Types and default Remarks
    public static readonly Dictionary<string, string> UsageToRemarks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RSD"] = "سكني",
        ["COM"] = "تجاري أحياء",
        ["RSC"] = "تجاري سكني",
        ["Mixed Use"] = "متعدد الاستخدام",
        ["MIX"] = "متعدد الاستخدام",
        ["UTL"] = "مرافق وخدمات",
        ["AGR"] = "زراعي",
        ["IND"] = "صناعي",
        ["MSQ"] = "مسجد",
        ["PAK"] = "حديقة",
        ["PRK"] = "مواقف سيارات",
        ["SRV"] = "خدمات",
        ["SCH"] = "تعليمي",
        ["HLT"] = "صحي",
        ["GOV"] = "حكومي"
    };

    // Standard Saudi Regions
    public static readonly Dictionary<string, string> Regions = new()
    {
        ["1"] = "الرياض",
        ["2"] = "مكة المكرمة",
        ["3"] = "المدينة المنورة",
        ["4"] = "القصيم",
        ["5"] = "المنطقة الشرقية",
        ["6"] = "عسير",
        ["7"] = "تبوك",
        ["8"] = "حائل",
        ["9"] = "الحدود الشمالية",
        ["10"] = "جازان",
        ["11"] = "نجران",
        ["12"] = "الباحة",
        ["13"] = "الجوف"
    };

    // Standard Major Saudi Cities
    public static readonly Dictionary<string, (string CityName, string RegionId)> Cities = new()
    {
        ["1"] = ("الرياض", "1"),
        ["2"] = ("جدة", "2"),
        ["3"] = ("مكة المكرمة", "2"),
        ["4"] = ("المدينة المنورة", "3"),
        ["5"] = ("الدمام", "5"),
        ["6"] = ("الخبر", "5"),
        ["7"] = ("الظهران", "5"),
        ["8"] = ("الأحساء", "5"),
        ["9"] = ("الطائف", "2"),
        ["10"] = ("بريدة", "4"),
        ["11"] = ("عنيزة", "4"),
        ["12"] = ("أبها", "6"),
        ["13"] = ("خميس مشيط", "6"),
        ["14"] = ("تبوك", "7"),
        ["15"] = ("حائل", "8"),
        ["16"] = ("عرعر", "9"),
        ["17"] = ("جازان", "10"),
        ["18"] = ("نجران", "11"),
        ["19"] = ("الباحة", "12"),
        ["20"] = ("سكاكا", "13"),
        ["21"] = ("ينبع", "3")
    };

    static MunicipalLookups()
    {
        // Include default user-mapped codes in BorderTypeDescriptions
        foreach (var kvp in UserMappedBorderTypes)
        {
            BorderTypeDescriptions[kvp.Key] = kvp.Value;
        }
    }

    public static bool IsOfficialBorderType(string code) =>
        !string.IsNullOrWhiteSpace(code) && OfficialBorderTypes.ContainsKey(code);

    public static bool IsUserMappedBorderType(string code) =>
        !string.IsNullOrWhiteSpace(code) && UserMappedBorderTypes.ContainsKey(code);

    public static string GetBorderTypeName(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "غير محدد";
        return BorderTypeDescriptions.TryGetValue(code, out string? name) ? name : "أخرى";
    }

    public static string GetBuildingTypeName(int code) =>
        BuildingTypes.TryGetValue(code, out string? name) ? name : "مسلح حديث";

    public static string GetRemarksForUsage(string usage) =>
        UsageToRemarks.TryGetValue(usage, out string? remarks) ? remarks : usage;

    /// <summary>
    /// Loads border types dynamically from the official Excel authority file (Border Types sheet).
    /// </summary>
    public static int LoadFromRequiredFieldsExcel(string excelPath)
    {
        if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
            return 0;

        try
        {
            using var workbook = new XLWorkbook(excelPath);
            var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Equals("Border Types", StringComparison.OrdinalIgnoreCase));
            if (sheet == null) return 0;

            int loaded = 0;
            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            for (int r = 2; r <= lastRow; r++)
            {
                string code = sheet.Cell(r, 1).GetString().Trim();
                string desc = sheet.Cell(r, 2).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(code))
                {
                    OfficialBorderTypes[code] = desc;
                    BorderTypeDescriptions[code] = desc;
                    loaded++;
                }
            }
            return loaded;
        }
        catch
        {
            return 0;
        }
    }

    public static void LoadFromLookupsDirectory(string lookupsDir)
    {
        if (string.IsNullOrWhiteSpace(lookupsDir) || !Directory.Exists(lookupsDir))
            return;

        try
        {
            // 1. Load BORDERTYPE.json
            string btFile = Path.Combine(lookupsDir, "BORDERTYPE.json");
            if (File.Exists(btFile))
            {
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(btFile));
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        BorderTypeDescriptions[kvp.Key] = kvp.Value;
                        if (!OfficialBorderTypes.ContainsKey(kvp.Key))
                        {
                            UserMappedBorderTypes[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }

            // 2. Load cities.json
            string cityFile = Path.Combine(lookupsDir, "cities.json");
            if (File.Exists(cityFile))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cityFile));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string id = prop.Name;
                    string name = prop.Value.TryGetProperty("cityName", out var n) ? n.GetString() ?? id : id;
                    string reg = prop.Value.TryGetProperty("regionId", out var r) ? r.GetString() ?? "1" : "1";
                    Cities[id] = (name, reg);
                }
            }
        }
        catch { }
    }

    public static string GetBorderTypeDescription(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        if (BorderTypeDescriptions.TryGetValue(code, out var desc)) return desc;
        if (UserMappedBorderTypes.TryGetValue(code, out var udesc)) return udesc;
        return code;
    }
}
