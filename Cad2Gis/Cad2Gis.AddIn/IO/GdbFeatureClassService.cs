using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Mapping;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;
using Cad2Gis.Core.Utils;
using Cad2Gis.AddIn.Geometry;

namespace Cad2Gis.AddIn.IO;

public static class GdbFeatureClassService
{
    public static string GetEffectiveGdbPath(string? customOutputFolder = null)
    {
        if (!string.IsNullOrWhiteSpace(customOutputFolder))
        {
            if (customOutputFolder.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
            {
                return customOutputFolder;
            }
            Directory.CreateDirectory(customOutputFolder);
            return Path.Combine(customOutputFolder, "Cad2Gis_Workspace.gdb");
        }

        string? projGdb = Project.Current?.DefaultGeodatabasePath;
        if (!string.IsNullOrWhiteSpace(projGdb) && (Directory.Exists(projGdb) || projGdb.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase)))
        {
            return projGdb;
        }

        string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CAD2GIS_Export");
        Directory.CreateDirectory(targetDir);
        return Path.Combine(targetDir, "Cad2Gis_Workspace.gdb");
    }

    public static async Task SaveMapEditsAsync()
    {
        try
        {
            await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(async () =>
            {
                if (Project.Current != null)
                {
                    await Project.Current.SaveEditsAsync();
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveMapEditsAsync] {ex.Message}");
        }
    }

    public static async Task EnsureGeodatabaseAsync(string gdbPath)
    {
        if (Directory.Exists(gdbPath)) return;

        string folder = Path.GetDirectoryName(gdbPath)!;
        string gdbName = Path.GetFileName(gdbPath);

        var args = Geoprocessing.MakeValueArray(folder, gdbName);
        var result = await Geoprocessing.ExecuteToolAsync("management.CreateFileGDB", args);
        if (result.IsFailed)
        {
            throw new InvalidOperationException($"Failed to create File Geodatabase at {gdbPath}: {result.Messages}");
        }
    }

    public static async Task EnsureFeatureClassFromTemplateAsync(string gdbPath, string fcName, string templatePath, int epsgCode = 0)
    {
        await EnsureGeodatabaseAsync(gdbPath);

        string targetFcPath = Path.Combine(gdbPath, fcName);

        // Check if feature class already exists in the GDB
        bool exists = false;
        SpatialReference? existingSr = null;
        try
        {
            await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
            {
                using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                var defs = gdb.GetDefinitions<FeatureClassDefinition>();
                var match = defs.FirstOrDefault(d => d.GetName().Equals(fcName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    exists = true;
                    existingSr = match.GetSpatialReference();
                }
            });
        }
        catch { }

        // If exists but spatial reference doesn't match the requested EPSG code (e.g. was created with GCS_WGS_1984 template),
        // we must fix or redefine its projection so coordinates align with the CAD dataset!
        if (exists && epsgCode > 0 && existingSr != null && existingSr.Wkid != epsgCode)
        {
            try
            {
                var defProjArgs = Geoprocessing.MakeValueArray(targetFcPath, epsgCode.ToString());
                await Geoprocessing.ExecuteToolAsync("management.DefineProjection", defProjArgs);
            }
            catch
            {
                // If DefineProjection fails (e.g. schema lock), attempt to delete and recreate
                try
                {
                    var delArgs = Geoprocessing.MakeValueArray(targetFcPath);
                    await Geoprocessing.ExecuteToolAsync("management.Delete", delArgs);
                    exists = false;
                }
                catch { }
            }
        }

        if (!exists)
        {
            // Use Geoprocessing FeatureClassToFeatureClass_conversion to clone schema from template
            var args = Geoprocessing.MakeValueArray(templatePath, gdbPath, fcName);
            var result = await Geoprocessing.ExecuteToolAsync("conversion.FeatureClassToFeatureClass", args);
            if (result.IsFailed)
            {
                throw new InvalidOperationException($"Failed to create Feature Class {fcName} from template {templatePath}: {result.Messages}");
            }

            // CRITICAL: Overwrite the template's WGS84 degree coordinate system with the active UTM coordinate system
            if (epsgCode > 0)
            {
                try
                {
                    var defProjArgs = Geoprocessing.MakeValueArray(targetFcPath, epsgCode.ToString());
                    await Geoprocessing.ExecuteToolAsync("management.DefineProjection", defProjArgs);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DefineProjection] Error: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Converts a CAD drawing (.dwg / .dxf) into a Geodatabase Feature Dataset
    /// projected to the user-chosen spatial reference (UTM Zone EPSG).
    /// </summary>
    public static async Task<bool> ConvertCadToDatasetAsync(string cadFilePath, string gdbPath, string datasetName, int epsgCode)
    {
        if (string.IsNullOrWhiteSpace(cadFilePath) || !File.Exists(cadFilePath))
            return false;

        await EnsureGeodatabaseAsync(gdbPath);

        string datasetPath = Path.Combine(gdbPath, datasetName);

        // Delete existing dataset if present to ensure clean import
        try
        {
            var delArgs = Geoprocessing.MakeValueArray(datasetPath);
            await Geoprocessing.ExecuteToolAsync("management.Delete", delArgs);
        }
        catch { }

        // Execute CADToGeodatabase with the specified EPSG coordinate system
        string srWktOrEpsg = epsgCode.ToString();
        try
        {
            var sr = ArcGIS.Core.Geometry.SpatialReferenceBuilder.CreateSpatialReference(epsgCode);
            if (sr != null && !string.IsNullOrWhiteSpace(sr.Wkt))
            {
                srWktOrEpsg = sr.Wkt;
            }
        }
        catch { }

        var args = Geoprocessing.MakeValueArray(
            cadFilePath,
            gdbPath,
            datasetName,
            "1000",
            srWktOrEpsg
        );

        var result = await Geoprocessing.ExecuteToolAsync("conversion.CADToGeodatabase", args);
        if (result.IsFailed)
        {
            System.Diagnostics.Debug.WriteLine($"conversion.CADToGeodatabase error: {result.Messages}");
            return false;
        }

        return true;
    }

    public static bool DatasetExists(string gdbPath, string datasetName)
    {
        if (string.IsNullOrWhiteSpace(gdbPath) || !Directory.Exists(gdbPath)) return false;
        try
        {
            if (File.Exists(Path.Combine(gdbPath, datasetName)) || Directory.Exists(Path.Combine(gdbPath, datasetName)))
                return true;

            if (ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.OnWorker)
            {
                return CheckDatasetInternal(gdbPath, datasetName);
            }
            return Task.Run(async () => await DatasetExistsAsync(gdbPath, datasetName)).GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> DatasetExistsAsync(string gdbPath, string datasetName)
    {
        if (string.IsNullOrWhiteSpace(gdbPath) || !Directory.Exists(gdbPath)) return false;
        try
        {
            if (File.Exists(Path.Combine(gdbPath, datasetName)) || Directory.Exists(Path.Combine(gdbPath, datasetName)))
                return true;

            if (ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.OnWorker)
            {
                return CheckDatasetInternal(gdbPath, datasetName);
            }
            return await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() => CheckDatasetInternal(gdbPath, datasetName));
        }
        catch
        {
            return false;
        }
    }

    public static bool DatasetExists(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        int gdbIdx = fullPath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
        if (gdbIdx >= 0)
        {
            string gdbPath = fullPath.Substring(0, gdbIdx + 4);
            string dsName = fullPath.Substring(gdbIdx + 4).TrimStart('\\', '/');
            return DatasetExists(gdbPath, dsName);
        }
        return false;
    }

    public static async Task<bool> DatasetExistsAsync(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        int gdbIdx = fullPath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
        if (gdbIdx >= 0)
        {
            string gdbPath = fullPath.Substring(0, gdbIdx + 4);
            string dsName = fullPath.Substring(gdbIdx + 4).TrimStart('\\', '/');
            return await DatasetExistsAsync(gdbPath, dsName);
        }
        return false;
    }

    public static bool ItemExists(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;
        int gdbIdx = fullPath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
        if (gdbIdx >= 0)
        {
            string gdbPath = fullPath.Substring(0, gdbIdx + 4);
            string itemName = fullPath.Substring(gdbIdx + 4).TrimStart('\\', '/');
            if (string.IsNullOrWhiteSpace(itemName))
                return Directory.Exists(gdbPath);
            return FeatureClassExists(gdbPath, itemName) || DatasetExists(gdbPath, itemName);
        }
        return false;
    }

    public static async Task<bool> ItemExistsAsync(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;
        int gdbIdx = fullPath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
        if (gdbIdx >= 0)
        {
            string gdbPath = fullPath.Substring(0, gdbIdx + 4);
            string itemName = fullPath.Substring(gdbIdx + 4).TrimStart('\\', '/');
            if (string.IsNullOrWhiteSpace(itemName))
                return Directory.Exists(gdbPath);
            bool fc = await FeatureClassExistsAsync(gdbPath, itemName);
            if (fc) return true;
            return await DatasetExistsAsync(gdbPath, itemName);
        }
        return false;
    }

    public static bool FeatureClassExists(string gdbPath, string fcName)
    {
        if (string.IsNullOrWhiteSpace(gdbPath) || !Directory.Exists(gdbPath)) return false;
        try
        {
            if (File.Exists(Path.Combine(gdbPath, fcName)) || Directory.Exists(Path.Combine(gdbPath, fcName)))
                return true;

            if (ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.OnWorker)
            {
                return CheckFeatureClassInternal(gdbPath, fcName);
            }
            return Task.Run(async () => await FeatureClassExistsAsync(gdbPath, fcName)).GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> FeatureClassExistsAsync(string gdbPath, string fcName)
    {
        if (string.IsNullOrWhiteSpace(gdbPath) || !Directory.Exists(gdbPath)) return false;
        try
        {
            if (File.Exists(Path.Combine(gdbPath, fcName)) || Directory.Exists(Path.Combine(gdbPath, fcName)))
                return true;

            if (ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.OnWorker)
            {
                return CheckFeatureClassInternal(gdbPath, fcName);
            }
            return await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() => CheckFeatureClassInternal(gdbPath, fcName));
        }
        catch
        {
            return false;
        }
    }

    public static bool FeatureClassExists(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        int gdbIdx = fullPath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
        if (gdbIdx >= 0)
        {
            string gdbPath = fullPath.Substring(0, gdbIdx + 4);
            string fcName = fullPath.Substring(gdbIdx + 4).TrimStart('\\', '/');
            return FeatureClassExists(gdbPath, fcName);
        }
        return File.Exists(fullPath);
    }

    public static async Task<bool> FeatureClassExistsAsync(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        int gdbIdx = fullPath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
        if (gdbIdx >= 0)
        {
            string gdbPath = fullPath.Substring(0, gdbIdx + 4);
            string fcName = fullPath.Substring(gdbIdx + 4).TrimStart('\\', '/');
            return await FeatureClassExistsAsync(gdbPath, fcName);
        }
        return File.Exists(fullPath);
    }

    private static bool CheckDatasetInternal(string gdbPath, string datasetName)
    {
        try
        {
            using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
            var dsDefs = gdb.GetDefinitions<FeatureDatasetDefinition>();
            if (dsDefs.Any(d => d.GetName().Equals(datasetName, StringComparison.OrdinalIgnoreCase) ||
                                d.GetName().EndsWith($"\\{datasetName}", StringComparison.OrdinalIgnoreCase) ||
                                d.GetName().EndsWith($"/{datasetName}", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var fcDefs = gdb.GetDefinitions<FeatureClassDefinition>();
            return fcDefs.Any(d => d.GetName().StartsWith($"{datasetName}\\", StringComparison.OrdinalIgnoreCase) ||
                                   d.GetName().StartsWith($"{datasetName}/", StringComparison.OrdinalIgnoreCase) ||
                                   d.GetName().StartsWith($"{datasetName}_", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckFeatureClassInternal(string gdbPath, string fcName)
    {
        try
        {
            using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
            var defs = gdb.GetDefinitions<FeatureClassDefinition>();
            return defs.Any(d => d.GetName().Equals(fcName, StringComparison.OrdinalIgnoreCase) ||
                                 d.GetName().EndsWith($"\\{fcName}", StringComparison.OrdinalIgnoreCase) ||
                                 d.GetName().EndsWith($"/{fcName}", StringComparison.OrdinalIgnoreCase) ||
                                 d.GetName().EndsWith($"_{fcName}", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public static void AddCadDatasetToMap(Map? map, string gdbPath, string datasetName)
    {
        if (map == null || !Directory.Exists(gdbPath)) return;
        try
        {
            using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
            string[] candidateFcNames = new[] { "Polygon", "Polyline", "Point", "Annotation" };

            foreach (var fcName in candidateFcNames)
            {
                try
                {
                    using var fc = OpenDatasetFeatureClass(gdb, fcName);
                    if (fc != null)
                    {
                        long count = 0;
                        try { count = fc.GetCount(); } catch { }
                        
                        // Add ONLY feature classes that exist in the dataset and contain features
                        if (count > 0)
                        {
                            string caption = $"{datasetName} {fcName}";
                            AddOrRefreshMapLayer(map, gdbPath, $"{datasetName}\\{fcName}", caption);
                        }
                    }
                }
                catch (Exception exFc)
                {
                    System.Diagnostics.Debug.WriteLine($"[AddCadDatasetToMap] Skip '{fcName}': {exFc.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddCadDatasetToMap] Error: {ex.Message}");
        }
    }

    private static FeatureClass? OpenDatasetFeatureClass(Geodatabase gdb, string targetName)
    {
        try
        {
            return gdb.OpenDataset<FeatureClass>(targetName);
        }
        catch
        {
            try
            {
                var defs = gdb.GetDefinitions<FeatureClassDefinition>();
                var match = defs.FirstOrDefault(d =>
                    d.GetName().Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
                    d.GetName().EndsWith($"\\{targetName}", StringComparison.OrdinalIgnoreCase) ||
                    d.GetName().EndsWith($"_{targetName}", StringComparison.OrdinalIgnoreCase) ||
                    d.GetName().EndsWith($"/{targetName}", StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return gdb.OpenDataset<FeatureClass>(match.GetName());
                }
            }
            catch { }
        }
        return null;
    }

    public static List<CadLayerInfo> GetCadLayersFromDataset(string gdbPath, string datasetName)
    {
        var result = new List<CadLayerInfo>();
        if (!Directory.Exists(gdbPath)) return result;

        try
        {
            using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
            var targetFcNames = new[] { "Polygon", "Polyline", "Point", "Annotation" };

            var layerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var layerGeomTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var layerTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fcName in targetFcNames)
            {
                using var fc = OpenDatasetFeatureClass(gdb, fcName);
                if (fc == null) continue;

                var def = fc.GetDefinition();
                int layerIdx = def.FindField("Layer");
                if (layerIdx < 0) continue;

                int textIdx = def.FindField("Text");
                if (textIdx < 0) textIdx = def.FindField("TxtMemo");

                string defaultGeom = fcName switch
                {
                    "Polygon" => "Polygon",
                    "Polyline" => "Line",
                    "Point" => "Point",
                    "Annotation" => "Text",
                    _ => "Polygon"
                };

                using var cursor = fc.Search(null, false);
                while (cursor.MoveNext())
                {
                    using var row = cursor.Current;
                    var layerVal = row[layerIdx]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(layerVal)) continue;

                    layerCounts[layerVal] = layerCounts.TryGetValue(layerVal, out int cnt) ? cnt + 1 : 1;

                    if (!layerGeomTypes.ContainsKey(layerVal) || defaultGeom == "Polygon")
                    {
                        layerGeomTypes[layerVal] = defaultGeom;
                    }

                    if (textIdx >= 0 && !layerTexts.ContainsKey(layerVal))
                    {
                        var txt = row[textIdx]?.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(txt) && txt.Length > 1)
                        {
                            layerTexts[layerVal] = txt;
                        }
                    }
                }
            }

            foreach (var kvp in layerCounts.OrderBy(k => k.Key))
            {
                string name = kvp.Key;
                int count = kvp.Value;
                string geom = layerGeomTypes.TryGetValue(name, out var g) ? g : "Polygon";

                var role = CadExtractionService.InferRoleFromName(name);
                var cat = CadLayerInfo.DetermineCategory(role, name);

                if (cat == "Ignore Layers" || cat == "Roads & References")
                {
                    role = LayerRole.Ignore;
                }

                var item = new CadLayerInfo
                {
                    LayerName = name,
                    EntityCount = count,
                    GeometryType = geom,
                    Role = role,
                    Category = cat,
                    IsSelected = (cat != "Ignore Layers" && cat != "Roads & References" && role != LayerRole.Ignore)
                };

                if (layerTexts.TryGetValue(name, out var rawTxt))
                {
                    item.RawCadText = rawTxt;
                    item.DecodedArabicText = ArabicCadDecoder.Decode(rawTxt);
                }

                CadExtractionService.EnrichCadLayerMetadata(item);
                result.Add(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cataloging layers from dataset: {ex.Message}");
        }

        return result;
    }

    public static DatasetCadFeatures ReadDatasetFeatures(string gdbPath, string datasetName, HashSet<string>? eligibleLayerNames)
    {
        var features = new DatasetCadFeatures();
        if (!Directory.Exists(gdbPath)) return features;

        try
        {
            using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));

            // 1. Read Polygon Feature Class
            using (var polyFc = OpenDatasetFeatureClass(gdb, "Polygon"))
            {
                if (polyFc != null)
                {
                    var def = polyFc.GetDefinition();
                    int layerIdx = def.FindField("Layer");
                    int textIdx = def.FindField("Text");
                    if (textIdx < 0) textIdx = def.FindField("TxtMemo");

                    using var cursor = polyFc.Search(null, false);
                    while (cursor.MoveNext())
                    {
                        using var feat = (Feature)cursor.Current;
                        string layerVal = layerIdx >= 0 ? feat[layerIdx]?.ToString()?.Trim() ?? "" : "";

                        if (eligibleLayerNames != null)
                        {
                            if (!eligibleLayerNames.Contains(layerVal)) continue;
                        }

                        if (feat.GetShape() is Polygon poly && !poly.IsEmpty)
                        {
                            var poly2D = ParcelPolygonizer.Ensure2D(poly);
                            string txt = textIdx >= 0 ? feat[textIdx]?.ToString()?.Trim() ?? "" : "";
                            features.Polygons.Add((poly2D, layerVal, txt));
                        }
                    }
                }
            }

            // 2. Read Polyline Feature Class
            using (var lineFc = OpenDatasetFeatureClass(gdb, "Polyline"))
            {
                if (lineFc != null)
                {
                    var def = lineFc.GetDefinition();
                    int layerIdx = def.FindField("Layer");

                    using var cursor = lineFc.Search(null, false);
                    while (cursor.MoveNext())
                    {
                        using var feat = (Feature)cursor.Current;
                        string layerVal = layerIdx >= 0 ? feat[layerIdx]?.ToString()?.Trim() ?? "" : "";

                        if (eligibleLayerNames != null)
                        {
                            if (!eligibleLayerNames.Contains(layerVal)) continue;
                        }

                        if (feat.GetShape() is Polyline polyline && !polyline.IsEmpty)
                        {
                            var lineGeom = new LineGeometry2D { LayerName = layerVal };
                            foreach (var pt in polyline.Points)
                            {
                                lineGeom.Points.Add(new Point2D(pt.X, pt.Y));
                            }
                            if (lineGeom.Points.Count >= 2)
                            {
                                features.LinesToPolygonize.Add(lineGeom);
                            }

                            // Extract true segments (lines, arcs, Béziers) preserving curves 100% intact
                            var curveEntity = new CurvePolylineEntity
                            {
                                LayerName = layerVal,
                                Category = "Main Parcels",
                                NativeGeometryTag = polyline
                            };

                            foreach (var part in polyline.Parts)
                            {
                                foreach (var seg in part)
                                {
                                    var pStart = seg.StartPoint;
                                    var pEnd = seg.EndPoint;
                                    var s2D = new Point2D(pStart.X, pStart.Y);
                                    var e2D = new Point2D(pEnd.X, pEnd.Y);

                                    if (seg is EllipticArcSegment arc)
                                    {
                                        var cPt = arc.CenterPoint;
                                        curveEntity.Segments.Add(new CoreBoundarySegment(s2D, e2D, CurveType.CircularArc, seg.Length)
                                        {
                                            Center = new Point2D(cPt.X, cPt.Y),
                                            Radius = arc.SemiMajorAxis,
                                            IsCounterClockwise = arc.IsCounterClockwise,
                                            NativeGeometryTag = seg
                                        });
                                    }
                                    else if (seg is CubicBezierSegment)
                                    {
                                        curveEntity.Segments.Add(new CoreBoundarySegment(s2D, e2D, CurveType.Bezier, seg.Length)
                                        {
                                            NativeGeometryTag = seg
                                        });
                                    }
                                    else
                                    {
                                        curveEntity.Segments.Add(new CoreBoundarySegment(s2D, e2D, CurveType.Straight, seg.Length)
                                        {
                                            NativeGeometryTag = seg
                                        });
                                    }
                                }
                            }

                            if (curveEntity.Segments.Count > 0)
                            {
                                features.CurveEntitiesToPolygonize.Add(curveEntity);
                            }
                        }
                    }
                }
            }

            // 3. Read Annotation Feature Class
            using (var annotFc = OpenDatasetFeatureClass(gdb, "Annotation"))
            {
                if (annotFc != null)
                {
                    var def = annotFc.GetDefinition();
                    int layerIdx = def.FindField("Layer");
                    int textIdx = def.FindField("Text");
                    int memoIdx = def.FindField("TxtMemo");
                    int strIdx = def.FindField("TextString");
                    int refIdx = def.FindField("RefName");

                    using var cursor = annotFc.Search(null, false);
                    while (cursor.MoveNext())
                    {
                        using var feat = (Feature)cursor.Current;
                        string rawText = "";
                        if (textIdx >= 0) rawText = feat[textIdx]?.ToString()?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(rawText) && memoIdx >= 0) rawText = feat[memoIdx]?.ToString()?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(rawText) && strIdx >= 0) rawText = feat[strIdx]?.ToString()?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(rawText) && refIdx >= 0) rawText = feat[refIdx]?.ToString()?.Trim() ?? "";

                        if (string.IsNullOrWhiteSpace(rawText)) continue;

                        string layerVal = layerIdx >= 0 ? feat[layerIdx]?.ToString()?.Trim() ?? "" : "";
                        string decodedText = ArabicCadDecoder.Decode(rawText);

                        var shape = feat.GetShape();
                        MapPoint? loc = null;
                        if (shape is MapPoint mp && !mp.IsEmpty)
                        {
                            loc = ParcelPolygonizer.Ensure2D(mp);
                        }
                        else if (shape != null && !shape.IsEmpty)
                        {
                            loc = ParcelPolygonizer.Ensure2D(shape.Extent.Center);
                        }

                        if (loc != null)
                        {
                            features.Annotations.Add((decodedText, loc, layerVal));
                        }
                    }
                }
            }

            // 4. Read Point Feature Class for point text / block attribute entities
            using (var ptFc = OpenDatasetFeatureClass(gdb, "Point"))
            {
                if (ptFc != null)
                {
                    var def = ptFc.GetDefinition();
                    int layerIdx = def.FindField("Layer");
                    int textIdx = def.FindField("Text");
                    int memoIdx = def.FindField("TxtMemo");
                    int strIdx = def.FindField("TextString");
                    int refIdx = def.FindField("RefName");

                    if (textIdx >= 0 || memoIdx >= 0 || strIdx >= 0 || refIdx >= 0)
                    {
                        using var cursor = ptFc.Search(null, false);
                        while (cursor.MoveNext())
                        {
                            using var feat = (Feature)cursor.Current;
                            string rawText = "";
                            if (textIdx >= 0) rawText = feat[textIdx]?.ToString()?.Trim() ?? "";
                            if (string.IsNullOrWhiteSpace(rawText) && memoIdx >= 0) rawText = feat[memoIdx]?.ToString()?.Trim() ?? "";
                            if (string.IsNullOrWhiteSpace(rawText) && strIdx >= 0) rawText = feat[strIdx]?.ToString()?.Trim() ?? "";
                            if (string.IsNullOrWhiteSpace(rawText) && refIdx >= 0) rawText = feat[refIdx]?.ToString()?.Trim() ?? "";

                            if (string.IsNullOrWhiteSpace(rawText)) continue;

                            string layerVal = layerIdx >= 0 ? feat[layerIdx]?.ToString()?.Trim() ?? "" : "";
                            string decodedText = ArabicCadDecoder.Decode(rawText);

                            var shape = feat.GetShape();
                            MapPoint? loc = null;
                            if (shape is MapPoint mp && !mp.IsEmpty)
                            {
                                loc = ParcelPolygonizer.Ensure2D(mp);
                            }
                            else if (shape != null && !shape.IsEmpty)
                            {
                                loc = ParcelPolygonizer.Ensure2D(shape.Extent.Center);
                            }

                            if (loc != null)
                            {
                                features.Annotations.Add((decodedText, loc, layerVal));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading dataset features: {ex.Message}");
        }

        return features;
    }

    public static void WriteParcelsToFeatureClass(string gdbPath, string fcName, IEnumerable<ParcelRecord> parcels, IReadOnlyDictionary<long, Polygon> geometries)
    {
        using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
        using var fc = gdb.OpenDataset<FeatureClass>(fcName);
        var def = fc.GetDefinition();
        var fcSr = def.GetSpatialReference();

        // Truncate existing records to avoid duplication on re-run
        using (var deleteCursor = fc.Search(null, false))
        {
            while (deleteCursor.MoveNext())
            {
                using var row = deleteCursor.Current;
                row.Delete();
            }
        }

        // Write parcel records
        using var insertCursor = fc.CreateInsertCursor();
        foreach (var p in parcels)
        {
            using var rowBuffer = fc.CreateRowBuffer();

            // Geometry
            if (geometries.TryGetValue(p.InternalId, out var poly))
            {
                var poly2D = ParcelPolygonizer.Ensure2D(poly);
                if (fcSr != null && (poly2D.SpatialReference == null || !poly2D.SpatialReference.IsEqual(fcSr)))
                {
                    var pb = new PolygonBuilderEx(poly2D) { SpatialReference = fcSr };
                    pb.HasZ = false;
                    pb.HasM = false;
                    poly2D = (Polygon)pb.ToGeometry();
                }
                rowBuffer[def.GetShapeField()] = poly2D;
            }

            SetFieldSafe(rowBuffer, def, "PARCEL_TYP", p.ParcelTyp);
            SetFieldSafe(rowBuffer, def, "REALESTA_1", p.RealestateType);
            SetFieldSafe(rowBuffer, def, "NEIGHBOURH", p.Neighbourhood);
            SetFieldSafe(rowBuffer, def, "REGION_ID", p.RegionId);
            SetFieldSafe(rowBuffer, def, "CITY_ID", p.CityId);
            SetFieldSafe(rowBuffer, def, "ADDRESS", p.Address);
            SetFieldSafe(rowBuffer, def, "USAGE", p.Usage);
            SetFieldSafe(rowBuffer, def, "ISBUILDING", p.IsBuilding);
            SetFieldSafe(rowBuffer, def, "BUILDINGTY", p.BuildingType);
            SetFieldSafe(rowBuffer, def, "REMARKS", p.Remarks);
            SetFieldSafe(rowBuffer, def, "LAYOUT", p.Layout);
            SetFieldSafe(rowBuffer, def, "PLOT_ID", p.PlotId);
            SetFieldSafe(rowBuffer, def, "BLOCK_NO", p.BlockNo);
            SetFieldSafe(rowBuffer, def, "PLAN_NO", p.PlanNo);
            SetFieldSafe(rowBuffer, def, "PLOT_NO1", p.PlotNo1);
            SetFieldSafe(rowBuffer, def, "PLOT_NO2", p.PlotNo2);
            SetFieldSafe(rowBuffer, def, "PLOT_NO3", p.PlotNo3);
            SetFieldSafe(rowBuffer, def, "PLOT_NO4", p.PlotNo4);
            SetFieldSafe(rowBuffer, def, "PLOT_NO5", p.PlotNo5);
            SetFieldSafe(rowBuffer, def, "PLOT_NO6", p.PlotNo6);
            SetFieldSafe(rowBuffer, def, "REQUEST_TY", p.RequestType);
            SetFieldSafe(rowBuffer, def, "REQUEST_NO", p.RequestNo);
            SetFieldSafe(rowBuffer, def, "REPORT_NO", p.ReportNo);
            SetFieldSafe(rowBuffer, def, "AREA_SQM", p.AreaSqm);
            SetFieldSafe(rowBuffer, def, "Shape_Area", p.ShapeArea > 0 ? p.ShapeArea : p.AreaSqm);
            SetFieldSafe(rowBuffer, def, "Shape_Leng", p.ShapeLength);

            try
            {
                insertCursor.Insert(rowBuffer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WriteParcelsToFeatureClass] Failed to insert parcel {p.PlotId}: {ex.Message}");
            }
        }
        insertCursor.Flush();
    }

    public static void UpdateParcelAttributesInFeatureClass(string gdbPath, string fcName, IEnumerable<ParcelRecord> parcels)
    {
        try
        {
            if (!Directory.Exists(gdbPath)) return;
            using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
            var fcs = gdb.GetDefinitions<FeatureClassDefinition>();
            if (!fcs.Any(f => f.GetName().Equals(fcName, StringComparison.OrdinalIgnoreCase)))
                return;

            using var fc = gdb.OpenDataset<FeatureClass>(fcName);
            var def = fc.GetDefinition();
            var parcelMap = parcels.ToDictionary(p => p.PlotId, p => p, StringComparer.OrdinalIgnoreCase);

            using var cursor = fc.Search(null, false);
            while (cursor.MoveNext())
            {
                using var row = (Feature)cursor.Current;
                string plotId = GetFieldSafe(row, def, "PLOT_ID")?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(plotId) && parcelMap.TryGetValue(plotId, out var p))
                {
                    SetFieldSafe(row, def, "NEIGHBOURH", p.Neighbourhood);
                    SetFieldSafe(row, def, "REGION_ID", p.RegionId);
                    SetFieldSafe(row, def, "CITY_ID", p.CityId);
                    SetFieldSafe(row, def, "ADDRESS", p.Address);
                    row.Store();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateParcelAttributes] Error: {ex.Message}");
        }
    }

    public static void WriteBordersToFeatureClass(string gdbPath, string fcName, IEnumerable<BorderRecord> borders, IReadOnlyDictionary<long, Polyline> geometries)
    {
        using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
        using var fc = gdb.OpenDataset<FeatureClass>(fcName);
        var def = fc.GetDefinition();
        var fcSr = def.GetSpatialReference();

        using (var deleteCursor = fc.Search(null, false))
        {
            while (deleteCursor.MoveNext())
            {
                using var row = deleteCursor.Current;
                row.Delete();
            }
        }

        using var insertCursor = fc.CreateInsertCursor();
        foreach (var b in borders)
        {
            using var rowBuffer = fc.CreateRowBuffer();

            if (geometries.TryGetValue(b.InternalId, out var line))
            {
                var line2D = ParcelPolygonizer.Ensure2D(line);
                if (fcSr != null && (line2D.SpatialReference == null || !line2D.SpatialReference.IsEqual(fcSr)))
                {
                    var pb = new PolylineBuilderEx(line2D) { SpatialReference = fcSr };
                    pb.HasZ = false;
                    pb.HasM = false;
                    line2D = (Polyline)pb.ToGeometry();
                }
                rowBuffer[def.GetShapeField()] = line2D;
            }

            SetFieldSafe(rowBuffer, def, "PLOT_ID", b.PlotId);
            SetFieldSafe(rowBuffer, def, "PLAN_NO", b.PlanNo);
            SetFieldSafe(rowBuffer, def, "BORDERNAME", b.BorderName);
            SetFieldSafe(rowBuffer, def, "BORDERTYPE", b.BorderType);
            SetFieldSafe(rowBuffer, def, "BORDERDESC", b.BorderDesc);
            SetFieldSafe(rowBuffer, def, "DIMENSION", b.Dimension);
            SetFieldSafe(rowBuffer, def, "SHAPE_Leng", b.ShapeLength);

            if (def.FindField("DESCRIPTIO") >= 0)
                SetFieldSafe(rowBuffer, def, "DESCRIPTIO", b.Descriptio);
            else if (def.FindField("DESCRIPTO") >= 0)
                SetFieldSafe(rowBuffer, def, "DESCRIPTO", b.Descriptio);

            try
            {
                insertCursor.Insert(rowBuffer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WriteBordersToFeatureClass] Failed to insert border {b.PlotId}/{b.BorderName}: {ex.Message}");
            }
        }
        insertCursor.Flush();
    }

    public static async Task ExportParcelsToGdbAsync(string gdbPath, string fcName, string templateParcelPath, IEnumerable<ParcelRecord> parcels, IReadOnlyDictionary<long, Polygon> geometries, int epsgCode = 0)
    {
        await EnsureFeatureClassFromTemplateAsync(gdbPath, fcName, templateParcelPath, epsgCode);
        await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
        {
            WriteParcelsToFeatureClass(gdbPath, fcName, parcels, geometries);
        });
    }

    public static async Task ExportBordersToGdbAsync(string gdbPath, string fcName, string templateBorderPath, IEnumerable<BorderRecord> borders, IReadOnlyDictionary<long, Polyline> geometries, int epsgCode = 0)
    {
        await EnsureFeatureClassFromTemplateAsync(gdbPath, fcName, templateBorderPath, epsgCode);
        await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
        {
            WriteBordersToFeatureClass(gdbPath, fcName, borders, geometries);
        });
    }

    public static (List<ParcelRecord> Parcels, Dictionary<long, Polygon> Geometries) ReadParcelsFromFeatureClass(string gdbPath, string fcName = "PARCEL", int epsgCode = 0)
    {
        var parcels = new List<ParcelRecord>();
        var geometries = new Dictionary<long, Polygon>();

        using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
        
        string targetFc = fcName;
        try
        {
            var fcDefs = gdb.GetDefinitions<FeatureClassDefinition>();
            if (!fcDefs.Any(d => d.GetName().Equals(fcName, StringComparison.OrdinalIgnoreCase)))
            {
                var polyDef = fcDefs.FirstOrDefault(d => d.GetShapeType() == GeometryType.Polygon);
                if (polyDef != null)
                {
                    targetFc = polyDef.GetName();
                }
            }
        }
        catch { }

        using var fc = gdb.OpenDataset<FeatureClass>(targetFc);
        var def = fc.GetDefinition();
        var fcSr = def.GetSpatialReference();
        var targetSr = epsgCode > 0 ? SpatialReferenceBuilder.CreateSpatialReference(epsgCode) : fcSr;

        long idCounter = 1;
        using var cursor = fc.Search(null, false);
        while (cursor.MoveNext())
        {
            using var row = cursor.Current;
            var p = new ParcelRecord
            {
                InternalId = idCounter++,
                RealestateType = Convert.ToInt32(GetFieldSafe(row, def, "REALESTA_1") ?? 2),
                Neighbourhood = GetFieldSafe(row, def, "NEIGHBOURH")?.ToString() ?? string.Empty,
                RegionId = GetFieldSafe(row, def, "REGION_ID")?.ToString() ?? string.Empty,
                CityId = GetFieldSafe(row, def, "CITY_ID")?.ToString() ?? string.Empty,
                Address = GetFieldSafe(row, def, "ADDRESS")?.ToString() ?? string.Empty,
                Usage = GetFieldSafe(row, def, "USAGE")?.ToString() ?? string.Empty,
                IsBuilding = Convert.ToInt32(GetFieldSafe(row, def, "ISBUILDING") ?? 0),
                BuildingType = Convert.ToInt32(GetFieldSafe(row, def, "BUILDINGTY") ?? 1),
                Remarks = GetFieldSafe(row, def, "REMARKS")?.ToString() ?? string.Empty,
                Layout = GetFieldSafe(row, def, "LAYOUT")?.ToString(),
                PlotId = GetFieldSafe(row, def, "PLOT_ID")?.ToString() ?? string.Empty,
                BlockNo = GetFieldSafe(row, def, "BLOCK_NO")?.ToString(),
                PlanNo = GetFieldSafe(row, def, "PLAN_NO")?.ToString() ?? string.Empty,
                PlotNo1 = GetFieldSafe(row, def, "PLOT_NO1")?.ToString() ?? string.Empty,
                PlotNo2 = GetFieldSafe(row, def, "PLOT_NO2")?.ToString(),
                PlotNo3 = GetFieldSafe(row, def, "PLOT_NO3")?.ToString(),
                PlotNo4 = GetFieldSafe(row, def, "PLOT_NO4")?.ToString(),
                PlotNo5 = GetFieldSafe(row, def, "PLOT_NO5")?.ToString(),
                PlotNo6 = GetFieldSafe(row, def, "PLOT_NO6")?.ToString(),
                RequestType = GetFieldSafe(row, def, "REQUEST_TY")?.ToString() ?? string.Empty,
                RequestNo = GetFieldSafe(row, def, "REQUEST_NO")?.ToString() ?? string.Empty,
                ReportNo = GetFieldSafe(row, def, "REPORT_NO")?.ToString() ?? string.Empty,
                AreaSqm = Convert.ToDouble(GetFieldSafe(row, def, "AREA_SQM") ?? 0.0),
                ShapeArea = Convert.ToDouble(GetFieldSafe(row, def, "Shape_Area") ?? 0.0),
                ShapeLength = Convert.ToDouble(GetFieldSafe(row, def, "Shape_Leng") ?? 0.0)
            };

            var parcelTypObj = GetFieldSafe(row, def, "PARCEL_TYP");
            if (parcelTypObj != null && int.TryParse(parcelTypObj.ToString(), out int pType))
            {
                p.Class = pType == 2 ? ParcelClass.Sub : ParcelClass.Main;
            }

            if (row is Feature feature && feature.GetShape() is Polygon poly)
            {
                var poly2D = ParcelPolygonizer.Ensure2D(poly);
                if (targetSr != null && (poly2D.SpatialReference == null || !poly2D.SpatialReference.IsEqual(targetSr)))
                {
                    var pb = new PolygonBuilderEx(poly2D) { SpatialReference = targetSr };
                    pb.HasZ = false;
                    pb.HasM = false;
                    poly2D = (Polygon)pb.ToGeometry();
                }
                geometries[p.InternalId] = poly2D;
                p.CentroidX = poly2D.Extent.Center.X;
                p.CentroidY = poly2D.Extent.Center.Y;
            }

            parcels.Add(p);
        }

        return (parcels, geometries);
    }

    public static (List<ParcelRecord> Parcels, Dictionary<long, Polygon> Geometries) ReadParcelsFromFeatureLayer(FeatureLayer layer, int epsgCode = 0)
    {
        var parcels = new List<ParcelRecord>();
        var geometries = new Dictionary<long, Polygon>();
        if (layer == null) return (parcels, geometries);

        using var fc = layer.GetFeatureClass();
        if (fc == null) return (parcels, geometries);

        var def = fc.GetDefinition();
        var targetSr = epsgCode > 0 ? SpatialReferenceBuilder.CreateSpatialReference(epsgCode) : def.GetSpatialReference();

        long idCounter = 1;
        using var cursor = layer.Search(null);
        while (cursor.MoveNext())
        {
            using var row = cursor.Current;
            var p = new ParcelRecord
            {
                InternalId = idCounter++,
                RealestateType = Convert.ToInt32(GetFieldSafe(row, def, "REALESTA_1") ?? 2),
                Neighbourhood = GetFieldSafe(row, def, "NEIGHBOURH")?.ToString() ?? string.Empty,
                RegionId = GetFieldSafe(row, def, "REGION_ID")?.ToString() ?? string.Empty,
                CityId = GetFieldSafe(row, def, "CITY_ID")?.ToString() ?? string.Empty,
                Address = GetFieldSafe(row, def, "ADDRESS")?.ToString() ?? string.Empty,
                Usage = GetFieldSafe(row, def, "USAGE")?.ToString() ?? string.Empty,
                IsBuilding = Convert.ToInt32(GetFieldSafe(row, def, "ISBUILDING") ?? 0),
                BuildingType = Convert.ToInt32(GetFieldSafe(row, def, "BUILDINGTY") ?? 1),
                Remarks = GetFieldSafe(row, def, "REMARKS")?.ToString() ?? string.Empty,
                Layout = GetFieldSafe(row, def, "LAYOUT")?.ToString(),
                PlotId = GetFieldSafe(row, def, "PLOT_ID")?.ToString() ?? string.Empty,
                BlockNo = GetFieldSafe(row, def, "BLOCK_NO")?.ToString(),
                PlanNo = GetFieldSafe(row, def, "PLAN_NO")?.ToString() ?? string.Empty,
                PlotNo1 = GetFieldSafe(row, def, "PLOT_NO1")?.ToString() ?? string.Empty,
                PlotNo2 = GetFieldSafe(row, def, "PLOT_NO2")?.ToString(),
                PlotNo3 = GetFieldSafe(row, def, "PLOT_NO3")?.ToString(),
                PlotNo4 = GetFieldSafe(row, def, "PLOT_NO4")?.ToString(),
                PlotNo5 = GetFieldSafe(row, def, "PLOT_NO5")?.ToString(),
                PlotNo6 = GetFieldSafe(row, def, "PLOT_NO6")?.ToString(),
                RequestType = GetFieldSafe(row, def, "REQUEST_TY")?.ToString() ?? string.Empty,
                RequestNo = GetFieldSafe(row, def, "REQUEST_NO")?.ToString() ?? string.Empty,
                ReportNo = GetFieldSafe(row, def, "REPORT_NO")?.ToString() ?? string.Empty,
                AreaSqm = Convert.ToDouble(GetFieldSafe(row, def, "AREA_SQM") ?? 0.0),
                ShapeArea = Convert.ToDouble(GetFieldSafe(row, def, "Shape_Area") ?? 0.0),
                ShapeLength = Convert.ToDouble(GetFieldSafe(row, def, "Shape_Leng") ?? 0.0)
            };

            var parcelTypObj = GetFieldSafe(row, def, "PARCEL_TYP");
            if (parcelTypObj != null && int.TryParse(parcelTypObj.ToString(), out int pType))
            {
                p.Class = pType == 2 ? ParcelClass.Sub : ParcelClass.Main;
            }

            if (row is Feature feature && feature.GetShape() is Polygon poly)
            {
                var poly2D = ParcelPolygonizer.Ensure2D(poly);
                if (targetSr != null && (poly2D.SpatialReference == null || !poly2D.SpatialReference.IsEqual(targetSr)))
                {
                    var pb = new PolygonBuilderEx(poly2D) { SpatialReference = targetSr };
                    pb.HasZ = false;
                    pb.HasM = false;
                    poly2D = (Polygon)pb.ToGeometry();
                }
                geometries[p.InternalId] = poly2D;
                p.CentroidX = poly2D.Extent.Center.X;
                p.CentroidY = poly2D.Extent.Center.Y;
            }

            parcels.Add(p);
        }

        return (parcels, geometries);
    }

    public static (List<BorderRecord> Borders, Dictionary<long, Polyline> Geometries) ReadBordersFromFeatureLayer(FeatureLayer layer, int epsgCode = 0)
    {
        var borders = new List<BorderRecord>();
        var geometries = new Dictionary<long, Polyline>();
        if (layer == null) return (borders, geometries);

        using var fc = layer.GetFeatureClass();
        if (fc == null) return (borders, geometries);

        var def = fc.GetDefinition();
        var targetSr = epsgCode > 0 ? SpatialReferenceBuilder.CreateSpatialReference(epsgCode) : def.GetSpatialReference();

        long idCounter = 1;
        using var cursor = layer.Search(null);
        while (cursor.MoveNext())
        {
            using var row = cursor.Current;
            var b = new BorderRecord
            {
                InternalId = idCounter++,
                PlotId = GetFieldSafe(row, def, "PLOT_ID")?.ToString() ?? string.Empty,
                PlanNo = GetFieldSafe(row, def, "PLAN_NO")?.ToString() ?? string.Empty,
                BorderName = Convert.ToInt32(GetFieldSafe(row, def, "BORDERNAME") ?? 1),
                BorderType = GetFieldSafe(row, def, "BORDERTYPE")?.ToString() ?? "STR",
                BorderDesc = Convert.ToInt64(GetFieldSafe(row, def, "BORDERDESC") ?? 0L),
                Dimension = GetFieldSafe(row, def, "DIMENSION")?.ToString() ?? "Single",
                ShapeLength = Convert.ToDouble(GetFieldSafe(row, def, "SHAPE_Leng") ?? 0.0),
                Descriptio = (GetFieldSafe(row, def, "DESCRIPTIO") ?? GetFieldSafe(row, def, "DESCRIPTO"))?.ToString() ?? string.Empty
            };

            if (row is Feature feature && feature.GetShape() is Polyline line)
            {
                var line2D = ParcelPolygonizer.Ensure2D(line);
                if (targetSr != null && (line2D.SpatialReference == null || !line2D.SpatialReference.IsEqual(targetSr)))
                {
                    var pb = new PolylineBuilderEx(line2D) { SpatialReference = targetSr };
                    pb.HasZ = false;
                    pb.HasM = false;
                    line2D = (Polyline)pb.ToGeometry();
                }
                geometries[b.InternalId] = line2D;
            }

            borders.Add(b);
        }

        return (borders, geometries);
    }

    public static (List<BorderRecord> Borders, Dictionary<long, Polyline> Geometries) ReadBordersFromFeatureClass(string gdbPath, string fcName = "Border", int epsgCode = 0)
    {
        var borders = new List<BorderRecord>();
        var geometries = new Dictionary<long, Polyline>();

        using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
        
        string targetFc = fcName;
        try
        {
            var fcDefs = gdb.GetDefinitions<FeatureClassDefinition>();
            if (!fcDefs.Any(d => d.GetName().Equals(fcName, StringComparison.OrdinalIgnoreCase)))
            {
                var lineDef = fcDefs.FirstOrDefault(d => d.GetShapeType() == GeometryType.Polyline);
                if (lineDef != null)
                {
                    targetFc = lineDef.GetName();
                }
            }
        }
        catch { }

        using var fc = gdb.OpenDataset<FeatureClass>(targetFc);
        var def = fc.GetDefinition();
        var fcSr = def.GetSpatialReference();
        var targetSr = epsgCode > 0 ? SpatialReferenceBuilder.CreateSpatialReference(epsgCode) : fcSr;

        long idCounter = 1;
        using var cursor = fc.Search(null, false);
        while (cursor.MoveNext())
        {
            using var row = cursor.Current;
            var b = new BorderRecord
            {
                InternalId = idCounter++,
                PlotId = GetFieldSafe(row, def, "PLOT_ID")?.ToString() ?? string.Empty,
                PlanNo = GetFieldSafe(row, def, "PLAN_NO")?.ToString() ?? string.Empty,
                BorderName = Convert.ToInt32(GetFieldSafe(row, def, "BORDERNAME") ?? 1),
                BorderType = GetFieldSafe(row, def, "BORDERTYPE")?.ToString() ?? "STR",
                BorderDesc = Convert.ToInt64(GetFieldSafe(row, def, "BORDERDESC") ?? 0L),
                Dimension = GetFieldSafe(row, def, "DIMENSION")?.ToString() ?? "Single",
                ShapeLength = Convert.ToDouble(GetFieldSafe(row, def, "SHAPE_Leng") ?? 0.0),
                Descriptio = (GetFieldSafe(row, def, "DESCRIPTIO") ?? GetFieldSafe(row, def, "DESCRIPTO"))?.ToString() ?? string.Empty
            };

            if (row is Feature feature && feature.GetShape() is Polyline line)
            {
                var line2D = ParcelPolygonizer.Ensure2D(line);
                if (targetSr != null && (line2D.SpatialReference == null || !line2D.SpatialReference.IsEqual(targetSr)))
                {
                    var pb = new PolylineBuilderEx(line2D) { SpatialReference = targetSr };
                    pb.HasZ = false;
                    pb.HasM = false;
                    line2D = (Polyline)pb.ToGeometry();
                }
                geometries[b.InternalId] = line2D;
            }

            borders.Add(b);
        }

        return (borders, geometries);
    }

    public static (List<ParcelRecord> Parcels, Dictionary<long, Polygon> Geometries, bool WasCopiedDueToLock, string? Notice) ReadParcelsFromDataset(
        string pathOrFolder, int targetUtmEpsg, string scratchDir)
    {
        var parcels = new List<ParcelRecord>();
        var geometries = new Dictionary<long, Polygon>();

        if (string.IsNullOrWhiteSpace(pathOrFolder))
            return (parcels, geometries, false, "Path is empty");

        // 1. Check if pathOrFolder points to an active Feature Layer in ArcGIS Pro Map Contents
        try
        {
            var map = MapView.Active?.Map;
            if (map != null)
            {
                var fl = map.GetLayersAsFlattenedList().OfType<FeatureLayer>()
                    .FirstOrDefault(l => l.Name.Equals(pathOrFolder, StringComparison.OrdinalIgnoreCase));
                if (fl != null)
                {
                    var res = ReadParcelsFromFeatureLayer(fl, targetUtmEpsg);
                    return (res.Parcels, res.Geometries, false, $"Loaded from active Map Layer '{fl.Name}'");
                }
            }
        }
        catch { }

        // 2. Check if pathOrFolder is a direct Feature Class path inside a GDB, e.g. "C:\Path\Data.gdb\FeatureClassName"
        int gdbIdx = pathOrFolder.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
        if (gdbIdx >= 0)
        {
            string gdbPath = pathOrFolder.Substring(0, gdbIdx + 4);
            string remainder = pathOrFolder.Substring(gdbIdx + 4).TrimStart('\\', '/');
            string fcName = !string.IsNullOrWhiteSpace(remainder) ? remainder : "PARCEL";

            if (Directory.Exists(gdbPath))
            {
                var res = ReadParcelsFromFeatureClass(gdbPath, fcName, targetUtmEpsg);
                return (res.Parcels, res.Geometries, false, null);
            }
        }

        // 3. Check if folder contains Cad2Gis_Workspace.gdb, Export_Deliverables.gdb or candidate shapefiles
        string targetPath = pathOrFolder;
        if (Directory.Exists(pathOrFolder) && !pathOrFolder.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
        {
            string deliverableGdb = Path.Combine(pathOrFolder, "Cad2Gis_Workspace.gdb");
            if (!Directory.Exists(deliverableGdb))
            {
                deliverableGdb = Path.Combine(pathOrFolder, "Export_Deliverables.gdb");
            }

            if (Directory.Exists(deliverableGdb))
            {
                var res = ReadParcelsFromFeatureClass(deliverableGdb, "PARCEL", targetUtmEpsg);
                return (res.Parcels, res.Geometries, false, null);
            }

            string candidate = Path.Combine(pathOrFolder, "PARCEL.shp");
            if (File.Exists(candidate)) targetPath = candidate;
        }

        var (resolvedPath, wasCopied, notice) = ExportStateManager.ResolveReadableCopy(targetPath, scratchDir);

        if (resolvedPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase) || (Directory.Exists(resolvedPath) && resolvedPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase)))
        {
            var res = ReadParcelsFromFeatureClass(resolvedPath, "PARCEL", targetUtmEpsg);
            return (res.Parcels, res.Geometries, wasCopied, notice);
        }

        if (File.Exists(resolvedPath))
        {
            string folder = Path.GetDirectoryName(resolvedPath)!;
            string fileName = Path.GetFileName(resolvedPath);

            var fileConn = new FileSystemConnectionPath(new Uri(folder), FileSystemDatastoreType.Shapefile);
            using var datastore = new FileSystemDatastore(fileConn);
            using var fc = datastore.OpenDataset<FeatureClass>(fileName);
            var def = fc.GetDefinition();
            var fcSr = def.GetSpatialReference();
            var targetSr = targetUtmEpsg > 0 ? SpatialReferenceBuilder.CreateSpatialReference(targetUtmEpsg) : fcSr;

            long idCounter = 1;
            using var cursor = fc.Search(null, false);
            while (cursor.MoveNext())
            {
                using var row = cursor.Current;
                var p = new ParcelRecord
                {
                    InternalId = idCounter++,
                    RealestateType = Convert.ToInt32(GetFieldSafe(row, def, "REALESTA_1") ?? 2),
                    Neighbourhood = GetFieldSafe(row, def, "NEIGHBOURH")?.ToString() ?? string.Empty,
                    RegionId = GetFieldSafe(row, def, "REGION_ID")?.ToString() ?? string.Empty,
                    CityId = GetFieldSafe(row, def, "CITY_ID")?.ToString() ?? string.Empty,
                    Address = GetFieldSafe(row, def, "ADDRESS")?.ToString() ?? string.Empty,
                    Usage = GetFieldSafe(row, def, "USAGE")?.ToString() ?? string.Empty,
                    IsBuilding = Convert.ToInt32(GetFieldSafe(row, def, "ISBUILDING") ?? 0),
                    BuildingType = Convert.ToInt32(GetFieldSafe(row, def, "BUILDINGTY") ?? 1),
                    Remarks = GetFieldSafe(row, def, "REMARKS")?.ToString() ?? string.Empty,
                    Layout = GetFieldSafe(row, def, "LAYOUT")?.ToString(),
                    PlotId = GetFieldSafe(row, def, "PLOT_ID")?.ToString() ?? string.Empty,
                    BlockNo = GetFieldSafe(row, def, "BLOCK_NO")?.ToString(),
                    PlanNo = GetFieldSafe(row, def, "PLAN_NO")?.ToString() ?? string.Empty,
                    PlotNo1 = GetFieldSafe(row, def, "PLOT_NO1")?.ToString() ?? string.Empty,
                    PlotNo2 = GetFieldSafe(row, def, "PLOT_NO2")?.ToString(),
                    PlotNo3 = GetFieldSafe(row, def, "PLOT_NO3")?.ToString(),
                    PlotNo4 = GetFieldSafe(row, def, "PLOT_NO4")?.ToString(),
                    PlotNo5 = GetFieldSafe(row, def, "PLOT_NO5")?.ToString(),
                    PlotNo6 = GetFieldSafe(row, def, "PLOT_NO6")?.ToString(),
                    RequestType = GetFieldSafe(row, def, "REQUEST_TY")?.ToString() ?? string.Empty,
                    RequestNo = GetFieldSafe(row, def, "REQUEST_NO")?.ToString() ?? string.Empty,
                    ReportNo = GetFieldSafe(row, def, "REPORT_NO")?.ToString() ?? string.Empty,
                    AreaSqm = Convert.ToDouble(GetFieldSafe(row, def, "AREA_SQM") ?? 0.0),
                    ShapeArea = Convert.ToDouble(GetFieldSafe(row, def, "Shape_Area") ?? 0.0),
                    ShapeLength = Convert.ToDouble(GetFieldSafe(row, def, "Shape_Leng") ?? 0.0)
                };

                var parcelTypObj = GetFieldSafe(row, def, "PARCEL_TYP");
                if (parcelTypObj != null && int.TryParse(parcelTypObj.ToString(), out int pType))
                {
                    p.Class = pType == 2 ? ParcelClass.Sub : ParcelClass.Main;
                }

                if (row is Feature feature && feature.GetShape() is Polygon poly)
                {
                    var poly2D = ParcelPolygonizer.Ensure2D(poly);
                    if (targetSr != null && (poly2D.SpatialReference == null || !poly2D.SpatialReference.IsEqual(targetSr)))
                    {
                        try
                        {
                            poly2D = (Polygon)GeometryEngine.Instance.Project(poly2D, targetSr);
                        }
                        catch
                        {
                            var pb = new PolygonBuilderEx(poly2D) { SpatialReference = targetSr };
                            poly2D = (Polygon)pb.ToGeometry();
                        }
                    }
                    geometries[p.InternalId] = poly2D;
                    p.CentroidX = poly2D.Extent.Center.X;
                    p.CentroidY = poly2D.Extent.Center.Y;
                }

                parcels.Add(p);
            }
        }

        return (parcels, geometries, wasCopied, notice);
    }

    public static (List<BorderRecord> Borders, Dictionary<long, Polyline> Geometries, bool WasCopiedDueToLock, string? Notice) ReadBordersFromDataset(
        string pathOrFolder, int targetUtmEpsg, string scratchDir)
    {
        var borders = new List<BorderRecord>();
        var geometries = new Dictionary<long, Polyline>();

        if (string.IsNullOrWhiteSpace(pathOrFolder))
            return (borders, geometries, false, "Path is empty");

        // 1. Check if pathOrFolder points to an active Feature Layer in ArcGIS Pro Map Contents
        try
        {
            var map = MapView.Active?.Map;
            if (map != null)
            {
                var fl = map.GetLayersAsFlattenedList().OfType<FeatureLayer>()
                    .FirstOrDefault(l => l.Name.Equals(pathOrFolder, StringComparison.OrdinalIgnoreCase));
                if (fl != null)
                {
                    var res = ReadBordersFromFeatureLayer(fl, targetUtmEpsg);
                    return (res.Borders, res.Geometries, false, $"Loaded from active Map Layer '{fl.Name}'");
                }
            }
        }
        catch { }

        // 2. Check if pathOrFolder is a direct Feature Class path inside a GDB, e.g. "C:\Path\Data.gdb\FeatureClassName"
        int gdbIdx = pathOrFolder.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
        if (gdbIdx >= 0)
        {
            string gdbPath = pathOrFolder.Substring(0, gdbIdx + 4);
            string remainder = pathOrFolder.Substring(gdbIdx + 4).TrimStart('\\', '/');
            string fcName = !string.IsNullOrWhiteSpace(remainder) ? remainder : "Border";

            if (Directory.Exists(gdbPath))
            {
                var res = ReadBordersFromFeatureClass(gdbPath, fcName, targetUtmEpsg);
                return (res.Borders, res.Geometries, false, null);
            }
        }

        // 3. Check if folder contains Cad2Gis_Workspace.gdb, Export_Deliverables.gdb or candidate shapefiles
        string targetPath = pathOrFolder;
        if (Directory.Exists(pathOrFolder) && !pathOrFolder.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
        {
            string deliverableGdb = Path.Combine(pathOrFolder, "Cad2Gis_Workspace.gdb");
            if (!Directory.Exists(deliverableGdb))
            {
                deliverableGdb = Path.Combine(pathOrFolder, "Export_Deliverables.gdb");
            }

            if (Directory.Exists(deliverableGdb))
            {
                var res = ReadBordersFromFeatureClass(deliverableGdb, "Border", targetUtmEpsg);
                return (res.Borders, res.Geometries, false, null);
            }

            string candidate = Path.Combine(pathOrFolder, "border.shp");
            if (File.Exists(candidate)) targetPath = candidate;
        }

        var (resolvedPath, wasCopied, notice) = ExportStateManager.ResolveReadableCopy(targetPath, scratchDir);

        if (resolvedPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase) || (Directory.Exists(resolvedPath) && resolvedPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase)))
        {
            var res = ReadBordersFromFeatureClass(resolvedPath, "Border", targetUtmEpsg);
            return (res.Borders, res.Geometries, wasCopied, notice);
        }

        if (File.Exists(resolvedPath))
        {
            string folder = Path.GetDirectoryName(resolvedPath)!;
            string fileName = Path.GetFileName(resolvedPath);

            var fileConn = new FileSystemConnectionPath(new Uri(folder), FileSystemDatastoreType.Shapefile);
            using var datastore = new FileSystemDatastore(fileConn);
            using var fc = datastore.OpenDataset<FeatureClass>(fileName);
            var def = fc.GetDefinition();
            var fcSr = def.GetSpatialReference();
            var targetSr = targetUtmEpsg > 0 ? SpatialReferenceBuilder.CreateSpatialReference(targetUtmEpsg) : fcSr;

            long idCounter = 1;
            var dirCountsPerPlot = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);

            using var cursor = fc.Search(null, false);
            while (cursor.MoveNext())
            {
                using var row = cursor.Current;
                string pId = GetFieldSafe(row, def, "PLOT_ID")?.ToString() ?? string.Empty;
                int bName = Convert.ToInt32(GetFieldSafe(row, def, "BORDERNAME") ?? 1);

                if (!dirCountsPerPlot.TryGetValue(pId, out var counts))
                {
                    counts = new Dictionary<int, int>();
                    dirCountsPerPlot[pId] = counts;
                }
                counts.TryGetValue(bName, out int existingCount);
                int ringIndex = existingCount;
                counts[bName] = existingCount + 1;

                var b = new BorderRecord
                {
                    InternalId = idCounter++,
                    PlotId = pId,
                    PlanNo = GetFieldSafe(row, def, "PLAN_NO")?.ToString() ?? string.Empty,
                    BorderName = bName,
                    BorderType = GetFieldSafe(row, def, "BORDERTYPE")?.ToString() ?? "STR",
                    BorderDesc = Convert.ToInt64(GetFieldSafe(row, def, "BORDERDESC") ?? 0L),
                    Dimension = GetFieldSafe(row, def, "DIMENSION")?.ToString() ?? "Single",
                    ShapeLength = Convert.ToDouble(GetFieldSafe(row, def, "SHAPE_Leng") ?? 0.0),
                    Descriptio = (GetFieldSafe(row, def, "DESCRIPTIO") ?? GetFieldSafe(row, def, "DESCRIPTO"))?.ToString() ?? string.Empty,
                    RingIndex = ringIndex
                };

                if (row is Feature feature && feature.GetShape() is Polyline line)
                {
                    var line2D = ParcelPolygonizer.Ensure2D(line);
                    if (targetSr != null && (line2D.SpatialReference == null || !line2D.SpatialReference.IsEqual(targetSr)))
                    {
                        try
                        {
                            line2D = (Polyline)GeometryEngine.Instance.Project(line2D, targetSr);
                        }
                        catch
                        {
                            var pb = new PolylineBuilderEx(line2D) { SpatialReference = targetSr };
                            line2D = (Polyline)pb.ToGeometry();
                        }
                    }
                    geometries[b.InternalId] = line2D;
                }

                borders.Add(b);
            }
        }

        return (borders, geometries, wasCopied, notice);
    }

    public static Layer? AddOrRefreshMapLayer(Map? map, string gdbPath, string fcName, string caption)
    {
        if (map == null) return null;

        var existing = map.GetLayersAsFlattenedList().FirstOrDefault(l => l.Name.Equals(caption, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.ClearDisplayCache();
            return existing;
        }

        try
        {
            var uri = new Uri(Path.Combine(gdbPath, fcName));
            var layer = LayerFactory.Instance.CreateLayer(uri, map, -1, caption);
            layer?.SetName(caption);
            return layer;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AddOrRefreshMapLayer] Error adding '{caption}': {ex.Message}");
            return null;
        }
    }

    public static async Task WriteDeviatingParcelsToFeatureClassAsync(
        string gdbPath, 
        string fcName, 
        IReadOnlyList<ParcelDeviationItem> deviations, 
        IReadOnlyDictionary<long, Polygon> geometries)
    {
        if (deviations == null || deviations.Count == 0) return;
        if (!Directory.Exists(gdbPath)) return;

        try
        {
            bool exists = false;
            await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
            {
                using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                var defs = gdb.GetDefinitions<FeatureClassDefinition>();
                exists = defs.Any(d => d.GetName().Equals(fcName, StringComparison.OrdinalIgnoreCase));
            });

            if (!exists)
            {
                var args = Geoprocessing.MakeValueArray(Path.Combine(gdbPath, "PARCEL"), gdbPath, fcName);
                var res = await Geoprocessing.ExecuteToolAsync("conversion.FeatureClassToFeatureClass", args);
                if (res.IsFailed) return;
            }

            await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
            {
                using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                using var fc = gdb.OpenDataset<FeatureClass>(fcName);
                var def = fc.GetDefinition();

                using var insertCursor = fc.CreateInsertCursor();
                foreach (var dev in deviations)
                {
                    var p = dev.Parcel;
                    using var rowBuffer = fc.CreateRowBuffer();

                    if (geometries.TryGetValue(p.InternalId, out var poly))
                    {
                        rowBuffer[def.GetShapeField()] = poly;
                    }

                    SetFieldSafe(rowBuffer, def, "REALESTA_1", p.RealestateType);
                    SetFieldSafe(rowBuffer, def, "NEIGHBOURH", p.Neighbourhood);
                    SetFieldSafe(rowBuffer, def, "REGION_ID", p.RegionId);
                    SetFieldSafe(rowBuffer, def, "CITY_ID", p.CityId);
                    SetFieldSafe(rowBuffer, def, "ADDRESS", p.Address);
                    SetFieldSafe(rowBuffer, def, "USAGE", p.Usage);
                    SetFieldSafe(rowBuffer, def, "ISBUILDING", p.IsBuilding);
                    SetFieldSafe(rowBuffer, def, "BUILDINGTY", p.BuildingType);
                    SetFieldSafe(rowBuffer, def, "REMARKS", dev.DeviationReason);
                    SetFieldSafe(rowBuffer, def, "LAYOUT", p.Layout);
                    SetFieldSafe(rowBuffer, def, "PLOT_ID", p.PlotId);
                    SetFieldSafe(rowBuffer, def, "BLOCK_NO", p.BlockNo);
                    SetFieldSafe(rowBuffer, def, "PLAN_NO", p.PlanNo);
                    SetFieldSafe(rowBuffer, def, "PLOT_NO1", p.PlotNo1);
                    SetFieldSafe(rowBuffer, def, "REQUEST_TY", p.RequestType);
                    SetFieldSafe(rowBuffer, def, "REQUEST_NO", p.RequestNo);
                    SetFieldSafe(rowBuffer, def, "REPORT_NO", p.ReportNo);
                    SetFieldSafe(rowBuffer, def, "AREA_SQM", p.AreaSqm);

                    insertCursor.Insert(rowBuffer);
                }
                insertCursor.Flush();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write deviating parcels to GDB: {ex.Message}");
        }
    }

    private static void SetFieldSafe(RowBuffer buffer, FeatureClassDefinition def, string fieldName, object? value)
    {
        int idx = def.FindField(fieldName);
        if (idx < 0 || value == null) return;

        try
        {
            var field = def.GetFields()[idx];
            object? converted = ShapefileExporter.ConvertToFieldType(value, field.FieldType, field.Length);
            if (converted != null)
            {
                buffer[idx] = converted;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GDB SetFieldSafe] Field '{fieldName}' ({value}) set notice: {ex.Message}");
        }
    }

    private static void SetFieldSafe(Row row, FeatureClassDefinition def, string fieldName, object? value)
    {
        int idx = def.FindField(fieldName);
        if (idx < 0 || value == null) return;

        try
        {
            var field = def.GetFields()[idx];
            object? converted = ShapefileExporter.ConvertToFieldType(value, field.FieldType, field.Length);
            if (converted != null)
            {
                row[idx] = converted;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GDB SetFieldSafe Row] Field '{fieldName}' ({value}) set notice: {ex.Message}");
        }
    }

    private static object? GetFieldSafe(Row row, FeatureClassDefinition def, string fieldName)
    {
        int idx = def.FindField(fieldName);
        return idx >= 0 ? row[idx] : null;
    }

    public static async Task ExportParcelsToGdbAsync(
        string gdbPath, 
        string fcName, 
        IReadOnlyList<ParcelRecord> parcels, 
        IReadOnlyDictionary<long, Polygon> geoms, 
        string templateFolder,
        int epsgCode = 0)
    {
        string templateShp = Path.Combine(templateFolder, "PARCEL.shp");
        await EnsureFeatureClassFromTemplateAsync(gdbPath, fcName, templateShp, epsgCode);
        await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
        {
            WriteParcelsToFeatureClass(gdbPath, fcName, parcels, geoms);
            AddOrRefreshMapLayer(MapView.Active?.Map, gdbPath, fcName, $"{fcName} GDB");
        });
    }

    public static async Task ExportBordersToGdbAsync(
        string gdbPath, 
        string fcName, 
        IReadOnlyList<BorderRecord> borders, 
        IReadOnlyDictionary<long, Polyline> geoms, 
        string templateFolder,
        int epsgCode = 0)
    {
        string templateShp = Path.Combine(templateFolder, "border.shp");
        await EnsureFeatureClassFromTemplateAsync(gdbPath, fcName, templateShp, epsgCode);
        await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
        {
            WriteBordersToFeatureClass(gdbPath, fcName, borders, geoms);
            AddOrRefreshMapLayer(MapView.Active?.Map, gdbPath, fcName, $"{fcName} GDB");
        });
    }

    /// <summary>
    /// Converts CAD polylines to closed 2D polygons using the native ArcGIS Geoprocessing 'Feature to Polygon'
    /// tool (management.FeatureToPolygon).
    /// Uses the user's active layer selection scope (Main Only, Main &amp; Sub, All) as a feature selection (where_clause),
    /// strictly preserving all native true curves (CircularArc, EllipticArc) and original coordinates completely intact.
    /// </summary>
    public static string BuildLayerWhere(IEnumerable<string> layers) => PolygonLayerScopeService.BuildLayerWhere(layers);

    /// <summary>
    /// Executes native ArcGIS Geoprocessing FeatureToPolygon strictly on selected CAD layers with Z/M disabled.
    /// Follows Section 1-7 specifications: pre-flight checks, strict UPPER(Layer) selection, zero polygon fact reporting,
    /// and explicit class assignment without silent fallback.
    /// </summary>
    public static async Task<FeatureToPolygonReport> RunFeatureToPolygonReportAsync(
        string gdbPath,
        string datasetName,
        IEnumerable<string> eligibleLayerNames,
        double snapToleranceMeters = 0.0,
        int epsgCode = 0,
        string outFcName = "Cad_Constructed_Parcels",
        ParcelClass assignedClass = ParcelClass.Main)
    {
        var report = new FeatureToPolygonReport
        {
            ToleranceMeters = snapToleranceMeters,
            AssignedClass = assignedClass
        };

        if (!Directory.Exists(gdbPath))
        {
            report.Success = false;
            report.DiagnosticMessage = $"Geodatabase not found at: {gdbPath}";
            return report;
        }

        string polylineFcPath = !string.IsNullOrWhiteSpace(datasetName)
            ? Path.Combine(gdbPath, datasetName, "Polyline")
            : Path.Combine(gdbPath, "Polyline");

        string outFcPath = Path.Combine(gdbPath, outFcName);
        report.OutputPath = outFcPath;

        var layerList = eligibleLayerNames?.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToList() ?? new List<string>();

        // Pre-flight check: At least one layer selected in Step 1
        if (layerList.Count == 0)
        {
            report.Success = false;
            report.DiagnosticMessage = "No CAD layers selected in Step 1. Please select at least one layer to polygonize.";
            return report;
        }

        // Section 2: Pre-flight checks on MCT
        string actualPolylinePath = polylineFcPath;
        long totalLinesInFc = 0;
        int detectedWkid = epsgCode;
        string linearUnitName = "Meter";
        bool inputHasZ = false;
        var distinctLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var countsByLayer = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in layerList)
            countsByLayer[l] = 0;

        string preflightError = string.Empty;

        await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
        {
            try
            {
                using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                using var fc = OpenDatasetFeatureClass(gdb, "Polyline");
                if (fc == null)
                {
                    preflightError = $"Dataset '{datasetName}' does not exist or does not contain 'Polyline' feature class in '{gdbPath}'.";
                    return;
                }

                var def = fc.GetDefinition();
                actualPolylinePath = Path.Combine(gdbPath, def.GetName());
                totalLinesInFc = fc.GetCount();

                var geomType = def.GetShapeType();
                if (geomType != GeometryType.Polyline)
                {
                    preflightError = $"Feature class '{actualPolylinePath}' must have geometry type Polyline (found: {geomType}).";
                    return;
                }

                int lIdx = def.FindField("Layer");
                if (lIdx < 0)
                {
                    preflightError = $"Feature class '{actualPolylinePath}' must contain a 'Layer' field.";
                    return;
                }

                var sr = def.GetSpatialReference();
                if (sr == null || sr.IsUnknown)
                {
                    preflightError = $"Feature class '{actualPolylinePath}' has an unknown spatial reference.";
                    return;
                }
                detectedWkid = sr.Wkid > 0 ? sr.Wkid : epsgCode;
                linearUnitName = sr.Unit?.Name ?? (sr.IsProjected ? "Meter" : "");
                if (string.IsNullOrWhiteSpace(linearUnitName) || (!sr.IsProjected && sr.IsGeographic))
                {
                    preflightError = $"Feature class '{actualPolylinePath}' must have a projected spatial reference with a linear unit (found WKID: {detectedWkid}, Unit: '{(string.IsNullOrEmpty(linearUnitName) ? "None" : linearUnitName)}').";
                    return;
                }

                using var cur = fc.Search(null, false);
                while (cur.MoveNext())
                {
                    using var row = cur.Current;
                    var lVal = row[lIdx]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(lVal))
                    {
                        distinctLayers.Add(lVal);
                        if (countsByLayer.ContainsKey(lVal))
                        {
                            countsByLayer[lVal]++;
                        }
                    }
                    if (!inputHasZ && row is Feature f && f.GetShape() is Polyline pl && pl.HasZ)
                    {
                        inputHasZ = true;
                    }
                }
            }
            catch (Exception ex)
            {
                preflightError = $"Pre-flight inspection failed on '{polylineFcPath}': {ex.Message}";
            }
        });

        if (!string.IsNullOrEmpty(preflightError))
        {
            report.Success = false;
            report.DiagnosticMessage = preflightError;
            return report;
        }

        report.InputPath = actualPolylinePath;
        report.TotalInputLines = totalLinesInFc;

        System.Diagnostics.Debug.WriteLine($"[FeatureToPolygon Pre-flight] Polyline FC: '{actualPolylinePath}', WKID: {detectedWkid}, Unit: '{linearUnitName}', Input HasZ: {inputHasZ}.");
        System.Diagnostics.Debug.WriteLine($"[FeatureToPolygon Pre-flight] Selected layers: {string.Join(", ", layerList)}");

        foreach (var kvp in countsByLayer)
        {
            System.Diagnostics.Debug.WriteLine($"[FeatureToPolygon Pre-flight] Layer '{kvp.Key}': {kvp.Value} polylines.");
            if (kvp.Value == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[FeatureToPolygon Pre-flight Warning] Selected layer '{kvp.Key}' has 0 polylines in the feature class.");
            }
        }

        long totalSelectedPolylines = countsByLayer.Values.Sum();
        report.EligibleInputLines = totalSelectedPolylines;

        if (totalSelectedPolylines == 0)
        {
            report.Success = false;
            report.DiagnosticMessage = $"Selected layers ({string.Join(", ", layerList)}) contain 0 total polylines in '{actualPolylinePath}'. Distinct layers present in feature class: {string.Join(", ", distinctLayers.OrderBy(x => x))}.";
            return report;
        }

        // Section 3: Building the selection (strict UPPER(Layer) IN (...))
        string whereClause = BuildLayerWhere(layerList);
        string tempLyr = "Cad_Selected_Polylines_" + Guid.NewGuid().ToString("N")[..8];

        System.Diagnostics.Debug.WriteLine($"[FeatureToPolygon START] GeometryMode=Lines, SourcePath='{actualPolylinePath}', SelectedLayers=[{string.Join(", ", layerList)}], WHERE=\"{whereClause}\", OutFC='{outFcName}', Class={assignedClass}");

        string clusterTolStr = snapToleranceMeters > 0.0001
            ? $"{snapToleranceMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)} {linearUnitName}"
            : "";

        try
        {
            var makeLyrArgs = Geoprocessing.MakeValueArray(actualPolylinePath, tempLyr, whereClause);
            var lyrResult = await Geoprocessing.ExecuteToolAsync("management.MakeFeatureLayer", makeLyrArgs);
            if (lyrResult.IsFailed)
            {
                report.Success = false;
                string msgs = lyrResult.Messages != null
                    ? string.Join(Environment.NewLine, lyrResult.Messages.Select(m => m.Text))
                    : "MakeFeatureLayer failed";
                report.GpMessages = msgs;
                report.DiagnosticMessage = $"MakeFeatureLayer failed with expression: {whereClause}\nGeoprocessing messages:\n{msgs}";
                return report;
            }

            // Compare selected count with pre-flight count
            long selectedCount = -1;
            try
            {
                var countArgs = Geoprocessing.MakeValueArray(tempLyr);
                var countResult = await Geoprocessing.ExecuteToolAsync("management.GetCount", countArgs);
                if (!countResult.IsFailed && countResult.ReturnValue != null && long.TryParse(countResult.ReturnValue.ToString(), out long cnt))
                {
                    selectedCount = cnt;
                }
            }
            catch { }
            System.Diagnostics.Debug.WriteLine($"[FeatureToPolygon Selection] MakeFeatureLayer selected {selectedCount} features (Expected: {totalSelectedPolylines}).");

            // Section 4: Running Feature To Polygon (Z disabled)
            try
            {
                var delArgs = Geoprocessing.MakeValueArray(outFcPath);
                await Geoprocessing.ExecuteToolAsync("management.Delete", delArgs);
            }
            catch { }

            var env = Geoprocessing.MakeEnvironmentArray(
                workspace: gdbPath,
                overwriteoutput: true,
                outputZFlag: "Disabled",
                outputMFlag: "Disabled"
            );

            var f2pArgs = Geoprocessing.MakeValueArray(
                tempLyr,
                outFcPath,
                clusterTolStr,
                "NO_ATTRIBUTES"
            );

            var f2pResult = await Geoprocessing.ExecuteToolAsync("management.FeatureToPolygon", f2pArgs, env);

            string gpMsgOutput = f2pResult?.Messages != null
                ? string.Join(Environment.NewLine, f2pResult.Messages.Select(m => m.Text))
                : string.Empty;
            report.GpMessages = gpMsgOutput;

            if (f2pResult == null || f2pResult.IsFailed)
            {
                report.Success = false;
                report.DiagnosticMessage = $"FeatureToPolygon failed.\nGeoprocessing messages:\n{gpMsgOutput}";
                return report;
            }
        }
        finally
        {
            try
            {
                var delLyrArgs = Geoprocessing.MakeValueArray(tempLyr);
                await Geoprocessing.ExecuteToolAsync("management.Delete", delLyrArgs);
            }
            catch { }
        }

        // Section 5: Verifying the result (report facts, not guesses)
        bool outputHasZ = false;
        long validPolygonCount = 0;
        var polygons = new List<Polygon>();

        await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
        {
            try
            {
                using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                using var outFc = gdb.OpenDataset<FeatureClass>(outFcName);
                if (outFc != null)
                {
                    var def = outFc.GetDefinition();
                    var fcSr = def.GetSpatialReference();
                    var targetSr = detectedWkid > 0 ? SpatialReferenceBuilder.CreateSpatialReference(detectedWkid) : fcSr;

                    using var cur = outFc.Search(null, false);
                    while (cur.MoveNext())
                    {
                        using var feat = (Feature)cur.Current;
                        if (feat.GetShape() is Polygon poly && !poly.IsEmpty && poly.Area > 0.000001)
                        {
                            if (poly.HasZ) outputHasZ = true;
                            var poly2D = ParcelPolygonizer.Ensure2D(poly);
                            if (targetSr != null && (poly2D.SpatialReference == null || !poly2D.SpatialReference.IsEqual(targetSr)))
                            {
                                var pb = new PolygonBuilderEx(poly2D) { SpatialReference = targetSr };
                                pb.HasZ = false;
                                pb.HasM = false;
                                poly2D = (Polygon)pb.ToGeometry();
                            }
                            polygons.Add(poly2D);
                            validPolygonCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FeatureToPolygon Inspection Error] {ex.Message}");
            }
        });

        report.OutputPolygonCount = validPolygonCount;

        string tolFact = string.IsNullOrEmpty(clusterTolStr) ? "none" : clusterTolStr;
        System.Diagnostics.Debug.WriteLine($"[FeatureToPolygon Verification] Selected lines: {totalSelectedPolylines}, Polygons: {validPolygonCount}, Tolerance: '{tolFact}', Linear unit: '{linearUnitName}', Output HasZ: {outputHasZ}");

        if (validPolygonCount == 0)
        {
            report.Success = false;
            string layerSummary = string.Join(", ", countsByLayer.Select(kv => $"{kv.Key} ({kv.Value} lines)"));
            report.DiagnosticMessage = $"Selected layers: {layerSummary}. Feature To Polygon produced 0 polygons. Tolerance used: {tolFact}. Spatial reference: WKID {detectedWkid}, unit {linearUnitName}. Geoprocessing messages:\n{report.GpMessages}";
            return report;
        }

        report.Success = true;
        foreach (var p in polygons)
        {
            report.ConstructedParcels.Add((p, outFcName));
            report.ConstructedClassParcels.Add((p, assignedClass));
        }
        report.DiagnosticMessage = $"Feature To Polygon succeeded: produced {validPolygonCount} polygons from {totalSelectedPolylines} lines (Tolerance: '{tolFact}', WKID: {detectedWkid}).";

        // Clean up intermediate feature class if not the final Cad_Constructed_Parcels
        if (!outFcName.Equals("Cad_Constructed_Parcels", StringComparison.OrdinalIgnoreCase) &&
            !outFcName.Equals("DEMO_Cad_Constructed_Parcels", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var delArgs = Geoprocessing.MakeValueArray(outFcPath);
                await Geoprocessing.ExecuteToolAsync("management.Delete", delArgs);
            }
            catch { }
        }

        System.Diagnostics.Debug.WriteLine($"[FeatureToPolygon END] GeometryMode=Lines, OutputPath='{outFcPath}', PolygonCount={validPolygonCount}, Tolerance='{tolFact}', SpatialRef='WKID {detectedWkid}, {linearUnitName}'");

        return report;
    }

    public static async Task<List<(Polygon Polygon, string LayerName)>> RunFeatureToPolygonAsync(
        string gdbPath,
        string datasetName,
        IEnumerable<string> eligibleLayerNames,
        double snapToleranceMeters = 0.0,
        int epsgCode = 0)
    {
        var rep = await RunFeatureToPolygonReportAsync(gdbPath, datasetName, eligibleLayerNames, snapToleranceMeters, epsgCode);
        return rep.ConstructedParcels;
    }

    public static async Task<FeatureToPolygonReport> RunArcPyLineToPolygonToolAsync(
        string gdbPath,
        string datasetName,
        IEnumerable<string> eligibleLayerNames,
        double snapToleranceMeters = 0.05,
        int epsgCode = 32637,
        string? helperFilesDir = null)
    {
        var report = new FeatureToPolygonReport
        {
            InputPath = $"{gdbPath}\\{datasetName}\\Polyline",
            ToleranceMeters = snapToleranceMeters
        };

        // 1. Try running ArcPy script via Python executable
        string? pythonExe = Cad2Gis.Core.Services.CadExtractionService.ResolvePythonExe();
        string? scriptPath = null;

        var scriptCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(helperFilesDir))
        {
            scriptCandidates.Add(Path.Combine(helperFilesDir, "scripts", "arcpy_line_to_polygon.py"));
        }
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        scriptCandidates.Add(Path.Combine(appDir, "HelperFiles", "scripts", "arcpy_line_to_polygon.py"));
        scriptCandidates.Add(Path.Combine(appDir, "scripts", "arcpy_line_to_polygon.py"));
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        scriptCandidates.Add(Path.Combine(localAppData, "CAD2GIS", "HelperFiles", "scripts", "arcpy_line_to_polygon.py"));

        foreach (var sc in scriptCandidates)
        {
            if (File.Exists(sc))
            {
                scriptPath = sc;
                break;
            }
        }

        string outFcName = "PARCEL_Lines_Polygons";
        string outFcPath = $"{gdbPath}\\{outFcName}";
        string layersParam = string.Join(",", eligibleLayerNames);

        bool arcpyExecuted = false;

        if (!string.IsNullOrEmpty(pythonExe) && !string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath))
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" --gdb \"{gdbPath}\" --dataset \"{datasetName}\" --out_fc \"{outFcName}\" --tolerance {snapToleranceMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)} --utm {epsgCode} --layers \"{layersParam}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        int jsonStart = output.IndexOf('{');
                        int jsonEnd = output.LastIndexOf('}');
                        if (jsonStart >= 0 && jsonEnd > jsonStart)
                        {
                            string json = output.Substring(jsonStart, jsonEnd - jsonStart + 1);
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            report.Success = root.TryGetProperty("success", out var s) && s.GetBoolean();
                            report.TotalInputLines = root.TryGetProperty("inputLineCount", out var inc) ? inc.GetInt64() : 0;
                            report.EligibleInputLines = report.TotalInputLines;
                            report.OutputPolygonCount = root.TryGetProperty("outputPolygonCount", out var opc) ? opc.GetInt64() : 0;
                            report.OutputPath = root.TryGetProperty("outputFcPath", out var ofc) ? ofc.GetString() ?? outFcPath : outFcPath;
                            report.GpMessages = root.TryGetProperty("gpMessages", out var gpm) ? gpm.GetString() ?? "" : "";
                            report.DiagnosticMessage = root.TryGetProperty("diagnostic", out var diag) ? diag.GetString() ?? "" : "";

                            arcpyExecuted = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArcPy Tool] Error executing python script: {ex.Message}");
            }
        }

        // If Python tool ran and succeeded, read the polygons into ConstructedParcels
        if (arcpyExecuted && report.OutputPolygonCount > 0)
        {
            await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
            {
                try
                {
                    using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                    using var fc = gdb.OpenDataset<FeatureClass>(outFcName);
                    if (fc != null)
                    {
                        var def = fc.GetDefinition();
                        var fcSr = def.GetSpatialReference();
                        var targetSr = epsgCode > 0 ? SpatialReferenceBuilder.CreateSpatialReference(epsgCode) : fcSr;
                        int layerIdx = def.FindField("Layer");

                        using var cursor = fc.Search(null, false);
                        while (cursor.MoveNext())
                        {
                            using var feat = (Feature)cursor.Current;
                            if (feat.GetShape() is Polygon poly && !poly.IsEmpty)
                            {
                                var poly2D = ParcelPolygonizer.Ensure2D(poly);
                                if (targetSr != null && (poly2D.SpatialReference == null || !poly2D.SpatialReference.IsEqual(targetSr)))
                                {
                                    var pb = new PolygonBuilderEx(poly2D) { SpatialReference = targetSr };
                                    pb.HasZ = false;
                                    pb.HasM = false;
                                    poly2D = (Polygon)pb.ToGeometry();
                                }

                                string lyrName = (layerIdx >= 0 && feat[layerIdx] != null)
                                    ? feat[layerIdx].ToString() ?? "Main Parcels"
                                    : "Main Parcels";

                                report.ConstructedParcels.Add((poly2D, lyrName));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ArcPy Tool] Error reading output feature class: {ex.Message}");
                }
            });

            return report;
        }

        // Fall back to ArcGIS Pro native GP FeatureToPolygon report
        return await RunFeatureToPolygonReportAsync(gdbPath, datasetName, eligibleLayerNames, snapToleranceMeters, epsgCode);
    }

    /// <summary>
    /// Verifies that a polygon feature class exists, is readable, has Polygon geometry,
    /// <summary>
    /// Verifies that a polygon feature class exists, is readable, has Polygon geometry,
    /// has feature count equal to expectedCount (or >0), and has matching spatial reference WKID.
    /// </summary>
    public static (bool ok, string reason) VerifyPolygonFeatureClass(string fcPath, int expectedCount, int expectedWkid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fcPath))
                return (false, "path is null or empty");

            if (File.Exists(fcPath) && !fcPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                return (true, "");

            if (ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.OnWorker)
            {
                return VerifyPolygonFeatureClassInternal(fcPath, expectedCount, expectedWkid);
            }
            return Task.Run(async () => await VerifyPolygonFeatureClassAsync(fcPath, expectedCount, expectedWkid)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<(bool ok, string reason)> VerifyPolygonFeatureClassAsync(string fcPath, int expectedCount, int expectedWkid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fcPath))
                return (false, "path is null or empty");

            if (File.Exists(fcPath) && !fcPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                return (true, "");

            if (ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.OnWorker)
            {
                return VerifyPolygonFeatureClassInternal(fcPath, expectedCount, expectedWkid);
            }
            return await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
                VerifyPolygonFeatureClassInternal(fcPath, expectedCount, expectedWkid)
            );
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool ok, string reason) VerifyPolygonFeatureClassInternal(string fcPath, int expectedCount, int expectedWkid)
    {
        try
        {
            string? dir = Path.GetDirectoryName(fcPath);
            string fcName = Path.GetFileName(fcPath);

            string gdbDir = dir ?? "";
            if (!gdbDir.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase) && gdbDir.Contains(".gdb"))
            {
                int gdbIdx = gdbDir.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
                gdbDir = gdbDir.Substring(0, gdbIdx + 4);
            }

            if (!Directory.Exists(gdbDir))
                return (false, $"geodatabase directory does not exist: {gdbDir}");

            using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbDir)));
            using var fc = OpenDatasetFeatureClass(gdb, fcName);
            if (fc == null)
                return (false, $"dataset '{fcName}' not found in gdb '{gdbDir}'");

            var def = fc.GetDefinition();
            if (def.GetShapeType() != GeometryType.Polygon)
                return (false, $"geometry type is not Polygon ({def.GetShapeType()})");

            long n = fc.GetCount();
            if (n == 0 || (expectedCount > 0 && n != expectedCount))
                return (false, $"feature count {n} != expected {expectedCount}");

            if (expectedWkid > 0)
            {
                var sr = def.GetSpatialReference();
                if (sr != null && sr.Wkid != expectedWkid)
                    return (false, $"spatial reference mismatch ({sr.Wkid} != expected {expectedWkid})");
            }

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Verifies that a polyline feature class exists, is readable, has Polyline geometry,
    /// has feature count equal to expectedCount (or >0), and has matching spatial reference WKID.
    /// </summary>
    public static (bool ok, string reason) VerifyPolylineFeatureClass(string fcPath, int expectedCount, int expectedWkid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fcPath))
                return (false, "path is null or empty");

            if (File.Exists(fcPath) && !fcPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                return (true, "");

            if (ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.OnWorker)
            {
                return VerifyPolylineFeatureClassInternal(fcPath, expectedCount, expectedWkid);
            }
            return Task.Run(async () => await VerifyPolylineFeatureClassAsync(fcPath, expectedCount, expectedWkid)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<(bool ok, string reason)> VerifyPolylineFeatureClassAsync(string fcPath, int expectedCount, int expectedWkid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fcPath))
                return (false, "path is null or empty");

            if (File.Exists(fcPath) && !fcPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                return (true, "");

            if (ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.OnWorker)
            {
                return VerifyPolylineFeatureClassInternal(fcPath, expectedCount, expectedWkid);
            }
            return await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
                VerifyPolylineFeatureClassInternal(fcPath, expectedCount, expectedWkid)
            );
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool ok, string reason) VerifyPolylineFeatureClassInternal(string fcPath, int expectedCount, int expectedWkid)
    {
        try
        {
            string? dir = Path.GetDirectoryName(fcPath);
            string fcName = Path.GetFileName(fcPath);

            string gdbDir = dir ?? "";
            if (!gdbDir.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase) && gdbDir.Contains(".gdb"))
            {
                int gdbIdx = gdbDir.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
                gdbDir = gdbDir.Substring(0, gdbIdx + 4);
            }

            if (!Directory.Exists(gdbDir))
                return (false, $"geodatabase directory does not exist: {gdbDir}");

            using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbDir)));
            using var fc = OpenDatasetFeatureClass(gdb, fcName);
            if (fc == null)
                return (false, $"dataset '{fcName}' not found in gdb '{gdbDir}'");

            var def = fc.GetDefinition();
            if (def.GetShapeType() != GeometryType.Polyline)
                return (false, $"geometry type is not Polyline ({def.GetShapeType()})");

            long n = fc.GetCount();
            if (n == 0 || (expectedCount > 0 && n != expectedCount))
                return (false, $"feature count {n} != expected {expectedCount}");

            if (expectedWkid > 0)
            {
                var sr = def.GetSpatialReference();
                if (sr != null && sr.Wkid != expectedWkid)
                    return (false, $"spatial reference mismatch ({sr.Wkid} != expected {expectedWkid})");
            }

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// P6 Verification: Verifies all deliverables in finalDir before updating latest export state:
    /// Final.gdb PARCEL and BORDER readable with counts equal to in-memory counts;
    /// Shape\PARCEL.shp and BORDER.shp with .shp/.shx/.dbf/.prj/.cpg;
    /// and required quality reports exist.
    /// </summary>
    public static (bool Ok, string Report) VerifyFinalExport(string finalDir, int expectedParcelCount, int expectedBorderCount, int expectedWkid)
    {
        if (string.IsNullOrWhiteSpace(finalDir) || !Directory.Exists(finalDir))
            return (false, $"Final export directory does not exist: {finalDir}");

        var issues = new List<string>();

        // 1. GDB Deliverable
        string finalGdb = Path.Combine(finalDir, "Final.gdb");
        if (!Directory.Exists(finalGdb))
        {
            issues.Add($"Final.gdb does not exist at '{finalGdb}'.");
        }
        else
        {
            var (pOk, pReason) = VerifyPolygonFeatureClass(Path.Combine(finalGdb, "PARCEL"), expectedParcelCount, expectedWkid);
            if (!pOk) issues.Add($"Final.gdb PARCEL verification failed: {pReason}");

            if (expectedBorderCount > 0)
            {
                var (bOk, bReason) = VerifyPolylineFeatureClass(Path.Combine(finalGdb, "BORDER"), expectedBorderCount, expectedWkid);
                if (!bOk) issues.Add($"Final.gdb BORDER verification failed: {bReason}");
            }
        }

        // 2. Shapefile Deliverables
        string shapeDir = Path.Combine(finalDir, "Shape");
        string[] requiredExtensions = new[] { ".shp", ".shx", ".dbf", ".prj", ".cpg" };

        string parcelShpBase = Path.Combine(shapeDir, "PARCEL");
        foreach (var ext in requiredExtensions)
        {
            string f = parcelShpBase + ext;
            if (!File.Exists(f)) issues.Add($"Missing required shapefile component: {Path.GetFileName(f)}");
        }

        if (expectedBorderCount > 0)
        {
            string borderShpBase = Path.Combine(shapeDir, "BORDER");
            foreach (var ext in requiredExtensions)
            {
                string f = borderShpBase + ext;
                if (!File.Exists(f)) issues.Add($"Missing required border shapefile component: {Path.GetFileName(f)}");
            }
        }

        // 3. Reports Deliverable
        string reportsDir = Path.Combine(finalDir, "Reports");
        if (!Directory.Exists(reportsDir))
        {
            issues.Add($"Reports folder does not exist at '{reportsDir}'.");
        }
        else
        {
            bool hasAnyReport = Directory.EnumerateFiles(reportsDir).Any();
            if (!hasAnyReport) issues.Add("Reports folder is empty.");
        }

        if (issues.Count > 0)
        {
            return (false, string.Join(Environment.NewLine, issues));
        }

        string reportSummary = $"✔ Final.gdb: PARCEL ({expectedParcelCount}), BORDER ({expectedBorderCount})\n" +
                               $"✔ Shape: PARCEL.shp, BORDER.shp (with all .shp/.shx/.dbf/.prj/.cpg sidecars)\n" +
                               $"✔ Reports: verified in '{reportsDir}'";
        return (true, reportSummary);
    }
}

public class DatasetCadFeatures
{
    public List<(Polygon Polygon, string LayerName, string RawText)> Polygons { get; } = new();
    public List<LineGeometry2D> LinesToPolygonize { get; } = new();
    public List<CurvePolylineEntity> CurveEntitiesToPolygonize { get; } = new();
    public List<(string Text, MapPoint Location, string LayerName)> Annotations { get; } = new();
}

public class FeatureToPolygonReport
{
    public bool Success { get; set; }
    public string InputPath { get; set; } = string.Empty;
    public long TotalInputLines { get; set; }
    public long EligibleInputLines { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public long OutputPolygonCount { get; set; }
    public double ToleranceMeters { get; set; }
    public string GpMessages { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;
    public ParcelClass AssignedClass { get; set; } = ParcelClass.Main;
    public List<(Polygon Polygon, string LayerName)> ConstructedParcels { get; } = new();
    public List<(Polygon Polygon, ParcelClass Class)> ConstructedClassParcels { get; } = new();
}



