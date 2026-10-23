using System;
using System.Collections.Generic;
using ArcGIS.Core.Geometry;
using Cad2Gis.Core.Models;

namespace Cad2Gis.AddIn.Geometry;

/// <summary>
/// Native ArcGIS Pro SDK geometry auditor that inspects true segments (LineSegment, EllipticArcSegment, CubicBezierSegment)
/// directly from native Polygon and Polyline objects.
/// </summary>
public static class NativeCurveAuditHelper
{
    /// <summary>
    /// Audits a native ArcGIS Pro Geometry and returns a detailed segment and curve breakdown.
    /// </summary>
    public static GeometryCurveAuditResult AuditNativeGeometry(ArcGIS.Core.Geometry.Geometry geom, long featureId = 0, string featureKey = "")
    {
        var result = new GeometryCurveAuditResult
        {
            FeatureId = featureId,
            FeatureKey = featureKey,
            GeometryType = geom?.GeometryType.ToString() ?? "Unknown"
        };

        if (geom == null || geom.IsEmpty)
            return result;

        result.VertexCount = geom.PointCount;
        result.ExactLength = GeometryEngine.Instance.Length(geom);
        if (geom is Polygon poly)
        {
            result.ExactArea = Math.Abs(GeometryEngine.Instance.Area(poly));
        }

        if (geom is Multipart multipart)
        {
            foreach (var part in multipart.Parts)
            {
                foreach (var seg in part)
                {
                    result.TotalSegments++;
                    if (seg is EllipticArcSegment arc)
                    {
                        if (arc.IsCircular || Math.Abs(arc.SemiMajorAxis - arc.SemiMinorAxis) < 1e-5)
                        {
                            result.CircularArcsCount++;
                        }
                        else
                        {
                            result.EllipticArcsCount++;
                        }

                        var center = arc.CenterPoint;
                        result.Arcs.Add(new ArcAuditInfo
                        {
                            CenterX = center.X,
                            CenterY = center.Y,
                            Radius = arc.SemiMajorAxis,
                            Length = seg.Length,
                            IsCounterClockwise = arc.IsCounterClockwise,
                            StartX = seg.StartPoint.X,
                            StartY = seg.StartPoint.Y,
                            EndX = seg.EndPoint.X,
                            EndY = seg.EndPoint.Y
                        });
                    }
                    else if (seg is CubicBezierSegment)
                    {
                        result.BezierCurvesCount++;
                    }
                    else
                    {
                        result.LineSegmentsCount++;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if a geometry contains any true curves (circular arcs, elliptic arcs, or Béziers).
    /// </summary>
    public static bool HasCurves(ArcGIS.Core.Geometry.Geometry geom)
    {
        if (geom is Multipart multipart)
        {
            return multipart.HasCurves;
        }
        return false;
    }

    /// <summary>
    /// Reverses the orientation of a native segment (line, elliptic arc, or Bézier)
    /// strictly preserving arc parameters and curve nature.
    /// </summary>
    public static Segment ReverseNativeSegment(Segment seg)
    {
        if (seg is LineSegment)
        {
            return LineBuilderEx.CreateLineSegment(seg.EndPoint, seg.StartPoint);
        }

        var polyline = PolylineBuilderEx.CreatePolyline(new[] { seg });
        if (GeometryEngine.Instance.ReverseOrientation(polyline) is Polyline revLine && revLine.Parts.Count > 0 && revLine.Parts[0].Count > 0)
        {
            return revLine.Parts[0][0];
        }

        return seg;
    }

    /// <summary>
    /// Extracts a subcurve from a native polyline or segment at given distance along the curve,
    /// preserving circular arcs and true curve nature (same circle, center, radius, direction).
    /// </summary>
    public static Polyline GetSubCurve(Polyline polyline, double fromDist, double toDist)
    {
        return GeometryEngine.Instance.GetSubCurve(polyline, fromDist, toDist, AsRatioOrLength.AsLength);
    }

    /// <summary>
    /// Converts a CurvePolygonRing (composed of CoreBoundarySegments, lines, and true curves)
    /// to a native ArcGIS Pro Polygon, chaining native segments with zero arc-to-line flattening.
    /// Minimal closing gaps up to tolerance are represented by minimal LineSegments without moving vertices.
    /// </summary>
    public static Polygon BuildNativePolygonFromRing(Cad2Gis.Core.Services.CurvePolygonRing ring, SpatialReference sr)
    {
        if (ring == null || ring.Segments.Count == 0)
            throw new ArgumentException("Ring has no segments.", nameof(ring));

        var nativeSegments = new List<Segment>();

        foreach (var seg in ring.Segments)
        {
            var p1 = MapPointBuilderEx.CreateMapPoint(seg.Start.X, seg.Start.Y, sr);
            var p2 = MapPointBuilderEx.CreateMapPoint(seg.End.X, seg.End.Y, sr);

            if (seg.Type == Cad2Gis.Core.Geometry.CurveType.CircularArc && seg.Center.HasValue && seg.Radius.HasValue && seg.Radius.Value > 1e-4)
            {
                // 1. Try matching with tagged native arc if endpoints match within precision
                if (seg.NativeGeometryTag is EllipticArcSegment nativeArc)
                {
                    double dStartFwd = Math.Sqrt(Math.Pow(nativeArc.StartPoint.X - seg.Start.X, 2) + Math.Pow(nativeArc.StartPoint.Y - seg.Start.Y, 2));
                    double dEndFwd = Math.Sqrt(Math.Pow(nativeArc.EndPoint.X - seg.End.X, 2) + Math.Pow(nativeArc.EndPoint.Y - seg.End.Y, 2));
                    if (dStartFwd < 1e-4 && dEndFwd < 1e-4)
                    {
                        nativeSegments.Add(nativeArc);
                        continue;
                    }

                    double dStartRev = Math.Sqrt(Math.Pow(nativeArc.StartPoint.X - seg.End.X, 2) + Math.Pow(nativeArc.StartPoint.Y - seg.End.Y, 2));
                    double dEndRev = Math.Sqrt(Math.Pow(nativeArc.EndPoint.X - seg.Start.X, 2) + Math.Pow(nativeArc.EndPoint.Y - seg.Start.Y, 2));
                    if (dStartRev < 1e-4 && dEndRev < 1e-4)
                    {
                        nativeSegments.Add(ReverseNativeSegment(nativeArc));
                        continue;
                    }
                }

                // 2. Reconstruct true native circular arc (EllipticArcSegment) strictly preserving center, radius, and orientation
                var cPt = new Coordinate2D(seg.Center.Value.X, seg.Center.Value.Y);
                bool ccw = seg.IsCounterClockwise ?? true;
                var orientation = ccw ? ArcOrientation.ArcCounterClockwise : ArcOrientation.ArcClockwise;
                try
                {
                    var arcSeg = EllipticArcBuilderEx.CreateCircularArc(p1, p2, cPt, orientation, sr);
                    nativeSegments.Add(arcSeg);
                }
                catch
                {
                    // Fallback only if degenerate
                    nativeSegments.Add(LineBuilderEx.CreateLineSegment(p1, p2));
                }
            }
            else
            {
                // Straight line segment
                if (seg.NativeGeometryTag is LineSegment nativeLine)
                {
                    double dStartFwd = Math.Sqrt(Math.Pow(nativeLine.StartPoint.X - seg.Start.X, 2) + Math.Pow(nativeLine.StartPoint.Y - seg.Start.Y, 2));
                    double dEndFwd = Math.Sqrt(Math.Pow(nativeLine.EndPoint.X - seg.End.X, 2) + Math.Pow(nativeLine.EndPoint.Y - seg.End.Y, 2));
                    if (dStartFwd < 1e-4 && dEndFwd < 1e-4)
                    {
                        nativeSegments.Add(nativeLine);
                        continue;
                    }
                    double dStartRev = Math.Sqrt(Math.Pow(nativeLine.StartPoint.X - seg.End.X, 2) + Math.Pow(nativeLine.StartPoint.Y - seg.End.Y, 2));
                    double dEndRev = Math.Sqrt(Math.Pow(nativeLine.EndPoint.X - seg.Start.X, 2) + Math.Pow(nativeLine.EndPoint.Y - seg.Start.Y, 2));
                    if (dStartRev < 1e-4 && dEndRev < 1e-4)
                    {
                        nativeSegments.Add(ReverseNativeSegment(nativeLine));
                        continue;
                    }
                }
                nativeSegments.Add(LineBuilderEx.CreateLineSegment(p1, p2));
            }
        }

        var pb = new PolygonBuilderEx(sr);
        pb.AddPart(nativeSegments);
        pb.HasZ = false;
        pb.HasM = false;
        return (Polygon)pb.ToGeometry();
    }
}
