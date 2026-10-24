using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Geometry;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.AddIn.Geometry;

public class NeighborTouchInfo
{
    public string BorderType { get; set; } = "STR";
    public string DescriptionPart { get; set; } = "شارع";
    public double ContactLength { get; set; }
}

public static class OutwardRaySensor
{
    public static void SenseAndEnrichBorders(
        ParcelRecord currentParcel,
        Dictionary<int, (BorderRecord Record, Polyline Geometry)> borders,
        IReadOnlyList<(ParcelRecord Parcel, Polygon Geometry)> allParcels,
        IReadOnlyList<Polyline>? roadLines = null,
        WidthInferenceOptions? inferenceOptions = null,
        double touchBufferDistance = 0.5)
    {
        var options = inferenceOptions ?? new WidthInferenceOptions();

        // Build obstacle segments for width inference
        var obstacleSegments = new List<Segment2D>();
        foreach (var other in allParcels)
        {
            if (other.Parcel.InternalId == currentParcel.InternalId) continue;
            if (other.Geometry == null || other.Geometry.IsEmpty) continue;

            var pts = other.Geometry.Points;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                obstacleSegments.Add(new Segment2D(
                    new Point2D(pts[i].X, pts[i].Y),
                    new Point2D(pts[i + 1].X, pts[i + 1].Y),
                    elementId: !string.IsNullOrEmpty(other.Parcel.PlotId) ? other.Parcel.PlotId : other.Parcel.TemporaryParcelClass,
                    elementType: other.Parcel.Class == ParcelClass.Main ? "Parcel" : "SubParcel"
                ));
            }
        }

