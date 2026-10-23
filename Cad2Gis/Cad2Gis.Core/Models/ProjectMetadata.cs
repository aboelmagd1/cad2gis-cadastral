namespace Cad2Gis.Core.Models;

public class ProjectMetadata
{
    public string PlanNo { get; set; } = string.Empty;
    private string _neighbourhoodCode = string.Empty;
    private string _neighbourhoodName = string.Empty;

    /// <summary>
    /// Neighbourhood Code (كود الحي) - Stored strictly in the NEIGHBOURH field of PARCEL (e.g. "101", "01").
    /// </summary>
    public string NeighbourhoodCode
    {
        get => _neighbourhoodCode;
        set => _neighbourhoodCode = value;
    }

    /// <summary>
    /// Neighbourhood Name (اسم الحي) - Used strictly in the generated ADDRESS string: حي/{NeighbourhoodName}/ب{CityName}/بمنطقة/{RegionName}
    /// </summary>
    public string NeighbourhoodName
    {
        get => _neighbourhoodName;
        set => _neighbourhoodName = value;
    }

    /// <summary>
    /// Backward-compatibility accessor mapping: sets both code and name.
    /// </summary>
    public string Neighbourhood
    {
        get => _neighbourhoodCode;
        set
        {
            _neighbourhoodCode = value;
            _neighbourhoodName = value;
        }
    }

    public string RequestType { get; set; } = string.Empty;
    public string RequestNo { get; set; } = string.Empty;
    public string ReportNo { get; set; } = string.Empty;

    public string RegionId { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public string CityId { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;

    /// <summary>
    /// Builds the standard Saudi cadastral address:
    /// حي/{اسم الحي}/ب{اسم المدينة}/بمنطقة/{اسم المنطقة}
    /// </summary>
    public string BuildAddress(string? customNeighbourhoodName = null)
    {
        string n = !string.IsNullOrWhiteSpace(customNeighbourhoodName)
            ? customNeighbourhoodName
            : (!string.IsNullOrWhiteSpace(NeighbourhoodName) ? NeighbourhoodName : string.Empty);
        string city = !string.IsNullOrWhiteSpace(CityName) ? CityName : string.Empty;
        string reg = !string.IsNullOrWhiteSpace(RegionName) ? RegionName : string.Empty;

        if (string.IsNullOrWhiteSpace(n) && string.IsNullOrWhiteSpace(city) && string.IsNullOrWhiteSpace(reg))
            return string.Empty;

        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(n)) parts.Add($"حي/{n}");
        if (!string.IsNullOrWhiteSpace(city)) parts.Add($"ب{city}");
        if (!string.IsNullOrWhiteSpace(reg)) parts.Add($"بمنطقة/{reg}");

        return string.Join("/", parts);
    }
}
