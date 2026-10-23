using System.Collections.Generic;
using Xunit;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class WidthInferenceAdvancedTests
{
    private static Segment2D CreateDimensionSegment() =>
        new(new Point2D(0, 0), new Point2D(20, 0));

    private static Point2D OutwardNormal() =>
        new(0, 1);

    [Fact]
    public void Classify_RayHitAt6M_ReturnsPassageLNE()
    {
        // Facing obstacle at y = 6.0m
        var border = new BorderRecord { InternalId = 1, PlotId = "101" };
        var dimSegment = CreateDimensionSegment();
        var normal = OutwardNormal();
        var obstacles = new List<Segment2D>
        {
            new(new Point2D(-10, 6.0), new Point2D(30, 6.0), "FacingParcel_1", "Parcel")
        };
        var options = new WidthInferenceOptions
        {
            PassageStreetThreshold = 10.0,
            MaxSearchDistance = 100.0,
            PassageBorderType = "LNE",
            StreetBorderType = "STR"
        };

        WidthInferenceService.InferBorderWidth(border, dimSegment, normal, obstacles, options);

        Assert.Equal("LNE", border.BorderType);
        Assert.Equal(6.0, border.MeasuredWidth);
        Assert.True(border.IsInferred);
        Assert.Contains("ممر", border.Descriptio);
        Assert.NotEmpty(border.RayLengths);
    }

    [Fact]
    public void Classify_RayHitAt12M_ReturnsStreetSTR()
    {
        // Facing obstacle at y = 12.0m
        var border = new BorderRecord { InternalId = 2, PlotId = "102" };
        var dimSegment = CreateDimensionSegment();
        var normal = OutwardNormal();
        var obstacles = new List<Segment2D>
        {
            new(new Point2D(-10, 12.0), new Point2D(30, 12.0), "FacingParcel_2", "Parcel")
        };
        var options = new WidthInferenceOptions
        {
            PassageStreetThreshold = 10.0,
            MaxSearchDistance = 100.0,
            PassageBorderType = "LNE",
            StreetBorderType = "STR"
        };

        WidthInferenceService.InferBorderWidth(border, dimSegment, normal, obstacles, options);

        Assert.Equal("STR", border.BorderType);
        Assert.Equal(12.0, border.MeasuredWidth);
        Assert.True(border.IsInferred);
        Assert.Contains("شارع", border.Descriptio);
    }

    [Fact]
    public void Classify_RayHitAtExactly10M_ReturnsStreetSTR()
    {
        // Facing obstacle at y = 10.0m (10 m or more -> street)
        var border = new BorderRecord { InternalId = 3, PlotId = "103" };
        var dimSegment = CreateDimensionSegment();
        var normal = OutwardNormal();
        var obstacles = new List<Segment2D>
        {
            new(new Point2D(-10, 10.0), new Point2D(30, 10.0), "FacingParcel_3", "Parcel")
        };
        var options = new WidthInferenceOptions
        {
            PassageStreetThreshold = 10.0,
            MaxSearchDistance = 100.0
        };

        WidthInferenceService.InferBorderWidth(border, dimSegment, normal, obstacles, options);

        Assert.Equal("STR", border.BorderType);
        Assert.Equal(10.0, border.MeasuredWidth);
    }

    [Fact]
    public void Classify_NoHitWithin100M_ReturnsUnknownWithNoWidth()
    {
        // No obstacle within 100m (e.g. at 150m or empty)
        var border = new BorderRecord { InternalId = 4, PlotId = "104" };
        var dimSegment = CreateDimensionSegment();
        var normal = OutwardNormal();
        var obstacles = new List<Segment2D>
        {
            new(new Point2D(-10, 150.0), new Point2D(30, 150.0), "FarParcel", "Parcel")
        };
        var options = new WidthInferenceOptions
        {
            MaxSearchDistance = 100.0
        };

        WidthInferenceService.InferBorderWidth(border, dimSegment, normal, obstacles, options);

        Assert.Equal("UNKNOWN", border.BorderType);
        Assert.Null(border.MeasuredWidth);
        Assert.Equal("UNKNOWN", border.Descriptio);
        Assert.True(border.IsInferred); // Must be flagged inferred to appear in review table
    }

    [Fact]
    public void Classify_MedianOutlierFiltering_SelectsMedianCorrectly()
    {
        // Obstacle segment spanning first half near y=12, second half stepping out to y=60
        // Dimension segment from x=0 to 20: rays are at x=5 (frac=0.25), x=10 (frac=0.5), x=15 (frac=0.75)
        var border = new BorderRecord { InternalId = 5, PlotId = "105" };
        var dimSegment = CreateDimensionSegment();
        var normal = OutwardNormal();
        var obstacles = new List<Segment2D>
        {
            // Covers x=0 to x=12 at y=12 (rays at x=5 and x=10 hit this at 12m)
            new(new Point2D(-5, 12.0), new Point2D(12, 12.0), "Obstacle_Near", "Parcel"),
            // Covers x=12 to x=25 at y=60 (ray at x=15 hits this at 60m - outlier)
            new(new Point2D(12, 60.0), new Point2D(25, 60.0), "Obstacle_Far", "Parcel")
        };
        var options = new WidthInferenceOptions
        {
            PassageStreetThreshold = 10.0,
            MaxSearchDistance = 100.0,
            RayCount = 3
        };

        WidthInferenceService.InferBorderWidth(border, dimSegment, normal, obstacles, options);

        // Rays hit at 12.0, 12.0, 60.0 -> median is 12.0 -> Street STR
        Assert.Equal(12.0, border.MeasuredWidth);
        Assert.Equal("STR", border.BorderType);
    }

    [Fact]
    public void SuggestedStandardWidth_FindsNearestFromConfiguredList()
    {
        var standardWidths = new List<double> { 6.0, 8.0, 10.0, 12.0, 15.0, 20.0, 25.0, 30.0 };

        double measured = 14.8;
        double suggested = WidthInferenceService.FindNearestStandardWidth(measured, standardWidths);

        // 14.8 is nearest to 15.0
        Assert.Equal(15.0, suggested);
    }
}
