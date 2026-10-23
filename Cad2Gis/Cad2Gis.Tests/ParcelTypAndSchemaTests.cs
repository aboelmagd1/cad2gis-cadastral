using Xunit;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Tests;

public class ParcelTypAndSchemaTests
{
    [Fact]
    public void ParcelTyp_MainParcel_HasValueOne()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 1,
            PlotId = "101",
            Class = ParcelClass.Main
        };

        // Official template field: 1 = MAIN
        Assert.Equal((short)1, parcel.ParcelTyp);
    }

    [Fact]
    public void ParcelTyp_SubParcel_HasValueTwo()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 2,
            PlotId = "102",
            Class = ParcelClass.Sub
        };

        // Official template field: 2 = SUB
        Assert.Equal((short)2, parcel.ParcelTyp);
    }

    [Fact]
    public void InternalClassProperty_IsDistinctFromOfficialParcelTyp()
    {
        var main = new ParcelRecord { Class = ParcelClass.Main };
        var sub = new ParcelRecord { Class = ParcelClass.Sub };

        Assert.Equal(ParcelClass.Main, main.Class);
        Assert.Equal(1, main.ParcelTyp);

        Assert.Equal(ParcelClass.Sub, sub.Class);
        Assert.Equal(2, sub.ParcelTyp);
    }
}
