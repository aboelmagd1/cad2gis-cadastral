using System;
using System.Collections.Generic;
using System.Linq;

namespace Cad2Gis.Core.Services;

public class UtmZoneInfo
{
    public int Epsg { get; set; }
    public string Name { get; set; } = string.Empty;
    public double CentralMeridianDeg { get; set; }
    public bool IsPlausibleForSaudi { get; set; }
    public double PlausibilityRank { get; set; } // Lower is better
    public string RecommendationNotes { get; set; } = string.Empty;
}

public static class CrsProjectionService
{
    public static readonly IReadOnlyList<int> SupportedUtmZones = new[]
    {
        32635, // UTM 35N (27°E)
        32636, // UTM 36N (33°E)
        32637, // UTM 37N (39°E) - Makkah, Jeddah, Madinah, Tabuk, Hail
        32638, // UTM 38N (45°E) - Riyadh, Qassim
        32639, // UTM 39N (51°E) - Eastern Province (Dammam, Al-Ahsa)
        32640  // UTM 40N (57°E)
    };

    // Plausible Saudi Arabia geographic extent in WGS 84 (EPSG:4326)
    // Approximate extent: 34°E - 56°E longitude, 16°N - 33°N latitude
    public const double SaudiLatMin = 16.0;
    public const double SaudiLatMax = 33.0;
    public const double SaudiLonMin = 34.0;
    public const double SaudiLonMax = 56.0;

    // Representative regional anchor points for Saudi UTM zones
    private static readonly (double Lat, double Lon, int Zone, string Name)[] SaudiZoneAnchors = new[]
    {
        // Zone 38N (Central & South: Riyadh, Qassim, Asir, Jazan)
        (24.7136, 46.6753, 38, "Riyadh"),
        (26.3260, 43.9750, 38, "Qassim / Buraidah"),
        (18.2164, 42.5053, 38, "Abha / Asir"),
        (16.8892, 42.5511, 38, "Jazan"),
        (17.4924, 44.1277, 38, "Najran"),

        // Zone 37N (Western & North-Western: Makkah, Jeddah, Madinah, Tabuk, Hail)
        (21.5433, 39.1728, 37, "Jeddah"),
        (21.3891, 39.8579, 37, "Makkah"),
        (24.4672, 39.6111, 37, "Madinah"),
        (28.3835, 36.5550, 37, "Tabuk"),
        (27.5219, 41.6907, 37, "Hail"),
        (21.2854, 40.4222, 37, "Taif"),
        (29.9697, 40.2064, 37, "Al-Jouf / Sakaka"),

        // Zone 39N (Eastern Province: Dammam, Al-Ahsa, Jubail, Khobar)
        (26.4207, 50.0888, 39, "Dammam"),
        (25.3835, 49.5862, 39, "Al-Ahsa / Hofuf"),
        (27.0046, 49.6591, 39, "Jubail"),
        (26.2172, 50.1971, 39, "Khobar"),

        // Zone 36N (Far North-West border: Duba, Haql, Turaif)
        (29.2833, 34.9333, 36, "Haql"),
        (27.3500, 35.6833, 36, "Duba")
    };

    public static List<UtmZoneInfo> EvaluateZonesForBbox(double minX, double minY, double maxX, double maxY)
    {
        double midX = (minX + maxX) / 2.0;
        double midY = (minY + maxY) / 2.0;

        var results = new List<UtmZoneInfo>();

        foreach (int epsg in SupportedUtmZones)
        {
            double cm = (epsg - 32601) * 6.0 - 180.0 + 3.0; // Central meridian
            int zoneNum = epsg - 32600;

            // Conversion from UTM to Lat/Lon for bounding box center under candidate zone
            var (lat, lon) = UtmToLatLon(midX, midY, zoneNum);

            bool inLat = lat >= SaudiLatMin && lat <= SaudiLatMax;
            bool inLon = lon >= SaudiLonMin && lon <= SaudiLonMax;
            bool isPlausible = inLat && inLon;

            double rank;
            string notes;

            if (isPlausible)
            {
                // Measure distance to known regional anchor cities in this specific zone
                var zoneAnchors = SaudiZoneAnchors.Where(a => a.Zone == zoneNum).ToList();
                if (zoneAnchors.Count > 0)
                {
                    double minAnchorDist = zoneAnchors
                        .Select(a => Math.Sqrt(Math.Pow(lat - a.Lat, 2) + Math.Pow(lon - a.Lon, 2)))
                        .Min();
                    rank = minAnchorDist;
                }
                else
                {
                    rank = 10.0 + Math.Abs(lon - cm);
                }

                notes = $"Plausible Saudi location (~{lat:F2}°N, {lon:F2}°E, distance to CM: {Math.Abs(lon - cm):F1}°)";
            }
            else
            {
                double latPenalty = lat < SaudiLatMin ? (SaudiLatMin - lat) : (lat > SaudiLatMax ? (lat - SaudiLatMax) : 0);
                double lonPenalty = lon < SaudiLonMin ? (SaudiLonMin - lon) : (lon > SaudiLonMax ? (lon - SaudiLonMax) : 0);
                rank = 1000.0 + (latPenalty + lonPenalty) * 10.0 + Math.Abs(lon - cm);
                notes = $"Falls outside plausible Saudi bounds (~{lat:F2}°N, {lon:F2}°E)";
            }

            results.Add(new UtmZoneInfo
            {
                Epsg = epsg,
                Name = $"WGS 84 / UTM zone {zoneNum}N (EPSG:{epsg})",
                CentralMeridianDeg = cm,
                IsPlausibleForSaudi = isPlausible,
                PlausibilityRank = rank,
                RecommendationNotes = notes
            });
        }

        return results.OrderBy(r => r.PlausibilityRank).ToList();
    }

