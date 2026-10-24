using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;
using Cad2Gis.AddIn.Geometry;

namespace Cad2Gis.AddIn.IO;

public static class ShapefileExporter
{
    public static async Task ExportToShapefileAsync(
        string outputFolder,
        string templateFolder,
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyDictionary<long, Polygon> parcelGeoms,
        IReadOnlyList<BorderRecord> borders,
        IReadOnlyDictionary<long, Polyline> borderGeoms,
        List<string> warnings,
        string parcelBaseName = "PARCEL",
        string borderBaseName = "border",
        int epsgCode = 0,
        double maxDeviationMeters = 0.001,
        bool alsoKeepExactUtmGdb = false)
    {
        Directory.CreateDirectory(outputFolder);

        // Pre-export DBF Text Length Validation
        var textIssues = DbfTextLengthService.CheckTextLengths(parcels, borders);
        foreach (var issue in textIssues)
        {
            warnings.Add($"[TextLength] {issue.EntityType} {issue.EntityId} field {issue.FieldName} was safely truncated from {issue.Utf8ByteLength} to {issue.MaxAllowedBytes} bytes.");
        }

        int convertedParcelCurves = 0;
        foreach (var kvp in parcelGeoms)
        {
            if (NativeCurveAuditHelper.HasCurves(kvp.Value)) convertedParcelCurves++;
        }
        int convertedBorderCurves = 0;
        foreach (var kvp in borderGeoms)
        {
            if (NativeCurveAuditHelper.HasCurves(kvp.Value)) convertedBorderCurves++;
        }

        if (convertedParcelCurves > 0 || convertedBorderCurves > 0)
        {
            warnings.Add($"[Controlled Curve Conversion] Shapefile deliverable cannot store true curves. Controlled deviation densification applied to {convertedParcelCurves} parcel(s) and {convertedBorderCurves} border(s) with max deviation tolerance {maxDeviationMeters:F4}m. Frozen metric values are strictly preserved from exact UTM true curves.");
        }

        // 1. Export PARCEL shapefile
        string parcelTemplate = Path.Combine(templateFolder, "PARCEL.shp");
        if (File.Exists(parcelTemplate))
        {
            CloneTemplateFiles(templateFolder, "PARCEL", outputFolder, parcelBaseName);
            if (QueuedTask.OnWorker)
            {
                WriteParcelsToShapefile(outputFolder, $"{parcelBaseName}.shp", parcels, parcelGeoms, maxDeviationMeters);
            }
            else
            {
                await QueuedTask.Run(() => WriteParcelsToShapefile(outputFolder, $"{parcelBaseName}.shp", parcels, parcelGeoms, maxDeviationMeters));
            }
            WriteCpgFile(outputFolder, $"{parcelBaseName}.cpg");
            WritePrjFile(outputFolder, parcelBaseName, epsgCode);
        }
        else
        {
            warnings.Add($"Parcel template not found at {parcelTemplate}");
        }

        // 2. Export border shapefile
        string borderTemplate = Path.Combine(templateFolder, "border.shp");
        if (File.Exists(borderTemplate))
        {
            CloneTemplateFiles(templateFolder, "border", outputFolder, borderBaseName);
            if (QueuedTask.OnWorker)
            {
                WriteBordersToShapefile(outputFolder, $"{borderBaseName}.shp", borders, borderGeoms, maxDeviationMeters);
            }
            else
            {
                await QueuedTask.Run(() => WriteBordersToShapefile(outputFolder, $"{borderBaseName}.shp", borders, borderGeoms, maxDeviationMeters));
            }
            WriteCpgFile(outputFolder, $"{borderBaseName}.cpg");
            WritePrjFile(outputFolder, borderBaseName, epsgCode);
        }
        else
        {
            warnings.Add($"Border template not found at {borderTemplate}");
        }

        // 3. Optional exact UTM File Geodatabase copy with true curves intact
        if (alsoKeepExactUtmGdb)
        {
            try
            {
                string exactGdbPath = Path.Combine(outputFolder, "Exact_UTM_Curves.gdb");
                await GdbFeatureClassService.ExportParcelsToGdbAsync(exactGdbPath, parcelBaseName, parcelTemplate, parcels, parcelGeoms, epsgCode);
                await GdbFeatureClassService.ExportBordersToGdbAsync(exactGdbPath, borderBaseName, borderTemplate, borders, borderGeoms, epsgCode);
                warnings.Add($"[Exact UTM Copy] Preserved exact true-curve feature classes in '{exactGdbPath}'.");
            }
            catch (Exception ex)
            {
                warnings.Add($"[Exact UTM Copy Error] Could not export true curves GDB: {ex.Message}");
            }
        }
    }

