using System;
using System.IO;
using Cad2Gis.Core.Services;
using Xunit;

namespace Cad2Gis.Tests;

public class ExportNamingTests
{
    [Fact]
    public void BuildExportFolderName_IncludesAllAvailableMetadataAndDate()
    {
        var date = new DateTime(2026, 10, 20);
        string folder = ExportNamingService.BuildExportFolderName("629", "Jeddah", "AlRabuah", date);

        Assert.Equal("Plan_629_Jeddah_AlRabuah_20261020", folder);
    }

    [Fact]
    public void BuildExportFolderName_OmitsMissingMetadataGracefully()
    {
        var date = new DateTime(2026, 10, 20);
        string folder = ExportNamingService.BuildExportFolderName("101", "", null, date);

        Assert.Equal("Plan_101_20261020", folder);
    }

    [Fact]
    public void ResolveUniqueExportFolder_CreatesIncrementingNumberedFolderWhenDataExists()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Cad2Gis_NamingTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            var date = new DateTime(2026, 10, 20);

            // First creation: creates base folder
            string folder1 = ExportNamingService.ResolveUniqueExportFolder(tempRoot, "629", "Jeddah", "AlRabuah", date);
            Assert.True(Directory.Exists(folder1));
            Assert.EndsWith("Plan_629_Jeddah_AlRabuah_20261020", folder1);

            // Add dummy file to simulate existing data
            File.WriteAllText(Path.Combine(folder1, "PARCEL.shp"), "dummy content");

            // Second creation: folder1 has data, so it should create _1
            string folder2 = ExportNamingService.ResolveUniqueExportFolder(tempRoot, "629", "Jeddah", "AlRabuah", date);
            Assert.True(Directory.Exists(folder2));
            Assert.EndsWith("Plan_629_Jeddah_AlRabuah_20261020_1", folder2);

            // Add dummy file to folder2
            File.WriteAllText(Path.Combine(folder2, "PARCEL.shp"), "dummy content");

            // Third creation: folder2 has data, so it should create _2
            string folder3 = ExportNamingService.ResolveUniqueExportFolder(tempRoot, "629", "Jeddah", "AlRabuah", date);
            Assert.True(Directory.Exists(folder3));
            Assert.EndsWith("Plan_629_Jeddah_AlRabuah_20261020_2", folder3);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }
}
