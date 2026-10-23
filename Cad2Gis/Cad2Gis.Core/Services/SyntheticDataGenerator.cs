using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

public class SyntheticDataset
{
    public List<ParcelRecord> MainParcels { get; set; } = new();
    public List<ParcelRecord> SubParcels { get; set; } = new();
    public List<ParcelRecord> AllParcels { get; set; } = new();

    public List<Segment2D> FacingObstacleSegments { get; set; } = new();
    public List<LineGeometry2D> UnclosedLineGroup { get; set; } = new();

    public ProjectMetadata Metadata { get; set; } = new();
    public string AreasExcelPath { get; set; } = string.Empty;
    public string ExpectedLayersExcelPath { get; set; } = string.Empty;
}

public static class SyntheticDataGenerator
{
    public static SyntheticDataset CreateSyntheticProject(string workingDirectory)
    {
        Directory.CreateDirectory(workingDirectory);

        var data = new SyntheticDataset
        {
            Metadata = new ProjectMetadata
            {
                PlanNo = "0000/0",
                Neighbourhood = "District 1",
                CityId = "1",
                CityName = "الرياض",
                RegionId = "1",
                RegionName = "الرياض",
                RequestType = "فرز",
                ReportNo = "REP-000"
            }
        };

        // 1. Two Main Parcels (each 20 x 30m = 600 m²)
        // Origin: X=500000, Y=2400000 (UTM Zone 37N)
        // Parcel 101: X: [500000..500020], Y: [2400000..2400030], 20m x 30m
        var p101 = new ParcelRecord
        {
            InternalId = 1,
            Class = ParcelClass.Main,
            PlotId = "101",
            PlotNo1 = "101",
            PlanNo = "0000/0",
            Neighbourhood = "District 1",
            Usage = "RSD",
            Remarks = "سكني",
            AreaSqm = 600.0,
            ShapeArea = 600.0,
            ShapeLength = 100.0, // 2*(20+30)
            CentroidX = 500010.0,
            CentroidY = 2400015.0
        };

        // Parcel 102: X: [500020..500040], Y: [2400000..2400030], 20m x 30m, adjacent to East of 101
        var p102 = new ParcelRecord
        {
            InternalId = 2,
            Class = ParcelClass.Main,
            PlotId = "102",
            PlotNo1 = "102",
            PlanNo = "0000/0",
            Neighbourhood = "District 1",
            Usage = "COM",
            Remarks = "تجاري أحياء",
            AreaSqm = 600.0,
            ShapeArea = 600.0,
            ShapeLength = 100.0,
            CentroidX = 500030.0,
            CentroidY = 2400015.0
        };

        data.MainParcels.Add(p101);
        data.MainParcels.Add(p102);

        // 2. Sub-Parcels
        // Garden Sub-Parcel: Adjacent to North edge of parcels (Y=2400030..2400040, X=500000..500040), NO plot ID
        var garden = new ParcelRecord
        {
            InternalId = 3,
            Class = ParcelClass.Sub,
            SubType = "GARDEN",
            PlotId = string.Empty, // A sub-parcel without a plot number is expected!
            PlanNo = "0000/0",
            Neighbourhood = "District 1",
            Usage = "PAK",
            Remarks = "حديقة",
            AreaSqm = 400.0,
            ShapeLength = 100.0,
            CentroidX = 500020.0,
            CentroidY = 2400035.0
        };

        // Corridor Sub-Parcel: Adjacent to West edge of 101 (X=499994..500000, Y=2400000..2400030), 6m wide
        var corridor = new ParcelRecord
        {
            InternalId = 4,
            Class = ParcelClass.Sub,
            SubType = "CORRIDOR",
            PlotId = string.Empty,
            PlanNo = "0000/0",
            Neighbourhood = "District 1",
            Usage = "SRV",
            Remarks = "خدمات طرق وممرات",
            AreaSqm = 180.0,
            ShapeLength = 72.0,
            CentroidX = 499997.0,
            CentroidY = 2400015.0
        };

        data.SubParcels.Add(garden);
        data.SubParcels.Add(corridor);

        data.AllParcels.AddRange(data.MainParcels);
        data.AllParcels.AddRange(data.SubParcels);

        // 3. Facing Obstacle Elements for Width Inference:
        // - South side: facing parcel edge 12m away (Y = 2399988.0, X: 500000..500040) -> Expected Street width = 12m
        data.FacingObstacleSegments.Add(new Segment2D(
            new Point2D(500000, 2399988),
            new Point2D(500040, 2399988),
            elementId: "FACING_BLOCK_SOUTH",
            elementType: "ParcelEdge"
        ));

        // - West side (beyond corridor): facing edge 6m away from corridor (X = 499988.0, Y: 2400000..2400030) -> Expected Passage width = 6m
        data.FacingObstacleSegments.Add(new Segment2D(
            new Point2D(499988, 2400000),
            new Point2D(499988, 2400030),
            elementId: "FACING_BLOCK_WEST",
            elementType: "ParcelEdge"
        ));

        // 4. Unclosed Lines Group (To test Lines -> Polygons)
        // A quadrilateral of 10 x 10m with a 0.03m gap (less than snap tolerance of 0.05m)
        // plus an extra dangling open line
        var line1 = new LineGeometry2D
        {
            LayerName = "CAD_OPEN_LINES",
            Points = new List<Point2D> { new(500100, 2400100), new(500110, 2400100) }
        };
        var line2 = new LineGeometry2D
        {
            LayerName = "CAD_OPEN_LINES",
            Points = new List<Point2D> { new(500110, 2400100), new(500110, 2400110) }
        };
        var line3 = new LineGeometry2D
        {
            LayerName = "CAD_OPEN_LINES",
            Points = new List<Point2D> { new(500110, 2400110), new(500100, 2400110) }
        };
        var line4 = new LineGeometry2D
        {
            LayerName = "CAD_OPEN_LINES",
            // Deliberate 0.03m gap at start: (500100.03, 2400110) instead of 500100.0
            Points = new List<Point2D> { new(500100.03, 2400110), new(500100, 2400100) }
        };
        var dangleLine = new LineGeometry2D
        {
            LayerName = "CAD_OPEN_LINES",
            Points = new List<Point2D> { new(500110, 2400100), new(500125, 2400100) } // Dangle extension
        };

        data.UnclosedLineGroup.Add(line1);
        data.UnclosedLineGroup.Add(line2);
        data.UnclosedLineGroup.Add(line3);
        data.UnclosedLineGroup.Add(line4);
        data.UnclosedLineGroup.Add(dangleLine);

        // 5. Generate Synthetic Areas Excel
        data.AreasExcelPath = Path.Combine(workingDirectory, "Areas_Demo.xlsx");
        CreateDemoAreasExcel(data.AreasExcelPath);

        // 6. Generate Synthetic Expected Layers Excel
        data.ExpectedLayersExcelPath = Path.Combine(workingDirectory, "Expected_Layers_Demo.xlsx");
        CreateDemoExpectedLayersExcel(data.ExpectedLayersExcelPath);

        return data;
    }