    public static void ExportToShapefile(
        string outputFolder,
        string templateFolder,
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyDictionary<long, Polygon> parcelGeoms,
        IReadOnlyList<BorderRecord> borders,
        IReadOnlyDictionary<long, Polyline> borderGeoms,
        out List<string> warnings,
        string parcelBaseName = "PARCEL",
        string borderBaseName = "border",
        int epsgCode = 0,
        double maxDeviationMeters = 0.001,
        bool alsoKeepExactUtmGdb = false)
    {
        var localWarns = new List<string>();
        if (QueuedTask.OnWorker)
        {
            ExportToShapefileAsync(outputFolder, templateFolder, parcels, parcelGeoms, borders, borderGeoms, localWarns, parcelBaseName, borderBaseName, epsgCode, maxDeviationMeters, alsoKeepExactUtmGdb).GetAwaiter().GetResult();
        }
        else
        {
            Task.Run(async () =>
            {
                await ExportToShapefileAsync(outputFolder, templateFolder, parcels, parcelGeoms, borders, borderGeoms, localWarns, parcelBaseName, borderBaseName, epsgCode, maxDeviationMeters, alsoKeepExactUtmGdb);
            }).GetAwaiter().GetResult();
        }
        warnings = localWarns;
    }

    public static async Task ExportToFeatureClassAsync(
        string targetGdbPath,
        string parcelFcName,
        string borderFcName,
        string templateFolder,
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyDictionary<long, Polygon> parcelGeoms,
        IReadOnlyList<BorderRecord> borders,
        IReadOnlyDictionary<long, Polyline> borderGeoms,
        List<string> warnings,
        int epsgCode = 0)
    {
        warnings.Add("[Notice] Target is File Geodatabase. 'Shape_Area' and 'Shape_Length' are system-managed fields; frozen metric planar UTM values are preserved in 'AREA_SQM', 'SHAPE_Leng', and 'BORDERDESC'.");
        warnings.Add("[Notice] Temporary class field 'PARCEL_CLASS' is excluded from final export schema.");

        string templateParcel = Path.Combine(templateFolder, "PARCEL.shp");
        string templateBorder = Path.Combine(templateFolder, "border.shp");

        await GdbFeatureClassService.ExportParcelsToGdbAsync(targetGdbPath, parcelFcName, templateParcel, parcels, parcelGeoms, epsgCode);
        await GdbFeatureClassService.ExportBordersToGdbAsync(targetGdbPath, borderFcName, templateBorder, borders, borderGeoms, epsgCode);
    }

    public static void ExportToFeatureClass(
        string targetGdbPath,
        string parcelFcName,
        string borderFcName,
        string templateFolder,
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyDictionary<long, Polygon> parcelGeoms,
        IReadOnlyList<BorderRecord> borders,
        IReadOnlyDictionary<long, Polyline> borderGeoms,
        out List<string> warnings)
    {
        warnings = new List<string>();

        // Warn that Shape_Area and Shape_Length are system-managed in GDB
        warnings.Add("[Notice] Target is File Geodatabase. 'Shape_Area' and 'Shape_Length' are system-managed fields; frozen metric planar UTM values are preserved in 'AREA_SQM', 'SHAPE_Leng', and 'BORDERDESC'.");

        // Temporary PARCEL_CLASS field is dropped in final export
        warnings.Add("[Notice] Temporary class field 'PARCEL_CLASS' is excluded from final export schema.");

        string templateParcel = Path.Combine(templateFolder, "PARCEL.shp");
        string templateBorder = Path.Combine(templateFolder, "border.shp");

        if (QueuedTask.OnWorker)
        {
            // Write parcels
            GdbFeatureClassService.WriteParcelsToFeatureClass(targetGdbPath, parcelFcName, parcels, parcelGeoms);

            // Write borders
            GdbFeatureClassService.WriteBordersToFeatureClass(targetGdbPath, borderFcName, borders, borderGeoms);
        }
        else
        {
            Task.Run(async () =>
            {
                await QueuedTask.Run(() =>
                {
                    GdbFeatureClassService.WriteParcelsToFeatureClass(targetGdbPath, parcelFcName, parcels, parcelGeoms);
                    GdbFeatureClassService.WriteBordersToFeatureClass(targetGdbPath, borderFcName, borders, borderGeoms);
                });
            }).GetAwaiter().GetResult();
        }
    }

