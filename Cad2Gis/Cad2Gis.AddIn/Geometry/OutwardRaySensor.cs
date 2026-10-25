using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ArcGIS.Core.Geometry;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Lookups;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.AddIn.Geometry;

public class NeighborTouchInfo
{
    public string BorderType { get; set; } = "STR";
    public string DescriptionPart { get; set; } = "شارع";
    public double ContactLength { get; set; }
    public bool IsMain { get; set; }
    public string NeighborPlotId { get; set; } = string.Empty;
}

public class ParcelSpatialItem
{
    public ParcelRecord Parcel { get; }
    public Polygon Geometry { get; }
    public Box2D Envelope { get; }
    public Segment2D[] OuterSegments { get; }

    public ParcelSpatialItem(ParcelRecord parcel, Polygon geometry, Box2D envelope, Segment2D[] outerSegments)
    {
        Parcel = parcel;
        Geometry = geometry;
        Envelope = envelope;
        OuterSegments = outerSegments;
    }
}

public class Step4SpatialIndex
{
    public SpatialGrid2D<ParcelSpatialItem> Grid { get; }
    public Dictionary<long, ParcelSpatialItem> ItemsById { get; }
    public Dictionary<long, Polygon> BufferedNeighbors { get; }
    public List<Segment2D> RoadSegments { get; }

    public Step4SpatialIndex(
        IEnumerable<(ParcelRecord Parcel, Polygon Geometry)> parcels,
        IReadOnlyList<Polyline>? roadLines = null,
        double cellSize = 100.0)
    {
        Grid = new SpatialGrid2D<ParcelSpatialItem>(cellSize);
        ItemsById = new Dictionary<long, ParcelSpatialItem>();
        BufferedNeighbors = new Dictionary<long, Polygon>();
        RoadSegments = new List<Segment2D>();

        foreach (var (p, poly) in parcels)
        {
            if (poly == null || poly.IsEmpty) continue;
            var ext = poly.Extent;
            var box = new Box2D(ext.XMin, ext.YMin, ext.XMax, ext.YMax);

            // Precompute outer segments using real segments where available
            var segList = new List<Segment2D>();
            try
            {
                if (poly.Parts.Count > 0)
                {
                    foreach (var seg in poly.Parts[0])
                    {
                        segList.Add(new Segment2D(
                            new Point2D(seg.StartPoint.X, seg.StartPoint.Y),
                            new Point2D(seg.EndPoint.X, seg.EndPoint.Y),
                            elementId: !string.IsNullOrEmpty(p.PlotId) ? p.PlotId : p.TemporaryParcelClass,
                            elementType: p.Class == ParcelClass.Main ? "Parcel" : "SubParcel"
                        ));
                    }
                }
            }
            catch
            {
                var pts = poly.Points;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    segList.Add(new Segment2D(
                        new Point2D(pts[i].X, pts[i].Y),
                        new Point2D(pts[i + 1].X, pts[i + 1].Y),
                        elementId: !string.IsNullOrEmpty(p.PlotId) ? p.PlotId : p.TemporaryParcelClass,
                        elementType: p.Class == ParcelClass.Main ? "Parcel" : "SubParcel"
                    ));
                }
            }

            var item = new ParcelSpatialItem(p, poly, box, segList.ToArray());
            ItemsById[p.InternalId] = item;
            Grid.Insert(item, box);
        }

        if (roadLines != null)
        {
            foreach (var rLine in roadLines)
            {
                if (rLine == null || rLine.IsEmpty) continue;
                try
                {
                    foreach (var part in rLine.Parts)
                    {
                        foreach (var seg in part)
                        {
                            RoadSegments.Add(new Segment2D(
                                new Point2D(seg.StartPoint.X, seg.StartPoint.Y),
                                new Point2D(seg.EndPoint.X, seg.EndPoint.Y),
                                elementId: "RoadEdge",
                                elementType: "Road"
                            ));
                        }
                    }
                }
                catch
                {
                    var pts = rLine.Points;
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        RoadSegments.Add(new Segment2D(
                            new Point2D(pts[i].X, pts[i].Y),
                            new Point2D(pts[i + 1].X, pts[i + 1].Y),
                            elementId: "RoadEdge",
                            elementType: "Road"
                        ));
                    }
                }
            }
        }
    }

    public Polygon GetOrCreateBuffered(long parcelId, Polygon poly, double snapTolerance)
    {
        if (BufferedNeighbors.TryGetValue(parcelId, out var cached))
            return cached;

        var buffered = GeometryEngine.Instance.Buffer(poly, snapTolerance) as Polygon;
        BufferedNeighbors[parcelId] = buffered ?? poly;
        return BufferedNeighbors[parcelId];
    }
}

