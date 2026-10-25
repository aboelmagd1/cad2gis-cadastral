using System;
using System.Collections.Generic;

namespace Cad2Gis.Core.Geometry;

/// <summary>
/// 2D Axis-Aligned Bounding Box for spatial indexing.
/// </summary>
public readonly struct Box2D : IEquatable<Box2D>
{
    public readonly double MinX;
    public readonly double MinY;
    public readonly double MaxX;
    public readonly double MaxY;

    public Box2D(double minX, double minY, double maxX, double maxY)
    {
        MinX = Math.Min(minX, maxX);
        MinY = Math.Min(minY, maxY);
        MaxX = Math.Max(minX, maxX);
        MaxY = Math.Max(minY, maxY);
    }

    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    public Box2D Expand(double delta) =>
        new Box2D(MinX - delta, MinY - delta, MaxX + delta, MaxY + delta);

    public Box2D Expand(double dx, double dy) =>
        new Box2D(MinX - dx, MinY - dy, MaxX + dx, MaxY + dy);

    public bool Intersects(Box2D other) =>
        MinX <= other.MaxX && MaxX >= other.MinX &&
        MinY <= other.MaxY && MaxY >= other.MinY;

    public bool Contains(double x, double y) =>
        x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    /// <summary>
    /// Computes minimum Euclidean distance between two bounding boxes (0 if overlapping).
    /// </summary>
    public double DistanceTo(Box2D other)
    {
        double dx = Math.Max(0.0, Math.Max(MinX - other.MaxX, other.MinX - MaxX));
        double dy = Math.Max(0.0, Math.Max(MinY - other.MaxY, other.MinY - MaxY));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public bool Equals(Box2D other) =>
        Math.Abs(MinX - other.MinX) < 1e-9 &&
        Math.Abs(MinY - other.MinY) < 1e-9 &&
        Math.Abs(MaxX - other.MaxX) < 1e-9 &&
        Math.Abs(MaxY - other.MaxY) < 1e-9;

    public override bool Equals(object? obj) => obj is Box2D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(MinX, MinY, MaxX, MaxY);
    public override string ToString() => $"Box2D({MinX:F2}, {MinY:F2}, {MaxX:F2}, {MaxY:F2})";
}

/// <summary>
/// Fast uniform 2D grid spatial index for parcel envelopes and boundary segments.
/// O(1) cell insertion and candidate queries for geometric intersections.
/// </summary>
public class SpatialGrid2D<T>
{
    private readonly double _cellSize;
    private readonly double _invCellSize;
    private readonly Dictionary<(int cx, int cy), List<SpatialItem>> _grid = new();
    private readonly List<SpatialItem> _allItems = new();

    private sealed class SpatialItem
    {
        public T Value { get; }
        public Box2D Box { get; }
        public int Id { get; }

        public SpatialItem(T value, Box2D box, int id)
        {
            Value = value;
            Box = box;
            Id = id;
        }
    }

    public SpatialGrid2D(double cellSize = 100.0)
    {
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");
        _cellSize = cellSize;
        _invCellSize = 1.0 / cellSize;
    }

    public double CellSize => _cellSize;
    public int Count => _allItems.Count;

    public void Insert(T item, Box2D box)
    {
        int id = _allItems.Count;
        var spatialItem = new SpatialItem(item, box, id);
        _allItems.Add(spatialItem);

        int minCx = (int)Math.Floor(box.MinX * _invCellSize);
        int maxCx = (int)Math.Floor(box.MaxX * _invCellSize);
        int minCy = (int)Math.Floor(box.MinY * _invCellSize);
        int maxCy = (int)Math.Floor(box.MaxY * _invCellSize);

        for (int cx = minCx; cx <= maxCx; cx++)
        {
            for (int cy = minCy; cy <= maxCy; cy++)
            {
                var cellKey = (cx, cy);
                if (!_grid.TryGetValue(cellKey, out var list))
                {
                    list = new List<SpatialItem>();
                    _grid[cellKey] = list;
                }
                list.Add(spatialItem);
            }
        }
    }

    /// <summary>
    /// Queries the grid for all items whose bounding box intersects queryBox.
    /// Deduplicates items across multiple overlapping grid cells.
    /// </summary>
    public List<T> Query(Box2D queryBox)
    {
        var results = new List<T>();
        if (_allItems.Count == 0) return results;

        int minCx = (int)Math.Floor(queryBox.MinX * _invCellSize);
        int maxCx = (int)Math.Floor(queryBox.MaxX * _invCellSize);
        int minCy = (int)Math.Floor(queryBox.MinY * _invCellSize);
        int maxCy = (int)Math.Floor(queryBox.MaxY * _invCellSize);

        var visitedIds = new HashSet<int>();

        for (int cx = minCx; cx <= maxCx; cx++)
        {
            for (int cy = minCy; cy <= maxCy; cy++)
            {
                if (_grid.TryGetValue((cx, cy), out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        if (visitedIds.Add(item.Id))
                        {
                            if (item.Box.Intersects(queryBox))
                            {
                                results.Add(item.Value);
                            }
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Brute-force query for verification and testing.
    /// </summary>
    public List<T> QueryBruteForce(Box2D queryBox)
    {
        var results = new List<T>();
        for (int i = 0; i < _allItems.Count; i++)
        {
            if (_allItems[i].Box.Intersects(queryBox))
            {
                results.Add(_allItems[i].Value);
            }
        }
        return results;
    }

    public void Clear()
    {
        _grid.Clear();
        _allItems.Clear();
    }
}
