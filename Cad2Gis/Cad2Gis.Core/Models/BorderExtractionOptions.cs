namespace Cad2Gis.Core.Models;

/// <summary>
/// Configuration options and tolerances for the CAD2GIS Part A Border Extraction Method.
/// </summary>
public class BorderExtractionOptions
{
    /// <summary>
    /// Minimum interior angle (degrees) measured from inside the parcel to qualify as a corner split point.
    /// Default: 10.0°.
    /// </summary>
    public double AMinDeg { get; set; } = 10.0;

    /// <summary>
    /// Maximum interior angle (degrees) measured from inside the parcel to qualify as a corner split point.
    /// Default: 160.0°.
    /// </summary>
    public double AMaxDeg { get; set; } = 160.0;

    /// <summary>
    /// Angular deviation tolerance (degrees) from 180° between consecutive segments in a merged border
    /// to define whether the border is "Multi" or "Single".
    /// If the angle between segments deviates from 180° by more than this tolerance (i.e. angle < 170° or > 190°),
    /// the border is considered "Multi"; otherwise "Single".
    /// Default: 10.0°.
    /// </summary>
    public double MultiSegmentAngleDeviationDeg { get; set; } = 10.0;

    /// <summary>
    /// If true, reflex (concave) corners with interior angles in [360 - AMaxDeg, 360 - AMinDeg]
    /// (default 230°..340°) are also treated as split points.
    /// Default: true.
    /// </summary>
    public bool IncludeReflexCorners { get; set; } = true;

    /// <summary>
    /// Tolerance in meters for comparing arc centers when checking for redundant joints.
    /// Default: 0.05m.
    /// </summary>
    public double ArcCenterToleranceMeters { get; set; } = 0.05;

    /// <summary>
    /// Tolerance in meters for comparing arc radii when checking for redundant joints.
    /// Default: 0.05m.
    /// </summary>
    public double ArcRadiusToleranceMeters { get; set; } = 0.05;

    /// <summary>
    /// Tolerance in degrees for comparing arc tangent directions at joints to identify smooth compound curves.
    /// Default: 2.0°.
    /// </summary>
    public double ArcTangentToleranceDeg { get; set; } = 2.0;

    /// <summary>
    /// Maximum distance (meters) between consecutive vertices to be clustered as one corner (chamfers / rounded corners).
    /// Default: 7.0m.
    /// </summary>
    public double ClusterLengthMeters { get; set; } = 7.0;

    /// <summary>
    /// Length ratio guard for Rule 1: an in-between run of direction Y must be shorter than both flanking X runs
    /// multiplied by this ratio (default: 1.0).
    /// </summary>
    public double Rule1LengthRatioGuard { get; set; } = 1.0;

    /// <summary>
    /// If true, UTM grid azimuths are corrected by the meridian convergence at the parcel centroid to follow true north.
    /// Default: true.
    /// </summary>
    public bool TrueNorthCorrection { get; set; } = true;
}
