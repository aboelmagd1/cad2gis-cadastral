using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

/// <summary>
/// Service to load and save consultant transformation profiles from/to Excel (.xlsx).
/// Allows consultants and GIS specialists to configure layer mappings and tolerances via familiar spreadsheets.
/// </summary>
public static class ProfileExcelService
{
    public static ConsultantProfileModel LoadProfileFromExcel(string excelPath)
    {
        var model = new ConsultantProfileModel();
        if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
            return model;

        try
        {
            using var workbook = new XLWorkbook(excelPath);
            var ws = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Profile_Settings", StringComparison.OrdinalIgnoreCase)) 
                     ?? workbook.Worksheets.FirstOrDefault();
            if (ws == null) return model;

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int r = 2; r <= lastRow; r++)
            {
                string rawKey = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(rawKey)) continue;

                // Strip any trailing parenthetical label like "SettingKey (مفتاح الإعداد)"
                string key = rawKey.Contains(" ") ? rawKey.Split(' ')[0].Trim() : rawKey;
                string val = ws.Cell(r, 2).GetString().Trim();
                settings[key] = val;
            }

            if (settings.TryGetValue("profileName", out var pName) && !string.IsNullOrWhiteSpace(pName)) model.ProfileName = pName;
            if (settings.TryGetValue("description", out var desc)) model.Description = desc;
            if (settings.TryGetValue("boundaryLayer", out var bLayer)) model.BoundaryLayer = bLayer;
            if (settings.TryGetValue("plotNoTextLayer", out var pLayer)) model.PlotNoTextLayer = pLayer;
            if (settings.TryGetValue("usageTextLayer", out var uLayer)) model.UsageTextLayer = uLayer;
            if (settings.TryGetValue("subParcelLayer", out var spLayer)) model.SubParcelLayer = spLayer;
            if (settings.TryGetValue("roadLayer", out var rLayer)) model.RoadLayer = rLayer;
            if (settings.TryGetValue("dimensionsLayer", out var dLayer)) model.DimensionsLayer = dLayer;
            if (settings.TryGetValue("directionSystem", out var dirSys)) model.DirectionSystem = dirSys;

            if (settings.TryGetValue("passageStreetThresholdMeters", out var pThresh) && 
                double.TryParse(pThresh, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                model.PassageStreetThresholdMeters = dVal;

            if (settings.TryGetValue("maxRaySearchDistanceMeters", out var mDist) && 
                double.TryParse(mDist, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dDist))
                model.MaxRaySearchDistanceMeters = dDist;

            if (settings.TryGetValue("rayCount", out var rCount) && int.TryParse(rCount, out int iCount))
                model.RayCount = iCount;

            if (settings.TryGetValue("rayMethod", out var rMethod)) model.RayMethod = rMethod;

            if (settings.TryGetValue("snapToleranceMeters", out var sTol) && 
                double.TryParse(sTol, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dTol))
                model.SnapToleranceMeters = dTol;

            if (settings.TryGetValue("mergeChamfers", out var mChamfer) && bool.TryParse(mChamfer, out bool bChamfer))
                model.MergeChamfers = bChamfer;

            if (settings.TryGetValue("chamferThresholdMeters", out var cThresh) && 
                double.TryParse(cThresh, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dCThresh))
                model.ChamferThresholdMeters = dCThresh;

            if (settings.TryGetValue("buildPolygonsFromLines", out var bPoly) && bool.TryParse(bPoly, out bool bPolyVal))
                model.BuildPolygonsFromLines = bPolyVal;

            if (settings.TryGetValue("defaultPlanNo", out var planNo)) model.DefaultPlanNo = planNo;
            if (settings.TryGetValue("defaultNeighbourhood", out var neigh)) model.DefaultNeighbourhood = neigh;
            if (settings.TryGetValue("defaultRequestType", out var reqTy)) model.DefaultRequestType = reqTy;
            if (settings.TryGetValue("defaultReportNo", out var repNo)) model.DefaultReportNo = repNo;

            if (settings.TryGetValue("utmEpsg", out var utm) && int.TryParse(utm, out int iUtm))
                model.UtmEpsg = iUtm;

            if (settings.TryGetValue("useArabicXArabDecoder", out var xArab) && bool.TryParse(xArab, out bool bXArab))
                model.UseArabicXArabDecoder = bXArab;

            if (settings.TryGetValue("reverseArabicText", out var revAr) && bool.TryParse(revAr, out bool bRevAr))
                model.ReverseArabicText = bRevAr;

            // Part A settings
            if (settings.TryGetValue("aMinDeg", out var aMinStr) && double.TryParse(aMinStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dAMin))
                model.AMinDeg = dAMin;
            if (settings.TryGetValue("aMaxDeg", out var aMaxStr) && double.TryParse(aMaxStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dAMax))
                model.AMaxDeg = dAMax;
            if (settings.TryGetValue("multiSegmentAngleDeviationDeg", out var msadStr) && double.TryParse(msadStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dMsad))
                model.MultiSegmentAngleDeviationDeg = dMsad;
            if (settings.TryGetValue("includeReflexCorners", out var refStr) && bool.TryParse(refStr, out bool bRef))
                model.IncludeReflexCorners = bRef;
            if (settings.TryGetValue("arcCenterToleranceMeters", out var actStr) && double.TryParse(actStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dAct))
                model.ArcCenterToleranceMeters = dAct;
            if (settings.TryGetValue("arcRadiusToleranceMeters", out var artStr) && double.TryParse(artStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dArt))
                model.ArcRadiusToleranceMeters = dArt;
            if (settings.TryGetValue("arcTangentToleranceDeg", out var attStr) && double.TryParse(attStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dAtt))
                model.ArcTangentToleranceDeg = dAtt;
            if (settings.TryGetValue("clusterLengthMeters", out var clStr) && double.TryParse(clStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dCl))
                model.ClusterLengthMeters = dCl;
            if (settings.TryGetValue("rule1LengthRatioGuard", out var r1Str) && double.TryParse(r1Str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dR1))
                model.Rule1LengthRatioGuard = dR1;
            if (settings.TryGetValue("trueNorthCorrection", out var tnStr) && bool.TryParse(tnStr, out bool bTn))
                model.TrueNorthCorrection = bTn;

            // Part B settings
            if (settings.TryGetValue("continueFromLatestExport", out var cfStr) && bool.TryParse(cfStr, out bool bCf))
                model.ContinueFromLatestExport = bCf;
            if (settings.TryGetValue("maxExportHistoryCount", out var mehStr) && int.TryParse(mehStr, out int iMeh))
                model.MaxExportHistoryCount = iMeh;

            // Geometry Preservation settings
            if (settings.TryGetValue("maxCurveExportDeviationMeters", out var devStr) && double.TryParse(devStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dDev))
                model.MaxCurveExportDeviationMeters = dDev;
            if (settings.TryGetValue("alsoKeepExactUtmGdbWithTrueCurves", out var gdbCurvStr) && bool.TryParse(gdbCurvStr, out bool bGdbCurv))
                model.AlsoKeepExactUtmGdbWithTrueCurves = bGdbCurv;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading profile Excel: {ex.Message}");
        }

        return model;
    }
}
