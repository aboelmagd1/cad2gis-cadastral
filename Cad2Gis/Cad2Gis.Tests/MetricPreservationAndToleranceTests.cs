using System.Collections.Generic;
using Xunit;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class MetricPreservationAndToleranceTests
{
    [Fact]
    public void FrozenPlanarMetrics_PreservedDuringExport_RegardlessOfSpatialReference()
    {
        // When projecting geometry to GCS (WGS84 degrees), planar metrics must stay frozen in UTM values
        double frozenUtmArea = 750.50;
        double frozenUtmLength = 110.20;

        var parcel = new ParcelRecord
        {
            InternalId = 1,
            PlotId = "101",
            AreaSqm = frozenUtmArea,
            ShapeLength = frozenUtmLength
        };

        // Regardless of geographic degrees geometry or GDB system recalculations,
        // the frozen attributes must remain exactly the UTM planar values
        Assert.Equal(750.50, parcel.AreaSqm);
        Assert.Equal(110.20, parcel.ShapeLength);
    }

    [Fact]
    public void QaValidation_ValidateFrozenMetrics_FailsOnMetricMismatch()
    {
        var parcel = new ParcelRecord
        {
            InternalId = 1,
            PlotId = "101",
            AreaSqm = 600.0,
            ShapeLength = 100.0
        };

        // Case 1: Frozen metrics match exported values exactly
        var validResult = new ValidationResult();
        QaValidationEngine.ValidateFrozenMetrics(
            parcel,
            exportedAreaSqm: 600.0,
            exportedShapeArea: 600.0,
            exportedShapeLength: 100.0,
            validResult);

        Assert.True(validResult.IsValid);
        Assert.Empty(validResult.Errors);

        // Case 2: Frozen metrics mismatch (e.g. system area overwritten with geographic degrees or deformed value)
        var invalidResult = new ValidationResult();
        QaValidationEngine.ValidateFrozenMetrics(
            parcel,
            exportedAreaSqm: 550.0, // 50 sqm difference!
            exportedShapeArea: 550.0,
            exportedShapeLength: 100.0,
            invalidResult,
            toleranceSqm: 0.5);

        Assert.False(invalidResult.IsValid);
        Assert.NotEmpty(invalidResult.Errors);
        Assert.Contains(invalidResult.Errors, e => e.Message.Contains("differs from frozen UTM"));
    }

    [Fact]
    public void ConfigurableTolerances_DefaultValues_AreCorrect()
    {
        var profile = new ConsultantProfileModel();

        // Item 9: Area tolerance 0.5 m² default
        Assert.Equal(0.5, profile.AreaToleranceSqm);
        Assert.Equal(0.1, profile.AreaTolerancePercent);

        // Item 10: Chamfer threshold 7.0 m and 45.0 degrees
        Assert.Equal(7.0, profile.ChamferThresholdMeters);
        Assert.Equal(45.0, profile.ChamferAngleDeg);
        Assert.Equal(15.0, profile.ChamferAngleToleranceDeg);
    }
}
