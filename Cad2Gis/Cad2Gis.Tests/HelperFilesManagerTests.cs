using System;
using System.IO;
using Xunit;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class HelperFilesManagerTests
{
    [Fact]
    public void ResolutionOrder_UserSetting_TakesPrecedence()
    {
        string tempProject = Path.Combine(Path.GetTempPath(), "Cad2Gis_Test_Proj_" + Guid.NewGuid());
        string tempUser = Path.Combine(Path.GetTempPath(), "Cad2Gis_Test_User_" + Guid.NewGuid());
        Directory.CreateDirectory(tempProject);
        Directory.CreateDirectory(tempUser);

        try
        {
            var manager = new HelperFilesManager(
                projectHomeFolder: tempProject,
                explicitHelperPath: tempUser);

            Assert.Equal(Path.GetFullPath(tempUser), Path.GetFullPath(manager.HelperFilesRoot));
            Assert.False(manager.IsFallbackToLocalAppData);
        }
        finally
        {
            if (Directory.Exists(tempProject)) Directory.Delete(tempProject, true);
            if (Directory.Exists(tempUser)) Directory.Delete(tempUser, true);
        }
    }

    [Fact]
    public void ResolutionOrder_ProjectFolder_ResolvedWhenExists()
    {
        string tempProject = Path.Combine(Path.GetTempPath(), "Cad2Gis_Test_Proj_" + Guid.NewGuid());
        string expected = Path.Combine(tempProject, "HelperFiles");
        Directory.CreateDirectory(expected); // Exists in project folder

        try
        {
            var manager = new HelperFilesManager(projectHomeFolder: tempProject);

            Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(manager.HelperFilesRoot));
            Assert.False(manager.IsFallbackToLocalAppData);
            Assert.False(manager.IsMissingFromProject);
            Assert.True(string.IsNullOrEmpty(manager.FallbackWarningMessage));
        }
        finally
        {
            if (Directory.Exists(tempProject)) Directory.Delete(tempProject, true);
        }
    }

    [Fact]
    public void ResolutionOrder_ProjectFolder_DoesNotCreateHelperFilesWhenMissing_AndShowsWarning()
    {
        string tempProject = Path.Combine(Path.GetTempPath(), "Cad2Gis_Test_Proj_" + Guid.NewGuid());
        Directory.CreateDirectory(tempProject); // project folder exists, but HelperFiles does NOT exist

        try
        {
            var manager = new HelperFilesManager(projectHomeFolder: tempProject);

            string helperDir = Path.Combine(tempProject, "HelperFiles");
            // Must NOT create HelperFiles in project!
            Assert.False(Directory.Exists(helperDir), "HelperFiles must NOT be auto-created in project folder if missing!");
            Assert.True(manager.IsMissingFromProject);
            Assert.Equal("يرجى نسخ HelperFiles في مجلد المشروع", manager.FallbackWarningMessage);
        }
        finally
        {
            if (Directory.Exists(tempProject)) Directory.Delete(tempProject, true);
        }
    }

    [Fact]
    public void ResolutionOrder_FallbackToLocalAppData_WhenProjectNotProvided_AndShowsWarning()
    {
        var manager = new HelperFilesManager(projectHomeFolder: null, explicitHelperPath: null);

        Assert.True(manager.IsMissingFromProject);
        Assert.Equal("يرجى نسخ HelperFiles في مجلد المشروع", manager.FallbackWarningMessage);
    }

    [Fact]
    public void Bootstrap_Idempotent_AndNeverOverwritesExistingFile()
    {
        string tempProject = Path.Combine(Path.GetTempPath(), "Cad2Gis_Bootstrap_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(tempProject, "HelperFiles"));

        try
        {
            var manager = new HelperFilesManager(projectHomeFolder: tempProject);

            // Pre-create a customized file in the target
            string targetDir = manager.HelperFilesRoot;
            Directory.CreateDirectory(targetDir);
            string customFile = Path.Combine(targetDir, "custom_user_file.txt");
            File.WriteAllText(customFile, "Original user content");

            // Create a fake seed folder
            string seedDir = Path.Combine(Path.GetTempPath(), "Cad2Gis_Seed_" + Guid.NewGuid());
            Directory.CreateDirectory(seedDir);
            File.WriteAllText(Path.Combine(seedDir, "custom_user_file.txt"), "Seed content that should NOT overwrite");
            File.WriteAllText(Path.Combine(seedDir, "new_seed_file.txt"), "New seed content");

            // Run bootstrap
            manager.BootstrapSeedFiles(seedDir);

            // Verify original file was preserved
            Assert.Equal("Original user content", File.ReadAllText(customFile));
            // Verify new file was copied
            Assert.True(File.Exists(Path.Combine(targetDir, "new_seed_file.txt")));

            // Run again (idempotent)
            manager.BootstrapSeedFiles(seedDir);
            Assert.Equal("Original user content", File.ReadAllText(customFile));

            Directory.Delete(seedDir, true);
        }
        finally
        {
            if (Directory.Exists(tempProject)) Directory.Delete(tempProject, true);
        }
    }
}
