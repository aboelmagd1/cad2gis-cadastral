using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Services;
using Xunit;

namespace Cad2Gis.Tests;

public class CustomSchemaFieldMappingTests
{
    [Fact]
    public void CheckSchema_WhenAllStandardFieldsPresent_ReturnsIdenticalTrue()
    {
        var standardFields = CustomSchemaDataService.MomrahTargetFields.Select(f => f.TargetField).ToList();
        var (isIdentical, matchedCount, total) = CustomSchemaDataService.CheckSchema(standardFields);

        Assert.True(isIdentical);
        Assert.Equal(total, matchedCount);
    }

    [Fact]
    public void CheckSchema_WhenDifferentSchema_ReturnsIdenticalFalse()
    {
        var customFields = new List<string> { "Parcel_Number", "Land_Use", "District", "Area" };
        var (isIdentical, matchedCount, total) = CustomSchemaDataService.CheckSchema(customFields);

        Assert.False(isIdentical);
        Assert.True(matchedCount < total);
    }

    [Theory]
    [InlineData("PLOT_ID", "PLOT_ID", "PLOT_ID")]
    [InlineData("PLOT_ID", "plot_id", "plot_id")]
    [InlineData("PLOT_ID", "PlotId", "PlotId")]
    [InlineData("PLOT_ID", "plot_no", "plot_no")]
    [InlineData("PLOT_ID", "رقم_القطعة", "رقم_القطعة")]
    [InlineData("PLOT_ID", "رقم القطعة", "رقم القطعة")]
    [InlineData("PLAN_NO", "plan_no", "plan_no")]
    [InlineData("PLAN_NO", "رقم_المخطط", "رقم_المخطط")]
    [InlineData("USAGE", "usage", "usage")]
    [InlineData("USAGE", "land_use", "land_use")]
    [InlineData("USAGE", "الاستخدام", "الاستخدام")]
    [InlineData("BLOCK_NO", "block_no", "block_no")]
    [InlineData("BLOCK_NO", "رقم_البلك", "رقم_البلك")]
    [InlineData("NEIGHBOURH", "neighbourhood", "neighbourhood")]
    [InlineData("NEIGHBOURH", "الحي", "الحي")]
    [InlineData("CITY_ID", "city", "city")]
    [InlineData("CITY_ID", "المدينة", "المدينة")]
    [InlineData("REGION_ID", "region", "region")]
    [InlineData("REGION_ID", "المنطقة", "المنطقة")]
    [InlineData("AREA_SQM", "area", "area")]
    [InlineData("AREA_SQM", "المساحة", "المساحة")]
    public void AutoMatchField_CorrectlyMatchesExactAndSynonyms(string targetField, string sourceFieldInput, string expectedMatched)
    {
        var sourceFields = new List<string> { "SOME_OTHER_FIELD", sourceFieldInput, "EXTRA_FIELD" };
        string matched = CustomSchemaDataService.AutoMatchField(targetField, sourceFields);

        Assert.Equal(expectedMatched, matched);
    }

    [Fact]
    public void AutoMatchField_WhenNoMatchFound_ReturnsEmpty()
    {
        var sourceFields = new List<string> { "RANDOM_COLUMN_1", "COMPLETELY_UNRELATED" };
        string matched = CustomSchemaDataService.AutoMatchField("PLOT_ID", sourceFields);

        Assert.Empty(matched);
    }

    [Fact]
    public void ReadExcelHeadersAndRows_WithCustomSchema_ReadsSuccessfully()
    {
        string tempXlsx = Path.Combine(Path.GetTempPath(), $"custom_schema_{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Parcels");
                ws.Cell(1, 1).Value = "رقم_القطعة";
                ws.Cell(1, 2).Value = "المخطط";
                ws.Cell(1, 3).Value = "الاستخدام";
                ws.Cell(1, 4).Value = "المساحة";

                ws.Cell(2, 1).Value = "101";
                ws.Cell(2, 2).Value = "1400/1";
                ws.Cell(2, 3).Value = "سكني";
                ws.Cell(2, 4).Value = "520.5";

                ws.Cell(3, 1).Value = "102";
                ws.Cell(3, 2).Value = "1400/1";
                ws.Cell(3, 3).Value = "تجاري";
                ws.Cell(3, 4).Value = "680.0";

                wb.SaveAs(tempXlsx);
            }

            var headers = CustomSchemaDataService.ReadExcelHeaders(tempXlsx);
            Assert.Equal(4, headers.Count);
            Assert.Contains("رقم_القطعة", headers);
            Assert.Contains("المخطط", headers);
            Assert.Contains("الاستخدام", headers);
            Assert.Contains("المساحة", headers);

            var rows = CustomSchemaDataService.ReadExcelRows(tempXlsx);
            Assert.Equal(2, rows.Count);
            Assert.Equal("101", rows[0]["رقم_القطعة"]);
            Assert.Equal("102", rows[1]["رقم_القطعة"]);

            // Auto-mapping test on these headers
            string matchedPlotId = CustomSchemaDataService.AutoMatchField("PLOT_ID", headers);
            string matchedPlanNo = CustomSchemaDataService.AutoMatchField("PLAN_NO", headers);
            string matchedUsage = CustomSchemaDataService.AutoMatchField("USAGE", headers);
            string matchedArea = CustomSchemaDataService.AutoMatchField("AREA_SQM", headers);

            Assert.Equal("رقم_القطعة", matchedPlotId);
            Assert.Equal("المخطط", matchedPlanNo);
            Assert.Equal("الاستخدام", matchedUsage);
            Assert.Equal("المساحة", matchedArea);
        }
        finally
        {
            if (File.Exists(tempXlsx)) File.Delete(tempXlsx);
        }
    }
}
