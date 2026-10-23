using System.IO;
using Xunit;
using Cad2Gis.Core.Lookups;

namespace Cad2Gis.Tests;

public class BorderTypeAndDimensionTests
{
    [Fact]
    public void BorderTypes_ContainsOfficialCodes_FromRequiredFieldsExcel()
    {
        // Must contain official municipal codes (67 codes in sheet)
        Assert.True(MunicipalLookups.IsOfficialBorderType("PLT")); // القطعة
        Assert.True(MunicipalLookups.IsOfficialBorderType("STR")); // شارع
        Assert.True(MunicipalLookups.IsOfficialBorderType("LNE")); // ممر
        Assert.True(MunicipalLookups.IsOfficialBorderType("MSQ")); // مسجد
        Assert.True(MunicipalLookups.IsOfficialBorderType("PAK")); // حديقة
        Assert.True(MunicipalLookups.IsOfficialBorderType("PRK")); // مواقف سيارات
        Assert.True(MunicipalLookups.IsOfficialBorderType("SRV")); // خدمات
    }

    [Fact]
    public void BorderTypes_NonOfficialCodes_AreTrackedAsUserMapped()
    {
        // WCH (water channel / مجرى سيل) has no official code in the sheet, so it is registered as user-mapped
        Assert.False(MunicipalLookups.IsOfficialBorderType("WCH"));
        Assert.True(MunicipalLookups.IsUserMappedBorderType("WCH"));

        // Description returned properly
        string desc = MunicipalLookups.GetBorderTypeDescription("WCH");
        Assert.Contains("قناة مائية", desc);
    }

    [Theory]
    [InlineData(2, "Single")]
    [InlineData(3, "Multi")]
    [InlineData(5, "Multi")]
    public void DimensionField_MatchesBenchmarkFormat_SingleOrMulti(int vertexCount, string expectedDimension)
    {
        // In the benchmark Border.shp in example/, DIMENSION contains Title-Cased "Single" and "Multi"
        string dimension = vertexCount <= 2 ? "Single" : "Multi";
        Assert.Equal(expectedDimension, dimension);
    }
}
