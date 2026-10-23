using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cad2Gis.Core.Services;

public enum AdminDetectionStatus
{
    Success,
    NoHit,
    MultipleHits,
    FileNotFound
}

public class AdminAreaDetectionResult
{
    public string RegionId { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public string CityId { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public AdminDetectionStatus Status { get; set; } = AdminDetectionStatus.NoHit;
    public string Warning { get; set; } = string.Empty;
}

public class AdminAreaDetectionService
{
    private class ShpPolygon
    {
        public double MinX, MinY, MaxX, MaxY;
        public List<int> Parts = new();
        public List<(double X, double Y)> Points = new();
        public Dictionary<string, string> Properties = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly string _adminAreasFolder;
    private List<ShpPolygon>? _cities;
    private List<ShpPolygon>? _regions;
    private readonly object _lock = new();

    public AdminAreaDetectionService(string adminAreasFolder)
    {
        _adminAreasFolder = adminAreasFolder ?? string.Empty;
    }

    public AdminAreaDetectionResult DetectFromUtm(double easting, double northing, int utmZone)
    {
        var (lat, lon) = CrsProjectionService.UtmToLatLon(easting, northing, utmZone);
        return DetectFromLatLon(lat, lon);
    }

    public AdminAreaDetectionResult DetectFromLatLon(double latitude, double longitude)
    {
        var result = new AdminAreaDetectionResult();

        if (string.IsNullOrWhiteSpace(_adminAreasFolder) || !Directory.Exists(_adminAreasFolder))
        {
            result.Status = AdminDetectionStatus.FileNotFound;
            result.Warning = $"Admin areas directory '{_adminAreasFolder}' not found. Fields left blank.";
            return result;
        }

        string citiesShp = Path.Combine(_adminAreasFolder, "cities.shp");
        string citiesDbf = Path.Combine(_adminAreasFolder, "cities.dbf");
        string regonsShp = Path.Combine(_adminAreasFolder, "regons.shp");
        string regonsDbf = Path.Combine(_adminAreasFolder, "regons.dbf");

        if (!File.Exists(citiesShp) || !File.Exists(citiesDbf) || !File.Exists(regonsShp) || !File.Exists(regonsDbf))
        {
            result.Status = AdminDetectionStatus.FileNotFound;
            result.Warning = "Missing cities.shp or regons.shp in admin areas folder. Fields left blank.";
            return result;
        }

        EnsureLoaded(citiesShp, citiesDbf, regonsShp, regonsDbf);

        // Point coordinates in GCS WGS84: X = Longitude, Y = Latitude
        double px = longitude;
        double py = latitude;

        // 1. Query Cities
        var cityHits = new List<ShpPolygon>();
        if (_cities != null)
        {
            foreach (var c in _cities)
            {
                if (PointInPolygon(px, py, c))
                {
                    cityHits.Add(c);
                }
            }
        }

        // 2. Query Regions
        var regionHits = new List<ShpPolygon>();
        if (_regions != null)
        {
            foreach (var r in _regions)
            {
                if (PointInPolygon(px, py, r))
                {
                    regionHits.Add(r);
                }
            }
        }

        if (cityHits.Count == 0 && regionHits.Count == 0)
        {
            result.Status = AdminDetectionStatus.NoHit;
            result.Warning = $"Coordinate ({latitude:F4}°N, {longitude:F4}°E) does not fall inside any known Saudi city or region polygon. Fields left blank.";
            return result;
        }

        if (cityHits.Count > 1 || regionHits.Count > 1)
        {
            result.Status = AdminDetectionStatus.MultipleHits;
            result.Warning = $"Coordinate ({latitude:F4}°N, {longitude:F4}°E) fell into multiple intersecting polygons ({cityHits.Count} cities, {regionHits.Count} regions). Ambiguous jurisdiction: fields left blank.";
            return result;
        }

        result.Status = AdminDetectionStatus.Success;

        if (cityHits.Count == 1)
        {
            var props = cityHits[0].Properties;
            if (props.TryGetValue("CITY_ID", out var cid)) result.CityId = cid;
            if (props.TryGetValue("CITY_NAME", out var cname)) result.CityName = cname;
            if (props.TryGetValue("REGION_ID", out var rid)) result.RegionId = rid;
            if (props.TryGetValue("REG_NAME", out var rname)) result.RegionName = rname;
        }

        if (regionHits.Count == 1)
        {
            var props = regionHits[0].Properties;
            if (string.IsNullOrWhiteSpace(result.RegionId) && props.TryGetValue("REGION_ID", out var rid))
                result.RegionId = rid;
            if (string.IsNullOrWhiteSpace(result.RegionName) && props.TryGetValue("REG_NAME", out var rname))
                result.RegionName = rname;
        }

        return result;
    }

    private void EnsureLoaded(string citiesShp, string citiesDbf, string regonsShp, string regonsDbf)
    {
        if (_cities != null && _regions != null) return;

        lock (_lock)
        {
            if (_cities == null)
                _cities = ReadShapefilePolygons(citiesShp, citiesDbf);

            if (_regions == null)
                _regions = ReadShapefilePolygons(regonsShp, regonsDbf);
        }
    }

    private static bool PointInPolygon(double x, double y, ShpPolygon poly)
    {
        // Bounding box test
        if (x < poly.MinX || x > poly.MaxX || y < poly.MinY || y > poly.MaxY)
            return false;

        var parts = new List<int>(poly.Parts) { poly.Points.Count };
        bool inside = false;

        for (int pIdx = 0; pIdx < parts.Count - 1; pIdx++)
        {
            int start = parts[pIdx];
            int count = parts[pIdx + 1] - start;
            if (count < 3) continue;

            int j = start + count - 1;
            for (int i = start; i < start + count; i++)
            {
                var (xi, yi) = poly.Points[i];
                var (xj, yj) = poly.Points[j];

                if (((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
                {
                    inside = !inside;
                }
                j = i;
            }
        }

        return inside;
    }

    private static List<ShpPolygon> ReadShapefilePolygons(string shpPath, string dbfPath)
    {
        var records = ReadDbf(dbfPath);
        var polygons = new List<ShpPolygon>();

        using var fs = File.OpenRead(shpPath);
        using var br = new BinaryReader(fs);

        if (fs.Length < 100) return polygons;

        // Skip 24 bytes, read file length in 16-bit words (Big Endian)
        fs.Seek(24, SeekOrigin.Begin);
        int fileLengthBytes = ReadBigEndianInt32(br) * 2;

        fs.Seek(100, SeekOrigin.Begin);
        int recIdx = 0;

        while (fs.Position < fileLengthBytes && recIdx < records.Count)
        {
            if (fs.Position + 8 > fs.Length) break;
            int recordNumber = ReadBigEndianInt32(br);
            int contentLengthBytes = ReadBigEndianInt32(br) * 2;
            long nextRecordPos = fs.Position + contentLengthBytes;

            if (contentLengthBytes >= 4)
            {
                int shapeType = br.ReadInt32(); // 5 = Polygon
                if (shapeType == 5 && contentLengthBytes >= 44)
                {
                    double minX = br.ReadDouble();
                    double minY = br.ReadDouble();
                    double maxX = br.ReadDouble();
                    double maxY = br.ReadDouble();
                    int numParts = br.ReadInt32();
                    int numPoints = br.ReadInt32();

                    var poly = new ShpPolygon
                    {
                        MinX = minX,
                        MinY = minY,
                        MaxX = maxX,
                        MaxY = maxY,
                        Properties = records[recIdx]
                    };

                    for (int p = 0; p < numParts; p++)
                    {
                        poly.Parts.Add(br.ReadInt32());
                    }

                    for (int pt = 0; pt < numPoints; pt++)
                    {
                        double px = br.ReadDouble();
                        double py = br.ReadDouble();
                        poly.Points.Add((px, py));
                    }

                    polygons.Add(poly);
                }
            }

            fs.Seek(nextRecordPos, SeekOrigin.Begin);
            recIdx++;
        }

        return polygons;
    }

    private static List<Dictionary<string, string>> ReadDbf(string dbfPath)
    {
        var rows = new List<Dictionary<string, string>>();
        using var fs = File.OpenRead(dbfPath);
        using var br = new BinaryReader(fs);

        if (fs.Length < 32) return rows;

        fs.Seek(4, SeekOrigin.Begin);
        int numRecords = br.ReadInt32();
        short headerLength = br.ReadInt16();
        short recordLength = br.ReadInt16();

        fs.Seek(32, SeekOrigin.Begin);
        var fields = new List<(string Name, int Length)>();

        while (fs.Position < headerLength)
        {
            byte first = br.ReadByte();
            if (first == 0x0D) break; // End of field descriptors

            byte[] nameBytes = new byte[11];
            nameBytes[0] = first;
            br.Read(nameBytes, 1, 10);
            string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0').Trim();

            char type = (char)br.ReadByte();
            br.ReadInt32(); // skip 4 bytes
            byte fieldLen = br.ReadByte();
            byte fieldDec = br.ReadByte();
            br.ReadBytes(14); // skip remaining 14 bytes

            fields.Add((name, fieldLen));
        }

        fs.Seek(headerLength, SeekOrigin.Begin);

        for (int r = 0; r < numRecords; r++)
        {
            if (fs.Position + recordLength > fs.Length) break;
            byte deletedFlag = br.ReadByte();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (fname, flen) in fields)
            {
                byte[] raw = br.ReadBytes(flen);
                string val = Encoding.UTF8.GetString(raw).Trim();
                dict[fname] = val;
            }

            if (deletedFlag != 0x2A) // 0x2A means deleted record
            {
                rows.Add(dict);
            }
        }

        return rows;
    }

    private static int ReadBigEndianInt32(BinaryReader br)
    {
        byte[] bytes = br.ReadBytes(4);
        Array.Reverse(bytes);
        return BitConverter.ToInt32(bytes, 0);
    }
}