    private static void CreateDemoAreasExcel(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Parcel Areas");

        ws.Cell(1, 1).Value = "Plot Number";
        ws.Cell(1, 2).Value = "Area SQM";
        ws.Cell(1, 3).Value = "Usage";

        // Plot 101 matches: 600.0 m²
        ws.Cell(2, 1).Value = "101";
        ws.Cell(2, 2).Value = 600.0;
        ws.Cell(2, 3).Value = "RSD";

        // Plot 102 deliberately mismatched: 750.0 m² (computed is 600.0 m²)
        ws.Cell(3, 1).Value = "102";
        ws.Cell(3, 2).Value = 750.0;
        ws.Cell(3, 3).Value = "COM";

        // Plot 103 in Excel only
        ws.Cell(4, 1).Value = "103";
        ws.Cell(4, 2).Value = 500.0;
        ws.Cell(4, 3).Value = "RSD";

        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    private static void CreateDemoExpectedLayersExcel(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Expected Layers");

        string[] headers = { "Profile", "Layer name", "Role", "Match type", "Notes" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
        }

        ws.Cell(2, 1).Value = "Default_Consultant_Profile";
        ws.Cell(2, 2).Value = "PARCEL_BOUNDARY";
        ws.Cell(2, 3).Value = "Main parcel";
        ws.Cell(2, 4).Value = "exact";
        ws.Cell(2, 5).Value = "Main cadastral boundaries";

        ws.Cell(3, 1).Value = "Default_Consultant_Profile";
        ws.Cell(3, 2).Value = "SUB_PARCELS";
        ws.Cell(3, 3).Value = "Sub-parcel";
        ws.Cell(3, 4).Value = "exact";
        ws.Cell(3, 5).Value = "Corridors, gardens, utilities";

        ws.Cell(4, 1).Value = "Default_Consultant_Profile";
        ws.Cell(4, 2).Value = "PLOT_NUMBERS";
        ws.Cell(4, 3).Value = "Plot-number text";
        ws.Cell(4, 4).Value = "exact";
        ws.Cell(4, 5).Value = "Text labels for plots";

        ws.Cell(5, 1).Value = "Default_Consultant_Profile";
        ws.Cell(5, 2).Value = "LAND_USE";
        ws.Cell(5, 3).Value = "Usage text";
        ws.Cell(5, 4).Value = "exact";
        ws.Cell(5, 5).Value = "Usage labels";

        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }
}
