using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Lookups;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class BorderTypeAndDimensionTests
{
    // =========================================================================
    // 1. PARCEs_landuse.xlsx empty or no match: Step 3 does not fail, fields completed by rules
    // =========================================================================

    [Fact]
    public void Requirement1_MissingOrEmptyExcel_DoesNotFail_FieldsCompletedByRules()
    {
        var mainParcel = new ParcelRecord { InternalId = 1, Class = ParcelClass.Main, PlotId = "105", UsageText = "سكني" };
        var subParcel = new ParcelRecord { InternalId = 2, Class = ParcelClass.Sub, PlotId = "P2" };

        var parcels = new List<ParcelRecord> { mainParcel, subParcel };
        var discrepancies = new List<string>();

        // Empty Excel dictionary
        LandUseExcelService.EnrichParcels(parcels, new Dictionary<string, LandUseItem>(), discrepancies, MainUsageMode.InferFromText);

        // Process succeeds without throwing
        Assert.NotNull(mainParcel.Remarks);
        Assert.NotNull(subParcel.Remarks);
        Assert.Equal("سكني", mainParcel.Remarks); // REMARKS from UsageText, never PlotId
        Assert.Equal("RSD", mainParcel.Usage);   // Inferred from UsageText
        Assert.Equal("UTL", subParcel.Usage);   // SUB gets UTL
        Assert.Equal("UTL", subParcel.Remarks); // Fallback to USAGE
    }

    // =========================================================================
    // 2. SUB -> UTL; No USAGE outside Usage Types (including PLT); MAIN_USAGE_MODE works
    // =========================================================================

    [Fact]
    public void Requirement2_Usage_StrictlyValidated_SubGetsUtl_MainUsageModeWorks()
    {
        // 1. Official usage types sheet contains only 6 codes
        Assert.True(MunicipalLookups.IsOfficialUsageType("RSD"));
        Assert.True(MunicipalLookups.IsOfficialUsageType("COM"));
        Assert.True(MunicipalLookups.IsOfficialUsageType("RSC"));
        Assert.True(MunicipalLookups.IsOfficialUsageType("AGR"));
        Assert.True(MunicipalLookups.IsOfficialUsageType("IND"));
        Assert.True(MunicipalLookups.IsOfficialUsageType("UTL"));
        // PLT is in Border Types, NOT in Usage Types!
        Assert.False(MunicipalLookups.IsOfficialUsageType("PLT"));
        Assert.False(MunicipalLookups.IsOfficialUsageType("MSD"));

        // 2. SUB parcel always gets UTL
        var sub = new ParcelRecord { InternalId = 1, Class = ParcelClass.Sub, PlotId = "P1" };
        var parcelsSub = new List<ParcelRecord> { sub };
        LandUseExcelService.EnrichParcels(parcelsSub, null);
        Assert.Equal("UTL", sub.Usage);

        // 3. MAIN parcel with UsageText containing usage ("سكني") -> inferred as RSD
        var mainRsd = new ParcelRecord { InternalId = 2, Class = ParcelClass.Main, PlotId = "101", UsageText = "سكني 101" };
        LandUseExcelService.EnrichParcels(new[] { mainRsd }, null, mainUsageMode: MainUsageMode.InferFromText);
        Assert.Equal("RSD", mainRsd.Usage);
        Assert.Equal("سكني 101", mainRsd.Remarks);

        // 4. MAIN parcel with unclassified text ("101") under InferFromText -> leaves USAGE empty
        var mainUnclassified = new ParcelRecord { InternalId = 3, Class = ParcelClass.Main, PlotId = "101" };
        var disc = new List<string>();
        LandUseExcelService.EnrichParcels(new[] { mainUnclassified }, null, disc, MainUsageMode.InferFromText);
        Assert.Equal(string.Empty, mainUnclassified.Usage);
        Assert.Contains(disc, d => d.Contains("MAIN parcel USAGE left empty"));

        // 5. MAIN parcel under LeaveBlank mode -> leaves USAGE empty
        var mainBlankMode = new ParcelRecord { InternalId = 4, Class = ParcelClass.Main, PlotId = "102", UsageText = "تجاري" };
        LandUseExcelService.EnrichParcels(new[] { mainBlankMode }, null, mainUsageMode: MainUsageMode.LeaveBlank);
        Assert.Equal(string.Empty, mainBlankMode.Usage);
    }

    // =========================================================================
    // 3. REMARKS from chosen text, then from USAGE if absent or empty
    // =========================================================================

    [Fact]
    public void Requirement3_Remarks_FromChosenText_ElseFromUsage()
    {
        // 1. Valid user chosen text from Step 1 (via UsageText)
        var p1 = new ParcelRecord { InternalId = 1, Class = ParcelClass.Main, PlotId = "55", UsageText = "قطعة تجارية 55" };
        LandUseExcelService.EnrichParcels(new[] { p1 }, null, mainUsageMode: MainUsageMode.InferFromText);
        Assert.Equal("قطعة تجارية 55", p1.Remarks);

        // 2. Placeholder text (no user text) with known USAGE -> Remarks = USAGE
        var p2 = new ParcelRecord { InternalId = 2, Class = ParcelClass.Sub, PlotId = "P99" };
        LandUseExcelService.EnrichParcels(new[] { p2 }, null);
        Assert.Equal("UTL", p2.Usage);
        Assert.Equal("UTL", p2.Remarks);

        // 3. Placeholder text and empty USAGE -> Remarks = empty
        var p3 = new ParcelRecord { InternalId = 3, Class = ParcelClass.Main, PlotId = "MAIN" };
        LandUseExcelService.EnrichParcels(new[] { p3 }, null, mainUsageMode: MainUsageMode.LeaveBlank);
        Assert.Equal(string.Empty, p3.Remarks);
    }

    // =========================================================================
    // 4. Shared segment < 0.5m -> street; >= 0.5m -> eligible neighbor; 3cm vs 20cm gap
    // =========================================================================

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.2, false)]
    [InlineData(0.49, false)]
    [InlineData(0.5, true)]
    [InlineData(0.51, true)]
    [InlineData(5.0, true)]
    public void Requirement4_SharedSegment_Threshold_0_5M(double length, bool expectedEligible)
    {
        Assert.Equal(expectedEligible, SharedSegmentDetector.IsEligibleSharedSegment(length, 0.5));
    }

    [Fact]
    public void Requirement4_SharedSegment_CoincidenceTolerance_3cmVs20cmGap()
    {
        var segA = new Segment2D(new Point2D(0, 0), new Point2D(10, 0));

        // 3cm gap (0.03m <= 0.05m tolerance) -> coincident and eligible overlap
        var segClose = new Segment2D(new Point2D(0, 0.03), new Point2D(10, 0.03));
        double sharedClose = SharedSegmentDetector.CalculateSharedLength(segA, segClose, snapTolerance: 0.05);
        Assert.Equal(10.0, sharedClose, precision: 2);
        Assert.True(SharedSegmentDetector.IsEligibleSharedSegment(sharedClose));

        // 20cm gap (0.20m > 0.05m tolerance) -> NOT coincident (0.0 shared)
        var segFar = new Segment2D(new Point2D(0, 0.20), new Point2D(10, 0.20));
        double sharedFar = SharedSegmentDetector.CalculateSharedLength(segA, segFar, snapTolerance: 0.05);
        Assert.Equal(0.0, sharedFar, precision: 3);
        Assert.False(SharedSegmentDetector.IsEligibleSharedSegment(sharedFar));
    }

    // =========================================================================
    // 5. Neighbor MAIN -> PLT; Neighbor SUB "حديقة" -> PAK; unmatched -> empty; multiple neighbors
    // =========================================================================

    [Fact]
    public void Requirement5_NeighborRules_MainIsPlt_SubIsMatchedFromKeywords_MultipleNeighbors()
    {
        // 1. Direct keywords match
        MunicipalLookups.EnsureBorderTypeKeywordsFile("test_keywords.csv");
        MunicipalLookups.LoadBorderTypeKeywords("test_keywords.csv");

        var matchPark = MunicipalLookups.MatchBorderTypeFromKeywords("حديقة الحي", "REMARKS");
        Assert.Equal("PAK", matchPark.Code);

        var matchMosque = MunicipalLookups.MatchBorderTypeFromKeywords("مسجد", "REMARKS");
        Assert.Equal("MSQ", matchMosque.Code);

        // 2. Unmatched text -> returns null, does not guess
        var matchNone = MunicipalLookups.MatchBorderTypeFromKeywords("نص_غير_معروف_نهائيا", "REMARKS");
        Assert.Null(matchNone.Code);

        // 3. Clean up test file if created
        if (File.Exists("test_keywords.csv")) File.Delete("test_keywords.csv");
    }

    // =========================================================================
    // 6. Every code written exists in Border Types; invalid code in keywords table ignored
    // =========================================================================

    [Fact]
    public void Requirement6_BorderTypesSheet_Validation_AndKeywordsTableFilter()
    {
        // Official codes exist
        Assert.True(MunicipalLookups.IsOfficialBorderType("PLT"));
        Assert.True(MunicipalLookups.IsOfficialBorderType("STR"));
        Assert.True(MunicipalLookups.IsOfficialBorderType("MSQ"));
        Assert.True(MunicipalLookups.IsOfficialBorderType("PAK"));
        Assert.True(MunicipalLookups.IsOfficialBorderType("LNE"));

        // MSD is NOT in official sheet
        Assert.False(MunicipalLookups.IsOfficialBorderType("MSD"));

        // Loading a keywords file with an invalid code (e.g. MSD) ignores that row and logs a warning
        string testCsv = "test_invalid_kw.csv";
        File.WriteAllText(testCsv, "Keyword,AppliesTo,MatchType,BorderTypeCode,Priority,Notes\nمستودع,BOTH,contains,WRH,1,valid\nمسجد قديم,BOTH,contains,MSD,1,invalid\n");

        var warnings = new List<string>();
        int loaded = MunicipalLookups.LoadBorderTypeKeywords(testCsv, msg => warnings.Add(msg));

        Assert.Equal(1, loaded); // Only 1 valid row loaded
        Assert.Contains(warnings, w => w.Contains("MSD"));

        if (File.Exists(testCsv)) File.Delete(testCsv);
    }

    // =========================================================================
    // 7. DESCRIPTO / DESCRIPTIO field name and UTF-8 byte length overflow check
    // =========================================================================

    [Fact]
    public void Requirement7_DescriptionLengthCheck_DoesNotTruncateSilently()
    {
        var border = new BorderRecord
        {
            InternalId = 1,
            PlotId = "101",
            BorderName = 1,
            // 60 Arabic characters = ~120 bytes in UTF-8, exceeding the 100-byte DBF limit
            Descriptio = "شارع بعرض 18 م ممتد من الشرق إلى الغرب مع تقاطع رئيسي وحديقة وممر مشاه طويل جداً"
        };

        var issues = DbfTextLengthService.CheckTextLengths(Array.Empty<ParcelRecord>(), new[] { border });
        Assert.NotEmpty(issues);
        Assert.True(issues[0].WillBeTruncated);
        Assert.True(issues[0].Utf8ByteLength > 100);
        Assert.NotNull(issues[0].SuggestedShorterValue);
    }

    // =========================================================================
    // 8. Width Measurement: 12m -> Street 12, 6m -> Passage 6, < 1m ignored, 100m limit
    // =========================================================================

    [Fact]
    public void Requirement8_WidthMeasurement_SingleRay_PassageVsStreet_MinGap1M()
    {
        var options = new WidthInferenceOptions
        {
            RayCount = 1,
            PassageStreetThreshold = 10.0,
            MinFacingGap = 1.0,
            MaxSearchDistance = 100.0
        };

        // 1. Street gap 12m -> Street with width 12
        var bStreet = new BorderRecord { InternalId = 1, PlotId = "101" };
        var dimSeg = new Segment2D(new Point2D(0, 0), new Point2D(20, 0));
        var normal = new Point2D(0, 1);
        var obsStreet = new List<Segment2D>
        {
            new(new Point2D(-10, 12.0), new Point2D(30, 12.0), "Facing1", "Parcel")
        };
        WidthInferenceService.InferBorderWidth(bStreet, dimSeg, normal, obsStreet, options);
        Assert.Equal(12.0, bStreet.MeasuredWidth);
        Assert.Equal("STR", bStreet.BorderType);
        Assert.Contains("شارع بعرض 12 م", bStreet.Descriptio);

        // 2. Passage gap 6m -> Passage (LNE) with width 6
        var bPassage = new BorderRecord { InternalId = 2, PlotId = "102" };
        var obsPassage = new List<Segment2D>
        {
            new(new Point2D(-10, 6.0), new Point2D(30, 6.0), "Facing2", "Parcel")
        };
        WidthInferenceService.InferBorderWidth(bPassage, dimSeg, normal, obsPassage, options);
        Assert.Equal(6.0, bPassage.MeasuredWidth);
        Assert.Equal("LNE", bPassage.BorderType);
        Assert.Contains("ممر بعرض 6 م", bPassage.Descriptio);

        // 3. Obstacle closer than 1m (e.g. 0.5m) -> ignored (MinFacingGap = 1.0m)
        var bClose = new BorderRecord { InternalId = 3, PlotId = "103" };
        var obsClose = new List<Segment2D>
        {
            new(new Point2D(-10, 0.5), new Point2D(30, 0.5), "TouchingParcel", "Parcel")
        };
        WidthInferenceService.InferBorderWidth(bClose, dimSeg, normal, obsClose, options);
        Assert.True(bClose.IsUnknown);
        Assert.Equal(string.Empty, bClose.BorderType);
        Assert.Equal(string.Empty, bClose.Descriptio);

        // 4. No obstacle within 100m -> IsUnknown = true, empty fields, UNKNOWN status
        var bFar = new BorderRecord { InternalId = 4, PlotId = "104" };
        var obsFar = new List<Segment2D>
        {
            new(new Point2D(-10, 150.0), new Point2D(30, 150.0), "FarParcel", "Parcel")
        };
        WidthInferenceService.InferBorderWidth(bFar, dimSeg, normal, obsFar, options);
        Assert.True(bFar.IsUnknown);
        Assert.Equal(string.Empty, bFar.BorderType);
        Assert.Equal(string.Empty, bFar.Descriptio);
        Assert.Equal("RequiresReview", bFar.ReviewStatus);
    }

    // =========================================================================
    // 9. Three Values: measured 9.5 -> rounded 10 (street), 14.8 -> rounded 15, suggested 15
    // =========================================================================

    [Fact]
    public void Requirement9_ThreeValues_Measured_Rounded_Suggested_NoAutoSnapping()
    {
        var options = new WidthInferenceOptions
        {
            RayCount = 1,
            StandardWidthTolerance = 1.0,
            StandardWidths = new List<double> { 4, 5, 6, 8, 10, 12, 15, 18, 20, 25, 30, 36, 40, 50, 60 }
        };

        // Measured 9.5m:
        // Raw RayLength = 9.5, Rounded = 10 (Street >= 10), Suggested = 10
        var b1 = new BorderRecord { InternalId = 1, PlotId = "101" };
        var dimSeg = new Segment2D(new Point2D(0, 0), new Point2D(20, 0));
        var normal = new Point2D(0, 1);
        var obs1 = new List<Segment2D> { new(new Point2D(-10, 9.5), new Point2D(30, 9.5)) };

        WidthInferenceService.InferBorderWidth(b1, dimSeg, normal, obs1, options);
        Assert.Equal(9.5, b1.RayLength);
        Assert.Equal(10.0, b1.MeasuredWidth); // Rounded to nearest integer
        Assert.Equal(10.0, b1.SuggestedWidth);
        Assert.Equal("STR", b1.BorderType); // 10 >= 10 -> Street!

        // Measured 14.8m:
        // Raw RayLength = 14.8, Rounded = 15, Suggested = 15
        var b2 = new BorderRecord { InternalId = 2, PlotId = "102" };
        var obs2 = new List<Segment2D> { new(new Point2D(-10, 14.8), new Point2D(30, 14.8)) };

        WidthInferenceService.InferBorderWidth(b2, dimSeg, normal, obs2, options);
        Assert.Equal(14.8, b2.RayLength);
        Assert.Equal(15.0, b2.MeasuredWidth);
        Assert.Equal(15.0, b2.SuggestedWidth);
    }

    // =========================================================================
    // 10. DIMENSION: 1 segment -> Single; 1 arc -> Single; 10° -> Single; 15° -> Single; 16° -> Multi; 359° vs 1° -> 2°
    // =========================================================================

    [Fact]
    public void Requirement10_Dimension_DeflectionAngleRules_15DegreesThreshold()
    {
        // 1. Single straight segment -> Single
        var piece1 = new CoreBoundaryPiece
        {
            Segments = new List<CoreBoundarySegment> { new(new Point2D(0, 0), new Point2D(15, 0)) }
        };
        Assert.Equal("Single", piece1.DetermineDimension(toleranceDeg: 15.0));

        // 2. Single arc segment -> Single
        var pieceArc = new CoreBoundaryPiece
        {
            Segments = new List<CoreBoundarySegment>
            {
                new(new Point2D(0, 0), new Point2D(5, 5), CurveType.CircularArc, 7.85)
                {
                    StartTangentDeg = 0.0,
                    EndTangentDeg = 90.0
                }
            }
        };
        Assert.Equal("Single", pieceArc.DetermineDimension(toleranceDeg: 15.0));

        // 3. Multi-segments with 10° deflection (<= 15°) -> Single
        Assert.Equal("Single", CreateTwoSegmentPiece(10.0).DetermineDimension(toleranceDeg: 15.0));

        // 4. Multi-segments with exactly 15.0° deflection (<= 15°) -> Single
        Assert.Equal("Single", CreateTwoSegmentPiece(15.0).DetermineDimension(toleranceDeg: 15.0));

        // 5. Multi-segments with 16.0° deflection (> 15°) -> Multi
        Assert.Equal("Multi", CreateTwoSegmentPiece(16.0).DetermineDimension(toleranceDeg: 15.0));

        // 6. Angular difference circular wrap: 359° vs 1° -> deflection is 2° (NOT 358°)
        double wrapDiff = BorderExtractionCore.AngularDifferenceDeg(359.0, 1.0);
        Assert.Equal(2.0, wrapDiff, precision: 3);
    }

    private static CoreBoundaryPiece CreateTwoSegmentPiece(double deflectionDeg)
    {
        var seg1 = new CoreBoundarySegment(new Point2D(0, 0), new Point2D(10, 0)); // Azimuth 90°
        double angleRad = (90.0 + deflectionDeg) * (Math.PI / 180.0);
        var pt2 = new Point2D(10.0 + 10.0 * Math.Sin(angleRad), 10.0 * Math.Cos(angleRad));
        var seg2 = new CoreBoundarySegment(new Point2D(10, 0), pt2);
        return new CoreBoundaryPiece { Segments = new List<CoreBoundarySegment> { seg1, seg2 } };
    }

    // =========================================================================
    // 11. Protection of manual edits
    // =========================================================================

    [Fact]
    public void Requirement11_ManualEdits_ProtectedFromOverwrite()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 1,
            PlotId = "101",
            Usage = "COM",
            Remarks = "محل تجاري يدوي مخصص"
        };

        // When MissingDataPolicy runs without forceUpdate, manual remarks are preserved
        var meta = new ProjectMetadata { PlanNo = "555/2" };
        MissingDataPolicy.ApplyDefaults(parcel, meta, forceUpdate: false);

        Assert.Equal("محل تجاري يدوي مخصص", parcel.Remarks);
        Assert.Equal("COM", parcel.Usage);
    }
}
