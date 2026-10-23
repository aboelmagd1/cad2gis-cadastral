using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Cad2Gis.Core.Encodings;

public static class XArabDecoder
{
    private static readonly Dictionary<char, string> TranslationMap = new()
    {
        ['H'] = "ا", ['h'] = "ا", ['f'] = "ب", ['F'] = "ب", ['B'] = "ب",
        ['j'] = "ت", ['J'] = "ت", ['e'] = "ث", ['E'] = "ث", ['{'] = "ج",
        ['p'] = "ح", ['o'] = "خ", [']'] = "د", ['}'] = "ذ", ['v'] = "ر",
        ['V'] = "ر", ['<'] = "ز", ['>'] = "ز", ['s'] = "س", ['a'] = "ش",
        ['w'] = "ص", ['q'] = "ض", ['Q'] = "ض", ['\''] = "ط", ['/'] = "ظ",
        ['u'] = "ع", ['U'] = "ع", ['y'] = "غ", ['t'] = "ف", ['T'] = "ف",
        ['r'] = "ق", ['R'] = "ق", [':'] = "ك", [';'] = "ك", ['g'] = "ل",
        ['G'] = "ل", ['l'] = "م", ['L'] = "م", ['k'] = "ن", ['K'] = "ن",
        ['i'] = "ه", ['I'] = "ه", [','] = "و", ['d'] = "ي", ['D'] = "ي",
        ['m'] = "ة", ['M'] = "ة", ['z'] = "ئ", ['c'] = "ؤ", ['x'] = "ء",
        ['n'] = "ى", ['b'] = "لا"
    };

    public static string Decode(string rawText, bool preserveSlash = true, bool reverseText = false)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;
        string text = rawText.Replace("\r", "").Replace("\n", "");

        const string slashPlaceholder = "\uE000";

        // Protect plot numbers and numeric sequences before reversing text so numbers keep correct reading order
        var numTokens = new List<string>();
        if (reverseText)
        {
            text = Regex.Replace(text, @"\d+(?:/\d+)*", m =>
            {
                string placeholder = $"\uE001{numTokens.Count}\uE001";
                numTokens.Add(m.Value);
                return placeholder;
            });

            char[] arr = text.ToCharArray();
            Array.Reverse(arr);
            text = new string(arr);

            // Restore numeric tokens maintaining their original internal reading order
            text = Regex.Replace(text, @"\uE001(\d+)\uE001", m =>
            {
                char[] idxChars = m.Groups[1].Value.ToCharArray();
                Array.Reverse(idxChars);
                int idx = int.Parse(new string(idxChars));
                return numTokens[idx];
            });
        }

        if (preserveSlash)
        {
            // Protect slashes strictly between digits only (e.g. 11/3314 or 1420/200)
            text = Regex.Replace(text, @"(?<=\d)/(?=\d)", slashPlaceholder);
        }

        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (TranslationMap.TryGetValue(c, out string? rep))
            {
                sb.Append(rep);
            }
            else if (c != '_' && c != '(' && c != ')')
            {
                sb.Append(c);
            }
        }

        string result = sb.ToString();
        if (preserveSlash)
        {
            result = result.Replace(slashPlaceholder, "/");
        }

        return Regex.Replace(result, @"\s+", " ").Trim();
    }
}
