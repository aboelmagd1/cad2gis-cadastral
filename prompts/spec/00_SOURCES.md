# CAD2GIS — Requirements Inventory & Sources Directory (00_SOURCES.md)

> **الملخص التنفيذي بالعربية:**
> يوثق هذا الملف الفهرس الشامل لجميع مصادر المتطلبات في مشروع CAD2GIS، بما يشمل ملفات التوجيهات السابقة (Prompts)، والكود المصدري الفعلي، ومشاريع الاختبارات، وقوالب وملفات المساعدة (HelperFiles). كما يوضح خريطة التحول من الملفات القديمة إلى المواصفات المعتمدة الجديدة (Old-to-New Map) وقواعد الأسبقية المعمارية لفض أي تعارض.

---

## 1. Inventory of Requirement Sources

### 1.1 Prompts & Architectural Guides (`prompts/`)

| File Path | Version / Date | One-Line Purpose | Status |
| :--- | :--- | :--- | :--- |
| `prompts/CAD2GIS_ArcGISPro_Addin_Plan_and_Prompt.md` | v4.4 (Historical) | Initial comprehensive master architectural plan and execution blueprint for the ArcGIS Pro Add-in. | SUPERSEDED by `prompts/spec/` |
| `prompts/CAD2GIS_Step3_Step4_Business_Rules_Prompt_v2.md` | v2 (Owner Reviewed) | Authoritative business rules for Step 3 USAGE/REMARKS and Step 4 shared segments, single ray, and border types. | MERGED into `05_STEP3_ATTRIBUTES_SPEC.md` & `06_STEP4_BORDERS_SPEC.md` |
| `prompts/CAD2GIS_Working_Data_Source_Prompt.md` | Latest Owner Rule | Establishes the 2-source options rule (Previous step vs Own data) and single output copy on disk. | MERGED into `02_PIPELINE_AND_DATA_FLOW.md` |
| `prompts/CAD2GIS_Border_Extraction_Prompt.md` | v3.6 (Historical) | Early border extraction specification for polygon boundary decomposition into cardinal lines. | SUPERSEDED by `06_STEP4_BORDERS_SPEC.md` |
| `prompts/CAD2GIS_Borders_and_Export_Continuity_Prompt.md` | v4.4 (Historical) | Intermediate specification for border extraction and export continuity (contained legacy latest-export mode). | SUPERSEDED by `06_STEP4_BORDERS_SPEC.md` & `07_STEP5_EXPORT_QA_SPEC.md` |
| `prompts/CAD2GIS_Lines_To_Polygons_FeatureToPolygon_Prompt.md` | v4.2 | Specifications for ArcPy FeatureToPolygon integration with strict 2D planar environment flags. | MERGED into `04_STEP2_PARCELS_SPEC.md` |
| `prompts/CAD2GIS_Performance_ReviewFixes_NewFeatures_Prompt.md` | v4.5 | Performance optimizations, spatial index caching, and review fixes across pipeline steps. | MERGED into `02_PIPELINE_AND_DATA_FLOW.md` & `06_STEP4_BORDERS_SPEC.md` |
| `prompts/CAD2GIS_Review_Fixes_Prompt.md` | v4.3 | Specific defect fixes for curve preservation, tolerance guards, and UI responsiveness. | MERGED into `02_PIPELINE_AND_DATA_FLOW.md` & `04_STEP2_PARCELS_SPEC.md` |
| `prompts/CAD2GIS_Addendum_Prompt.md` | v3.2 | Early production addendum covering dark theme UI and dynamic Excel layer loading. | SUPERSEDED by `03_STEP1_LAYERS_SPEC.md` & `10_UI_SPEC.md` |
| `prompts/CHANGELOG_MODIFICATIONS.md` | v4.4 (Living) | Detailed historical modification changelog and master definition of Immutable Invariants (INV-01 to INV-08). | ACTIVE (Historical reference) |
| `prompts/USAGE_GUIDE.md` | v4.4 (User-Facing) | Comprehensive end-user operations manual detailing how each tool and button in the Dockpane functions. | ACTIVE (User Manual) |
| `prompts/convert.md` | v4.2 | ArcPy FeatureToPolygon reference script specification for strict 2D planar polygon building. | MERGED into `04_STEP2_PARCELS_SPEC.md` |
| `prompts/gui.md` | v4.4 | Comprehensive UI/UX specification, color palette tokens, grid layout, and button bindings. | MERGED into `10_UI_SPEC.md` |
| `prompts/promt 01010.md` | v4.4 | 10 foundational architectural rules approved by project owner for production compliance. | MERGED into `01_PRODUCT_OVERVIEW.md` & `02_PIPELINE_AND_DATA_FLOW.md` |
| `prompts/CAD2GIS_Dockpane_Buttons_Guide_Reviewed.xlsx` | Latest Review | Reviewed button-by-button command reference, UI binding, and acceptance criteria for all 5 steps. | ACTIVE (UI Reference spreadsheet) |
| `prompts/CAD2GIS_Dockpane_Buttons_Guide.xlsx` | Draft | Earlier unreviewed draft of Dockpane button mapping. | SUPERSEDED by `CAD2GIS_Dockpane_Buttons_Guide_Reviewed.xlsx` |
| `prompts/steps/Step1_CAD_Read_and_Inspection_Prompt.md` | Step 1 Prompt | Detailed instructions for Step 1 CAD ingestion, CRS selection, and layer classification. | MERGED into `03_STEP1_LAYERS_SPEC.md` |
| `prompts/steps/Step2_Polygonization_and_Parcel_Extraction_Prompt.md` | Step 2 Prompt | Detailed instructions for Step 2 parcel polygonization, curve preservation, and text joins. | MERGED into `04_STEP2_PARCELS_SPEC.md` |
| `prompts/steps/Step3_Municipal_Schema_and_Data_Population_Prompt.md` | Step 3 Prompt | Detailed instructions for Step 3 MOMRAH 26-field schema population and Excel reconciliation. | MERGED into `05_STEP3_ATTRIBUTES_SPEC.md` |
| `prompts/steps/Step4_Facade_Decomposition_and_Street_Sensing_Prompt.md` | Step 4 Prompt | Detailed instructions for Step 4 facade decomposition, ray casting, and border dimensions. | MERGED into `06_STEP4_BORDERS_SPEC.md` |
| `prompts/steps/Step5_QA_Validation_and_Final_Export_Prompt.md` | Step 5 Prompt | Detailed instructions for Step 5 deliverables packaging, shapefile export, and QA validation. | MERGED into `07_STEP5_EXPORT_QA_SPEC.md` |