    /// <summary>
    /// Converts UTM (Easting, Northing in meters, Northern Hemisphere) to WGS84 (Lat, Lon in degrees).
    /// Standard Transverse Mercator formulation (WGS 84 ellipsoid).
    /// </summary>
    public static (double LatDeg, double LonDeg) UtmToLatLon(double easting, double northing, int zone)
    {
        const double a = 6378137.0; // semi-major axis
        const double f = 1.0 / 298.257223563;
        double b = a * (1.0 - f);
        double eSq = (a * a - b * b) / (a * a);
        double ePrimeSq = eSq / (1.0 - eSq);
        const double k0 = 0.9996;

        double x = easting - 500000.0;
        double y = northing;

        double m = y / k0;
        double mu = m / (a * (1.0 - eSq / 4.0 - 3.0 * eSq * eSq / 64.0 - 5.0 * Math.Pow(eSq, 3) / 256.0));

        double e1 = (1.0 - Math.Sqrt(1.0 - eSq)) / (1.0 + Math.Sqrt(1.0 - eSq));
        double phi1Rad = mu + (3.0 * e1 / 2.0 - 27.0 * Math.Pow(e1, 3) / 32.0) * Math.Sin(2.0 * mu)
            + (21.0 * e1 * e1 / 16.0 - 55.0 * Math.Pow(e1, 4) / 32.0) * Math.Sin(4.0 * mu)
            + (151.0 * Math.Pow(e1, 3) / 96.0) * Math.Sin(6.0 * mu);

        double n1 = a / Math.Sqrt(1.0 - eSq * Math.Sin(phi1Rad) * Math.Sin(phi1Rad));
        double t1 = Math.Tan(phi1Rad) * Math.Tan(phi1Rad);
        double c1 = ePrimeSq * Math.Cos(phi1Rad) * Math.Cos(phi1Rad);
        double r1 = a * (1.0 - eSq) / Math.Pow(1.0 - eSq * Math.Sin(phi1Rad) * Math.Sin(phi1Rad), 1.5);
        double d = x / (n1 * k0);

        double latRad = phi1Rad - (n1 * Math.Tan(phi1Rad) / r1) * (
            d * d / 2.0 - (5.0 + 3.0 * t1 + 10.0 * c1 - 4.0 * c1 * c1 - 9.0 * ePrimeSq) * Math.Pow(d, 4) / 24.0
            + (61.0 + 90.0 * t1 + 298.0 * c1 + 45.0 * t1 * t1 - 252.0 * ePrimeSq - 3.0 * c1 * c1) * Math.Pow(d, 6) / 720.0);

        double lonRad = (d - (1.0 + 2.0 * t1 + c1) * Math.Pow(d, 3) / 6.0
            + (5.0 - 2.0 * c1 + 28.0 * t1 - 3.0 * c1 * c1 + 8.0 * ePrimeSq + 24.0 * t1 * t1) * Math.Pow(d, 5) / 120.0) / Math.Cos(phi1Rad);

        double centralMeridianDeg = (zone - 1) * 6 - 180 + 3;
        double latDeg = latRad * (180.0 / Math.PI);
        double lonDeg = centralMeridianDeg + lonRad * (180.0 / Math.PI);

        return (latDeg, lonDeg);
    }

    public static List<UtmZoneInfo> RankPlausibleUtmZones(double easting, double northing) =>
        EvaluateZonesForBbox(easting, northing, easting, northing);

    public static bool IsPlausibleSaudiGeographic(double longitude, double latitude) =>
        latitude >= SaudiLatMin && latitude <= SaudiLatMax && longitude >= SaudiLonMin && longitude <= SaudiLonMax;

    /// <summary>
    /// Computes the meridian convergence angle (gamma) in degrees at a UTM coordinate.
    /// Used to correct UTM grid azimuth to true north: TrueAzimuth = (GridAzimuth + Gamma + 360) % 360.
    /// Gamma is positive when east of the central meridian, negative when west.
    /// </summary>
    public static double ComputeMeridianConvergenceDeg(double easting, double northing, int zone)
    {
        var (latDeg, lonDeg) = UtmToLatLon(easting, northing, zone);
        double centralMeridianDeg = (zone - 1) * 6 - 180 + 3;
        double deltaLonDeg = lonDeg - centralMeridianDeg;
        double latRad = latDeg * (Math.PI / 180.0);
        // Transverse Mercator convergence approximation: gamma = deltaLon * sin(lat)
        return deltaLonDeg * Math.Sin(latRad);
    }
}

