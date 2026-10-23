using System.Collections.Generic;
using Xunit;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class QaValidationTests
{
    [Fact]
    public void ValidateAll_ValidParcelAnd4Borders_PassesValidation()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "101",
            PlanNo = "0000/0",
            Neighbourhood = "District 1",
            Address = "حي/District 1/بالرياض/بمنطقة/الرياض",
            AreaSqm = 600.0,
            ShapeLength = 100.0
        };

        var borders = new List<BorderRecord>
        {
            new() { InternalId = 1, PlotId = "101", PlanNo = "0000/0", BorderName = 1, ShapeLength = 20.0, BorderDesc = 20, Descriptio = "شارع" },
            new() { InternalId = 2, PlotId = "101", PlanNo = "0000/0", BorderName = 2, ShapeLength = 20.0, BorderDesc = 20, Descriptio = "شارع" },
            new() { InternalId = 3, PlotId = "101", PlanNo = "0000/0", BorderName = 3, ShapeLength = 30.0, BorderDesc = 30, Descriptio = "قطعة رقم 102" },
            new() { InternalId = 4, PlotId = "101", PlanNo = "0000/0", BorderName = 4, ShapeLength = 30.0, BorderDesc = 30, Descriptio = "قطعة رقم 100" }
        };

        var result = QaValidationEngine.ValidateAll(new[] { parcel }, borders);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateAll_MainParcelMissingPlotId_IsError_SubParcelWithoutPlotId_IsExpected()
    {
        var mainParcel = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "", // Missing on MAIN
            PlanNo = "0000/0",
            AreaSqm = 600.0,
            ShapeLength = 100.0
        };

        var subParcel = new ParcelRecord
        {
            InternalId = 2,
            Class = ParcelClass.Sub,
            PlotId = "", // Expected on SUB
            PlanNo = "0000/0",
            AreaSqm = 200.0,
            ShapeLength = 60.0
        };

        var result = QaValidationEngine.ValidateAll(new[] { mainParcel, subParcel }, new List<BorderRecord>());

        // MAIN without PLOT_ID must produce Error
        Assert.Contains(result.Errors, e => e.EntityId == "1" && e.FieldName == "PLOT_ID");
        // SUB without PLOT_ID must NOT produce an Error for PLOT_ID
        Assert.DoesNotContain(result.Errors, e => e.EntityId == "2" && e.FieldName == "PLOT_ID");
    }

    [Fact]
    public void ValidateAll_SubParcelWithBorders_ProducesError()
    {
        var subParcel = new ParcelRecord
        {
            InternalId = 2,
            Class = ParcelClass.Sub,
            PlotId = "GARDEN_01",
            PlanNo = "0000/0",
            AreaSqm = 200.0,
            ShapeLength = 60.0
        };

        var borders = new List<BorderRecord>
        {
            new() { InternalId = 1, PlotId = "GARDEN_01", BorderName = 1, ShapeLength = 10.0 }
        };

        var result = QaValidationEngine.ValidateAll(new[] { subParcel }, borders);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.EntityType == "SubParcel" && e.FieldName == "Borders");
    }

    [Fact]
    public void ValidateAll_ParcelMissingOneCardinalBorder_FailsValidation()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "101",
            PlanNo = "0000/0",
            AreaSqm = 600.0,
            ShapeLength = 100.0
        };

        // Only 3 borders (missing West = 4)
        var borders = new List<BorderRecord>
        {
            new() { InternalId = 1, PlotId = "101", PlanNo = "0000/0", BorderName = 1, ShapeLength = 20.0, BorderDesc = 20 },
            new() { InternalId = 2, PlotId = "101", PlanNo = "0000/0", BorderName = 2, ShapeLength = 20.0, BorderDesc = 20 },
            new() { InternalId = 3, PlotId = "101", PlanNo = "0000/0", BorderName = 3, ShapeLength = 30.0, BorderDesc = 30 }
        };

        var result = QaValidationEngine.ValidateAll(new[] { parcel }, borders);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldName == "BordersCount" || e.FieldName == "MissingCardinal");
    }

    [Fact]
    public void ValidateAll_DuplicateCardinalDirection_FailsValidation()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "101",
            PlanNo = "0000/0",
            AreaSqm = 600.0,
            ShapeLength = 100.0
        };

        // Two South borders (BorderName = 2) and no West (4)
        var borders = new List<BorderRecord>
        {
            new() { InternalId = 1, PlotId = "101", PlanNo = "0000/0", BorderName = 1, ShapeLength = 20.0 },
            new() { InternalId = 2, PlotId = "101", PlanNo = "0000/0", BorderName = 2, ShapeLength = 20.0 },
            new() { InternalId = 3, PlotId = "101", PlanNo = "0000/0", BorderName = 2, ShapeLength = 30.0 },
            new() { InternalId = 4, PlotId = "101", PlanNo = "0000/0", BorderName = 3, ShapeLength = 30.0 }
        };

        var result = QaValidationEngine.ValidateAll(new[] { parcel }, borders);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldName.StartsWith("DuplicateCardinal"));
    }

    [Fact]
    public void ValidateAll_ExportSchemaVerification_FlagsParcelClassFieldIfPresent()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "101",
            PlanNo = "0000/0",
            AreaSqm = 600.0,
            ShapeLength = 100.0
        };

        var borders = new List<BorderRecord>
        {
            new() { InternalId = 1, PlotId = "101", PlanNo = "0000/0", BorderName = 1, ShapeLength = 20.0 },
            new() { InternalId = 2, PlotId = "101", PlanNo = "0000/0", BorderName = 2, ShapeLength = 20.0 },
            new() { InternalId = 3, PlotId = "101", PlanNo = "0000/0", BorderName = 3, ShapeLength = 30.0 },
            new() { InternalId = 4, PlotId = "101", PlanNo = "0000/0", BorderName = 4, ShapeLength = 30.0 }
        };

        // If PARCEL_CLASS is mistakenly present in export schema
        var result = QaValidationEngine.ValidateAll(
            new[] { parcel }, 
            borders, 
            verifyExportSchema: true, 
            exportFieldNames: new[] { "PLOT_ID", "PLAN_NO", "PARCEL_CLASS", "AREA_SQM" }
        );

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldName == "PARCEL_CLASS");
    }
}