---

### 1.2 Solution Codebase & Projects (`Cad2Gis/`)

| Project / Component | Target Framework | One-Line Purpose |
| :--- | :--- | :--- |
| `Cad2Gis/Cad2Gis.sln` | Visual Studio 2022 | Solution root containing AddIn, Core, and Tests projects. |
| `Cad2Gis/Cad2Gis.Core/` | `net8.0` | Pure C# domain models, geometric algorithms, Excel/CSV parsers, and QA engines (100% testable without ArcGIS Pro). |
| `Cad2Gis/Cad2Gis.AddIn/` | `net8.0-windows` | ArcGIS Pro 3.3.0 Add-in (.NET 8 WPF MVVM), Geodatabase I/O, ArcGIS Pro SDK integrations, and ribbon commands. |
| `Cad2Gis/Cad2Gis.Tests/` | `net8.0` | Automated xUnit test suite (240 tests) verifying all core algorithms with synthetic fictional datasets. |
| `Cad2Gis/Cad2Gis.AddIn/Config.daml` | ArcGIS DAML Schema | Declarative Add-in registration: Tab (`Cad2Gis_Tab`), 3 Ribbon Groups, 7 Buttons, and 1 Dockpane (`Cad2Gis_Dockpane`). |
| `Cad2Gis/package_addin.ps1` | PowerShell | Zero-click staging script compiling `Cad2Gis.sln` and packaging root `Cad2Gis.esriAddinX` without auto-install. |

---

### 1.3 Helper Files, Lookups & Templates (`HelperFiles/`)

