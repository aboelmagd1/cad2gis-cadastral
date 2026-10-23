using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;

namespace Cad2Gis.Tests;

public class PipelineIntegrationTests
{
    [Fact]
    public void EndToEnd_LandUseAndPolicyWorkflow_CompletesSuccessfully()
    {
        var meta = new ProjectMetadata
        {
            PlanNo = "0000/0",
            Neighbourhood = "District 1",
            CityId = "1",
            CityName = "الرياض",
            RegionId = "1",
            RegionName = "الرياض",
            RequestType = "فرز",
            ReportNo = "REP-000"
        };

        var parcels = new List<ParcelRecord>
        {
            new() { InternalId = 1, PlotId = "1", AreaSqm = 600.0, ShapeLength = 100.0 },
            new() { InternalId = 2, PlotId = "2", AreaSqm = 750.0, ShapeLength = 110.0 }
        };

        var borders = new List<BorderRecord>();
        long borderSeq = 1;
        foreach (var p in parcels)
        {
            MissingDataPolicy.ApplyDefaults(p, meta);

            for (int dir = 1; dir <= 4; dir++)
            {
                var b = new BorderRecord
                {
                    InternalId = borderSeq++,
                    BorderName = dir,
                    ShapeLength = 25.0
                };
                MissingDataPolicy.ApplyDefaults(b, p);
                borders.Add(b);
            }
        }

        // Validate
        var result = QaValidationEngine.ValidateAll(parcels, borders);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.TotalParcels);
        Assert.Equal(8, result.TotalBorders);
        Assert.Equal("حي/District 1/بالرياض/بمنطقة/الرياض", parcels[0].Address);
        Assert.Equal("0000/0", borders[0].PlanNo);
    }
}
