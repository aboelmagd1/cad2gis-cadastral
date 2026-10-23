using Xunit;
using Cad2Gis.Core.Encodings;

namespace Cad2Gis.Tests;

public class XArabDecoderAdvancedTests
{
    [Fact]
    public void Decode_PlotNumberWithSlash_RemainsUnchanged()
    {
        // Item 6: A plot number 11/3314 stays unchanged with preserveSlash
        string plotNumber = "11/3314";

        string resultNormal = XArabDecoder.Decode(plotNumber, preserveSlash: true, reverseText: false);
        Assert.Equal("11/3314", resultNormal);

        string resultReversed = XArabDecoder.Decode(plotNumber, preserveSlash: true, reverseText: true);
        Assert.Equal("11/3314", resultReversed);
    }

    [Fact]
    public void Decode_EncodedWordWithSlashBetweenLetters_DecodesWithZha()
    {
        // In xArab, '/' maps to 'ظ' (Unicode \u0638).
        // For example, in xArab encoding:
        // 'n' -> 'ت', '/' -> 'ظ', 'l' -> 'م'
        // If '/' is between letters, it must NOT be treated as a slash; it must decode to 'ظ'.
        string encoded = "n/l"; // ت + ظ + م -> "تظم"

        string decodedWithoutReverse = XArabDecoder.Decode(encoded, preserveSlash: true, reverseText: false);
        Assert.Contains("ظ", decodedWithoutReverse);
        Assert.DoesNotContain("/", decodedWithoutReverse);

        string decodedWithReverse = XArabDecoder.Decode(encoded, preserveSlash: true, reverseText: true);
        Assert.Contains("ظ", decodedWithReverse);
        Assert.DoesNotContain("/", decodedWithReverse);
    }

    [Fact]
    public void Decode_DigitRuns_PreserveCorrectReadingOrder_WhenReverseTextIsTrue()
    {
        // A string containing numbers and Arabic words:
        // e.g. "قطعة 123" where "123" must stay "123", not reverse to "321"
        string input = "123/456";
        string result = XArabDecoder.Decode(input, preserveSlash: true, reverseText: true);
        Assert.Equal("123/456", result);
    }
}