        if (roadLines != null)
        {
            foreach (var rLine in roadLines)
            {
                var pts = rLine.Points;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    obstacleSegments.Add(new Segment2D(
                        new Point2D(pts[i].X, pts[i].Y),
                        new Point2D(pts[i + 1].X, pts[i + 1].Y),
                        elementId: "RoadEdge",
                        elementType: "Road"
                    ));
                }
            }
        }

        foreach (var kvp in borders)
        {
            int cardinal = kvp.Key;
            var borderRec = kvp.Value.Record;
            var borderGeom = kvp.Value.Geometry;

            var detectedNeighbors = new List<NeighborTouchInfo>();

            // 1. Check adjoining touch neighbors
            SpatialReference? activeSr = borderGeom.SpatialReference;
            if (activeSr == null)
            {
                activeSr = allParcels.FirstOrDefault(p => p.Geometry?.SpatialReference != null).Geometry?.SpatialReference;
                if (activeSr != null)
                {
                    var pb = new PolylineBuilderEx(borderGeom) { SpatialReference = activeSr };
                    borderGeom = (Polyline)pb.ToGeometry();
                }
            }

            Polygon? searchArea = null;
            try
            {
                searchArea = GeometryEngine.Instance.Buffer(borderGeom, touchBufferDistance) as Polygon;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OutwardRaySensor] Buffer failed on border {cardinal}: {ex.Message}");
            }

            if (searchArea != null && !searchArea.IsEmpty)
            {
                var targetSr = searchArea.SpatialReference ?? activeSr;

                foreach (var other in allParcels)
                {
                    if (other.Parcel.InternalId == currentParcel.InternalId) continue;
                    if (!string.IsNullOrEmpty(other.Parcel.PlotId) &&
                        string.Equals(other.Parcel.PlotId, currentParcel.PlotId, StringComparison.OrdinalIgnoreCase)) continue;

                    var otherGeom = other.Geometry;
                    if (otherGeom == null || otherGeom.IsEmpty) continue;

                    try
                    {
                        // Defensively align spatial references to guarantee GeometryEngine compatibility
                        if (targetSr != null && (otherGeom.SpatialReference == null || !targetSr.IsEqual(otherGeom.SpatialReference)))
                        {
                            var b = new PolygonBuilderEx(otherGeom) { SpatialReference = targetSr };
                            otherGeom = (Polygon)b.ToGeometry();
                        }
                        else if (targetSr == null && otherGeom.SpatialReference != null)
                        {
                            var b = new PolygonBuilderEx(searchArea) { SpatialReference = otherGeom.SpatialReference };
                            searchArea = (Polygon)b.ToGeometry();
                            targetSr = otherGeom.SpatialReference;
                        }

                        if (GeometryEngine.Instance.Intersects(searchArea, otherGeom))
                        {
                            var intersection = GeometryEngine.Instance.Intersection(searchArea, otherGeom);
                            double contactLen = GeometryEngine.Instance.Length(intersection);

                            if (contactLen > 0.1 || !intersection.IsEmpty)
                            {
                                if (other.Parcel.Class == ParcelClass.Main)
                                {
                                    detectedNeighbors.Add(new NeighborTouchInfo
                                    {
                                        BorderType = "PLT",
                                        DescriptionPart = $"قطعة رقم {other.Parcel.PlotId}",
                                        ContactLength = contactLen
                                    });
                                }
                                else
                                {
                                    // Sub-parcel neighbor (Garden, Corridor, Service, Utility)
                                    string subDesc = !string.IsNullOrWhiteSpace(other.Parcel.Remarks) 
                                        ? other.Parcel.Remarks 
                                        : (!string.IsNullOrWhiteSpace(other.Parcel.SubType) ? other.Parcel.SubType : "مرفق");
                                    string bType = other.Parcel.Usage switch
                                    {
                                        "PAK" => "PAK",
                                        "SRV" => "SRV",
                                        "UTL" => "UTL",
                                        _ => "SRV"
                                    };

                                    detectedNeighbors.Add(new NeighborTouchInfo
                                    {
                                        BorderType = bType,
                                        DescriptionPart = subDesc,
                                        ContactLength = contactLen
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OutwardRaySensor] Intersect check skipped for neighbor {other.Parcel.PlotId}: {ex.Message}");
                    }
                }
            }

            if (detectedNeighbors.Count > 0)
            {
                // Neighbor parcel or sub-parcel found
                var parts = detectedNeighbors.Select(n => n.DescriptionPart).Distinct().ToList();
                borderRec.BorderType = detectedNeighbors[0].BorderType;
                borderRec.Descriptio = string.Join("، ", parts);
                borderRec.IsInferred = false;

                if (parts.Count > 1)
                {
                    borderRec.Dimension = "Multi";
                }
            }
            else
            {
                // 2. No adjoining parcel/sub-parcel found -> Perform Width Inference
                // Anchor rays at the midpoint (by length) of its longest constituent piece, using that piece's outward normal (Section A6)
                var pts = borderGeom.Points;
                if (pts.Count >= 2)
                {
                    Segment2D seg;
                    if (pts.Count > 2)
                    {
                        // Final line has bends: find longest constituent piece by length
                        double maxLen = -1;
                        int bestIdx = 0;
                        for (int i = 0; i < pts.Count - 1; i++)
                        {
                            double dx = pts[i + 1].X - pts[i].X;
                            double dy = pts[i + 1].Y - pts[i].Y;
                            double sLen = Math.Sqrt(dx * dx + dy * dy);
                            if (sLen > maxLen)
                            {
                                maxLen = sLen;
                                bestIdx = i;
                            }
                        }
                        seg = new Segment2D(new Point2D(pts[bestIdx].X, pts[bestIdx].Y), new Point2D(pts[bestIdx + 1].X, pts[bestIdx + 1].Y));
                    }
                    else
                    {
                        seg = new Segment2D(new Point2D(pts[0].X, pts[0].Y), new Point2D(pts[pts.Count - 1].X, pts[pts.Count - 1].Y));
                    }

                    // Outward normal vector:
                    // 1 = North: (0, 1)
                    // 2 = East:  (1, 0)
                    // 3 = South: (0, -1)
                    // 4 = West:  (-1, 0)
                    Point2D normal;
                    if (borderRec.OutwardNormalAzimuth > 0.001)
                    {
                        double azRad = borderRec.OutwardNormalAzimuth * (Math.PI / 180.0);
                        normal = new Point2D(Math.Sin(azRad), Math.Cos(azRad));
                    }
                    else
                    {
                        normal = borderRec.BorderName switch
                        {
                            1 => new Point2D(0, 1),   // North
                            2 => new Point2D(1, 0),   // East
                            3 => new Point2D(0, -1),  // South
                            4 => new Point2D(-1, 0),  // West
                            _ => new Point2D(0, 1)
                        };
                    }

                    WidthInferenceService.InferBorderWidth(borderRec, seg, normal, obstacleSegments, options);
                }
                else
                {
                    borderRec.IsInferred = true;
                    borderRec.IsUnknown = true;
                    borderRec.ReviewStatus = "RequiresReview";
                    borderRec.Descriptio = "";
                    borderRec.BorderType = "";
                }
            }
        }
    }
}
