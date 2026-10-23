using Xunit;
using Cad2Gis.Core.Lookups;

namespace Cad2Gis.Tests;

public class MunicipalLookupsTests
{
    [Theory]
    [InlineData("STR", "شارع")]
    [InlineData("PLT", "القطعة")]
    [InlineData("SRV", "خدمات")]
    [InlineData("WCH", "قناة مائية / مجرى سيل")]
    [InlineData("MSQ", "مسجد")]
    public void BorderTypeDescriptions_ContainsOfficialCodes(string code, string expectedName)
    {
        string name = MunicipalLookups.GetBorderTypeName(code);
        Assert.Equal(expectedName, name);
    }

    [Theory]
    [InlineData(1, "مسلح حديث")]
    [InlineData(2, "مسلح قديم")]
    [InlineData(3, "شعبي")]
    [InlineData(4, "مهدم")]
    public void BuildingTypes_HasFourOfficialTypes(int code, string expected)
    {
        string name = MunicipalLookups.GetBuildingTypeName(code);
        Assert.Equal(expected, name);
    }

    [Fact]
    public void UsageToRemarks_MapsStandardUsages()
    {
        Assert.Equal("سكني", MunicipalLookups.GetRemarksForUsage("RSD"));
        Assert.Equal("تجاري أحياء", MunicipalLookups.GetRemarksForUsage("COM"));
        Assert.Equal("متعدد الاستخدام", MunicipalLookups.GetRemarksForUsage("Mixed Use"));
    }

    [Fact]
    public void CityLookup_ContainsMajorSaudiCities()
    {
        Assert.True(MunicipalLookups.Cities.ContainsKey("1")); // Riyadh
        Assert.True(MunicipalLookups.Cities.ContainsKey("2")); // Jeddah
        Assert.Equal("جدة", MunicipalLookups.Cities["2"].CityName);
        Assert.Equal("2", MunicipalLookups.Cities["2"].RegionId);
    }
}
