using System;
using System.IO;
using ArcGIS.Core.Geometry;
using Cad2Gis.Core.Services;

namespace Cad2Gis.AddIn.SpatialEnrichment;

public class AdminLocationResult
{
    public string RegionId { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public string CityId { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
    public bool IsSuccess => !string.IsNullOrEmpty(RegionId) && !string.IsNullOrEmpty(CityId);
}

public class AdminAreaDetector
{
    private readonly string _adminAreasFolder;
    private readonly AdminAreaDetectionService _detectionService;

    public bool HasValidFiles => IsValidAdminFolder(_adminAreasFolder);
    public string AdminAreasFolder => _adminAreasFolder;

    public AdminAreaDetector(string? adminAreasFolder = null)
    {
        _adminAreasFolder = ResolveFolder(adminAreasFolder);
        _detectionService = new AdminAreaDetectionService(_adminAreasFolder);
    }

    public static bool IsValidAdminFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        string citiesShp = Path.Combine(path, "cities.shp");
        string citiesDbf = Path.Combine(path, "cities.dbf");
        string regonsShp = Path.Combine(path, "regons.shp");
        string regonsDbf = Path.Combine(path, "regons.dbf");
        return File.Exists(citiesShp) && File.Exists(citiesDbf) &&
               File.Exists(regonsShp) && File.Exists(regonsDbf);
    }

    public static string ResolveFolder(string? customFolder)
    {
        // 1. If custom folder is already valid with all required files, return it directly
        if (IsValidAdminFolder(customFolder))
            return customFolder!;

        // 2. Discover potential source directories containing the master admin area shapefiles
        var candidateSources = new System.Collections.Generic.List<string>();

        // (a) Dynamic search relative to base directory
        string? baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string? curB = baseDir;
        for (int i = 0; i < 6 && curB != null; i++)
        {
            string cand = Path.Combine(curB, "HelperFiles", "adminareas");
            if (!candidateSources.Contains(cand, StringComparer.OrdinalIgnoreCase))
                candidateSources.Add(cand);
            curB = Directory.GetParent(curB)?.FullName;
        }

        // (b) Add-in assembly location and parent directories
        try
        {
            string? asmDir = Path.GetDirectoryName(typeof(AdminAreaDetector).Assembly.Location);
            if (!string.IsNullOrWhiteSpace(asmDir))
            {
                candidateSources.Add(Path.Combine(asmDir, "HelperFiles", "adminareas"));
                candidateSources.Add(Path.Combine(asmDir, "Install", "HelperFiles", "adminareas"));
                candidateSources.Add(Path.Combine(asmDir, "..", "HelperFiles", "adminareas"));

                string? cur = asmDir;
                for (int i = 0; i < 5 && cur != null; i++)
                {
                    string cand = Path.Combine(cur, "HelperFiles", "adminareas");
                    if (!candidateSources.Contains(cand, StringComparer.OrdinalIgnoreCase))
                        candidateSources.Add(cand);
                    cur = Directory.GetParent(cur)?.FullName;
                }
            }
        }
        catch { }

        // (c) AppDomain BaseDirectory Install folder
        try
        {
            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                candidateSources.Add(Path.Combine(baseDir, "Install", "HelperFiles", "adminareas"));
            }
        }
        catch { }

        // (d) %LOCALAPPDATA%\CAD2GIS\HelperFiles\adminareas
        try
        {
            string localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CAD2GIS", "HelperFiles", "adminareas");
            candidateSources.Add(localApp);
        }
        catch { }

        // 3. Find first candidate with valid files
        string? validSource = null;
        foreach (var c in candidateSources)
        {
            if (IsValidAdminFolder(c))
            {
                validSource = c;
                break;
            }
        }

        // 4. If a valid source is found:
        if (validSource != null)
        {
            // If customFolder was specified (e.g. project HelperFiles/adminareas), copy files to make it valid and self-contained
            if (!string.IsNullOrWhiteSpace(customFolder))
            {
                try
                {
                    Directory.CreateDirectory(customFolder);
                    foreach (var srcFile in Directory.GetFiles(validSource))
                    {
                        string dstFile = Path.Combine(customFolder, Path.GetFileName(srcFile));
                        if (!File.Exists(dstFile))
                        {
                            File.Copy(srcFile, dstFile, false);
                        }
                    }
                    if (IsValidAdminFolder(customFolder))
                        return customFolder;
                }
                catch { }
            }

            // Also ensure %LOCALAPPDATA%\CAD2GIS\HelperFiles\adminareas is seeded
            try
            {
                string localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CAD2GIS", "HelperFiles", "adminareas");
                if (!IsValidAdminFolder(localApp))
                {
                    Directory.CreateDirectory(localApp);
                    foreach (var srcFile in Directory.GetFiles(validSource))
                    {
                        string dst = Path.Combine(localApp, Path.GetFileName(srcFile));
                        if (!File.Exists(dst)) File.Copy(srcFile, dst, false);
                    }
                }
            }
            catch { }

            return validSource;
        }

        return !string.IsNullOrWhiteSpace(customFolder) ? customFolder : candidateSources[0];
    }

    public AdminLocationResult DetectAdminLocation(MapPoint? samplePoint, int utmZone = 37)
    {
        var result = new AdminLocationResult();

        if (samplePoint == null || samplePoint.IsEmpty)
        {
            result.Warning = "Sample point is null or empty. Fields left blank.";
            return result;
        }

        try
        {
            AdminAreaDetectionResult coreResult;

            // Check if point coordinates are already in GCS (longitude around 34-56, latitude around 16-33)
            if (samplePoint.X >= 30.0 && samplePoint.X <= 60.0 && samplePoint.Y >= 10.0 && samplePoint.Y <= 40.0)
            {
                coreResult = _detectionService.DetectFromLatLon(samplePoint.Y, samplePoint.X);
            }
            else
            {
                // Planar UTM meters
                coreResult = _detectionService.DetectFromUtm(samplePoint.X, samplePoint.Y, utmZone);
            }

            result.RegionId = coreResult.RegionId;
            result.RegionName = coreResult.RegionName;
            result.CityId = coreResult.CityId;
            result.CityName = coreResult.CityName;
            result.Warning = coreResult.Warning;
        }
        catch (Exception ex)
        {
            result.Warning = $"Detection exception: {ex.Message}. Fields left blank.";
            System.Diagnostics.Debug.WriteLine($"[AdminAreaDetector] {result.Warning}");
        }

        return result;
    }
}