| File / Subfolder | Format | One-Line Purpose |
| :--- | :--- | :--- |
| `HelperFiles/Required Fields in the Shapefile.xlsx` | Excel (.xlsx) | Authoritative master schema dictionary: `Parcel_Table` (26 fields), `Border_Table` (10 fields), `Border Types` (68 codes), `Building Types` (4 codes), `Usage Types` (6 codes), `CITY_LOOKUP` (179 cities). |
| `HelperFiles/PARCEs_landuse.xlsx` | Excel (.xlsx) | Optional external tabular enrichment source matching `PLOT_ID` to `BLOCK_NO`, `REMARKS`, `USAGE`, `PLAN_NO`, `AREA_SQM`. |
| `HelperFiles/settings.json` | JSON | Persistent workstation user preferences (default tolerances, angles, language, retention days, ray parameters). |
| `HelperFiles/lookups/BORDERTYPE.json` | JSON | Code-to-Arabic description dictionary for all 68 valid Balady border types. |
| `HelperFiles/lookups/BORDERNAME.json` | JSON | Code-to-name mapping for 4 cardinal directions (1=North, 2=East, 3=South, 4=West). |
| `HelperFiles/lookups/USAGE.json` | JSON | Official Balady usage codes (`RSD`, `COM`, `RSC`, `AGR`, `IND`, `UTL`) and Arabic translations. |
| `HelperFiles/lookups/BUILDINGTY.json` | JSON | Official Balady building construction types (1 to 4). |
| `HelperFiles/lookups/BorderType_Keywords.csv` | CSV (UTF-8) | Editable lookup table matching neighbour text patterns to valid `BorderTypeCode`s. |
| `HelperFiles/lookups/Usage_Keywords.csv` | CSV (UTF-8) | Editable lookup table matching parcel text annotations to official `UsageCode`s. |
| `HelperFiles/lookups/standard_widths.json` | JSON | Array of standard municipal street and passage widths (4.0m to 60.0m) used for suggestion scoring. |
| `HelperFiles/lookups/cities.json` | JSON | Comprehensive administrative lookup linking city IDs, names, and regional jurisdictions. |
| `HelperFiles/templates/PARCEL.shp` (+ dbf, shx, prj, cpg) | ESRI Shapefile | Master blank template for PARCEL layer defining field names, lengths, and MOMRAH types. |
| `HelperFiles/templates/border.shp` (+ dbf, shx, prj, cpg) | ESRI Shapefile | Master blank template for BORDER layer defining 10 approved schema fields (`DESCRIPTO`). |
| `HelperFiles/expected_layers/Expected_Layers.xlsx` | Excel (.xlsx) | Dynamic catalog mapping standard CAD layer names to cadastral categories and auto-assignment roles. |
| `HelperFiles/adminareas/` | Shapefiles / GeoJSON | Administrative boundary boundaries for Riyadh, Makkah, Eastern Province, etc., for auto-detecting City/Region IDs. |
| `HelperFiles/Cad2Gis_ArcPy.pyt` | Python Toolbox | ArcPy fallback toolbox providing geoprocessing tools for LineToPolygon, WidthInference, and full pipeline execution. |
| `HelperFiles/state/pipeline_state.json` | JSON | Authoritative pipeline registry tracking verified single output paths, feature counts, and content stamps across steps. |

---

## 2. Old-to-New Document Transformation Map

