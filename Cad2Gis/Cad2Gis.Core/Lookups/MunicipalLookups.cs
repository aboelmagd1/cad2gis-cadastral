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

    // Official 6 MOMRAH Usage Types (Required Fields in the Shapefile.xlsx, Usage Types sheet)
    public static readonly Dictionary<string, string> OfficialUsageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RSD"] = "سكني",
        ["COM"] = "تجاري",
        ["RSC"] = "سكني تجاري",
        ["AGR"] = "زراعي",
        ["IND"] = "صناعي",
        ["UTL"] = "مرافق"
    };

    public static bool IsOfficialUsageType(string? code) =>
        !string.IsNullOrWhiteSpace(code) && OfficialUsageTypes.ContainsKey(code.Trim());

    public static string? ExtractUsageCode(string? labelOrCode)
    {
        if (string.IsNullOrWhiteSpace(labelOrCode)) return null;
        string trimmed = labelOrCode.Trim();
        if (IsOfficialUsageType(trimmed)) return trimmed.ToUpperInvariant();

        int dashIdx = trimmed.IndexOfAny(new[] { '—', '-', ' ', ':', '/' });
        if (dashIdx > 0)
        {
            string prefix = trimmed.Substring(0, dashIdx).Trim();
            if (IsOfficialUsageType(prefix)) return prefix.ToUpperInvariant();
        }

        foreach (var kvp in OfficialUsageTypes)
        {
            if (trimmed.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }

        return null;
    }

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
    /// Matches candidate text (such as parcel REMARKS or USAGE) to the most appropriate official BORDERTYPE code
    /// from the official 'Border Types' list. Uses text normalization, exact code matching, description matching,
    /// containment, and token/fuzzy similarity. Returns null if no match meets the threshold.
    /// </summary>
    public static (string? Code, string? MatchedDesc, double Score) MatchBestBorderType(string? text, double threshold = 0.5)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null, 0.0);

        string raw = text.Trim();

        // 1. Direct match with official code (case-insensitive)
        if (OfficialBorderTypes.TryGetValue(raw, out var codeDesc))
        {
            return (raw.ToUpperInvariant(), codeDesc, 1.0);
        }

        string normInput = Utils.ArabicTextNormalizer.Normalize(raw, normalizeTehMarbuta: true).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normInput))
            return (null, null, 0.0);

        // 2. Exact match with normalized description
        foreach (var kvp in OfficialBorderTypes)
        {
            string normDesc = Utils.ArabicTextNormalizer.Normalize(kvp.Value, normalizeTehMarbuta: true).ToLowerInvariant();
            if (normInput == normDesc)
            {
                return (kvp.Key, kvp.Value, 1.0);
            }
        }

        // 3. Whole-word / Token match (e.g. "مسجد" in "مسجد الحي" -> MSQ, "حديقة" in "حديقة عامة" -> PAK)
        var inputTokens = normInput.Split(new[] { ' ', '،', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var kvp in OfficialBorderTypes)
        {
            string normDesc = Utils.ArabicTextNormalizer.Normalize(kvp.Value, normalizeTehMarbuta: true).ToLowerInvariant();
            var descTokens = normDesc.Split(new[] { ' ', '،', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

            if (inputTokens.Contains(normDesc) || descTokens.Contains(normInput) ||
                inputTokens.Any(t => descTokens.Contains(t) && t.Length >= 3))
            {
                return (kvp.Key, kvp.Value, 0.95);
            }
        }

        // 4. Substring containment match
        string? bestContainCode = null;
        string? bestContainDesc = null;
        double bestContainScore = 0.0;

        foreach (var kvp in OfficialBorderTypes)
        {
            string normDesc = Utils.ArabicTextNormalizer.Normalize(kvp.Value, normalizeTehMarbuta: true).ToLowerInvariant();
            if (normInput.Contains(normDesc) || normDesc.Contains(normInput))
            {
                double score = (double)Math.Min(normInput.Length, normDesc.Length) / Math.Max(normInput.Length, normDesc.Length);
                if (score > bestContainScore)
                {
                    bestContainScore = score;
                    bestContainCode = kvp.Key;
                    bestContainDesc = kvp.Value;
                }
            }
        }

        if (bestContainScore >= threshold && bestContainCode != null)
        {
            return (bestContainCode, bestContainDesc, bestContainScore);
        }

        // 4. Fuzzy Levenshtein similarity against descriptions
        string? bestFuzzyCode = null;
        string? bestFuzzyDesc = null;
        double maxFuzzyScore = 0.0;

        foreach (var kvp in OfficialBorderTypes)
        {
            string normDesc = Utils.ArabicTextNormalizer.Normalize(kvp.Value, normalizeTehMarbuta: true).ToLowerInvariant();
            double sim = CalculateSimilarity(normInput, normDesc);
            if (sim > maxFuzzyScore)
            {
                maxFuzzyScore = sim;
                bestFuzzyCode = kvp.Key;
                bestFuzzyDesc = kvp.Value;
            }
        }

        if (maxFuzzyScore >= threshold && bestFuzzyCode != null)
        {
            return (bestFuzzyCode, bestFuzzyDesc, maxFuzzyScore);
        }

        return (null, null, maxFuzzyScore);
    }

    private static double CalculateSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
        if (s1 == s2) return 1.0;

        int maxLen = Math.Max(s1.Length, s2.Length);
        if (maxLen == 0) return 1.0;

        int dist = LevenshteinDistance(s1, s2);
        return 1.0 - ((double)dist / maxLen);
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

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

    /// <summary>
    /// Classifies an Arabic text or token into one of the official MOMRAH Usage Types (RSD, COM, RSC, AGR, IND, UTL).
    /// Matches against UsageKeywords if loaded, or standard keyword patterns. Returns null if no match is found.
    /// </summary>
    public static string? ClassifyUsageFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string raw = text.Trim();
        string upper = raw.ToUpperInvariant();

        // 1. Direct match with official code
        if (IsOfficialUsageType(upper)) return upper;

        string norm = Utils.ArabicTextNormalizer.Normalize(raw, normalizeTehMarbuta: true).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(norm)) return null;

        // 2. Query UsageKeywords table if loaded
        if (UsageKeywords.Count > 0)
        {
            var candidates = UsageKeywords
                .OrderBy(k => k.Priority)
                .ThenByDescending(k => k.Keyword.Length)
                .ToList();

            foreach (var item in candidates)
            {
                string normKw = Utils.ArabicTextNormalizer.Normalize(item.Keyword, normalizeTehMarbuta: true).ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(normKw)) continue;

                if (item.MatchType.Equals("exact", StringComparison.OrdinalIgnoreCase))
                {
                    if (norm == normKw) return item.UsageCode;
                }
                else
                {
                    if (norm == normKw || norm.Contains(normKw)) return item.UsageCode;
                }
            }
        }

        // 3. Built-in fallback normalization
        if (norm.Contains("سكني تجاري") || norm.Contains("تجاري سكني") || norm == "rsc")
            return "RSC";
        if (norm.Contains("سكني") || norm == "rsd")
            return "RSD";
        if (norm.Contains("تجاري") || norm == "com")
            return "COM";
        if (norm.Contains("زراعي") || norm == "agr")
            return "AGR";
        if (norm.Contains("صناعي") || norm == "ind")
            return "IND";
        if (norm.Contains("مرافق") || norm.Contains("مرفق") || norm.Contains("خدمات") || norm == "utl")
            return "UTL";

        return null;
    }

    /// <summary>
    /// Loads usage types dynamically from the official Excel authority file (Usage Types sheet).
    /// </summary>
    public static int LoadUsageTypesFromRequiredFieldsExcel(string excelPath)
    {
        if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
            return 0;

        try
        {
            using var workbook = new XLWorkbook(excelPath);
            var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Equals("Usage Types", StringComparison.OrdinalIgnoreCase));
            if (sheet == null) return 0;

            int loaded = 0;
            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            for (int r = 2; r <= lastRow; r++)
            {
                string code = sheet.Cell(r, 1).GetString().Trim();
                string desc = sheet.Cell(r, 2).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(code))
                {
                    OfficialUsageTypes[code] = desc;
                    UsageToRemarks[code] = desc;
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

    public static readonly List<BorderTypeKeywordItem> BorderTypeKeywords = new();

    public static int LoadBorderTypeKeywords(string csvPath, Action<string>? warningLog = null)
    {
        BorderTypeKeywords.Clear();
        if (string.IsNullOrWhiteSpace(csvPath)) return 0;

        if (!File.Exists(csvPath))
        {
            EnsureBorderTypeKeywordsFile(csvPath);
        }

        if (!File.Exists(csvPath)) return 0;

        int loaded = 0;
        try
        {
            var lines = File.ReadAllLines(csvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (parts.Length < 4) continue;

                string kw = parts[0].Trim();
                string appliesTo = parts[1].Trim().ToUpperInvariant();
                string matchType = parts[2].Trim().ToLowerInvariant();
                string code = parts[3].Trim().ToUpperInvariant();
                int priority = (parts.Length > 4 && int.TryParse(parts[4].Trim(), out int p)) ? p : 2;
                string notes = parts.Length > 5 ? parts[5].Trim() : string.Empty;

                if (!IsOfficialBorderType(code))
                {
                    warningLog?.Invoke($"تحذير: تم تجاهل صف الكلمات الدلالية '{kw}' لأن الكود '{code}' غير موجود في ورقة Border Types المعتمدة.");
                    continue;
                }

                BorderTypeKeywords.Add(new BorderTypeKeywordItem
                {
                    Keyword = kw,
                    AppliesTo = appliesTo,
                    MatchType = matchType,
                    BorderTypeCode = code,
                    Priority = priority,
                    Notes = notes
                });
                loaded++;
            }
        }
        catch (Exception ex)
        {
            warningLog?.Invoke($"خطأ أثناء قراءة ملف الكلمات الدلالية: {ex.Message}");
        }

        return loaded;
    }

    public static void EnsureBorderTypeKeywordsFile(string csvPath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(csvPath)) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Keyword,AppliesTo,MatchType,BorderTypeCode,Priority,Notes");
            sb.AppendLine("ممر مشاة,BOTH,contains,LNE,1,ممر مشاة");
            sb.AppendLine("ممر مشاه,BOTH,contains,LNE,1,ممر مشاه");
            sb.AppendLine("حديقة,BOTH,contains,PAK,1,حديقة");
            sb.AppendLine("حديقه,BOTH,contains,PAK,1,حديقة عامة");
            sb.AppendLine("مسجد,BOTH,contains,MSQ,1,مسجد");
            sb.AppendLine("جامع,BOTH,contains,MSQ,1,جامع");
            sb.AppendLine("مصلى,BOTH,contains,MSQ,1,مصلى");
            sb.AppendLine("ممر,BOTH,contains,HLW,2,ممر");
            sb.AppendLine("خدمات,BOTH,contains,SRV,2,خدمات");
            sb.AppendLine("مرفق,BOTH,contains,UTL,2,مرفق");
            sb.AppendLine("مرافق,BOTH,contains,UTL,2,مرافق");
            sb.AppendLine("شارع,BOTH,contains,STR,2,شارع");
            sb.AppendLine("طريق,BOTH,contains,ROD,2,طريق");
            sb.AppendLine("مواقف,BOTH,contains,PRK,2,مواقف");
            sb.AppendLine("موقف,BOTH,contains,PRK,2,موقف");
            sb.AppendLine("نافذ,BOTH,contains,PNT,2,نافذ");
            sb.AppendLine("غرفة كهرباء,BOTH,contains,ECR,2,غرفة كهرباء");
            sb.AppendLine("ساحة,BOTH,contains,YRD,2,ساحة");
            sb.AppendLine("أرض فضاء,BOTH,contains,LAN,2,أرض فضاء");
            sb.AppendLine("القطعة,BOTH,exact,PLT,3,القطعة");
            sb.AppendLine("قطعة,BOTH,contains,PLT,3,القطعة");
            File.WriteAllText(csvPath, sb.ToString(), System.Text.Encoding.UTF8);
        }
        catch { }
    }

    public static (string? Code, string? MatchedKeyword, double Score) MatchBorderTypeFromKeywords(
        string? text,
        string fieldType = "REMARKS",
        double threshold = 0.5)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null, 0.0);

        string raw = text.Trim();
        string upper = raw.ToUpperInvariant();

        // 1. Direct match with official code
        if (IsOfficialBorderType(upper))
        {
            return (upper, raw, 1.0);
        }

        string normInput = Utils.ArabicTextNormalizer.Normalize(raw, normalizeTehMarbuta: true).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normInput))
            return (null, null, 0.0);

        // Filter and sort keywords
        var candidates = BorderTypeKeywords
            .Where(k => k.AppliesTo.Equals("BOTH", StringComparison.OrdinalIgnoreCase) ||
                        k.AppliesTo.Equals(fieldType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k.Priority)
            .ThenByDescending(k => k.Keyword.Length)
            .ToList();

        var matches = new List<(string Code, string Keyword, double Score)>();

        foreach (var item in candidates)
        {
            string normKw = Utils.ArabicTextNormalizer.Normalize(item.Keyword, normalizeTehMarbuta: true).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normKw)) continue;

            if (item.MatchType.Equals("exact", StringComparison.OrdinalIgnoreCase))
            {
                if (normInput == normKw)
                {
                    matches.Add((item.BorderTypeCode, item.Keyword, 1.0));
                }
            }
            else // contains
            {
                if (normInput == normKw)
                {
                    matches.Add((item.BorderTypeCode, item.Keyword, 1.0));
                }
                else if (normInput.Contains(normKw) || normKw.Contains(normInput))
                {
                    double score = (double)Math.Min(normInput.Length, normKw.Length) / Math.Max(normInput.Length, normKw.Length);
                    if (score >= threshold)
                    {
                        matches.Add((item.BorderTypeCode, item.Keyword, score));
                    }
                }
            }
        }

        if (matches.Count > 0)
        {
            var topMatches = matches.OrderByDescending(m => m.Score).ToList();
            var best = topMatches[0];

            // Ambiguity check: if top matches have different codes and score gap <= 0.05
            var conflicting = topMatches.Where(m => !m.Code.Equals(best.Code, StringComparison.OrdinalIgnoreCase) && (best.Score - m.Score) <= 0.05).ToList();
            if (conflicting.Count > 0)
            {
                // Ambiguous match -> do not guess, return null for manual review
                return (null, null, best.Score);
            }

            return (best.Code, best.Keyword, best.Score);
        }

        // Fallback to general matching
        return MatchBestBorderType(text, threshold);
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

            // 3. Load BorderType_Keywords.csv
            string kwFile = Path.Combine(lookupsDir, "BorderType_Keywords.csv");
            LoadBorderTypeKeywords(kwFile);

            // 4. Load Usage_Keywords.csv
            string uKwFile = Path.Combine(lookupsDir, "Usage_Keywords.csv");
            LoadUsageKeywords(uKwFile);
        }
        catch { }
    }

    public static readonly List<UsageKeywordItem> UsageKeywords = new();

    public static int LoadUsageKeywords(string csvPath, Action<string>? warningLog = null)
    {
        UsageKeywords.Clear();
        if (string.IsNullOrWhiteSpace(csvPath)) return 0;

        if (!File.Exists(csvPath))
        {
            EnsureUsageKeywordsFile(csvPath);
        }

        if (!File.Exists(csvPath)) return 0;

        int loaded = 0;
        try
        {
            var lines = File.ReadAllLines(csvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (parts.Length < 4) continue;

                string kw = parts[0].Trim();
                string appliesTo = parts[1].Trim().ToUpperInvariant();
                string matchType = parts[2].Trim().ToLowerInvariant();
                string code = parts[3].Trim().ToUpperInvariant();
                int priority = (parts.Length > 4 && int.TryParse(parts[4].Trim(), out int p)) ? p : 2;
                string notes = parts.Length > 5 ? parts[5].Trim() : string.Empty;

                if (!IsOfficialUsageType(code))
                {
                    warningLog?.Invoke($"تحذير: تم تجاهل صف كلمات الاستخدام الدلالية '{kw}' لأن الكود '{code}' غير موجود في ورقة Usage Types المعتمدة.");
                    continue;
                }

                UsageKeywords.Add(new UsageKeywordItem
                {
                    Keyword = kw,
                    AppliesTo = appliesTo,
                    MatchType = matchType,
                    UsageCode = code,
                    Priority = priority,
                    Notes = notes
                });
                loaded++;
            }
        }
        catch (Exception ex)
        {
            warningLog?.Invoke($"خطأ أثناء قراءة ملف كلمات الاستخدام الدلالية: {ex.Message}");
        }

        return loaded;
    }

    public static void EnsureUsageKeywordsFile(string csvPath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(csvPath)) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Keyword,AppliesTo,MatchType,UsageCode,Priority,Notes");
            sb.AppendLine("سكني,BOTH,contains,RSD,1,سكني");
            sb.AppendLine("تجاري,BOTH,contains,COM,1,تجاري");
            sb.AppendLine("سكني تجاري,BOTH,contains,RSC,1,سكني تجاري");
            sb.AppendLine("تجاري سكني,BOTH,contains,RSC,1,تجاري سكني");
            sb.AppendLine("زراعي,BOTH,contains,AGR,1,زراعي");
            sb.AppendLine("صناعي,BOTH,contains,IND,1,صناعي");
            sb.AppendLine("مرافق,BOTH,contains,UTL,1,مرافق");
            sb.AppendLine("مرفق,BOTH,contains,UTL,1,مرفق");
            sb.AppendLine("خدمات,BOTH,contains,UTL,2,خدمات");
            File.WriteAllText(csvPath, sb.ToString(), System.Text.Encoding.UTF8);
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

public class BorderTypeKeywordItem
{
    public string Keyword { get; set; } = string.Empty;
    public string AppliesTo { get; set; } = "BOTH"; // REMARKS, USAGE, BOTH
    public string MatchType { get; set; } = "contains"; // exact, contains
    public string BorderTypeCode { get; set; } = string.Empty;
    public int Priority { get; set; } = 1;
    public string Notes { get; set; } = string.Empty;
}

public class UsageKeywordItem
{
    public string Keyword { get; set; } = string.Empty;
    public string AppliesTo { get; set; } = "BOTH"; // REMARKS, USAGE, BOTH
    public string MatchType { get; set; } = "contains"; // exact, contains
    public string UsageCode { get; set; } = string.Empty;
    public int Priority { get; set; } = 1;
    public string Notes { get; set; } = string.Empty;
}
