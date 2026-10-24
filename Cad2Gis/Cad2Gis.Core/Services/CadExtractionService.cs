using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

/// <summary>
/// Service for cataloging CAD drawing layers from AutoCAD .dwg / .dxf files
/// and intelligently inferring cadastral roles based on MOMRAH / Balady standards.
/// </summary>
public static class CadExtractionService
{
    /// <summary>
    /// Unified extractor for any CAD drawing (.dwg or .dxf).
    /// </summary>
    public static List<CadLayerInfo> ExtractLayers(string cadPath, string? helperFilesDir = null)
    {
        if (string.IsNullOrWhiteSpace(cadPath) || !File.Exists(cadPath))
            return new List<CadLayerInfo>();

        string ext = Path.GetExtension(cadPath).ToLowerInvariant();
        if (ext == ".dxf")
        {
            return ExtractLayersFromDxf(cadPath);
        }
        else
        {
            return ExtractLayersFromDwg(cadPath, helperFilesDir);
        }
    }

    /// <summary>
    /// Reads a DXF file stream and extracts unique CAD layer names with entity counts.
    /// In the DXF specification, Group Code 8 designates the layer name for any entity.
    /// </summary>
    public static List<CadLayerInfo> ExtractLayersFromDxf(string dxfPath)
    {
        var list = new List<CadLayerInfo>();
        if (string.IsNullOrWhiteSpace(dxfPath) || !File.Exists(dxfPath))
            return list;

        try
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using var reader = new StreamReader(dxfPath);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Trim() == "8")
                {
                    string? layerVal = reader.ReadLine()?.Trim();
                    if (!string.IsNullOrWhiteSpace(layerVal))
                    {
                        counts[layerVal] = counts.TryGetValue(layerVal, out int c) ? c + 1 : 1;
                    }
                }
            }

            foreach (var kvp in counts.OrderBy(k => k.Key))
            {
                var role = InferRoleFromName(kvp.Key);
                var cat = CadLayerInfo.DetermineCategory(role, kvp.Key);

                var item = new CadLayerInfo
                {
                    LayerName = kvp.Key,
                    EntityCount = kvp.Value,
                    Role = role,
                    Category = cat,
                    IsSelected = (cat != "Ignore Layers" && cat != "Roads & References" && role != LayerRole.Ignore)
                };
                EnrichCadLayerMetadata(item);
                list.Add(item);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to extract DXF layers: {ex.Message}");
        }

        return list;
    }

    /// <summary>
    /// Extracts layers from AutoCAD .dwg file using ArcPy Python script,
    /// with binary scan fallback.
    /// </summary>
    public static List<CadLayerInfo> ExtractLayersFromDwg(string dwgPath, string? helperFilesDir = null)
    {
        var list = new List<CadLayerInfo>();
        if (string.IsNullOrWhiteSpace(dwgPath) || !File.Exists(dwgPath))
            return list;

        // 1. Try ArcPy python script
        string? scriptPath = ResolveScriptPath(helperFilesDir);
        string? pythonExe = ResolvePythonExe();

        if (scriptPath != null && pythonExe != null)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" \"{dwgPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(30000);

                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        int jsonStart = output.IndexOf('[');
                        int jsonEnd = output.LastIndexOf(']');
                        if (jsonStart >= 0 && jsonEnd > jsonStart)
                        {
                            string json = output.Substring(jsonStart, jsonEnd - jsonStart + 1);
                            using var doc = JsonDocument.Parse(json);
                            foreach (var element in doc.RootElement.EnumerateArray())
                            {
                                string name = element.GetProperty("LayerName").GetString() ?? "";
                                int count = element.GetProperty("EntityCount").GetInt32();
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    string geom = element.TryGetProperty("GeometryType", out var g) ? g.GetString() ?? "Polygon" : "Polygon";
                                    string color = element.TryGetProperty("Color", out var col) ? col.GetString() ?? "#FACC15" : "#FACC15";
                                    string rawText = element.TryGetProperty("RawCadText", out var rt) ? rt.GetString() ?? "" : "";
                                    string decodedText = element.TryGetProperty("DecodedArabicText", out var dt) ? dt.GetString() ?? "" : "";
                                    string cat = element.TryGetProperty("Category", out var ct) ? ct.GetString() ?? "Main Parcels" : "Main Parcels";
                                    string roleStr = element.TryGetProperty("Role", out var rl) ? rl.GetString() ?? "" : "";
                                    
                                    LayerRole role = Enum.TryParse<LayerRole>(roleStr, true, out var parsedRole)
                                        ? parsedRole
                                        : InferRoleFromName(name);

                                    var item = new CadLayerInfo
                                    {
                                        LayerName = name,
                                        EntityCount = count,
                                        GeometryType = geom,
                                        Color = color,
                                        RawCadText = rawText,
                                        DecodedArabicText = decodedText,
                                        Role = role,
                                        Category = cat,
                                        IsSelected = (cat != "Ignore Layers" && cat != "Roads & References" && role != LayerRole.Ignore)
                                    };
                                    EnrichCadLayerMetadata(item);
                                    list.Add(item);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DWG extraction via Python error: {ex.Message}");
            }
        }

        // 2. Fallback: Fast Binary Scan of DWG if script didn't return layers
        if (list.Count == 0)
        {
            list = ExtractLayersFromDwgBinaryFallback(dwgPath);
        }

        return list;
    }

    public static string? ResolvePythonExe()
    {
        string[] candidates = new[]
        {
            @"C:\Program Files\ArcGIS\Pro\bin\Python\envs\arcgispro-py3\python.exe",
            @"C:\Program Files\ArcGIS\Pro\bin\Python\python.exe",
            @"C:\Python314\python.exe",
            "python.exe",
            "python"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return "python.exe";
    }

    private static string? ResolveScriptPath(string? helperFilesDir)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(helperFilesDir))
        {
            candidates.Add(Path.Combine(helperFilesDir, "scripts", "extract_cad_layers.py"));
        }
        string addinDir = Path.GetDirectoryName(typeof(CadExtractionService).Assembly.Location) ?? "";
        if (!string.IsNullOrWhiteSpace(addinDir))
        {
            candidates.Add(Path.Combine(addinDir, "HelperFiles", "scripts", "extract_cad_layers.py"));
            candidates.Add(Path.Combine(addinDir, "..", "HelperFiles", "scripts", "extract_cad_layers.py"));
            candidates.Add(Path.Combine(addinDir, "..", "scripts", "extract_cad_layers.py"));
        }
        string? curBase = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6 && curBase != null; i++)
        {
            string cand = Path.Combine(curBase, "HelperFiles", "scripts", "extract_cad_layers.py");
            if (!candidates.Contains(cand, StringComparer.OrdinalIgnoreCase))
                candidates.Add(cand);
            curBase = Directory.GetParent(curBase)?.FullName;
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Pure C# binary scanner for DWG strings to extract layer tables without external dependencies.
    /// </summary>
    public static List<CadLayerInfo> ExtractLayersFromDwgBinaryFallback(string dwgPath)
    {
        var foundLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            byte[] bytes = File.ReadAllBytes(dwgPath);

            // Scan ASCII strings
            var sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                if ((b >= 32 && b <= 126) || b > 127) // printable ASCII + Extended/Arabic UTF-8
                {
                    sb.Append((char)b);
                }
                else
                {
                    if (sb.Length >= 2 && sb.Length <= 45)
                    {
                        string str = sb.ToString().Trim();
                        if (IsValidCadLayerName(str))
                        {
                            foundLayers.Add(str);
                        }
                    }
                    sb.Clear();
                }
            }

            // Scan UTF-16LE strings (standard in AutoCAD 2007+ DWG files)
            for (int offset = 0; offset <= 1; offset++)
            {
                var sb16 = new StringBuilder();
                for (int i = offset; i < bytes.Length - 1; i += 2)
                {
                    byte b1 = bytes[i];
                    byte b2 = bytes[i + 1];
                    if (b2 == 0 && (b1 >= 32 && b1 <= 126))
                    {
                        sb16.Append((char)b1);
                    }
                    else
                    {
                        if (sb16.Length >= 2 && sb16.Length <= 45)
                        {
                            string str = sb16.ToString().Trim();
                            if (IsValidCadLayerName(str))
                            {
                                foundLayers.Add(str);
                            }
                        }
                        sb16.Clear();
                    }
                }
            }
        }
        catch { }

        return foundLayers.OrderBy(x => x).Select(name =>
        {
            var role = InferRoleFromName(name);
            var cat = CadLayerInfo.DetermineCategory(role, name);

            var item = new CadLayerInfo
            {
                LayerName = name,
                EntityCount = 1,
                Role = role,
                Category = cat,
                IsSelected = (cat != "Ignore Layers" && cat != "Roads & References" && role != LayerRole.Ignore)
            };
            EnrichCadLayerMetadata(item);
            return item;
        }).ToList();
    }

    /// <summary>
    /// Intelligently enriches CAD layers with colors, geometry icons, and preview text
    /// matching MOMRAH and cadastral UI specifications.
    /// </summary>
    public static void EnrichCadLayerMetadata(CadLayerInfo layer)
    {
        string l = layer.LayerName.ToLowerInvariant();

        // Color enrichment
        if (string.IsNullOrWhiteSpace(layer.Color) || layer.Color == "#808080" || layer.Color == "#FACC15")
        {
            if (l.Contains("residential") || l.Contains("سكني") || l.Contains("01 res"))
                layer.Color = "#FACC15"; // Yellow
            else if (l.Contains("commercial") || l.Contains("تجاري") || l.Contains("الاوجه"))
                layer.Color = "#D946EF"; // Magenta
            else if (l.Contains("service") || l.Contains("خدمات") || l.Contains("serv"))
                layer.Color = "#06B6D4"; // Cyan
            else if (l.Contains("park") || l.Contains("garden") || l.Contains("government") || l.Contains("حكومي") || l.Contains("حدائق"))
                layer.Color = "#22C55E"; // Green
            else if (l.Contains("utility") || l.Contains("easement") || l.Contains("مرافق") || l.Contains("مواقف") || l.Contains("parking"))
                layer.Color = "#86EFAC"; // Light Green
            else if (l.Contains("water") || l.Contains("canal") || l.Contains("قناة") || l.Contains("مجرى"))
                layer.Color = "#38BDF8"; // Sky Blue
            else if (l.Contains("road") || l.Contains("street") || l.Contains("pathes") || l.Contains("شوارع") || l.Contains("ممرات"))
                layer.Color = "#94A3B8"; // Slate
            else if (l.Contains("dim") || l.Contains("ابعاد"))
                layer.Color = "#F59E0B"; // Amber
            else
                layer.Color = "#FACC15";
        }

        // Geometry Type enrichment
        if (string.IsNullOrWhiteSpace(layer.GeometryType) || layer.GeometryType == "Polygon")
        {
            if (l.Contains("commercial"))
                layer.GeometryType = "Line";
            else if (l.Contains("service_lane") || (l.Contains("service") && layer.EntityCount < 10) || l.Contains("point"))
                layer.GeometryType = "Point";
            else if (l.Contains("government") || l.Contains("icon") || l.Contains("bld"))
                layer.GeometryType = "Point";
            else if (l.Contains("road") || l.Contains("street") || l.Contains("line") || l.Contains("dim") || l.Contains("pathes") || l.Contains("تنظيم"))
                layer.GeometryType = "Line";
            else if (l.Contains("text") || l.Contains("number") || l.Contains("ترقيم"))
                layer.GeometryType = "Text";
            else
                layer.GeometryType = "Polygon";
        }

        // Live Preview text enrichment if empty
        if (string.IsNullOrWhiteSpace(layer.RawCadText))
        {
            if (l.Contains("residential") || l.Contains("01 res"))
            {
                layer.RawCadText = "S_RES_01";
                layer.DecodedArabicText = "سكني - 1";
            }
            else if (l.Contains("commercial") || l.Contains("03 res"))
            {
                layer.RawCadText = "S_RES_01";
                layer.DecodedArabicText = "سكني - 1";
            }
            else if (l.Contains("service_lane") || l.Contains("services") || l.Contains("خدمات"))
            {
                layer.RawCadText = "SERV_LANE";
                layer.DecodedArabicText = "ممر خدمة";
            }
            else if (l.Contains("الاوجه"))
            {
                layer.RawCadText = "COMM_FRONT";
                layer.DecodedArabicText = "واجهات تجارية";
            }
        }
    }

    private static bool IsValidCadLayerName(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;
        if (str.Length < 2 || str.Length > 50) return false;
        if (str.Contains('<') || str.Contains('>') || str.Contains('\\') || str.Contains('/') || str.Contains(':') || str.Contains('"') || str.Contains('\0') || str.Contains('|'))
            return false;

        string lower = str.ToLowerInvariant();
        if (lower.StartsWith("acdb") || lower.StartsWith("section") || lower.StartsWith("header") || lower.StartsWith("block") || lower.StartsWith("table"))
            return false;

        return lower.Contains("parcel") || lower.Contains("plot") || lower.Contains("road") ||
               lower.Contains("street") || lower.Contains("dim") || lower.Contains("text") ||
               lower.Contains("residential") || lower.Contains("comm") || lower.Contains("serv") ||
               lower.Contains("garden") || lower.Contains("park") || lower.Contains("mosque") ||
               lower.Contains("canal") || lower.Contains("water") || lower.Contains("pathes") ||
               lower.Contains("deed") || lower.Contains("room") || lower.Contains("area") ||
               lower.Contains("hatch") || lower.Contains("number") || lower.Contains("حدود") ||
               lower.Contains("قطع") || lower.Contains("اراضي") || lower.Contains("شوارع") ||
               lower.Contains("ممرات") || lower.Contains("ابعاد") || lower.Contains("تنظيم") ||
               lower.Contains("تجاري") || lower.Contains("سكني") || lower.Contains("خدمات");
    }

    /// <summary>
    /// Maps Arabic and English CAD layer names to cadastral functional roles.
    /// </summary>
    public static LayerRole InferRoleFromName(string layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName)) return LayerRole.Ignore;
        string l = layerName.ToLowerInvariant().Trim();
        string trimmed = l.TrimEnd('.');

        // 1. Plot Numbers / Texts (checked before Boundary so PLOT_NUMBERS or ارقام_القطع are not classified as Boundary)
        if (l.Contains("num") || l.Contains("text") || l.Contains("memo") || l.Contains("txt") ||
            l.Contains("رقم") || l.Contains("ارقام") || l.Contains("أرقام") || l.Contains("ترقيم") || l.Contains("نصوص"))
            return LayerRole.PlotText;

        // 2. Land Use / Activities (excluding mixed-use parcel layers)
        if ((l.Contains("use") || l.Contains("land") || l.Contains("usage") ||
            l.Contains("استعمال") || l.Contains("نشاط") || l.Contains("انشطة") || l.Contains("أنشطة"))
            && !l.Contains("mixed"))
            return LayerRole.UsageText;

        // 3. Dimensions / Chamfers
        if (l.Contains("dim") || l.Contains("meas") ||
            l.Contains("ابعاد") || l.Contains("أبعاد") || l.Contains("شطف") || l.Contains("شطفات") || l.Contains("اضلاع") || l.Contains("أضلاع"))
            return LayerRole.Dimensions;

        // 4. Roads / Streets / Passages
        if (l.Contains("road") || l.Contains("street") || l.Contains("str") ||
            l.Contains("شارع") || l.Contains("طريق") || l.Contains("ممر") || l.Contains("ممرات") || l.Contains("محاور") || l.Contains("pathes"))
            return LayerRole.Street;

        // 5. Water Channels
        if (l.Contains("water") || l.Contains("canal") || l.Contains("قناة") || l.Contains("مجرى") || l.Contains("وادي"))
            return LayerRole.WaterChannel;

        // 6. Services / Facilities
        if (trimmed == "msq" || trimmed.StartsWith("msq") || trimmed == "kh" || trimmed.StartsWith("kh") ||
            l.Contains("serv") || l.Contains("util") ||
            l.Contains("خدمات") || l.Contains("خدمة") || l.Contains("مرفق") || l.Contains("مرافق") || l.Contains("مسجد") || l.Contains("مساجد") || l.Contains("جامع") || l.Contains("mosq") || l.Contains("حديقة") || l.Contains("حدائق") || l.Contains("garden") || l.Contains("park") || l.Contains("room") || l.Contains("parking") || l.Contains("hospital") || l.Contains("مستشفى") || l.Contains("school") || l.Contains("مدرسة") || l.Contains("حكومي") || l.Contains("مواقف") || l.Contains("كهرباء"))
            return LayerRole.Service;

        // 7. Boundary / Parcels / Plots
        if (trimmed == "s" || trimmed == "gr" || trimmed == "t" || trimmed == "st" ||
            trimmed.StartsWith("s1") || trimmed.StartsWith("s2") || trimmed.StartsWith("s3") || trimmed.StartsWith("s4") || trimmed.StartsWith("s5") || trimmed.StartsWith("s_") || trimmed.StartsWith("s-") ||
            trimmed.StartsWith("t1") || trimmed.StartsWith("t2") || trimmed.StartsWith("t3") || trimmed.StartsWith("t_") || trimmed.StartsWith("t-") ||
            trimmed.StartsWith("gr_") || trimmed.StartsWith("gr-") || trimmed.StartsWith("ground") ||
            l.Contains("outline") || l.Contains("inner") || l.Contains("outer") || l.Contains("border") ||
            l.Contains("bound") || l.Contains("parcel") || l.Contains("plot") || l.Contains("residential") || l.Contains("commercial") || l.Contains("comm") || l.Contains("mixed") ||
            l.Contains("حدود") || l.Contains("قطع") || l.Contains("قطعة") || l.Contains("اراضي") || l.Contains("أراضي") || l.Contains("مخطط") ||
            l.Contains("سكني") || l.Contains("تجاري") || l.Contains("استثماري") || l.Contains("الاوجه") || l.Contains("الاجهه") || l.Contains("واجهات"))
            return LayerRole.Boundary;

        return LayerRole.Ignore;
    }
}
