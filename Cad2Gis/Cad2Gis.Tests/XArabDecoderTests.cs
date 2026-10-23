using Xunit;
using Cad2Gis.Core.Encodings;

namespace Cad2Gis.Tests;

public class XArabDecoderTests
{
    [Fact]
    public void Decode_SimpleArabicText_TranslatesCorrectly()
    {
        // 'H' -> 'ا', 'f' -> 'ب', 'l' -> 'م', 'v' -> 'ر', 'd' -> 'ي'
        string encoded = "Hf";
        string result = XArabDecoder.Decode(encoded);
        Assert.Equal("اب", result);
    }

    [Fact]
    public void Decode_PreservesSlash_WhenConfigured()
    {
        string raw = "1420/200";
        string result = XArabDecoder.Decode(raw, preserveSlash: true);
        Assert.Contains("/", result);
    }

    [Fact]
    public void Decode_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, XArabDecoder.Decode(""));
        Assert.Equal(string.Empty, XArabDecoder.Decode("   "));
        Assert.Equal(string.Empty, XArabDecoder.Decode(null!));
    }
}
