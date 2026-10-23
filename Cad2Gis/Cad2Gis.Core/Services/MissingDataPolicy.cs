using Cad2Gis.Core.Models;
using Cad2Gis.Core.Lookups;

namespace Cad2Gis.Core.Services;

public static class MissingDataPolicy
{
    public static void ApplyDefaults(ParcelRecord parcel, ProjectMetadata meta)
    {
        ApplyDefaults(parcel, meta, false);
    }

    public static void ApplyDefaults(ParcelRecord parcel, ProjectMetadata meta, bool forceUpdate)
    {
        if (forceUpdate || string.IsNullOrWhiteSpace(parcel.PlanNo))
        {
            if (!string.IsNullOrWhiteSpace(meta.PlanNo))
                parcel.PlanNo = meta.PlanNo;
        }

        if (forceUpdate || string.IsNullOrWhiteSpace(parcel.Neighbourhood))
        {
            if (!string.IsNullOrWhiteSpace(meta.NeighbourhoodCode))
                parcel.Neighbourhood = meta.NeighbourhoodCode;
        }

        if (forceUpdate || string.IsNullOrWhiteSpace(parcel.RegionId))
        {
            if (!string.IsNullOrWhiteSpace(meta.RegionId))
                parcel.RegionId = meta.RegionId;
        }

        if (forceUpdate || string.IsNullOrWhiteSpace(parcel.CityId))
        {
            if (!string.IsNullOrWhiteSpace(meta.CityId))
                parcel.CityId = meta.CityId;
        }

        string fullAddress = meta.BuildAddress(meta.NeighbourhoodName);
        if (forceUpdate || string.IsNullOrWhiteSpace(parcel.Address) || (!string.IsNullOrWhiteSpace(fullAddress) && parcel.Address != fullAddress))
        {
            if (!string.IsNullOrWhiteSpace(fullAddress))
                parcel.Address = fullAddress;
        }

        if (!string.IsNullOrWhiteSpace(parcel.Usage) && string.IsNullOrWhiteSpace(parcel.Remarks))
            parcel.Remarks = MunicipalLookups.GetRemarksForUsage(parcel.Usage);

        if (string.IsNullOrWhiteSpace(parcel.PlotNo1))
            parcel.PlotNo1 = parcel.PlotId;

        if (forceUpdate || string.IsNullOrWhiteSpace(parcel.RequestType))
        {
            if (!string.IsNullOrWhiteSpace(meta.RequestType))
                parcel.RequestType = meta.RequestType;
        }

        if (forceUpdate || string.IsNullOrWhiteSpace(parcel.RequestNo))
        {
            if (!string.IsNullOrWhiteSpace(meta.RequestNo))
                parcel.RequestNo = meta.RequestNo;
        }

        if (forceUpdate || string.IsNullOrWhiteSpace(parcel.ReportNo))
        {
            if (!string.IsNullOrWhiteSpace(meta.ReportNo))
                parcel.ReportNo = meta.ReportNo;
        }
    }

    public static void ApplyDefaults(BorderRecord border, ParcelRecord parent)
    {
        if (string.IsNullOrWhiteSpace(border.PlotId))
            border.PlotId = parent.PlotId;

        if (string.IsNullOrWhiteSpace(border.PlanNo))
            border.PlanNo = parent.PlanNo;

        if (border.BorderDesc <= 0 && border.ShapeLength > 0)
            border.BorderDesc = (long)System.Math.Round(border.ShapeLength);

        if (string.IsNullOrWhiteSpace(border.Dimension))
            border.Dimension = "Single";
    }
}
