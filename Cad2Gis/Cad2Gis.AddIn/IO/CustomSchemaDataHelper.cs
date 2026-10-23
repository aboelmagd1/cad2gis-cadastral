using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Mapping;
using Cad2Gis.Core.Services;

namespace Cad2Gis.AddIn.IO;

public class FieldMappingItem : PropertyChangedBase
{
    public string TargetField { get; set; } = string.Empty;
    public string TargetDescription { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }

    private string _selectedSourceField = "-- غير محدد --";
    public string SelectedSourceField
    {
        get => _selectedSourceField;
        set
        {
            if (SetProperty(ref _selectedSourceField, value))
            {
                NotifyPropertyChanged(nameof(IsMapped));
                NotifyPropertyChanged(nameof(StatusText));
                NotifyPropertyChanged(nameof(StatusColor));
            }
        }
    }

    public ObservableCollection<string> AvailableSourceFields { get; } = new();

    public bool IsMapped => !string.IsNullOrEmpty(SelectedSourceField) && 
                            !SelectedSourceField.StartsWith("--", StringComparison.Ordinal);

    public string StatusText => IsMapped ? "✔ مربوط" : (IsMandatory ? "إلزامي (غير مربوط)" : "اختياري");
    public string StatusColor => IsMapped ? "#10B981" : (IsMandatory ? "#EF4444" : "#94A3B8");
}

public static class CustomSchemaDataHelper
{
    public static List<string> ExtractFields(string filePath)
    {
        var fields = new List<string>();
        if (string.IsNullOrWhiteSpace(filePath)) return fields;

        if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || 
            filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            return CustomSchemaDataService.ReadExcelHeaders(filePath);
        }

