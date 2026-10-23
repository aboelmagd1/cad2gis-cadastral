using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cad2Gis.Core.Utils;

/// <summary>
/// High-fidelity decoder for legacy CAD Arabic text (XArab, reversed strings, MText formatting).
/// Used for real-time live preview of CAD layer texts in Cad2Gis.
/// </summary>
public static class ArabicCadDecoder
{
    private static readonly Regex MTextFormattingRegex = new(@"\\[a-zA-Z][^;]*;|[{}\r\n\\]", RegexOptions.Compiled);
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);

    // Standard XArab character substitution table
    private static readonly Dictionary<char, char> XArabMap = new()
    {
        { 'H', 'ا' }, { 'h', 'ا' }, { 'f', 'ب' }, { 'F', 'ب' }, { 'B', 'ب' },
        { 'j', 'ت' }, { 'J', 'ت' }, { 'e', 'ث' }, { 'E', 'ث' }, { '{', 'ج' },
        { 'p', 'ح' }, { 'o', 'خ' }, { ']', 'د' }, { '}', 'ذ' }, { 'v', 'ر' },
        { 'V', 'ر' }, { '<', 'ز' }, { '>', 'ز' }, { 's', 'س' }, { 'a', 'ش' },
        { 'w', 'ص' }, { 'q', 'ض' }, { 'Q', 'ض' }, { '\'', 'ط' }, { '/', 'ظ' },
        { 'u', 'ع' }, { 'U', 'ع' }, { 'y', 'غ' }, { 't', 'ف' }, { 'T', 'ف' },
        { 'r', 'ق' }, { 'R', 'ق' }, { ':', 'ك' }, { ';', 'ك' }, { 'g', 'ل' },
        { 'G', 'ل' }, { 'l', 'م' }, { 'L', 'م' }, { 'k', 'ن' }, { 'K', 'ن' },
        { 'i', 'ه' }, { 'I', 'ه' }, { ',', 'و' }, { 'd', 'ي' }, { 'D', 'ي' },
        { 'm', 'ة' }, { 'M', 'ة' }, { 'z', 'ئ' }, { 'c', 'ؤ' }, { 'x', 'ء' },
        { 'n', 'ى' }
    };

    public static string Decode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // 1. Strip CAD MText formatting codes
        string cleaned = MTextFormattingRegex.Replace(raw, " ");
        cleaned = MultipleSpacesRegex.Replace(cleaned, " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;

        // 2. Check if already contains Unicode Arabic
        bool hasArabic = cleaned.Any(c => c >= '\u0600' && c <= '\u06FF');
        if (hasArabic)
        {
            // Reverse if it was stored reversed in legacy CAD
            if (IsReversedArabic(cleaned))
            {
                return ReverseString(cleaned);
            }
            return cleaned;
        }

        // 3. Attempt XArab transliteration
        int xarabMatches = cleaned.Count(c => XArabMap.ContainsKey(c));
        if (xarabMatches >= 2 && xarabMatches >= (cleaned.Length * 0.35))
        {
            var sb = new StringBuilder();
            foreach (char c in cleaned)
            {
                sb.Append(XArabMap.TryGetValue(c, out char mapped) ? mapped : c);
            }
            string decoded = sb.ToString();
            // XArab is almost always stored right-to-left reversed
            return ReverseString(decoded);
        }

        return cleaned;
    }

    private static bool IsReversedArabic(string s)
    {
        // Simple heuristic: if ends with common prefixes or starts with common suffixes
        if (s.StartsWith("ة") || s.StartsWith("ات") || s.StartsWith("ين")) return true;
        return false;
    }

    private static string ReverseString(string s)
    {
        char[] arr = s.ToCharArray();
        Array.Reverse(arr);
        return new string(arr);
    }
}
