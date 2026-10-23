using System.Text.RegularExpressions;

namespace Cad2Gis.Core.Utils;

public static class ArabicTextNormalizer
{
    private static readonly Regex DiacriticsRegex = new(@"[\u064B-\u065F\u0670]", RegexOptions.Compiled);
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string input, bool normalizeTehMarbuta = false)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Remove diacritics (Tashkeel)
        string text = DiacriticsRegex.Replace(input, "");

        // Remove Tatweel (Kashida)
        text = text.Replace("\u0640", "");

        // Normalize Alef forms
        text = text.Replace('أ', 'ا')
                   .Replace('إ', 'ا')
                   .Replace('آ', 'ا')
                   .Replace('ٱ', 'ا');

        // Normalize Alef Maqsura to Yeh
        text = text.Replace('ى', 'ي');

        if (normalizeTehMarbuta)
        {
            text = text.Replace('ة', 'ه');
        }

        // Clean invisible control characters and multiple whitespaces
        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return MultipleSpacesRegex.Replace(text, " ").Trim();
    }
}
