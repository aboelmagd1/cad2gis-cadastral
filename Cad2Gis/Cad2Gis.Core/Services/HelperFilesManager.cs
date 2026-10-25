using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

public class ToolSettings
{
    public string HelperFolderPath { get; set; } = string.Empty;
    public string SharedProfilesFolderPath { get; set; } = string.Empty;
    public string UiLanguage { get; set; } = "en";
    public string LastUsedProfile { get; set; } = "Default_Consultant_Profile";
    public string LastOutputFolder { get; set; } = string.Empty;
    public int DefaultUtmZone { get; set; } = 32637;
    public double DefaultClosureToleranceMeters { get; set; } = 0.05;
    public double DefaultPassageStreetThresholdMeters { get; set; } = 10.0;
    public double DefaultMaxRayDistanceMeters { get; set; } = 100.0;
    public int DefaultRayCount { get; set; } = 1;
    public double DefaultMinSharedSegmentLength { get; set; } = 0.5;
    public double AreaToleranceSqm { get; set; } = 0.5;
    public double AreaTolerancePercent { get; set; } = 0.1;
    public double ChamferThresholdMeters { get; set; } = 7.0;
    public double ChamferAngleDeg { get; set; } = 45.0;
    public double ChamferAngleToleranceDeg { get; set; } = 15.0;
    public bool RunAllStepsMode { get; set; } = true;
    public bool PauseAtReviewTables { get; set; } = true;
    public int LogRetentionDays { get; set; } = 30;

    // Part A: Border Extraction Settings
    public double AMinDeg { get; set; } = 10.0;
    public double AMaxDeg { get; set; } = 160.0;
    public double MultiSegmentAngleDeviationDeg { get; set; } = 15.0;
    public bool IncludeReflexCorners { get; set; } = true;
    public double ArcCenterToleranceMeters { get; set; } = 0.05;
    public double ArcRadiusToleranceMeters { get; set; } = 0.05;
    public double ArcTangentToleranceDeg { get; set; } = 2.0;
    public double ClusterLengthMeters { get; set; } = 7.0;
    public double Rule1LengthRatioGuard { get; set; } = 1.0;
    public bool TrueNorthCorrection { get; set; } = true;

    // Part B: Continue from Latest Export Settings
    public bool ContinueFromLatestExport { get; set; } = false;
    public int MaxExportHistoryCount { get; set; } = 10;

    // Geometry Preservation Settings
    public double MaxCurveExportDeviationMeters { get; set; } = 0.001;
    public bool AlsoKeepExactUtmGdbWithTrueCurves { get; set; } = false;
}

public class HelperResolutionResult
{
    public string ResolvedPath { get; set; } = string.Empty;
    public string ResolutionSource { get; set; } = "ProjectFolder"; // "UserSettings", "ProjectFolder", "LocalAppDataFallback", "MissingFromProject"
    public bool IsFallbackToLocalAppData { get; set; } = false;
    public bool IsMissingFromProject { get; set; } = false;
    public string FallbackWarningMessage { get; set; } = string.Empty;
}

