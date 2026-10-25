namespace Cad2Gis.Core.Models;

/// <summary>
/// Model encapsulating all configuration parameters for a consultant profile.
/// Can be loaded directly from Example_Profile.xlsx or JSON.
/// </summary>
public class ConsultantProfileModel
{
    public string ProfileName { get; set; } = "Default_Consultant_Profile";
    public string Description { get; set; } = "Standard cadastral transformation profile";
    public string BoundaryLayer { get; set; } = "PARCEL_BOUNDARY";
    public string PlotNoTextLayer { get; set; } = "PLOT_NUMBERS";
    public string UsageTextLayer { get; set; } = "LAND_USE";
    public string SubParcelLayer { get; set; } = "SUB_PARCELS";
    public string RoadLayer { get; set; } = "ROADS";
    public string DimensionsLayer { get; set; } = "DIMENSIONS";
    public string DirectionSystem { get; set; } = "MOMRAH"; // MOMRAH (1=N, 2=E, 3=S, 4=W) or ADDENDUM (1=N, 2=S, 3=E, 4=W)
    public double PassageStreetThresholdMeters { get; set; } = 10.0;
    public double MaxRaySearchDistanceMeters { get; set; } = 100.0;
    public int RayCount { get; set; } = 1;
    public double MinSharedSegmentLength { get; set; } = 0.5;
    public string RayMethod { get; set; } = "Median";
    public double SnapToleranceMeters { get; set; } = 0.05;
    public bool MergeChamfers { get; set; } = true;
    public double ChamferThresholdMeters { get; set; } = 7.0;
    public double ChamferAngleDeg { get; set; } = 45.0;
    public double ChamferAngleToleranceDeg { get; set; } = 15.0;
    public double AreaToleranceSqm { get; set; } = 0.5;
    public double AreaTolerancePercent { get; set; } = 0.1;
    public bool BuildPolygonsFromLines { get; set; } = false;
    public string DefaultPlanNo { get; set; } = string.Empty;
    public string DefaultNeighbourhood { get; set; } = string.Empty;
    public string DefaultRequestType { get; set; } = string.Empty;
    public string DefaultReportNo { get; set; } = string.Empty;
    public int UtmEpsg { get; set; } = 32637;
    public bool UseArabicXArabDecoder { get; set; } = true;
    public bool ReverseArabicText { get; set; } = false;

    // Part A: Border Extraction Settings
    public double AMinDeg { get; set; } = 10.0;
    public double AMaxDeg { get; set; } = 160.0;
    public double MultiSegmentAngleDeviationDeg { get; set; } = 15.0;
    public bool IncludeReflexCorners { get; set; } = true;
    public double ArcCenterToleranceMeters { get; set; } = 0.05;
    public double ArcRadiusToleranceMeters { get; set; } = 0.05;
    public double ArcTangentToleranceDeg { get; set; } = 2.0;
    public double ClusterLengthMeters { get; set; } = 7.0;
    public double Rule1LengthRatioGuard { get; set; } = 1.0;
    public bool TrueNorthCorrection { get; set; } = true;

    // Part B: Continue from Latest Export Settings
    public bool ContinueFromLatestExport { get; set; } = false;
    public int MaxExportHistoryCount { get; set; } = 10;

    // Geometry Preservation Settings
    public double MaxCurveExportDeviationMeters { get; set; } = 0.001;
    public bool AlsoKeepExactUtmGdbWithTrueCurves { get; set; } = false;
}