    public static async Task ExportSingleParcelShapefileAsync(
        string outputFolder,
        string templateFolder,
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyDictionary<long, Polygon> parcelGeoms,
        string baseName = "PARCEL",
        int epsgCode = 0)
    {
        Directory.CreateDirectory(outputFolder);
        string parcelTemplate = Path.Combine(templateFolder, "PARCEL.shp");
        if (File.Exists(parcelTemplate))
        {
            CloneTemplateFiles(templateFolder, "PARCEL", outputFolder, baseName);
            if (QueuedTask.OnWorker)
            {
                WriteParcelsToShapefile(outputFolder, $"{baseName}.shp", parcels, parcelGeoms);
            }
            else
            {
                await QueuedTask.Run(() => WriteParcelsToShapefile(outputFolder, $"{baseName}.shp", parcels, parcelGeoms));
            }
            WriteCpgFile(outputFolder, $"{baseName}.cpg");
            WritePrjFile(outputFolder, baseName, epsgCode);
        }
    }

    public static void ExportSingleParcelShapefile(
        string outputFolder,
        string templateFolder,
        IReadOnlyList<ParcelRecord> parcels,
        IReadOnlyDictionary<long, Polygon> parcelGeoms,
        string baseName = "PARCEL",
        int epsgCode = 0)
    {
        if (QueuedTask.OnWorker)
        {
            ExportSingleParcelShapefileAsync(outputFolder, templateFolder, parcels, parcelGeoms, baseName, epsgCode).GetAwaiter().GetResult();
        }
        else
        {
            Task.Run(async () => await ExportSingleParcelShapefileAsync(outputFolder, templateFolder, parcels, parcelGeoms, baseName, epsgCode)).GetAwaiter().GetResult();
        }
    }

    public static async Task ExportSingleBorderShapefileAsync(
        string outputFolder,
        string templateFolder,
        IReadOnlyList<BorderRecord> borders,
        IReadOnlyDictionary<long, Polyline> borderGeoms,
        string baseName = "border",
        int epsgCode = 0)
    {
        Directory.CreateDirectory(outputFolder);
        string borderTemplate = Path.Combine(templateFolder, "border.shp");
        if (File.Exists(borderTemplate))
        {
            CloneTemplateFiles(templateFolder, "border", outputFolder, baseName);
            if (QueuedTask.OnWorker)
            {
                WriteBordersToShapefile(outputFolder, $"{baseName}.shp", borders, borderGeoms);
            }
            else
            {
                await QueuedTask.Run(() => WriteBordersToShapefile(outputFolder, $"{baseName}.shp", borders, borderGeoms));
            }
            WriteCpgFile(outputFolder, $"{baseName}.cpg");
            WritePrjFile(outputFolder, baseName, epsgCode);
        }
    }

    public static void ExportSingleBorderShapefile(
        string outputFolder,
        string templateFolder,
        IReadOnlyList<BorderRecord> borders,
        IReadOnlyDictionary<long, Polyline> borderGeoms,
        string baseName = "border",
        int epsgCode = 0)
    {
        if (QueuedTask.OnWorker)
        {
            ExportSingleBorderShapefileAsync(outputFolder, templateFolder, borders, borderGeoms, baseName, epsgCode).GetAwaiter().GetResult();
        }
        else
        {
            Task.Run(async () => await ExportSingleBorderShapefileAsync(outputFolder, templateFolder, borders, borderGeoms, baseName, epsgCode)).GetAwaiter().GetResult();
        }
    }