        // Shapefile
        if (filePath.EndsWith(".shp", StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
        {
            try
            {
                string folder = Path.GetDirectoryName(filePath)!;
                string file = Path.GetFileName(filePath);
                var fileConn = new FileSystemConnectionPath(new Uri(folder), FileSystemDatastoreType.Shapefile);
                using var datastore = new FileSystemDatastore(fileConn);
                using var fc = datastore.OpenDataset<FeatureClass>(file);
                if (fc != null)
                {
                    var def = fc.GetDefinition();
                    foreach (var f in def.GetFields())
                    {
                        string fName = f.Name;
                        if (!string.Equals(fName, "FID", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(fName, "Shape", StringComparison.OrdinalIgnoreCase))
                        {
                            fields.Add(fName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExtractFields Shapefile] Error: {ex.Message}");
            }
            return fields;
        }

        // Geodatabase Feature Class / Table
        if (filePath.Contains(".gdb", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                int gdbIdx = filePath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
                string gdbPath = filePath.Substring(0, gdbIdx + 4);
                string fcName = filePath.Substring(gdbIdx + 4).Trim('\\', '/');

                if (Directory.Exists(gdbPath))
                {
                    using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                    using var fc = string.IsNullOrWhiteSpace(fcName)
                        ? gdb.GetDefinitions<FeatureClassDefinition>().FirstOrDefault() != null
                            ? gdb.OpenDataset<FeatureClass>(gdb.GetDefinitions<FeatureClassDefinition>().First().GetName())
                            : null
                        : gdb.OpenDataset<FeatureClass>(fcName);

                    if (fc != null)
                    {
                        var def = fc.GetDefinition();
                        foreach (var f in def.GetFields())
                        {
                            string fName = f.Name;
                            if (!string.Equals(fName, "OBJECTID", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(fName, "Shape", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(fName, "Shape_Length", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(fName, "Shape_Area", StringComparison.OrdinalIgnoreCase))
                            {
                                fields.Add(fName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExtractFields GDB] Error: {ex.Message}");
            }
            return fields;
        }

        // Active Map FeatureLayer
        try
        {
            var map = MapView.Active?.Map;
            if (map != null)
            {
                var fl = map.GetLayersAsFlattenedList().OfType<FeatureLayer>()
                    .FirstOrDefault(l => string.Equals(l.Name, filePath, StringComparison.OrdinalIgnoreCase) ||
                                         filePath.StartsWith(l.Name, StringComparison.OrdinalIgnoreCase));
                if (fl != null)
                {
                    using var fc = fl.GetFeatureClass();
                    if (fc != null)
                    {
                        var def = fc.GetDefinition();
                        foreach (var f in def.GetFields())
                        {
                            string fName = f.Name;
                            if (!string.Equals(fName, "OBJECTID", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(fName, "FID", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(fName, "Shape", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(fName, "Shape_Length", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(fName, "Shape_Area", StringComparison.OrdinalIgnoreCase))
                            {
                                fields.Add(fName);
                            }
                        }
                        if (fields.Count > 0) return fields;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtractFields MapLayer] Error: {ex.Message}");
        }

        return fields;
    }

    public static List<Dictionary<string, object>> ReadRecords(string filePath)
    {
        var records = new List<Dictionary<string, object>>();
        if (string.IsNullOrWhiteSpace(filePath)) return records;

        if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || 
            filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            return CustomSchemaDataService.ReadExcelRows(filePath);
        }

        // Shapefile
        if (filePath.EndsWith(".shp", StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
        {
            try
            {
                string folder = Path.GetDirectoryName(filePath)!;
                string file = Path.GetFileName(filePath);
                var fileConn = new FileSystemConnectionPath(new Uri(folder), FileSystemDatastoreType.Shapefile);
                using var datastore = new FileSystemDatastore(fileConn);
                using var fc = datastore.OpenDataset<FeatureClass>(file);
                if (fc != null)
                {
                    var def = fc.GetDefinition();
                    var fNames = def.GetFields().Select(f => f.Name).ToList();
                    using var cursor = fc.Search(null, false);
                    while (cursor.MoveNext())
                    {
                        using var row = cursor.Current;
                        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (var fn in fNames)
                        {
                            int idx = def.FindField(fn);
                            if (idx >= 0) dict[fn] = row[idx]?.ToString()?.Trim() ?? string.Empty;
                        }
                        records.Add(dict);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReadRecords Shapefile] Error: {ex.Message}");
            }
            return records;
        }

        // Geodatabase
        if (filePath.Contains(".gdb", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                int gdbIdx = filePath.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase);
                string gdbPath = filePath.Substring(0, gdbIdx + 4);
                string fcName = filePath.Substring(gdbIdx + 4).Trim('\\', '/');

                if (Directory.Exists(gdbPath))
                {
                    using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                    using var fc = string.IsNullOrWhiteSpace(fcName)
                        ? gdb.GetDefinitions<FeatureClassDefinition>().FirstOrDefault() != null
                            ? gdb.OpenDataset<FeatureClass>(gdb.GetDefinitions<FeatureClassDefinition>().First().GetName())
                            : null
                        : gdb.OpenDataset<FeatureClass>(fcName);

                    if (fc != null)
                    {
                        var def = fc.GetDefinition();
                        var fNames = def.GetFields().Select(f => f.Name).ToList();
                        using var cursor = fc.Search(null, false);
                        while (cursor.MoveNext())
                        {
                            using var row = cursor.Current;
                            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            foreach (var fn in fNames)
                            {
                                int idx = def.FindField(fn);
                                if (idx >= 0) dict[fn] = row[idx]?.ToString()?.Trim() ?? string.Empty;
                            }
                            records.Add(dict);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReadRecords GDB] Error: {ex.Message}");
            }
            return records;
        }

        // Active Map FeatureLayer
        try
        {
            var map = MapView.Active?.Map;
            if (map != null)
            {
                var fl = map.GetLayersAsFlattenedList().OfType<FeatureLayer>()
                    .FirstOrDefault(l => string.Equals(l.Name, filePath, StringComparison.OrdinalIgnoreCase) ||
                                         filePath.StartsWith(l.Name, StringComparison.OrdinalIgnoreCase));
                if (fl != null)
                {
                    using var fc = fl.GetFeatureClass();
                    if (fc != null)
                    {
                        var def = fc.GetDefinition();
                        var fNames = def.GetFields().Select(f => f.Name).ToList();
                        using var cursor = fc.Search(null, false);
                        while (cursor.MoveNext())
                        {
                            using var row = cursor.Current;
                            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            foreach (var fn in fNames)
                            {
                                int idx = def.FindField(fn);
                                if (idx >= 0) dict[fn] = row[idx]?.ToString()?.Trim() ?? string.Empty;
                            }
                            records.Add(dict);
                        }
                        if (records.Count > 0) return records;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReadRecords MapLayer] Error: {ex.Message}");
        }

        return records;
    }
}
