using System;
using System.Collections.Generic;
using System.Text;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

public class TextLengthIssue
{
    public string EntityType { get; set; } = "Border"; // Parcel or Border
    public string EntityId { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string OriginalValue { get; set; } = string.Empty;
    public int Utf8ByteLength { get; set; }
    public int MaxAllowedBytes { get; set; }
    public string SafeTruncatedValue { get; set; } = string.Empty;
    public string? SuggestedShorterValue { get; set; }
    public bool WillBeTruncated => Utf8ByteLength > MaxAllowedBytes;
}

public static class DbfTextLengthService
{
    // Standard MOMRAH template field max byte widths (from helpers/Templet/border.shp and PARCEL.shp)
    public static readonly Dictionary<string, int> DefaultBorderFieldWidths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PLOT_ID"] = 50,
        ["PLAN_NO"] = 50,
        ["BORDERTYPE"] = 20,
        ["DIMENSION"] = 20,
        ["DESCRIPTIO"] = 100,
        ["DESCRIPTO"] = 100
    };

    public static readonly Dictionary<string, int> DefaultParcelFieldWidths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NEIGHBOURH"] = 50,
        ["REGION_ID"] = 10,
        ["CITY_ID"] = 10,
        ["ADDRESS"] = 254,
        ["USAGE"] = 50,
        ["REMARKS"] = 100,
        ["LAYOUT"] = 50,
        ["PLOT_ID"] = 50,
        ["BLOCK_NO"] = 50,
        ["PLAN_NO"] = 50,
        ["PLOT_NO1"] = 50,
        ["REQUEST_TY"] = 50,
        ["REQUEST_NO"] = 50,
        ["REPORT_NO"] = 50
    };

    public static int GetUtf8ByteLength(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return Encoding.UTF8.GetByteCount(text);
    }

    /// <summary>
    /// Safely truncates a UTF-8 string to a maximum byte limit, ensuring the cut
    /// happens at a whole Unicode character (code point / rune) boundary, NEVER mid-character.
    /// </summary>
    public static string TruncateUtf8Safe(string text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text) || maxBytes <= 0) return string.Empty;
        if (GetUtf8ByteLength(text) <= maxBytes) return text;

        var sb = new StringBuilder();
        int currentBytes = 0;

        for (int i = 0; i < text.Length; i++)
        {
            int charLen = 1;
            if (char.IsSurrogatePair(text, i))
            {
                charLen = 2;
            }

            string codePoint = text.Substring(i, charLen);
            int codePointBytes = Encoding.UTF8.GetByteCount(codePoint);

            if (currentBytes + codePointBytes > maxBytes)
            {
                break;
            }

            sb.Append(codePoint);
            currentBytes += codePointBytes;
            if (charLen == 2) i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Proposes a shorter phrasing for descriptions to avoid truncation, e.g.
    /// "شارع بعرض 12 م" -> "ش 12م" or "ممر بعرض 6 م" -> "م 6م"
    /// </summary>
    public static string SuggestShorterPhrasing(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return description;

        string shorter = description;
        shorter = shorter.Replace("شارع بعرض", "ش.");
        shorter = shorter.Replace("ممر بعرض", "ممر");
        shorter = shorter.Replace("قطعة رقم", "ق.");
        shorter = shorter.Replace("خدمات طرق وممرات", "خدمات");
        shorter = shorter.Replace("متر", "م");

        return shorter.Trim();
    }

    public static List<TextLengthIssue> CheckTextLengths(
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyList<BorderRecord> borders,
        IReadOnlyDictionary<string, int>? customParcelLimits = null,
        IReadOnlyDictionary<string, int>? customBorderLimits = null)
    {
        var issues = new List<TextLengthIssue>();
        var pLimits = customParcelLimits ?? DefaultParcelFieldWidths;
        var bLimits = customBorderLimits ?? DefaultBorderFieldWidths;

        // Check Borders
        foreach (var b in borders)
        {
            CheckBorderField(issues, b, "PLOT_ID", b.PlotId, bLimits);
            CheckBorderField(issues, b, "PLAN_NO", b.PlanNo, bLimits);
            CheckBorderField(issues, b, "BORDERTYPE", b.BorderType, bLimits);
            CheckBorderField(issues, b, "DIMENSION", b.Dimension, bLimits);
            CheckBorderField(issues, b, "DESCRIPTIO", b.Descriptio, bLimits);
        }

        // Check Parcels
        foreach (var p in parcels)
        {
            CheckParcelField(issues, p, "PLOT_ID", p.PlotId, pLimits);
            CheckParcelField(issues, p, "PLAN_NO", p.PlanNo, pLimits);
            CheckParcelField(issues, p, "NEIGHBOURH", p.Neighbourhood, pLimits);
            CheckParcelField(issues, p, "ADDRESS", p.Address, pLimits);
            CheckParcelField(issues, p, "USAGE", p.Usage, pLimits);
            CheckParcelField(issues, p, "REMARKS", p.Remarks, pLimits);
        }

        return issues;
    }

    private static void CheckBorderField(List<TextLengthIssue> issues, BorderRecord b, string fieldName, string? value, IReadOnlyDictionary<string, int> limits)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (limits.TryGetValue(fieldName, out int maxBytes))
        {
            int byteLen = GetUtf8ByteLength(value);
            if (byteLen > maxBytes)
            {
                issues.Add(new TextLengthIssue
                {
                    EntityType = "Border",
                    EntityId = $"{b.PlotId}_{b.BorderName}",
                    FieldName = fieldName,
                    OriginalValue = value,
                    Utf8ByteLength = byteLen,
                    MaxAllowedBytes = maxBytes,
                    SafeTruncatedValue = TruncateUtf8Safe(value, maxBytes),
                    SuggestedShorterValue = SuggestShorterPhrasing(value)
                });
            }
        }
    }

    private static void CheckParcelField(List<TextLengthIssue> issues, ParcelRecord p, string fieldName, string? value, IReadOnlyDictionary<string, int> limits)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (limits.TryGetValue(fieldName, out int maxBytes))
        {
            int byteLen = GetUtf8ByteLength(value);
            if (byteLen > maxBytes)
            {
                issues.Add(new TextLengthIssue
                {
                    EntityType = "Parcel",
                    EntityId = p.PlotId,
                    FieldName = fieldName,
                    OriginalValue = value,
                    Utf8ByteLength = byteLen,
                    MaxAllowedBytes = maxBytes,
                    SafeTruncatedValue = TruncateUtf8Safe(value, maxBytes),
                    SuggestedShorterValue = SuggestShorterPhrasing(value)
                });
            }
        }
    }
}
