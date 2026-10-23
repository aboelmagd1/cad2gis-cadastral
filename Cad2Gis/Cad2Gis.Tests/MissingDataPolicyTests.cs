using Xunit;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class MissingDataPolicyTests
{
    [Fact]
    public void ParcelRecord_InitialState_HasNoSilentDefaults()
    {
        var parcel = new ParcelRecord();

        // Must NOT default to Region 2, City 2, RSD, سكني, فرز
        Assert.Equal(string.Empty, parcel.RegionId);
        Assert.Equal(string.Empty, parcel.CityId);
        Assert.Equal(string.Empty, parcel.Usage);
        Assert.Equal(string.Empty, parcel.Remarks);
        Assert.Equal(string.Empty, parcel.RequestType);
        Assert.Equal(string.Empty, parcel.PlanNo);
    }

    [Fact]
    public void ProjectMetadata_InitialState_HasNoSilentDefaults()
    {
        var meta = new ProjectMetadata();

        Assert.Equal(string.Empty, meta.RegionId);
        Assert.Equal(string.Empty, meta.RegionName);
        Assert.Equal(string.Empty, meta.CityId);
        Assert.Equal(string.Empty, meta.CityName);
        Assert.Equal(string.Empty, meta.RequestType);
        Assert.Equal(string.Empty, meta.PlanNo);
        Assert.Equal(string.Empty, meta.Neighbourhood);
    }

    [Fact]
    public void BuildAddress_WithoutDefaults_LeavesMissingPartsClean()
    {
        var meta = new ProjectMetadata
        {
            Neighbourhood = "النسيم",
            CityName = "الرياض",
            RegionName = "الرياض"
        };

        string address = meta.BuildAddress();
        Assert.Equal("حي/النسيم/بالرياض/بمنطقة/الرياض", address);

        // When city and region are empty
        var emptyMeta = new ProjectMetadata { Neighbourhood = "حي تجريبي" };
        string emptyAddress = emptyMeta.BuildAddress();
        Assert.Equal("حي/حي تجريبي", emptyAddress);
        Assert.DoesNotContain("جدة", emptyAddress);
        Assert.DoesNotContain("مكة", emptyAddress);
    }

    [Fact]
    public void MissingDataPolicy_StrictBlank_LeavesUnsetFieldsBlank()
    {
        var parcel = new ParcelRecord();
        var meta = new ProjectMetadata(); // all empty

        MissingDataPolicy.ApplyDefaults(parcel, meta);

        Assert.Equal(string.Empty, parcel.RegionId);
        Assert.Equal(string.Empty, parcel.CityId);
        Assert.Equal(string.Empty, parcel.Usage);
        Assert.Equal(string.Empty, parcel.RequestType);
        Assert.NotEqual("2", parcel.RegionId);
        Assert.NotEqual("RSD", parcel.Usage);
    }
}