public static class OutwardRaySensor
{
    public static void SenseAndEnrichBorders(
        ParcelRecord currentParcel,
        Dictionary<int, (BorderRecord Record, Polyline Geometry)> borders,
        IReadOnlyList<(ParcelRecord Parcel, Polygon Geometry)> allParcels,
        IReadOnlyList<Polyline>? roadLines = null,
        WidthInferenceOptions? inferenceOptions = null,
        double minSharedSegmentLength = 0.5,
        Action<string>? qaLog = null,
        Step4SpatialIndex? spatialIndex = null,
        Stopwatch? swNeighborCheck = null,
        Stopwatch? swWidthInference = null)
    {
        var options = inferenceOptions ?? new WidthInferenceOptions();
        var index = spatialIndex ?? new Step4SpatialIndex(allParcels, roadLines, options.MaxSearchDistance);
        index.ItemsById.TryGetValue(currentParcel.InternalId, out var currentItem);
        var currentGeom = currentItem?.Geometry ?? allParcels.FirstOrDefault(p => p.Parcel.InternalId == currentParcel.InternalId).Geometry;

        foreach (var kvp in borders)
        {
            int cardinal = kvp.Key;
            var borderRec = kvp.Value.Record;
            var borderGeom = kvp.Value.Geometry;

            var detectedNeighbors = new List<NeighborTouchInfo>();

            // 1. Check adjoining touch neighbors with eligible shared segment length >= 0.5m
            swNeighborCheck?.Start();

            SpatialReference? activeSr = borderGeom.SpatialReference;
            if (activeSr == null)
            {
                activeSr = currentGeom?.SpatialReference ?? allParcels.FirstOrDefault(p => p.Geometry?.SpatialReference != null).Geometry?.SpatialReference;
                if (activeSr != null)
                {
                    var pb = new PolylineBuilderEx(borderGeom) { SpatialReference = activeSr };
                    borderGeom = (Polyline)pb.ToGeometry();
                }
            }

            // Check linear units of coordinate system
            double effectiveMinSharedLength = minSharedSegmentLength;
            double snapTolerance = 0.05;
            if (activeSr?.Unit != null && activeSr.Unit.ConversionFactor > 0)
            {
                effectiveMinSharedLength = minSharedSegmentLength / activeSr.Unit.ConversionFactor;
                snapTolerance = 0.05 / activeSr.Unit.ConversionFactor;
            }

            // Spatial filter for neighbor candidates: query index with border extent expanded by snapTolerance
            var borderExt = borderGeom.Extent;
            var borderBox = new Box2D(borderExt.XMin, borderExt.YMin, borderExt.XMax, borderExt.YMax).Expand(snapTolerance);
            var neighborCandidates = index.Grid.Query(borderBox);

            foreach (var otherItem in neighborCandidates)
            {
                var other = otherItem.Parcel;
                if (other.InternalId == currentParcel.InternalId) continue;
                if (!string.IsNullOrEmpty(other.PlotId) &&
                    string.Equals(other.PlotId, currentParcel.PlotId, StringComparison.OrdinalIgnoreCase)) continue;

                var otherGeom = otherItem.Geometry;
                if (otherGeom == null || otherGeom.IsEmpty) continue;

                try
                {
                    // Defensively align spatial references to guarantee GeometryEngine compatibility
                    if (activeSr != null && (otherGeom.SpatialReference == null || !activeSr.IsEqual(otherGeom.SpatialReference)))
                    {
                        var b = new PolygonBuilderEx(otherGeom) { SpatialReference = activeSr };
                        otherGeom = (Polygon)b.ToGeometry();
                    }

                    // Buffer neighbor polygon by snap tolerance (0.05m) to catch coincident CAD boundaries (cached per parcel)
                    var neighborBuffered = index.GetOrCreateBuffered(other.InternalId, otherGeom, snapTolerance);
                    if (neighborBuffered == null || neighborBuffered.IsEmpty) continue;

                    if (GeometryEngine.Instance.Intersects(borderGeom, neighborBuffered))
                    {
                        var sharedGeom = GeometryEngine.Instance.Intersection(borderGeom, neighborBuffered);
                        double sharedLength = 0.0;
                        if (sharedGeom != null && !sharedGeom.IsEmpty)
                        {
                            sharedLength = GeometryEngine.Instance.Length(sharedGeom);
                        }

                        // Shared Segment Rule (Section 2.2):
                        // Only eligible if sharedLength >= 0.5 meters (effectiveMinSharedLength)
                        if (sharedLength >= (effectiveMinSharedLength - 1e-4))
                        {
                            bool isMainNeighbor = other.Class == ParcelClass.Main || other.ParcelTyp == 1;
                            string bType;
                            string descPart;

                            if (isMainNeighbor)
                            {
                                // Section 3.2: MAIN neighbor -> PLT directly (no text matching)
                                bType = "PLT";
                                descPart = $"قطعة رقم {other.PlotId}";
                            }
                            else
                            {
                                // Section 3.2: SUB neighbor -> check REMARKS then USAGE via Keywords table
                                string? matchedCode = null;
                                if (!string.IsNullOrWhiteSpace(other.Remarks))
                                {
                                    var m = MunicipalLookups.MatchBorderTypeFromKeywords(other.Remarks, "REMARKS");
                                    if (!string.IsNullOrEmpty(m.Code) && MunicipalLookups.IsOfficialBorderType(m.Code))
                                        matchedCode = m.Code;
                                }
                                if (string.IsNullOrEmpty(matchedCode) && !string.IsNullOrWhiteSpace(other.Usage))
                                {
                                    var m = MunicipalLookups.MatchBorderTypeFromKeywords(other.Usage, "USAGE");
                                    if (!string.IsNullOrEmpty(m.Code) && MunicipalLookups.IsOfficialBorderType(m.Code))
                                        matchedCode = m.Code;
                                }
                                if (string.IsNullOrEmpty(matchedCode) && string.Equals(other.Usage, "UTL", StringComparison.OrdinalIgnoreCase))
                                {
                                    matchedCode = "UTL";
                                }

                                if (string.IsNullOrEmpty(matchedCode))
                                {
                                    qaLog?.Invoke($"Plot {currentParcel.PlotId} border {cardinal}: Shared segment of {sharedLength:F2}m with SUB neighbor (Plot {other.PlotId}) has no approved BORDERTYPE match for text '{other.Remarks}'. Left blank.");
                                }

                                bType = matchedCode ?? string.Empty;
                                descPart = !string.IsNullOrWhiteSpace(other.Remarks)
                                    ? other.Remarks
                                    : (!string.IsNullOrWhiteSpace(other.SubType) ? other.SubType : other.PlotId);
                            }

                            detectedNeighbors.Add(new NeighborTouchInfo
                            {
                                BorderType = bType,
                                DescriptionPart = descPart,
                                ContactLength = sharedLength,
                                IsMain = isMainNeighbor,
                                NeighborPlotId = other.PlotId
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OutwardRaySensor] Intersect check skipped for neighbor {other.PlotId}: {ex.Message}");
                }
            }
            swNeighborCheck?.Stop();

            if (detectedNeighbors.Count > 0)
            {
                // Section 2.4: Multiple neighbors on the same border
                // Border remains a single line. BORDERTYPE is chosen from the neighbor with longest shared segment.
                var sortedNeighbors = detectedNeighbors
                    .OrderByDescending(n => n.ContactLength)
                    .ThenBy(n => n.NeighborPlotId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                borderRec.BorderType = sortedNeighbors[0].BorderType;
                if (sortedNeighbors.Count > 1)
                {
                    var altTypes = sortedNeighbors.Skip(1).Where(n => !string.IsNullOrEmpty(n.BorderType) && n.BorderType != borderRec.BorderType).Select(n => $"{n.NeighborPlotId}: {n.BorderType}").ToList();
                    if (altTypes.Count > 0)
                    {
                        qaLog?.Invoke($"Plot {currentParcel.PlotId} border {cardinal} touches multiple neighbors: BORDERTYPE assigned '{borderRec.BorderType}' from longest shared segment ({sortedNeighbors[0].ContactLength:F2}m). Other types: {string.Join(", ", altTypes)}.");
                    }
                }

                // Section 4: Narrative DESCRIPTO formatting
                // Group MAIN parcels: "قطعة رقم 12 و13", followed by SUB parcel descriptions separated by " و "
                var mainPlotIds = detectedNeighbors
                    .Where(n => n.IsMain && !string.IsNullOrWhiteSpace(n.NeighborPlotId))
                    .OrderBy(n => n.NeighborPlotId, StringComparer.OrdinalIgnoreCase)
                    .Select(n => n.NeighborPlotId)
                    .Distinct()
                    .ToList();

                string? mainGroupDesc = null;
                if (mainPlotIds.Count == 1)
                {
                    mainGroupDesc = $"قطعة رقم {mainPlotIds[0]}";
                }
                else if (mainPlotIds.Count > 1)
                {
                    mainGroupDesc = $"قطعة رقم {string.Join(" و", mainPlotIds)}";
                }

                var subParts = detectedNeighbors
                    .Where(n => !n.IsMain)
                    .Select(n => n.DescriptionPart)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Distinct()
                    .ToList();

                if (mainGroupDesc != null && subParts.Count > 0)
                {
                    borderRec.Descriptio = $"{mainGroupDesc} و {string.Join(" و ", subParts)}";
                }
                else if (mainGroupDesc != null)
                {
                    borderRec.Descriptio = mainGroupDesc;
                }
                else
                {
                    borderRec.Descriptio = string.Join(" و ", subParts);
                }

                borderRec.IsInferred = false;
                borderRec.IsUnknown = false;
            }
            else
            {
                // 2. No adjoining parcel/sub-parcel found -> Perform Width Inference
                // Build obstacle segments lazily and locally from parcels in search range
                swWidthInference?.Start();

                var pts = borderGeom.Points;
                if (pts.Count >= 2)
                {
                    var obstacleSegments = new List<Segment2D>();
                    var searchBox = borderBox.Expand(options.MaxSearchDistance);
                    var obstacleCandidates = index.Grid.Query(searchBox);

                    foreach (var otherItem in obstacleCandidates)
                    {
                        if (otherItem.Parcel.InternalId == currentParcel.InternalId) continue;
                        if (otherItem.Geometry == null || otherItem.Geometry.IsEmpty) continue;

                        // Minimum facing gap: envelope check first to skip exact distance
                        if (currentGeom != null && !currentGeom.IsEmpty && currentItem != null)
                        {
                            double boxDist = currentItem.Envelope.DistanceTo(otherItem.Envelope);
                            if (boxDist < (options.MinFacingGap - 1e-4))
                            {
                                try
                                {
                                    double dist = GeometryEngine.Instance.Distance(currentGeom, otherItem.Geometry);
                                    if (dist < (options.MinFacingGap - 1e-4))
                                    {
                                        continue; // Touching or closer than 1m -> neighbor, not facing parcel across street!
                                    }
                                }
                                catch { }
                            }
                        }

                        if (otherItem.OuterSegments != null && otherItem.OuterSegments.Length > 0)
                        {
                            obstacleSegments.AddRange(otherItem.OuterSegments);
                        }
                    }

                    if (index.RoadSegments.Count > 0)
                    {
                        obstacleSegments.AddRange(index.RoadSegments);
                    }

                    Segment2D seg;
                    if (pts.Count > 2)
                    {
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
                swWidthInference?.Stop();
            }
        }
    }
}