public class HelperFilesManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public string HelperFilesRoot { get; private set; } = string.Empty;
    public bool IsFallbackToLocalAppData { get; private set; }
    public bool IsMissingFromProject { get; private set; }
    public string ResolutionSource { get; private set; } = "ProjectFolder";
    public string FallbackWarningMessage { get; private set; } = string.Empty;
    public ToolSettings Settings { get; private set; } = new();

    public string ProfilesDir => Path.Combine(HelperFilesRoot, "profiles");
    public string ExpectedLayersDir
    {
        get
        {
            string cand1 = Path.Combine(HelperFilesRoot, "expected_layers");
            if (Directory.Exists(cand1)) return cand1;
            string cand2 = Path.Combine(HelperFilesRoot, "Expected Layers");
            if (Directory.Exists(cand2)) return cand2;
            return cand1;
        }
    }
    public string ExpectedLayersExcel
    {
        get
        {
            string dir = ExpectedLayersDir;
            string cand1 = Path.Combine(dir, "Expected_Layers.xlsx");
            if (File.Exists(cand1)) return cand1;
            string cand2 = Path.Combine(dir, "Expected Layers.xlsx");
            if (File.Exists(cand2)) return cand2;
            string cand3 = Path.Combine(HelperFilesRoot, "Expected Layers", "Expected_Layers.xlsx");
            if (File.Exists(cand3)) return cand3;
            string cand4 = Path.Combine(HelperFilesRoot, "Expected Layers", "Expected Layers.xlsx");
            if (File.Exists(cand4)) return cand4;
            return cand1;
        }
    }
    public string LookupsDir => Path.Combine(HelperFilesRoot, "lookups");
    public string TemplatesDir => Path.Combine(HelperFilesRoot, "templates");
    public string TempletDir => TemplatesDir; // Alias for backward compatibility
    public string AdminAreasDir => Path.Combine(HelperFilesRoot, "adminareas");
    public string LogsDir => Path.Combine(HelperFilesRoot, "logs");
    public string DemoDir => Path.Combine(HelperFilesRoot, "demo");
    public string StateDir => Path.Combine(HelperFilesRoot, "state");
    public string ScratchDir => Path.Combine(HelperFilesRoot, "scratch");
    public string LastExportStateFile => Path.Combine(StateDir, "last_export.json");
    public string PipelineStateFile => Path.Combine(StateDir, "pipeline_state.json");
    public string SettingsFile => Path.Combine(HelperFilesRoot, "settings.json");
    public string LandUseTemplateExcel => Path.Combine(HelperFilesRoot, "PARCEs_landuse.xlsx");

    public string RequiredFieldsExcel
    {
        get
        {
            string cand1 = Path.Combine(HelperFilesRoot, "Required Fields in the Shapefile.xlsx");
            if (File.Exists(cand1)) return cand1;
            string cand2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HelperFiles", "Required Fields in the Shapefile.xlsx");
            if (File.Exists(cand2)) return cand2;
            string cand3 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Install", "HelperFiles", "Required Fields in the Shapefile.xlsx");
            if (File.Exists(cand3)) return cand3;
            return cand1;
        }
    }

    public HelperFilesManager(string? projectHomeFolder = null, string? explicitHelperPath = null, string? seedFilesDir = null)
    {
        var res = ResolveHelperFolderWithInfo(projectHomeFolder, explicitHelperPath);
        HelperFilesRoot = res.ResolvedPath;
        ResolutionSource = res.ResolutionSource;
        IsFallbackToLocalAppData = res.IsFallbackToLocalAppData;
        IsMissingFromProject = res.IsMissingFromProject;
        FallbackWarningMessage = res.FallbackWarningMessage;

        // Strict Requirement: Never auto-create HelperFiles if missing from project!
        if (Directory.Exists(HelperFilesRoot) && !IsMissingFromProject)
        {
            EnsureDirectories();
            BootstrapSeedFiles(seedFilesDir);
            LoadSettings();
        }
    }

    /// <summary>
    /// Strict Resolution Order:
    /// (1) Explicit path set by user in settings
    /// (2) &lt;project folder&gt;\HelperFiles (Project.Current.HomeFolderPath) - ONLY if already existing
    /// (3) Prompt to copy HelperFiles to project folder if missing (never auto-create)
    /// </summary>
    public static HelperResolutionResult ResolveHelperFolderWithInfo(string? projectHomeFolder, string? explicitPath)
    {
        var result = new HelperResolutionResult();

        // 1. Explicit path set by user in settings
        if (!string.IsNullOrWhiteSpace(explicitPath) && Directory.Exists(explicitPath))
        {
            result.ResolvedPath = explicitPath;
            result.ResolutionSource = "UserSettings";
            result.IsFallbackToLocalAppData = false;
            result.IsMissingFromProject = false;
            return result;
        }

        // 2. <project folder>\HelperFiles
        if (!string.IsNullOrWhiteSpace(projectHomeFolder))
        {
            string candidate = Path.Combine(projectHomeFolder, "HelperFiles");
            if (Directory.Exists(candidate))
            {
                result.ResolvedPath = candidate;
                result.ResolutionSource = "ProjectFolder";
                result.IsFallbackToLocalAppData = false;
                result.IsMissingFromProject = false;
                result.FallbackWarningMessage = string.Empty;
                return result;
            }
            else
            {
                // Strict requirement: DO NOT create HelperFiles in project if missing!
                // Prompt user to copy HelperFiles to project folder.
                result.ResolvedPath = candidate;
                result.ResolutionSource = "MissingFromProject";
                result.IsMissingFromProject = true;
                result.IsFallbackToLocalAppData = true;
                result.FallbackWarningMessage = "يرجى نسخ HelperFiles في مجلد المشروع";
                return result;
            }
        }

        // 3. Project home folder is empty or not provided
        result.IsMissingFromProject = true;
        result.IsFallbackToLocalAppData = true;
        result.ResolutionSource = "MissingFromProject";
        result.FallbackWarningMessage = "يرجى نسخ HelperFiles في مجلد المشروع";
        
        string localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CAD2GIS", "HelperFiles");
        result.ResolvedPath = Directory.Exists(localApp) ? localApp : (projectHomeFolder != null ? Path.Combine(projectHomeFolder, "HelperFiles") : localApp);

        return result;
    }

    public static string ResolveHelperFolder(string? projectHomeFolder, string? explicitPath) =>
        ResolveHelperFolderWithInfo(projectHomeFolder, explicitPath).ResolvedPath;

    private void EnsureDirectories()
    {
        if (string.IsNullOrWhiteSpace(HelperFilesRoot) || !Directory.Exists(HelperFilesRoot)) return;
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(ExpectedLayersDir);
        Directory.CreateDirectory(LookupsDir);
        Directory.CreateDirectory(TemplatesDir);
        Directory.CreateDirectory(AdminAreasDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(DemoDir);
        Directory.CreateDirectory(StateDir);
    }

    public void BootstrapSeedFiles(string? seedFilesDir)
    {
        string? resolvedSeed = seedFilesDir;
        if (string.IsNullOrWhiteSpace(resolvedSeed) || !Directory.Exists(resolvedSeed))
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string? asmDir = null;
            try { asmDir = Path.GetDirectoryName(typeof(HelperFilesManager).Assembly.Location); } catch { }

            var seedCandidates = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(seedFilesDir)) seedCandidates.Add(seedFilesDir);

            if (!string.IsNullOrWhiteSpace(asmDir))
            {
                seedCandidates.Add(Path.Combine(asmDir, "HelperFiles"));
                seedCandidates.Add(Path.Combine(asmDir, "Install", "HelperFiles"));
                seedCandidates.Add(Path.Combine(asmDir, "..", "HelperFiles"));

                string? cur = asmDir;
                for (int i = 0; i < 6 && cur != null; i++)
                {
                    string cand = Path.Combine(cur, "HelperFiles");
                    if (!seedCandidates.Contains(cand, StringComparer.OrdinalIgnoreCase))
                        seedCandidates.Add(cand);
                    cur = Directory.GetParent(cur)?.FullName;
                }
            }

            seedCandidates.Add(Path.Combine(baseDir, "HelperFiles"));
            seedCandidates.Add(Path.Combine(baseDir, "Install", "HelperFiles"));
            seedCandidates.Add(Path.Combine(baseDir, "..", "HelperFiles"));

            string? curBase = baseDir;
            for (int i = 0; i < 6 && curBase != null; i++)
            {
                string cand = Path.Combine(curBase, "HelperFiles");
                if (!seedCandidates.Contains(cand, StringComparer.OrdinalIgnoreCase))
                    seedCandidates.Add(cand);
                curBase = Directory.GetParent(curBase)?.FullName;
            }

            string localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CAD2GIS", "HelperFiles");
            seedCandidates.Add(localApp);

            foreach (var sc in seedCandidates)
            {
                if (Directory.Exists(sc) && (Directory.Exists(Path.Combine(sc, "adminareas")) || Directory.Exists(Path.Combine(sc, "templates"))))
                {
                    resolvedSeed = sc;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedSeed) || !Directory.Exists(resolvedSeed))
            return;

        if (string.Equals(Path.GetFullPath(HelperFilesRoot), Path.GetFullPath(resolvedSeed), StringComparison.OrdinalIgnoreCase))
            return;

        CopyMissingFiles(resolvedSeed, HelperFilesRoot);
    }

    private static void CopyMissingFiles(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            string relPath = Path.GetRelativePath(sourceDir, file);
            string destPath = Path.Combine(targetDir, relPath);
            string destDir = Path.GetDirectoryName(destPath)!;
            Directory.CreateDirectory(destDir);

            // STRICT: Never overwrite an existing file!
            if (!File.Exists(destPath))
            {
                try
                {
                    File.Copy(file, destPath, false);
                }
                catch { }
            }
        }
    }

    public void LoadSettings()
    {
        if (File.Exists(SettingsFile))
        {
            try
            {
                string json = File.ReadAllText(SettingsFile);
                var loaded = JsonSerializer.Deserialize<ToolSettings>(json, JsonOptions);
                if (loaded != null) Settings = loaded;
            }
            catch { }
        }
    }

    public void SaveSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }

    public List<double> LoadStandardWidths()
    {
        string path = Path.Combine(LookupsDir, "standard_widths.json");
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<double>>(json, JsonOptions);
                if (list != null && list.Count > 0) return list;
            }
            catch { }
        }
        return new List<double> { 6.0, 8.0, 10.0, 12.0, 15.0, 20.0, 25.0, 30.0 };
    }
}
