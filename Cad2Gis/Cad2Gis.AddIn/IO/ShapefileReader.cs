using System;
using System.Collections.Generic;
using System.IO;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using Cad2Gis.AddIn.Geometry;
using Cad2Gis.Core.Models;

namespace Cad2Gis.AddIn.IO;

public static class ShapefileReader
{
    public static (List<ParcelRecord> Parcels, Dictionary<long, Polygon> Geometries) LoadParcels(string dataPath)
    {
        var parcels = new List<ParcelRecord>();
        var geometries = new Dictionary<long, Polygon>();

        if (string.IsNullOrWhiteSpace(dataPath))
            return (parcels, geometries);

        try
        {
            if (dataPath.Contains(".gdb", StringComparison.OrdinalIgnoreCase))
            {
                // Geodatabase Feature Class
                int gdbIdx = dataPath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
                string gdbPath = dataPath.Substring(0, gdbIdx + 4);
                string fcName = dataPath.Substring(gdbIdx + 4).TrimStart('\\', '/');

                if (Directory.Exists(gdbPath))
                {
                    using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                    using var fc = string.IsNullOrWhiteSpace(fcName)
                        ? gdb.GetDefinitions<FeatureClassDefinition>().FirstOrDefault(d => d.GetShapeType() == GeometryType.Polygon) != null
                            ? gdb.OpenDataset<FeatureClass>(gdb.GetDefinitions<FeatureClassDefinition>().First(d => d.GetShapeType() == GeometryType.Polygon).GetName())
                            : null
                        : gdb.OpenDataset<FeatureClass>(fcName);

                    if (fc != null)
                    {
                        ReadFeaturesFromFeatureClass(fc, parcels, geometries);
                    }
                }
            }
            else if (File.Exists(dataPath))
            {
                // Shapefile
                string folder = Path.GetDirectoryName(dataPath)!;
                string file = Path.GetFileName(dataPath);

                var fileConn = new FileSystemConnectionPath(new Uri(folder), FileSystemDatastoreType.Shapefile);
                using var datastore = new FileSystemDatastore(fileConn);
                using var fc = datastore.OpenDataset<FeatureClass>(file);

                if (fc != null)
                {
                    ReadFeaturesFromFeatureClass(fc, parcels, geometries);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading parcels from {dataPath}: {ex.Message}");
        }

        return (parcels, geometries);
    }

    private static void ReadFeaturesFromFeatureClass(
        FeatureClass fc, 
        List<ParcelRecord> parcels, 
        Dictionary<long, Polygon> geometries)
    {
        var def = fc.GetDefinition();
        long idCounter = 1;

        using var cursor = fc.Search(null, false);
        while (cursor.MoveNext())
        {
            using var row = cursor.Current;

            string plotId = FindFieldAny(row, def, "PLOT_ID", "plot_id", "PlotId", "PLOTID", "PARCEL_ID", "Plot_No", "PLOT_NO", "رقم_القطعة", "ID")?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(plotId))
            {
                plotId = $"P{idCounter}";
            }

            var p = new ParcelRecord
            {
                InternalId = idCounter++,
                PlotId = plotId,
                RealestateType = Convert.ToInt32(FindFieldAny(row, def, "REALESTA_1", "RealestateType") ?? 2),
                Neighbourhood = FindFieldAny(row, def, "NEIGHBOURH", "Neighbourhood", "الحي")?.ToString() ?? string.Empty,
                RegionId = FindFieldAny(row, def, "REGION_ID", "RegionId", "المنطقة")?.ToString() ?? string.Empty,
                CityId = FindFieldAny(row, def, "CITY_ID", "CityId", "المدينة")?.ToString() ?? string.Empty,
                Address = FindFieldAny(row, def, "ADDRESS", "Address", "العنوان")?.ToString() ?? string.Empty,
                Usage = FindFieldAny(row, def, "USAGE", "Usage", "الاستخدام")?.ToString() ?? string.Empty,
                IsBuilding = Convert.ToInt32(FindFieldAny(row, def, "ISBUILDING", "IsBuilding") ?? 0),
                BuildingType = Convert.ToInt32(FindFieldAny(row, def, "BUILDINGTY", "BuildingType") ?? 1),
                Remarks = FindFieldAny(row, def, "REMARKS", "Remarks", "ملاحظات")?.ToString() ?? string.Empty,
                Layout = FindFieldAny(row, def, "LAYOUT", "Layout")?.ToString(),
                BlockNo = FindFieldAny(row, def, "BLOCK_NO", "BlockNo", "البلوك")?.ToString(),
                PlanNo = FindFieldAny(row, def, "PLAN_NO", "PlanNo", "المخطط")?.ToString() ?? string.Empty,
                PlotNo1 = FindFieldAny(row, def, "PLOT_NO1", "PlotNo1")?.ToString() ?? plotId,
                PlotNo2 = FindFieldAny(row, def, "PLOT_NO2")?.ToString(),
                PlotNo3 = FindFieldAny(row, def, "PLOT_NO3")?.ToString(),
                PlotNo4 = FindFieldAny(row, def, "PLOT_NO4")?.ToString(),
                PlotNo5 = FindFieldAny(row, def, "PLOT_NO5")?.ToString(),
                PlotNo6 = FindFieldAny(row, def, "PLOT_NO6")?.ToString(),
                RequestType = FindFieldAny(row, def, "REQUEST_TY", "RequestType")?.ToString() ?? string.Empty,
                RequestNo = FindFieldAny(row, def, "REQUEST_NO", "RequestNo")?.ToString() ?? string.Empty,
                ReportNo = FindFieldAny(row, def, "REPORT_NO", "ReportNo")?.ToString() ?? string.Empty,
                AreaSqm = Convert.ToDouble(FindFieldAny(row, def, "AREA_SQM", "AreaSqm", "المساحة") ?? 0.0),
                ShapeArea = Convert.ToDouble(FindFieldAny(row, def, "Shape_Area", "Area") ?? 0.0),
                ShapeLength = Convert.ToDouble(FindFieldAny(row, def, "Shape_Leng", "Length") ?? 0.0)
            };

            if (row is Feature feature && feature.GetShape() is Polygon poly && !poly.IsEmpty)
            {
                // Strip Z and M values ensuring pure 2D planar polygon without any densification
                var poly2D = ParcelPolygonizer.Clean2DPlanar(poly);
                geometries[p.InternalId] = poly2D;

                var metrics = ParcelPolygonizer.ComputeMetrics(poly2D);
                p.CentroidX = metrics.centroid.X;
                p.CentroidY = metrics.centroid.Y;

                if (p.AreaSqm <= 0.001)
                    p.AreaSqm = metrics.areaSqm;
                if (p.ShapeLength <= 0.001)
                    p.ShapeLength = metrics.lengthM;
            }

            parcels.Add(p);
        }
    }

    private static object? FindFieldAny(Row row, FeatureClassDefinition def, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            int idx = def.FindField(c);
            if (idx >= 0 && row[idx] != null && !Convert.IsDBNull(row[idx]))
            {
                return row[idx];
            }
        }
        return null;
    }
}
