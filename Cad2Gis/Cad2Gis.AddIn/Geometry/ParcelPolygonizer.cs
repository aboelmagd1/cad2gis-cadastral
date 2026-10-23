using System;
using System.Collections.Generic;
using ArcGIS.Core.Geometry;
using Cad2Gis.Core.Models;

namespace Cad2Gis.AddIn.Geometry;

public static class ParcelPolygonizer
{
    /// <summary>
    /// Strips Z and M dimensions from a Polygon, ensuring pure 2D planar geometry.
    /// </summary>
    public static Polygon Ensure2D(Polygon poly)
    {
        if (poly == null || poly.IsEmpty) return poly!;
        if (!poly.HasZ && !poly.HasM) return poly;
        var b = new PolygonBuilderEx(poly);
        b.HasZ = false;
        b.HasM = false;
        return (Polygon)b.ToGeometry();
    }

    /// <summary>
    /// Strips Z and M dimensions from a Polyline, ensuring pure 2D planar geometry.
    /// </summary>
    public static Polyline Ensure2D(Polyline line)
    {
        if (line == null || line.IsEmpty) return line!;
        if (!line.HasZ && !line.HasM) return line;
        var b = new PolylineBuilderEx(line);
        b.HasZ = false;
        b.HasM = false;
        return (Polyline)b.ToGeometry();
    }

    /// <summary>
    /// Strips Z and M dimensions from a MapPoint, ensuring pure 2D planar geometry.
    /// </summary>
    public static MapPoint Ensure2D(MapPoint pt)
    {
        if (pt == null || pt.IsEmpty) return pt!;
        if (!pt.HasZ && !pt.HasM) return pt;
        var b = new MapPointBuilderEx(pt);
        b.HasZ = false;
        b.HasM = false;
        return (MapPoint)b.ToGeometry();
    }

    /// <summary>
    /// Creates a native LineSegment between two 2D points.
    /// </summary>
    public static LineSegment CreateLineSegment(MapPoint start, MapPoint end)
    {
        return LineBuilderEx.CreateLineSegment(start, end);
    }

    /// <summary>
    /// Builds a native Polygon from an ordered sequence of native Segments (LineSegment, EllipticArcSegment, CubicBezierSegment)
    /// preserving 100% of true curves and arcs.
    /// </summary>
    public static Polygon BuildPolygonFromSegments(IEnumerable<Segment> segments, SpatialReference sr)
    {
        var pb = new PolygonBuilderEx(sr);
        pb.AddPart(segments);
        pb.HasZ = false;
        pb.HasM = false;
        return (Polygon)pb.ToGeometry();
    }

    /// <summary>
    /// Preserves original geometry, curves, arcs, and vertices 100% intact.
    /// Strictly strips Z and M dimensions to ensure pure 2D planar geometry (MOMRAH / Balady compliance)
    /// with zero densification, simplification, or altering of original CAD curves.
    /// </summary>
    public static Polygon Clean2DPlanar(Polygon inputPolygon)
    {
        if (inputPolygon == null || inputPolygon.IsEmpty)
            throw new ArgumentNullException(nameof(inputPolygon));

        return Ensure2D(inputPolygon);
    }

    [Obsolete("Use Clean2DPlanar instead. Curves and vertices are strictly preserved without densification.")]
    public static Polygon DensifyAndClean(Polygon inputPolygon, double maxSegmentLength = 1.0)
    {
        return Clean2DPlanar(inputPolygon);
    }

    /// <summary>
    /// Computes frozen planar UTM metrics directly from the ArcGIS Pro geometry engine.
    /// </summary>
    public static (double areaSqm, double lengthM, MapPoint centroid) ComputeMetrics(Polygon polygon)
    {
        if (polygon == null || polygon.IsEmpty)
            return (0.0, 0.0, MapPointBuilderEx.CreateMapPoint(0, 0));

        var poly2D = Ensure2D(polygon);
        double area = Math.Abs(GeometryEngine.Instance.Area(poly2D));
        double length = GeometryEngine.Instance.Length(poly2D);

        MapPoint centroid;
        try
        {
            centroid = GeometryEngine.Instance.Centroid(poly2D);
        }
        catch
        {
            centroid = poly2D.Extent.Center;
        }

        return (area, length, Ensure2D(centroid));
    }
}
