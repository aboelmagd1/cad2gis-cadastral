using System;
using System.Collections.Generic;
using System.Linq;

namespace Cad2Gis.Core.Services;

public class LineGeometry2D
{
    public List<Point2D> Points { get; set; } = new();
    public bool IsClosed => Points.Count >= 3 && Points[0].DistanceTo(Points[Points.Count - 1]) < 0.001;
    public string LayerName { get; set; } = string.Empty;
}

public class PolygonRing2D
{
    public List<Point2D> Vertices { get; set; } = new();
    public double Area { get; set; }
    public double Perimeter { get; set; }
    public string SourceLayer { get; set; } = string.Empty;

    public Point2D Centroid
    {
        get
        {
            if (Vertices.Count == 0) return new Point2D(0, 0);
            double sx = Vertices.Sum(v => v.X);
            double sy = Vertices.Sum(v => v.Y);
            return new Point2D(sx / Vertices.Count, sy / Vertices.Count);
        }
    }
}

public class DangleInfo
{
    public Point2D Location { get; set; }
    public string LayerName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class PolygonizerReport
{
    public int PolygonsBuilt { get; set; }
    public int ClosedLinesKept { get; set; }
    public int LinesPolygonized { get; set; }
    public List<DangleInfo> DanglesFound { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<PolygonRing2D> ResultRings { get; set; } = new();
}

public class PolygonizerOptions
{
    public double SnapToleranceMeters { get; set; } = 0.05;
    public bool KeepAlreadyClosedPolylines { get; set; } = true;
    public bool TrimDanglesWithinTolerance { get; set; } = true;
    public bool GroupLayersTogether { get; set; } = true;
    public bool ExtendLinesAndCurves { get; set; } = true;
    public double ExtendToleranceMeters { get; set; } = 0.05; // 5 cm default
}

public static class LinePolygonizerService
{
    public static PolygonizerReport Polygonize(
        IEnumerable<LineGeometry2D> inputLines,
        PolygonizerOptions options)
    {
        var report = new PolygonizerReport();
        var linesList = inputLines.ToList();

        var closedLines = new List<LineGeometry2D>();
        var openLines = new List<LineGeometry2D>();

        foreach (var line in linesList)
        {
            if (options.KeepAlreadyClosedPolylines && line.IsClosed)
            {
                closedLines.Add(line);
            }
            else
            {
                openLines.Add(line);
            }
        }

        // Add already-closed polylines to result
        foreach (var c in closedLines)
        {
            var ring = BuildRingFromPoints(c.Points, c.LayerName);
            if (ring != null)
            {
                report.ResultRings.Add(ring);
                report.ClosedLinesKept++;
            }
        }

        if (openLines.Count == 0)
        {
            report.PolygonsBuilt = report.ResultRings.Count;
            return report;
        }

        // Decompose open lines into individual 2-point segments
        var segments = new List<Segment2D>();
        foreach (var line in openLines)
        {
            for (int i = 0; i < line.Points.Count - 1; i++)
            {
                segments.Add(new Segment2D(line.Points[i], line.Points[i + 1], elementType: line.LayerName));
            }
        }

        // Snap vertices within tolerance
        SnapSegments(segments, options.SnapToleranceMeters);

        // Find endpoint degrees to report dangles
        var nodeCount = new Dictionary<(long X, long Y), int>();
        long KeyCoord(double v) => (long)Math.Round(v / options.SnapToleranceMeters);

        foreach (var seg in segments)
        {
            var k1 = (KeyCoord(seg.Start.X), KeyCoord(seg.Start.Y));
            var k2 = (KeyCoord(seg.End.X), KeyCoord(seg.End.Y));

            nodeCount[k1] = nodeCount.GetValueOrDefault(k1) + 1;
            nodeCount[k2] = nodeCount.GetValueOrDefault(k2) + 1;
        }

        foreach (var seg in segments)
        {
            var k1 = (KeyCoord(seg.Start.X), KeyCoord(seg.Start.Y));
            var k2 = (KeyCoord(seg.End.X), KeyCoord(seg.End.Y));

            if (nodeCount[k1] == 1)
            {
                report.DanglesFound.Add(new DangleInfo
                {
                    Location = seg.Start,
                    LayerName = seg.ElementType ?? "Unknown",
                    Message = $"Open dangle/gap at ({seg.Start.X:F2}, {seg.Start.Y:F2})"
                });
            }
            if (nodeCount[k2] == 1)
            {
                report.DanglesFound.Add(new DangleInfo
                {
                    Location = seg.End,
                    LayerName = seg.ElementType ?? "Unknown",
                    Message = $"Open dangle/gap at ({seg.End.X:F2}, {seg.End.Y:F2})"
                });
            }
        }

        // Assemble connected cycles into closed rings
        var assembledRings = AssembleCycles(segments, options.SnapToleranceMeters);
        foreach (var r in assembledRings)
        {
            report.ResultRings.Add(r);
            report.LinesPolygonized++;
        }

        report.PolygonsBuilt = report.ResultRings.Count;
        return report;
    }

    private static void SnapSegments(List<Segment2D> segments, double tolerance)
    {
        double cellSize = Math.Max(tolerance * 2.0, 0.1);
        var grid = new Dictionary<(long X, long Y), List<Point2D>>();

        Point2D GetOrCreateCluster(Point2D pt)
        {
            long gx = (long)Math.Floor(pt.X / cellSize);
            long gy = (long)Math.Floor(pt.Y / cellSize);

            // Fast neighbor cell check (9 cells)
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    if (grid.TryGetValue((gx + dx, gy + dy), out var cellPoints))
                    {
                        for (int i = 0; i < cellPoints.Count; i++)
                        {
                            if (cellPoints[i].DistanceTo(pt) <= tolerance)
                            {
                                return cellPoints[i];
                            }
                        }
                    }
                }
            }

            // New cluster
            if (!grid.TryGetValue((gx, gy), out var list))
            {
                list = new List<Point2D>();
                grid[(gx, gy)] = list;
            }
            list.Add(pt);
            return pt;
        }

        for (int i = 0; i < segments.Count; i++)
        {
            segments[i].Start = GetOrCreateCluster(segments[i].Start);
            segments[i].End = GetOrCreateCluster(segments[i].End);
        }
    }