    private static void CloneTemplateFiles(string srcDir, string srcBaseName, string dstDir, string dstBaseName)
    {
        string[] extensions = { ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx" };
        foreach (var ext in extensions)
        {
            string srcFile = Path.Combine(srcDir, srcBaseName + ext);
            if (File.Exists(srcFile))
            {
                string dstFile = Path.Combine(dstDir, dstBaseName + ext);
                File.Copy(srcFile, dstFile, true);
            }
        }
    }

    private static void WriteCpgFile(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        File.WriteAllText(path, "UTF-8", Encoding.UTF8);
    }

    public static void WritePrjFile(string folder, string baseName, int epsgCode)
    {
        if (epsgCode <= 0) return;
        try
        {
            var sr = ArcGIS.Core.Geometry.SpatialReferenceBuilder.CreateSpatialReference(epsgCode);
            if (sr != null && !string.IsNullOrWhiteSpace(sr.Wkt))
            {
                string prjPath = Path.Combine(folder, baseName + ".prj");
                File.WriteAllText(prjPath, sr.Wkt, Encoding.ASCII);
                return;
            }
        }
        catch { }

        // Fallback standard Esri WKT for common Middle East / Saudi Arabia UTM projections
        string? fallbackWkt = epsgCode switch
        {
            32636 => "PROJCS[\"WGS_1984_UTM_Zone_36N\",GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",33.0],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]",
            32637 => "PROJCS[\"WGS_1984_UTM_Zone_37N\",GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",39.0],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]",
            32638 => "PROJCS[\"WGS_1984_UTM_Zone_38N\",GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",45.0],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]",
            32639 => "PROJCS[\"WGS_1984_UTM_Zone_39N\",GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",51.0],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]",
            32640 => "PROJCS[\"WGS_1984_UTM_Zone_40N\",GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",0.0],PARAMETER[\"Central_Meridian\",57.0],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]",
            _ => null
        };

        if (fallbackWkt != null)
        {
            string prjPath = Path.Combine(folder, baseName + ".prj");
            File.WriteAllText(prjPath, fallbackWkt, Encoding.ASCII);
        }
    }

    private static void WriteParcelsToShapefile(
        string folder, 
        string fileName, 
        IReadOnlyList<ParcelRecord> parcels, 
        IReadOnlyDictionary<long, Polygon> geoms,
        double maxDeviationMeters = 0.001)
    {
        var fileConn = new FileSystemConnectionPath(new Uri(folder), FileSystemDatastoreType.Shapefile);
        using var datastore = new FileSystemDatastore(fileConn);
        using var fc = datastore.OpenDataset<FeatureClass>(fileName);
        var def = fc.GetDefinition();

        using var insertCursor = fc.CreateInsertCursor();
        foreach (var p in parcels)
        {
            using var buffer = fc.CreateRowBuffer();

            if (geoms.TryGetValue(p.InternalId, out var poly))
            {
                Polygon exportPoly = poly;
                if (maxDeviationMeters > 0 && NativeCurveAuditHelper.HasCurves(poly))
                {
                    exportPoly = (Polygon)GeometryEngine.Instance.DensifyByDeviation(poly, maxDeviationMeters);
                }
                buffer[def.GetShapeField()] = ParcelPolygonizer.Ensure2D(exportPoly);
            }

            SetFieldSafe(buffer, def, "PARCEL_TYP", p.ParcelTyp);
            SetFieldSafe(buffer, def, "REALESTA_1", p.RealestateType);
            SetFieldSafe(buffer, def, "NEIGHBOURH", SafeTruncate(p.Neighbourhood, 50));
            SetFieldSafe(buffer, def, "REGION_ID", SafeTruncate(p.RegionId, 10));
            SetFieldSafe(buffer, def, "CITY_ID", SafeTruncate(p.CityId, 10));
            SetFieldSafe(buffer, def, "ADDRESS", SafeTruncate(p.Address, 254));
            SetFieldSafe(buffer, def, "USAGE", SafeTruncate(p.Usage, 50));
            SetFieldSafe(buffer, def, "ISBUILDING", p.IsBuilding);
            SetFieldSafe(buffer, def, "BUILDINGTY", p.BuildingType);
            SetFieldSafe(buffer, def, "REMARKS", SafeTruncate(p.Remarks, 100));
            SetFieldSafe(buffer, def, "LAYOUT", SafeTruncate(p.Layout, 50));
            SetFieldSafe(buffer, def, "PLOT_ID", SafeTruncate(p.PlotId, 50));
            SetFieldSafe(buffer, def, "BLOCK_NO", SafeTruncate(p.BlockNo, 50));
            SetFieldSafe(buffer, def, "PLAN_NO", SafeTruncate(p.PlanNo, 50));
            SetFieldSafe(buffer, def, "PLOT_NO1", SafeTruncate(p.PlotNo1, 50));
            SetFieldSafe(buffer, def, "PLOT_NO2", SafeTruncate(p.PlotNo2, 50));
            SetFieldSafe(buffer, def, "PLOT_NO3", SafeTruncate(p.PlotNo3, 50));
            SetFieldSafe(buffer, def, "PLOT_NO4", SafeTruncate(p.PlotNo4, 50));
            SetFieldSafe(buffer, def, "PLOT_NO5", SafeTruncate(p.PlotNo5, 50));
            SetFieldSafe(buffer, def, "PLOT_NO6", SafeTruncate(p.PlotNo6, 50));
            SetFieldSafe(buffer, def, "REQUEST_TY", SafeTruncate(p.RequestType, 50));
            SetFieldSafe(buffer, def, "REQUEST_NO", SafeTruncate(p.RequestNo, 50));
            SetFieldSafe(buffer, def, "REPORT_NO", SafeTruncate(p.ReportNo, 50));

            // Frozen planar metric values
            SetFieldSafe(buffer, def, "AREA_SQM", p.AreaSqm);
            SetFieldSafe(buffer, def, "Shape_Area", p.ShapeArea > 0 ? p.ShapeArea : p.AreaSqm);
            SetFieldSafe(buffer, def, "Shape_Leng", p.ShapeLength);

            // NOTE: Temporary PARCEL_CLASS field is NEVER set or written here.

            insertCursor.Insert(buffer);
        }
        insertCursor.Flush();
    }

    private static void WriteBordersToShapefile(
        string folder, 
        string fileName, 
        IReadOnlyList<BorderRecord> borders, 
        IReadOnlyDictionary<long, Polyline> geoms,
        double maxDeviationMeters = 0.001)
    {
        var fileConn = new FileSystemConnectionPath(new Uri(folder), FileSystemDatastoreType.Shapefile);
        using var datastore = new FileSystemDatastore(fileConn);
        using var fc = datastore.OpenDataset<FeatureClass>(fileName);
        var def = fc.GetDefinition();

        using var insertCursor = fc.CreateInsertCursor();
        foreach (var b in borders)
        {
            using var buffer = fc.CreateRowBuffer();

            if (geoms.TryGetValue(b.InternalId, out var line))
            {
                Polyline exportLine = line;
                if (maxDeviationMeters > 0 && NativeCurveAuditHelper.HasCurves(line))
                {
                    exportLine = (Polyline)GeometryEngine.Instance.DensifyByDeviation(line, maxDeviationMeters);
                }
                buffer[def.GetShapeField()] = ParcelPolygonizer.Ensure2D(exportLine);
            }

            SetFieldSafe(buffer, def, "PLOT_ID", SafeTruncate(b.PlotId, 50));
            SetFieldSafe(buffer, def, "PLAN_NO", SafeTruncate(b.PlanNo, 50));
            SetFieldSafe(buffer, def, "BORDERNAME", b.BorderName);
            SetFieldSafe(buffer, def, "BORDERTYPE", SafeTruncate(b.BorderType, 20));
            SetFieldSafe(buffer, def, "BORDERDESC", b.BorderDesc);
            SetFieldSafe(buffer, def, "DIMENSION", SafeTruncate(b.Dimension, 20));
            SetFieldSafe(buffer, def, "SHAPE_Leng", b.ShapeLength);

            string safeDesc = SafeTruncate(b.Descriptio, 100) ?? string.Empty;
            if (def.FindField("DESCRIPTIO") >= 0)
                SetFieldSafe(buffer, def, "DESCRIPTIO", safeDesc);
            else if (def.FindField("DESCRIPTO") >= 0)
                SetFieldSafe(buffer, def, "DESCRIPTO", safeDesc);

            insertCursor.Insert(buffer);
        }
        insertCursor.Flush();
    }

    public static async Task ExportDeviatingParcelsToShapefileAsync(
        string outputFolder,
        string templateFolder,
        IReadOnlyList<ParcelDeviationItem> deviations,
        IReadOnlyDictionary<long, Polygon> parcelGeoms,
        int epsgCode = 0)
    {
        if (deviations == null || deviations.Count == 0) return;

        string parcelTemplate = Path.Combine(templateFolder, "PARCEL.shp");
        if (!File.Exists(parcelTemplate)) return;

        CloneTemplateFiles(templateFolder, "PARCEL", outputFolder, "PARCEL_Differences");
        WriteCpgFile(outputFolder, "PARCEL_Differences.cpg");
        WritePrjFile(outputFolder, "PARCEL_Differences", epsgCode);

        if (QueuedTask.OnWorker)
        {
            WriteDeviatingParcelsToShapefile(outputFolder, deviations, parcelGeoms);
        }
        else
        {
            await QueuedTask.Run(() => WriteDeviatingParcelsToShapefile(outputFolder, deviations, parcelGeoms));
        }
    }

    public static void ExportDeviatingParcelsToShapefile(
        string outputFolder,
        string templateFolder,
        IReadOnlyList<ParcelDeviationItem> deviations,
        IReadOnlyDictionary<long, Polygon> parcelGeoms,
        int epsgCode = 0)
    {
        if (QueuedTask.OnWorker)
        {
            ExportDeviatingParcelsToShapefileAsync(outputFolder, templateFolder, deviations, parcelGeoms, epsgCode).GetAwaiter().GetResult();
        }
        else
        {
            Task.Run(async () => await ExportDeviatingParcelsToShapefileAsync(outputFolder, templateFolder, deviations, parcelGeoms, epsgCode)).GetAwaiter().GetResult();
        }
    }

    private static void WriteDeviatingParcelsToShapefile(
        string outputFolder,
        IReadOnlyList<ParcelDeviationItem> deviations,
        IReadOnlyDictionary<long, Polygon> parcelGeoms)
    {
        var fileConn = new FileSystemConnectionPath(new Uri(outputFolder), FileSystemDatastoreType.Shapefile);
        using var datastore = new FileSystemDatastore(fileConn);
        using var fc = datastore.OpenDataset<FeatureClass>("PARCEL_Differences.shp");
        var def = fc.GetDefinition();

        using var insertCursor = fc.CreateInsertCursor();
        foreach (var dev in deviations)
        {
            var p = dev.Parcel;
            using var buffer = fc.CreateRowBuffer();

            if (parcelGeoms.TryGetValue(p.InternalId, out var poly))
            {
                buffer[def.GetShapeField()] = ParcelPolygonizer.Ensure2D(poly);
            }

            SetFieldSafe(buffer, def, "PARCEL_TYP", p.ParcelTyp);
            SetFieldSafe(buffer, def, "REALESTA_1", p.RealestateType);
            SetFieldSafe(buffer, def, "NEIGHBOURH", SafeTruncate(p.Neighbourhood, 50));
            SetFieldSafe(buffer, def, "REGION_ID", SafeTruncate(p.RegionId, 10));
            SetFieldSafe(buffer, def, "CITY_ID", SafeTruncate(p.CityId, 10));
            SetFieldSafe(buffer, def, "ADDRESS", SafeTruncate(p.Address, 254));
            SetFieldSafe(buffer, def, "USAGE", SafeTruncate(p.Usage, 50));
            SetFieldSafe(buffer, def, "ISBUILDING", p.IsBuilding);
            SetFieldSafe(buffer, def, "BUILDINGTY", p.BuildingType);
            SetFieldSafe(buffer, def, "REMARKS", SafeTruncate(dev.DeviationReason, 254));
            SetFieldSafe(buffer, def, "LAYOUT", SafeTruncate(p.Layout, 50));
            SetFieldSafe(buffer, def, "PLOT_ID", SafeTruncate(p.PlotId, 50));
            SetFieldSafe(buffer, def, "BLOCK_NO", SafeTruncate(p.BlockNo, 50));
            SetFieldSafe(buffer, def, "PLAN_NO", SafeTruncate(p.PlanNo, 50));
            SetFieldSafe(buffer, def, "PLOT_NO1", SafeTruncate(p.PlotNo1, 50));
            SetFieldSafe(buffer, def, "REQUEST_TY", SafeTruncate(p.RequestType, 50));
            SetFieldSafe(buffer, def, "REQUEST_NO", SafeTruncate(p.RequestNo, 50));
            SetFieldSafe(buffer, def, "REPORT_NO", SafeTruncate(p.ReportNo, 50));
            SetFieldSafe(buffer, def, "AREA_SQM", p.AreaSqm);
            SetFieldSafe(buffer, def, "Shape_Area", p.ShapeArea > 0 ? p.ShapeArea : p.AreaSqm);
            SetFieldSafe(buffer, def, "Shape_Leng", p.ShapeLength);

            insertCursor.Insert(buffer);
        }
        insertCursor.Flush();
    }

    private static string? SafeTruncate(string? val, int maxBytes)
    {
        if (string.IsNullOrEmpty(val)) return val;
        return DbfTextLengthService.TruncateUtf8Safe(val, maxBytes);
    }

    private static void SetFieldSafe(RowBuffer buffer, FeatureClassDefinition def, string fieldName, object? value)
    {
        int idx = def.FindField(fieldName);
        if (idx < 0 || value == null) return;

        try
        {
            var field = def.GetFields()[idx];
            object? converted = ConvertToFieldType(value, field.FieldType, field.Length);
            if (converted != null)
            {
                buffer[idx] = converted;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shapefile SetFieldSafe] Field '{fieldName}' ({value}) set notice: {ex.Message}");
        }
    }

    public static object? ConvertToFieldType(object? value, FieldType targetType, int length)
    {
        if (value == null) return null;

        string strVal = value.ToString()?.Trim() ?? string.Empty;

        switch (targetType)
        {
            case FieldType.String:
                if (length > 0 && strVal.Length > length)
                {
                    return DbfTextLengthService.TruncateUtf8Safe(strVal, length);
                }
                return strVal;

            case FieldType.SmallInteger:
                if (value is short s) return s;
                if (short.TryParse(strVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out short parsedShort))
                    return parsedShort;
                if (int.TryParse(strVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pInt) && pInt >= short.MinValue && pInt <= short.MaxValue)
                    return (short)pInt;
                if (double.TryParse(strVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double pDblShort) && pDblShort >= short.MinValue && pDblShort <= short.MaxValue)
                    return (short)Math.Round(pDblShort);
                return (short)0;

            case FieldType.Integer:
                if (value is int i) return i;
                if (int.TryParse(strVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                    return parsedInt;
                if (long.TryParse(strVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out long pLongInt) && pLongInt >= int.MinValue && pLongInt <= int.MaxValue)
                    return (int)pLongInt;
                if (double.TryParse(strVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double pDblInt) && pDblInt >= int.MinValue && pDblInt <= int.MaxValue)
                    return (int)Math.Round(pDblInt);
                return 0;

            case FieldType.BigInteger:
                if (value is long l) return l;
                if (long.TryParse(strVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedLong))
                    return parsedLong;
                if (double.TryParse(strVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double pDblLong))
                    return (long)Math.Round(pDblLong);
                return 0L;

            case FieldType.Single:
                if (value is float f) return f;
                if (float.TryParse(strVal, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedFloat))
                    return parsedFloat;
                return 0f;

            case FieldType.Double:
                if (value is double d) return d;
                if (double.TryParse(strVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDouble))
                    return parsedDouble;
                return string.IsNullOrEmpty(strVal) ? null : 0.0;

            case FieldType.Date:
                if (value is DateTime dt) return dt;
                if (DateTime.TryParse(strVal, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    return parsedDate;
                return null;

            default:
                return value;
        }
    }
}
