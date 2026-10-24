using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Win32;
using ArcGIS.Core.Geometry;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core.Geoprocessing;
using Cad2Gis.AddIn.Geometry;
using Cad2Gis.AddIn.IO;
using Cad2Gis.AddIn.SpatialEnrichment;
using Cad2Gis.Core.Models;
using Cad2Gis.Core.Geometry;
using Cad2Gis.Core.Services;
using Cad2Gis.Core.Lookups;
using Cad2Gis.Core.Utils;

namespace Cad2Gis.AddIn.DockPane;

/// <summary>
/// Partial class for Cad2GisDockpaneViewModel - PIPELINE & COORDINATION
/// Coordinates running all 5 steps sequentially and showing step outputs on map.
/// </summary>
public partial class Cad2GisDockpaneViewModel
{
    // Backward-compatibility aliases
    public Task ExecuteStep3Async(bool isPartOfPipeline = false) => ExecuteStep4BordersAsync(isPartOfPipeline);
    public Task ExecuteStep4Async(bool isPartOfPipeline = false) => ExecuteStep5ExportQaAsync(isPartOfPipeline);

    #region Intermediate Step Exports

    // Every step has exactly ONE output Feature Class in the working geodatabase:
    //   Step 2: Cad_Constructed_Parcels, Step 3: PARCEL_Approved_Schema, Step 4: BORDER_Cardinal_Approved.
    // That Feature Class IS the export (no extra shapefiles or copies, and no separate "live" version).
    // These commands only make sure that single output is on the map.
    private async Task ShowStepOutputOnMapAsync(int step, string fcName)
    {
        IsBusy = true;
        try
        {
            string fcPath = Path.Combine(GdbPath ?? string.Empty, fcName);
            if (string.IsNullOrWhiteSpace(GdbPath) || !await GdbFeatureClassService.FeatureClassExistsAsync(fcPath))
            {
                ReportError($"Step {step} output '{fcName}' does not exist yet. Run Step {step} first.", $"Step {step} output missing");
                return;
            }

            await QueuedTask.Run(() =>
            {
                GdbFeatureClassService.AddOrRefreshMapLayer(MapView.Active?.Map, GdbPath, fcName, fcName);
            });
            ResetProgress($"Step {step} output '{fcName}' is on the map: {fcPath}", $"Step {step} output");
        }
        catch (Exception ex)
        {
            ReportError($"Step {step} output error: {ex.Message}", $"Step {step} output error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion


    public async Task ExecutePipelineAsync()
    {
        IsBusy = true;
        ReportProgress(5, "خط المعالجة: بدء التشغيل المتتابع", "تشغيل الخطوات من 1 إلى 5 بالكامل...", phase: "خط المعالجة");

        try
        {
            SelectedTabIndex = 0;
            ReportProgress(10, "خط المعالجة: المرحلة 1/5", "فحص واستيراد طبقات الكاد وتحويله لداتاسيت...", phase: "المرحلة 1 من 5");
            await ExecuteStep1Async();

            SelectedTabIndex = 1;
            ReportProgress(30, "خط المعالجة: المرحلة 2/5", "استخراج وبلمرة مضلعات البارسلات (2D Planar)...", phase: "المرحلة 2 من 5");
            await ExecuteStep2Async(isPartOfPipeline: true);

            SelectedTabIndex = 2;
            ReportProgress(50, "خط المعالجة: المرحلة 3/5", "تعبئة البيانات والسكيما المعتمدة وجداول الاستخدام...", phase: "المرحلة 3 من 5");
            await ExecuteStep3PopulateSchemaAsync(isPartOfPipeline: true);

            SelectedTabIndex = 3;
            ReportProgress(70, "خط المعالجة: المرحلة 4/5", "تفكيك الواجهات واستشعار الشوارع بالأشعة...", phase: "المرحلة 4 من 5");
            await ExecuteStep4BordersAsync(isPartOfPipeline: true);

            if (PauseAtReviewTables && InferredBorders.Count > 0)
            {
                ReportProgress(80, "توقف مؤقت للمراجعة", $"جاهز للمراجعة: {InferredBorders.Count} حد مستنتج. انقر 'الخطوة 5' للاستمرار والتصدير.", phase: "المرحلة 4 من 5");
                SelectedTabIndex = 3;
                return;
            }

            SelectedTabIndex = 4;
            ReportProgress(90, "خط المعالجة: المرحلة 5/5", "التصدير النهائي والتدقيق الشامل...", phase: "المرحلة 5 من 5");
            await ExecuteStep5ExportQaAsync(isPartOfPipeline: true);

            ResetProgress("🎉 اكتمل خط المعالجة المتتابع بالكامل بنجاح 100%!", "اكتملت جميع المراحل 100%");
        }
        catch (Exception ex)
        {
            ReportError($"Pipeline Error: {ex.Message}", "خطأ في خط المعالجة");
        }
        finally
        {
            IsBusy = false;
        }
    }

}
