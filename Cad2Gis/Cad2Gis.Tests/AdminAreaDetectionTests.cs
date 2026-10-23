using System;
using System.IO;
using Xunit;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class AdminAreaDetectionTests
{
    private static string GetAdminAreasDir()
    {
        // Resolve relative to test output or source
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "HelperFiles", "adminareas"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Cad2Gis", "HelperFiles", "adminareas"),
            Path.Combine(baseDir, "HelperFiles", "adminareas")
        };

        foreach (var c in candidates)
        {
            string full = Path.GetFullPath(c);
            if (Directory.Exists(full) && File.Exists(Path.Combine(full, "cities.shp")))
            {
                return full;
            }
        }

        return string.Empty;
    }

    [Fact]
    public void DetectJurisdiction_PointInRiyadh_ReturnsRiyadhRegionAndCity()
    {
        string adminDir = GetAdminAreasDir();
        if (string.IsNullOrEmpty(adminDir)) return; // Skip if run in environment without shapefiles

        var service = new AdminAreaDetectionService(adminDir);

        // Riyadh geographic coords: 24.7136°N, 46.6753°E
        var result = service.DetectFromLatLon(24.7136, 46.6753);

        Assert.Equal(AdminDetectionStatus.Success, result.Status);
        Assert.Equal("1", result.RegionId);
        Assert.Equal("الرياض", result.RegionName);
        Assert.Equal("1", result.CityId);
        Assert.Equal("الرياض", result.CityName);
        Assert.Empty(result.Warning);
    }

    [Fact]
    public void DetectJurisdiction_PointInJeddah_ReturnsMakkahRegionAndJeddahCity()
    {
        string adminDir = GetAdminAreasDir();
        if (string.IsNullOrEmpty(adminDir)) return;

        var service = new AdminAreaDetectionService(adminDir);

        // Jeddah geographic coords: 21.5433°N, 39.1728°E
        var result = service.DetectFromLatLon(21.5433, 39.1728);

        Assert.Equal(AdminDetectionStatus.Success, result.Status);
        Assert.Equal("2", result.RegionId);
        Assert.Equal("مكة المكرمة", result.RegionName);
        Assert.Equal("2", result.CityId);
        Assert.Equal("جدة", result.CityName);
        Assert.Empty(result.Warning);
    }

    [Fact]
    public void DetectJurisdiction_PointOutsideSaudiPolygons_LeavesFieldsBlankWithWarning()
    {
        string adminDir = GetAdminAreasDir();
        if (string.IsNullOrEmpty(adminDir)) return;

        var service = new AdminAreaDetectionService(adminDir);

        // Red sea / outside point: 20.0°N, 37.0°E
        var result = service.DetectFromLatLon(20.0, 37.0);

        Assert.NotEqual(AdminDetectionStatus.Success, result.Status);
        Assert.Equal(string.Empty, result.RegionId);
        Assert.Equal(string.Empty, result.RegionName);
        Assert.Equal(string.Empty, result.CityId);
        Assert.Equal(string.Empty, result.CityName);
        Assert.False(string.IsNullOrWhiteSpace(result.Warning));
        // Must NEVER silently fall back to Makkah/Jeddah
        Assert.NotEqual("2", result.RegionId);
        Assert.NotEqual("جدة", result.CityName);
    }

    [Fact]
    public void DetectJurisdictionUtm_ProjectsAndDetectsCorrectly()
    {
        string adminDir = GetAdminAreasDir();
        if (string.IsNullOrEmpty(adminDir)) return;

        var service = new AdminAreaDetectionService(adminDir);

        // Riyadh in UTM 38N (Zone 38)
        var result = service.DetectFromUtm(670000.0, 2733000.0, 38);

        Assert.Equal(AdminDetectionStatus.Success, result.Status);
        Assert.Equal("1", result.RegionId);
        Assert.Equal("الرياض", result.CityName);
    }
}