| Legacy Source Document | Action Taken | Master Consolidated Target File |
| :--- | :--- | :--- |
| `CAD2GIS_ArcGISPro_Addin_Plan_and_Prompt.md` | Archiving to `_archive/` | `01_PRODUCT_OVERVIEW.md`, `02_PIPELINE_AND_DATA_FLOW.md`, `08_DATA_DICTIONARY_AND_LOOKUPS.md` |
| `CAD2GIS_Step3_Step4_Business_Rules_Prompt_v2.md` | Maintained in `prompts/` with header | `05_STEP3_ATTRIBUTES_SPEC.md`, `06_STEP4_BORDERS_SPEC.md` |
| `CAD2GIS_Working_Data_Source_Prompt.md` | Maintained in `prompts/` with header | `02_PIPELINE_AND_DATA_FLOW.md` |
| `CAD2GIS_Border_Extraction_Prompt.md` | Archiving to `_archive/` | `06_STEP4_BORDERS_SPEC.md` |
| `CAD2GIS_Borders_and_Export_Continuity_Prompt.md` | Archiving to `_archive/` | `06_STEP4_BORDERS_SPEC.md`, `07_STEP5_EXPORT_QA_SPEC.md` |
| `CAD2GIS_Lines_To_Polygons_FeatureToPolygon_Prompt.md`| Archiving to `_archive/` | `04_STEP2_PARCELS_SPEC.md` |
| `CAD2GIS_Performance_ReviewFixes_NewFeatures_Prompt.md`| Archiving to `_archive/` | `02_PIPELINE_AND_DATA_FLOW.md`, `06_STEP4_BORDERS_SPEC.md` |
| `CAD2GIS_Review_Fixes_Prompt.md` | Archiving to `_archive/` | `02_PIPELINE_AND_DATA_FLOW.md`, `04_STEP2_PARCELS_SPEC.md` |
| `CAD2GIS_Addendum_Prompt.md` | Archiving to `_archive/` | `03_STEP1_LAYERS_SPEC.md`, `10_UI_SPEC.md` |
| `convert.md` | Archiving to `_archive/` | `04_STEP2_PARCELS_SPEC.md` |
| `gui.md` | Maintained in `prompts/` with header | `10_UI_SPEC.md` |
| `promt 01010.md` | Archiving to `_archive/` | `01_PRODUCT_OVERVIEW.md`, `02_PIPELINE_AND_DATA_FLOW.md` |
| `Step1_CAD_Read_and_Inspection_Prompt.md` | Maintained in `steps/` with header | `03_STEP1_LAYERS_SPEC.md` |
| `Step2_Polygonization_and_Parcel_Extraction_Prompt.md`| Maintained in `steps/` with header | `04_STEP2_PARCELS_SPEC.md` |
| `Step3_Municipal_Schema_and_Data_Population_Prompt.md`| Maintained in `steps/` with header | `05_STEP3_ATTRIBUTES_SPEC.md` |
| `Step4_Facade_Decomposition_and_Street_Sensing_Prompt.md`| Maintained in `steps/` with header | `06_STEP4_BORDERS_SPEC.md` |
| `Step5_QA_Validation_and_Final_Export_Prompt.md` | Maintained in `steps/` with header | `07_STEP5_EXPORT_QA_SPEC.md` |
| `CAD2GIS_Dockpane_Buttons_Guide.xlsx` | Archiving to `_archive/` | `CAD2GIS_Dockpane_Buttons_Guide_Reviewed.xlsx` & `10_UI_SPEC.md` |

---

## 3. Conflict Resolution & Precedence Rules

When statements between historical files and specifications conflict, the following **strict order of precedence** MUST govern:

1. **Tier 1 (Highest Authority):** Explicit product owner decisions documented in `13_DECISIONS_LOG.md` (D01 through D20, newest first).
2. **Tier 2 (Authoritative Overriding Prompts):** Latest owner prompts:
   - `CAD2GIS_Step3_Step4_Business_Rules_Prompt_v2.md`
   - `CAD2GIS_Working_Data_Source_Prompt.md`
   - Prompt for Step 3 `PLOT_ID` from `PLOT_TXT`.
3. **Tier 3 (Authoritative Code Facts):** The active local C# codebase in `Cad2Gis/` (exact class names, namespaces, and runtime logic) where owner specifications are silent.
4. **Tier 4 (Living Documentation):** `CHANGELOG_MODIFICATIONS.md` (for master immutable invariants INV-01 through INV-08) and `gui.md`.
5. **Tier 5 (Legacy Prompts):** Older historical prompts (`CAD2GIS_ArcGISPro_Addin_Plan_and_Prompt.md`, `CAD2GIS_Borders_and_Export_Continuity_Prompt.md`, etc.). Any statements in Tier 5 that contradict Tiers 1–3 are completely superseded.