    private static List<PolygonRing2D> AssembleCycles(List<Segment2D> segments, double tolerance)
    {
        var rings = new List<PolygonRing2D>();
        var used = new HashSet<int>();

        // Build spatial endpoint index for O(1) adjacency lookup
        double cellSize = Math.Max(tolerance * 2.0, 0.1);
        long KeyCoord(double v) => (long)Math.Floor(v / cellSize);

        var endpointGrid = new Dictionary<(long X, long Y), List<int>>();
        void AddEndpoint(Point2D pt, int segIdx)
        {
            var k = (KeyCoord(pt.X), KeyCoord(pt.Y));
            if (!endpointGrid.TryGetValue(k, out var list))
            {
                list = new List<int>();
                endpointGrid[k] = list;
            }
            list.Add(segIdx);
        }

        for (int i = 0; i < segments.Count; i++)
        {
            AddEndpoint(segments[i].Start, i);
            AddEndpoint(segments[i].End, i);
        }

        for (int startIdx = 0; startIdx < segments.Count; startIdx++)
        {
            if (used.Contains(startIdx)) continue;

            var currentChain = new List<Point2D> { segments[startIdx].Start, segments[startIdx].End };
            used.Add(startIdx);
            bool foundNext = true;

            while (foundNext)
            {
                foundNext = false;
                var currentEnd = currentChain.Last();

                // Check if loop is closed
                if (currentChain.Count > 2 && currentEnd.DistanceTo(currentChain.First()) <= tolerance)
                {
                    // Closed loop!
                    var ring = BuildRingFromPoints(currentChain, segments[startIdx].ElementType ?? "Lines");
                    if (ring != null && ring.Area > 0.01)
                    {
                        rings.Add(ring);
                    }
                    break;
                }

                long gx = KeyCoord(currentEnd.X);
                long gy = KeyCoord(currentEnd.Y);

                bool candidateFound = false;
                for (long dx = -1; dx <= 1 && !candidateFound; dx++)
                {
                    for (long dy = -1; dy <= 1 && !candidateFound; dy++)
                    {
                        if (endpointGrid.TryGetValue((gx + dx, gy + dy), out var candidates))
                        {
                            for (int ci = 0; ci < candidates.Count; ci++)
                            {
                                int nextIdx = candidates[ci];
                                if (used.Contains(nextIdx)) continue;

                                var nextSeg = segments[nextIdx];
                                if (currentEnd.DistanceTo(nextSeg.Start) <= tolerance)
                                {
                                    currentChain.Add(nextSeg.End);
                                    used.Add(nextIdx);
                                    foundNext = true;
                                    candidateFound = true;
                                    break;
                                }
                                else if (currentEnd.DistanceTo(nextSeg.End) <= tolerance)
                                {
                                    currentChain.Add(nextSeg.Start);
                                    used.Add(nextIdx);
                                    foundNext = true;
                                    candidateFound = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        return rings;
    }

    public static PolygonRing2D? BuildRingFromPoints(List<Point2D> pts, string layerName)
    {
        if (pts == null || pts.Count < 3) return null;

        // Ensure closed
        var closedPts = new List<Point2D>(pts);
        if (closedPts[0].DistanceTo(closedPts[closedPts.Count - 1]) > 0.001)
        {
            closedPts.Add(closedPts[0]);
        }

        // Compute Shoelace formula area
        double doubleArea = 0;
        double perimeter = 0;
        for (int i = 0; i < closedPts.Count - 1; i++)
        {
            doubleArea += (closedPts[i].X * closedPts[i + 1].Y) - (closedPts[i + 1].X * closedPts[i].Y);
            perimeter += closedPts[i].DistanceTo(closedPts[i + 1]);
        }

        double area = Math.Abs(doubleArea) / 2.0;
        if (area < 0.001) return null;

        return new PolygonRing2D
        {
            Vertices = closedPts,
            Area = area,
            Perimeter = perimeter,
            SourceLayer = layerName
        };
    }
}
