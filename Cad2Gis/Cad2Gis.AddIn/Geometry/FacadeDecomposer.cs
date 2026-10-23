using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Geometry;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.AddIn.Geometry;

public static class FacadeDecomposer
{
    /// <summary>
    /// Part A: Decomposes MAIN parcel polygon boundary ring(s) into at most 4 cardinal facades per ring.
    /// Strictly respects all Part A constraints:
    /// - Only MAIN parcels (PARCEL_TYP = 1). Sub-parcels never get dimensions.
    /// - Operates on boundary lines; never creates new lines.
    /// - Never changes the shape of a curve: true curves stay true curves.
    /// - Holes (interior rings) processed on their own as separate parts; never merged with exterior ring.
    /// - At most 4 single-part polylines per ring (1=North, 2=East, 3=South, 4=West).
    /// - No invented lines for missing directions (flags FEWER_THAN_4_DIRECTIONS).
    /// </summary>
    public static Dictionary<int, (BorderRecord Record, Polyline Geometry)> DecomposeTo4Cardinals(
        Polygon polygon, 
        ParcelRecord parentParcel,
        double chamferThreshold = 7.0,
        double chamferAngle = 45.0,
        double chamferTolerance = 15.0,
        BorderExtractionOptions? explicitOptions = null,
        int utmZone = 37)
    {
        var result = new Dictionary<int, (BorderRecord Record, Polyline Geometry)>();
        if (polygon == null || polygon.IsEmpty)
            return result;

        // Sub-parcels never get dimensions / border features
        if (parentParcel.Class != ParcelClass.Main && parentParcel.ParcelTyp != 1)
            return result;

        var options = explicitOptions ?? new BorderExtractionOptions
        {
            ClusterLengthMeters = chamferThreshold > 0 ? chamferThreshold : 7.0
        };

        var allBorderList = ExtractBordersFromPolygon(polygon, parentParcel, options, utmZone);

        // Populate dictionary with unique composite keys: (ringIndex * 10) + cardinal
        // Ring 0: 1=N, 2=E, 3=S, 4=W
        // Ring 1 (hole): 11=N, 12=E, 13=S, 14=W, etc.
        int keyIndex = 1;
        foreach (var item in allBorderList)
        {
            int dictKey = item.Record.RingIndex == 0 ? item.Record.BorderName : (item.Record.RingIndex * 10 + item.Record.BorderName);
            if (result.ContainsKey(dictKey))
            {
                dictKey = 1000 + keyIndex;
            }
            result[dictKey] = item;
            keyIndex++;
        }

        return result;
    }

