namespace Cad2Gis.Core.Models;

public class MappingProfileV2
{
    public string BoundaryLayer { get; set; } = "PARCEL_BOUNDARY";
    public string PlotNoTextLayer { get; set; } = "PLOT_NUMBERS";
    public string UsageTextLayer { get; set; } = "LAND_USE";
    public string DimensionsLayer { get; set; } = "DIMENSIONS";
    public string StreetLayer { get; set; } = "STREETS";
    public string ServiceLayer { get; set; } = "SERVICES";

    public bool UseArabicXArabDecoder { get; set; } = true;
    public bool OverwriteExistingGdbLayers { get; set; } = true;
    public bool ExportShapefilesOnStep4 { get; set; } = true;

    public string DefaultPlanNo { get; set; } = "200/ب/1420";
    public string DefaultNeighbourhood { get; set; } = "المروج";
    public string DefaultRequestType { get; set; } = "فرز";
    public string DefaultReportNo { get; set; } = "REP-01";
}
