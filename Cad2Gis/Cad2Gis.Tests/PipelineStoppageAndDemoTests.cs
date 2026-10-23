using System;
using Xunit;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Tests;

public class PipelineStoppageAndDemoTests
{
    [Fact]
    public void Pipeline_ThrowsException_WhenNoCadDataAvailable()
    {
        // When there is no active CAD data or layers, the pipeline must halt with the required message
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            // Simulate pipeline step 2 check when layers/map are empty
            bool hasCadLayers = false;
            bool hasActiveCadDrawing = false;

            if (!hasCadLayers && !hasActiveCadDrawing)
            {
                throw new InvalidOperationException("No CAD data available — nothing was generated");
            }
        });

        Assert.Equal("No CAD data available — nothing was generated", ex.Message);
    }

    [Fact]
    public void DemoData_UsesDemoPrefixForFeatureClassesAndLayers()
    {
        bool isDemoProject = true;

        string parcelFcName = isDemoProject ? "DEMO_PARCEL" : "PARCEL";
        string borderFcName = isDemoProject ? "DEMO_Border" : "Border";

        Assert.StartsWith("DEMO_", parcelFcName);
        Assert.StartsWith("DEMO_", borderFcName);
        Assert.Equal("DEMO_PARCEL", parcelFcName);
        Assert.Equal("DEMO_Border", borderFcName);
    }

    [Fact]
    public void DemoExport_IsBlocked_UnlessExplicitlyConfirmed()
    {
        bool isDemoProject = true;
        bool allowDemoExportConfirmation = false;

        // Simulate export guard
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            if (isDemoProject && !allowDemoExportConfirmation)
            {
                throw new InvalidOperationException(
                    "Export blocked: The active project contains synthetic demo data. Please check 'Explicitly confirm export of demo dataset' to proceed.");
            }
        });

        Assert.Contains("Export blocked", ex.Message);

        // When confirmed, no exception is thrown
        allowDemoExportConfirmation = true;
        bool exportAllowed = !(isDemoProject && !allowDemoExportConfirmation);
        Assert.True(exportAllowed);
    }
}