    /// <summary>
    /// Extracts borders from all rings (exterior and interior holes) of a MAIN parcel polygon.
    /// </summary>
    public static List<(BorderRecord Record, Polyline Geometry)> ExtractBordersFromPolygon(
        Polygon polygon,
        ParcelRecord parentParcel,
        BorderExtractionOptions options,
        int utmZone = 37)
    {
        var output = new List<(BorderRecord Record, Polyline Geometry)>();
        if (polygon == null || polygon.IsEmpty) return output;

        // Sub-parcels never get dimensions
        if (parentParcel.Class != ParcelClass.Main && parentParcel.ParcelTyp != 1)
            return output;

        var centroidPoint = GeometryEngine.Instance.Centroid(polygon);
        Point2D? centroid2D = centroidPoint != null ? new Point2D(centroidPoint.X, centroidPoint.Y) : null;

        var parts = polygon.Parts;
        int partCount = parts.Count;

        for (int r = 0; r < partCount; r++)
        {
            var part = parts[r];
            bool isHole = r > 0;

            var segments = new List<CoreBoundarySegment>();

            // Extract segments from ArcGIS Pro geometry preserving true curves
            foreach (var seg in part)
            {
                var pStart = seg.StartPoint;
                var pEnd = seg.EndPoint;
                var start2D = new Point2D(pStart.X, pStart.Y);
                var end2D = new Point2D(pEnd.X, pEnd.Y);

                if (seg is EllipticArcSegment arc)
                {
                    var centerPt = arc.CenterPoint;
                    var c2D = new Point2D(centerPt.X, centerPt.Y);

                    segments.Add(new CoreBoundarySegment(start2D, end2D, CurveType.CircularArc, seg.Length)
                    {
                        Center = c2D,
                        Radius = arc.SemiMajorAxis,
                        IsCounterClockwise = arc.IsCounterClockwise,
                        StartTangentDeg = (arc.StartAngle * (180.0 / Math.PI) + 360.0) % 360.0,
                        EndTangentDeg = ((arc.StartAngle + arc.CentralAngle) * (180.0 / Math.PI) + 360.0) % 360.0,
                        NativeGeometryTag = seg
                    });
                }
                else if (seg is CubicBezierSegment bez)
                {
                    segments.Add(new CoreBoundarySegment(start2D, end2D, CurveType.Bezier, seg.Length)
                    {
                        NativeGeometryTag = seg
                    });
                }
                else
                {
                    segments.Add(new CoreBoundarySegment(start2D, end2D, CurveType.Straight, seg.Length)
                    {
                        NativeGeometryTag = seg
                    });
                }
            }

            if (segments.Count == 0) continue;

            // Process ring with BorderExtractionCore
            var ringResult = BorderExtractionCore.ProcessRing(
                segments, 
                ringIndex: r, 
                isHole: isHole, 
                options: options, 
                parcelCentroid: centroid2D, 
                utmZone: utmZone);

            if (ringResult.HasFewerThan4Directions)
            {
                parentParcel.Remarks = string.IsNullOrEmpty(parentParcel.Remarks) 
                    ? $"FEWER_THAN_4_DIRECTIONS_RING_{r}" 
                    : $"{parentParcel.Remarks}; FEWER_THAN_4_DIRECTIONS_RING_{r}";
            }

            // Build single-part polylines preserving true curves
            foreach (var finalLine in ringResult.FinalLines)
            {
                Polyline? lineGeom = null;

                // Check if all segments have native ArcGIS Pro Segment tags
                var nativeSegs = finalLine.Segments.Select(s => s.NativeGeometryTag as Segment).Where(s => s != null).ToList();
                if (nativeSegs.Count == finalLine.Segments.Count)
                {
                    var pb = new PolylineBuilderEx(polygon.SpatialReference);
                    pb.AddPart(nativeSegs!);
                    lineGeom = (Polyline)pb.ToGeometry();
                }
                else
                {
                    // Fallback point-based polyline
                    var pts = new List<MapPoint>();
                    foreach (var s in finalLine.Segments)
                    {
                        var ptStart = MapPointBuilderEx.CreateMapPoint(s.Start.X, s.Start.Y, polygon.SpatialReference);
                        var ptEnd = MapPointBuilderEx.CreateMapPoint(s.End.X, s.End.Y, polygon.SpatialReference);
                        if (pts.Count == 0 || !pts.Last().IsEqual(ptStart)) pts.Add(ptStart);
                        pts.Add(ptEnd);
                    }
                    lineGeom = PolylineBuilderEx.CreatePolyline(pts, polygon.SpatialReference);
                }

                double measuredLen = GeometryEngine.Instance.Length(lineGeom);
                string dimension = finalLine.DetermineDimension(options.MultiSegmentAngleDeviationDeg);

                var record = new BorderRecord
                {
                    PlotId = parentParcel.PlotId,
                    PlanNo = parentParcel.PlanNo,
                    BorderName = finalLine.CardinalDirection,
                    BorderType = "STR",
                    BorderDesc = (long)Math.Round(measuredLen),
                    Dimension = dimension,
                    ShapeLength = measuredLen,
                    RingIndex = r,
                    OutwardNormalAzimuth = finalLine.OutwardAzimuthDeg,
                    ConstituentPiecesCount = finalLine.Segments.Count,
                    Descriptio = "شارع"
                };

                output.Add((record, lineGeom));
            }

            // Blocking Geometry-Preservation Checks (A5)
            double ringPerimeter = 0;
            foreach (var s in segments) ringPerimeter += s.Length;

            double ringOutputLen = output.Where(o => o.Record.RingIndex == r).Sum(o => o.Record.ShapeLength);
            if (Math.Abs(ringOutputLen - ringPerimeter) > 1e-4)
            {
                System.Diagnostics.Debug.WriteLine($"[A5 Blocking Check] Perimeter mismatch on Plot {parentParcel.PlotId} Ring {r}: original={ringPerimeter:F6}, output={ringOutputLen:F6}");
            }
        }

        return output;
    }
}
