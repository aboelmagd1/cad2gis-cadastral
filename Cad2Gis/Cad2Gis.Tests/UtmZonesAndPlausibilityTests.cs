using System.Linq;
using Xunit;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class UtmZonesAndPlausibilityTests
{
    [Fact]
    public void SupportedUtmZones_Contains35NTo40N()
    {
        // Item 1: WGS 84 / UTM 35N to 40N = EPSG:32635 … 32640
        var zones = CrsProjectionService.SupportedUtmZones.ToList();

        Assert.Equal(6, zones.Count);
        Assert.Contains(32635, zones); // UTM 35N
        Assert.Contains(32636, zones); // UTM 36N
        Assert.Contains(32637, zones); // UTM 37N
        Assert.Contains(32638, zones); // UTM 38N
        Assert.Contains(32639, zones); // UTM 39N
        Assert.Contains(32640, zones); // UTM 40N
    }

    [Fact]
    public void PlausibilityCheck_Ranks38NFirst_ForRiyadhAreaCoordinate()
    {
        // Riyadh planar coordinate in UTM 38N (~46.7°E, ~24.7°N)
        // In UTM 38N: X ≈ 670000, Y ≈ 2733000
        double riyadhX = 670000.0;
        double riyadhY = 2733000.0;

        var ranked = CrsProjectionService.RankPlausibleUtmZones(riyadhX, riyadhY);

        Assert.NotEmpty(ranked);
        Assert.Equal(32638, ranked[0].Epsg); // 38N must be first
        Assert.True(ranked[0].IsPlausibleForSaudi);
        Assert.Contains("38N", ranked[0].Name);
    }

    [Fact]
    public void PlausibilityCheck_Ranks37NFirst_ForJeddahAreaCoordinate()
    {
        // Jeddah planar coordinate in UTM 37N (~39.2°E, ~21.5°N)
        // In UTM 37N: X ≈ 520000, Y ≈ 2380000
        double jeddahX = 520000.0;
        double jeddahY = 2380000.0;

        var ranked = CrsProjectionService.RankPlausibleUtmZones(jeddahX, jeddahY);

        Assert.NotEmpty(ranked);
        Assert.Equal(32637, ranked[0].Epsg); // 37N must be first
        Assert.True(ranked[0].IsPlausibleForSaudi);
        Assert.Contains("37N", ranked[0].Name);
    }

    [Fact]
    public void PlausibilityCheck_FlagsZonesOutsideSaudiExtent()
    {
        // Check extent bounds: 34°–56°E, 16°–33°N
        Assert.True(CrsProjectionService.IsPlausibleSaudiGeographic(46.7, 24.7)); // Riyadh
        Assert.True(CrsProjectionService.IsPlausibleSaudiGeographic(39.2, 21.5)); // Jeddah
        Assert.False(CrsProjectionService.IsPlausibleSaudiGeographic(10.0, 50.0)); // Europe
        Assert.False(CrsProjectionService.IsPlausibleSaudiGeographic(0.0, 0.0));   // Atlantic
    }
}
