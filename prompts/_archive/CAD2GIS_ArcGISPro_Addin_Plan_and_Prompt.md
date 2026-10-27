---
Status: SUPERSEDED by prompts/spec/
Date: 2026-10-27
Notes: Fully superseded and consolidated into authoritative specification suite (prompts/spec/00_INDEX.md through 14_REBUILD_PLAYBOOK.md). Retained in archive for historical record.
---

# CAD2GIS as an ArcGIS Pro 3.3.0 Add-in — Plan & Execution Prompt (Production v4.4)
## Native Geodatabase Feature Classes Engine, 5-Step Pipeline, Smart Schema Auto-Mapping & Deliverables Exporter

**Contents**
- [Part 1 — Implementation Plan](#part-1--implementation-plan)
  - [1. Executive Summary & Core Objective](#1-executive-summary--core-objective)
  - [2. System Inputs, Reference Benchmarks & Workspace Structure (`helpers` & `example`)](#2-system-inputs-reference-benchmarks--workspace-structure-helpers--example)
  - [3. Smart Spatial Enrichment Engine & Adapting Administrative Data (`helpers/adminareas`)](#3-smart-spatial-enrichment-engine--adapting-administrative-data-helpersadminareas)
  - [4. Cadastral Domain Rules & 4-Cardinal Boundary Model (`example`)](#4-cadastral-domain-rules--4-cardinal-boundary-model-example)
  - [5. High-Level Architectural Pipeline (Feature Class Native + Multi-Format Export)](#5-high-level-architectural-pipeline-feature-class-native--multi-format-export)
  - [6. English UI, DAML Ribbon Configuration & MVVM DockPane Workflow](#6-english-ui-daml-ribbon-configuration--mvvm-dockpane-workflow)
  - [7. Engineering Phases, Testing & Deployment Automation](#7-engineering-phases-testing--deployment-automation)
  - [8. Risk Analysis & Mitigation Matrix](#8-risk-analysis--mitigation-matrix)
- [Part 2 — Execution Master Prompt](#part-2--execution-master-prompt)
  - [0. Target Platform & Technical Constraints](#0-target-platform--technical-constraints)
  - [1. Visual Studio Solution Architecture (`Cad2Gis.sln`)](#1-visual-studio-solution-architecture-cad2gissln)
  - [2. Core Domain Models & Embedded Dictionaries (`Cad2Gis.Core`)](#2-core-domain-models--embedded-dictionaries-cad2giscore)
  - [3. Arabic CAD Font & Annotation Decoder (`XArabDecoder.cs`)](#3-arabic-cad-font--annotation-decoder-xarabdecodercs)
  - [4. Spatial Enrichment & Admin Area Detection (`AdminAreaDetector.cs`)](#4-spatial-enrichment--admin-area-detection-adminareadetectorcs)
  - [5. Stage 2: Parcel Construction, Layer Scopes, Line Polygonization & Geometry Preservation (`PARCEL`)](#5-stage-2-parcel-construction-layer-scopes-line-polygonization--native-gdb-feature-class-parcel)
  - [6. Stage 3: Populate Schema & Custom Schema Field Mapping Engine (`CustomSchemaDataService.cs` & `CustomSchemaDataHelper.cs`)](#6-stage-3-4-cardinal-facade-decomposer--gdb-feature-class-border)
  - [7. Stage 4: 4-Cardinal Facade Decomposer & GDB Feature Class (`Border`)](#7-native-geodatabase-feature-class-service-gdbfeatureclassservicecs)
  - [8. Native Geodatabase Feature Class Service (`GdbFeatureClassService.cs`)](#8-stage-4-optional-shapefile-exporter--qaqc-audit-engine)
  - [9. Stage 5: Shapefile Exporter, Delivery Packaging & QA/QC Audit Engine](#9-arcgis-pro-daml-configuration--english-mvvm-dockpane-ui)
  - [10. Automated Packaging Script (`package_addin.ps1`)](#10-automated-packaging--zero-click-deployment-script-package_addinps1)
  - [11. Production Addendum (v3.2) — Unified Dark Workstation Theme & Dynamic Excel Layers Loading](#11-production-addendum-v32--unified-dark-workstation-theme--dynamic-excel-layers-loading)
  - [12. Production Addendum (v3.3) — Polygon Layer Creation Scopes, 4-Tier CAD Feature Layer Discovery & Line Polygonization](#12-production-addendum-v33--polygon-layer-creation-scopes-4-tier-cad-feature-layer-discovery--line-polygonization)
  - [13. Production Addendum (v3.4) — High-Performance Polygonizer, Resilient Final Export & Auto-Numbered Delivery Folder](#13-production-addendum-v34--high-performance-polygonizer-resilient-final-export--auto-numbered-delivery-folder)
  - [14. Production Addendum (v3.5) — Custom Schema Ingestion, Automated Schema Verification & Instant Cadastral Field Mapping Engine](#14-production-addendum-v35--custom-schema-ingestion-automated-schema-verification--instant-cadastral-field-mapping-engine)
  - [15. Production Addendum (v3.6) — Border Extraction Method from Main Parcel Boundaries (Part A)](#15-production-addendum-v36--border-extraction-method-from-main-parcel-boundaries-part-a)
  - [16. Production Addendum (v3.7) — "Continue from Latest Export" Workflow & Manual Edits Preservation (Part B)](#16-production-addendum-v37--continue-from-latest-export-workflow--manual-edits-preservation-part-b)
  - [17. Production Addendum (v3.8) — ArcGIS Pro SDK Non-Blocking Anti-Freeze Architecture & Threading Standards](#17-production-addendum-v38--arcgis-pro-sdk-non-blocking-anti-freeze-architecture--threading-standards)
  - [18. Production Addendum (v3.9) — Line Mode Ingestion of All Selected Layers & Universal Working Data Source Across All Steps](#18-production-addendum-v39--line-mode-ingestion-of-all-selected-layers--universal-working-data-source-across-all-steps)
  - [19. Production Addendum (v4.0) — Strict Geometry Preservation & Zero Arc-to-Line Conversion Standard](#19-production-addendum-v40--strict-geometry-preservation--zero-arc-to-line-conversion-standard)
  - [20. Production Addendum (v4.1) — Elimination of Fake Files & Dynamic Expected Layers Excel Sync](#20-production-addendum-v41--elimination-of-fake-files--dynamic-expected-layers-excel-sync)
  - [21. Production Addendum (v4.2) — ArcPy Feature To Polygon 2D Integration (convert.md)](#21-production-addendum-v42--arcpy-feature-to-polygon-2d-integration-convertmd)
  - [22. Production Addendum (v4.3) — Structured Delivery Hierarchy (Final/Final.gdb, Shape, Reports) & Live Map Display](#22-production-addendum-v43--structured-delivery-hierarchy-finalfinalgdb-shape-reports--live-map-display)
  - [23. Production Addendum (v4.4) — Curvature-Preserving Arc Extension & Unified Multi-Layer Network (148 Tests)](#23-production-addendum-v44--curvature-preserving-arc-extension--unified-multi-layer-network-148-tests)
  - [24. Production Addendum (v4.5) — Reviewed DockPane UI Reference (CAD2GIS_Dockpane_Buttons_Guide_Reviewed.xlsx)](#24-production-addendum-v45--reviewed-dockpane-ui-reference)
  - [25. Production Addendum (v4.6) — Master Immutable Invariants & 10 Core Architectural Requirements](#25-production-addendum-v46--master-immutable-invariants--10-core-architectural-requirements)

---

# Part 1 — Implementation Plan

## 1. Executive Summary & Core Objective
Deliver **CAD2GIS** as an enterprise-grade C# Add-in for **ArcGIS Pro 3.3.0** (.NET 8, ArcGIS Pro SDK for .NET). The tool automates the transformation of raw AutoCAD subdivision layouts (`DWG`/`DXF`) into fully compliant, topologically verified Saudi Municipal Cadastral GIS Datasets compliant with the Saudi Ministry of Municipal and Rural Affairs & Housing (MOMRAH / Balady / Amanat) standards.

### Fundamental Architectural Paradigm (v2.5):
1. **Native Feature Classes as the Primary Core Workflow**:
   - Unlike legacy script-based workflows that manipulate flat files, all intermediate and primary processing is performed directly on **native Feature Classes** inside a File Geodatabase (`.gdb`) — utilizing `Project.Current.DefaultGeodatabasePath` or an automated project workspace GDB (`Cad2Gis_Workspace.gdb`).
   - Intermediate outputs (`PARCEL` polygon feature class and `Border` polyline feature class) are instantly committed to the Geodatabase and displayed directly as live layers on the active ArcGIS Pro map (`MapView.Active.Map`).
2. **Optional Final Shapefile Export**:
   - In the final delivery stage (Step 4), the tool provides a selectable toggle:
     `[✔] Export to ESRI Shapefiles (PARCEL.shp & border.shp + UTF-8 .cpg)`.
   - If checked: generates the formal GCS WGS84 shapefiles accompanied by UTF-8 `.cpg` files for Balady submission.
   - If unchecked: preserves deliverables strictly inside the enterprise Geodatabase.
3. **Independent Stepper Execution or Full Pipeline**:
   - Users can execute each step independently (1. Scan Layers, 2. Build Parcels, 3. Build Borders, 4. Export & QA) with custom inputs, or run the entire pipeline sequentially with a single click.
4. **Modern English Interface with Instant Ribbon Responsiveness**:
   - Fully localized in English with standard Left-to-Right (LTR) presentation.
   - Ribbon controls are registered with `loadOnClick="true"` and robust DAML namespace resolution to ensure zero grayed-out or non-responsive buttons upon startup.

---

## HelperFiles Resolution Order & Runtime Management
The Add-in implements a strict 3-tier runtime resolution order for all templates, lookups, and administrative boundaries:
1. User-configured custom path set in Settings.
2. `<ArcGIS Pro Project Folder>\HelperFiles` (`Project.Current.HomeFolderPath`).
3. `%LOCALAPPDATA%\CAD2GIS\HelperFiles` (fallback when project is unsaved, accompanied by a visible UI warning).

On first run, the seed HelperFiles packaged with the Add-in are bootstrapped idempotently into the target location, copying missing files only and never overwriting user-modified files.

## 2. System Inputs, Reference Benchmarks & Workspace Structure (`helpers` & `example`)

The solution is purposefully wired to consume baseline templates from `helpers/` and validate outputs against the real-world production benchmark in `example/`:

```
<Workspace Root>/
├── helpers/                                 # Baseline Schemas, Templates & Reference Lookups
│   ├── Templet/
│   │   ├── PARCEL.shp                       # Official 26-attribute empty schema template
│   │   └── border.shp                       # Official 9-attribute empty schema template
│   ├── Required Fields in the Shapefile.xlsx# MOMRAH standards authority:
│   │   ├── Parcel_Table (26 fields: types, lengths, nullability)
│   │   ├── Border_Table (9 fields: types, lengths, nullability)
│   │   ├── Border Types (68 official codes: STR, PLT, SRV, WCH, LNE, PRK, MSQ...)
│   │   ├── Building Types (1..4: مسلح حديث, مسلح قديم, شعبي, مهدم)
│   │   ├── Usage Types (RSD, COM, RSC, Mixed Use, AGR, IND, UTL)
│   │   └── CITY_LOOKUP (172 Saudi cities with official IDs)
│   ├── PARCEs_landuse.xlsx                  # External Land-Use tabular template
│   └── adminareas/                          # Spatial Boundaries for Automated Enrichment
│       ├── regons.shp                       # 13 Saudi Administrative Regions (REGION_ID, REG_NAME)
│       └── cities.shp                       # 149 Saudi Cities & Governorates (CITY_ID, CITY_NAME, REGION_ID)
│
├── example/                                 # Reference Benchmark Dataset
│   ├── sample_plan.dwg                      # Reference CAD drawing with benchmark parcels
│   ├── PARCEs_landuse.xlsx                  # Reference land-use table with parcel metadata
│   └── reference_deliverables/              # Official Reference Deliverables
│       ├── Parcel.shp                       # Reference parcels with complete municipal attributes
│       └── Border.shp                       # Reference boundary records (4 cardinal facades per main parcel)
│
└── Cad2Gis/                                 # The Production Add-in Solution
    ├── Cad2Gis.sln
    ├── Cad2Gis.Core/                        # Standalone Domain Engine (.NET 8)
    ├── Cad2Gis.AddIn/                       # ArcGIS Pro 3.3 Add-in (WPF, Pro SDK, DAML)
    └── Cad2Gis.Tests/                       # Automated Unit & Integration Tests (Automated Suite Passing)
```

---

## 3. Smart Spatial Enrichment Engine & Adapting Administrative Data (`helpers/adminareas`)

To eliminate human error in entering administrative codes, the Add-in integrates an automated **Spatial Enrichment Engine**:
- **Automatic Spatial Query**: When CAD parcel boundaries are formed in UTM, their planar centroid is projected against `helpers/adminareas/cities.shp` and `helpers/adminareas/regons.shp`.
- **Automatic Multi-Tier Discovery & Self-Healing**:
  - The Add-in resolves `adminareas/` across: (1) Active project folder `<ProjectFolder>\HelperFiles\adminareas`, (2) Master workspace paths `<WorkspaceRoot>\HelperFiles\adminareas` and `Cad2Gis\HelperFiles\adminareas`, (3) Add-in assembly installation directory, and (4) `%LOCALAPPDATA%\CAD2GIS\HelperFiles\adminareas`.
  - If a project's `HelperFiles\adminareas` is empty, shapefiles are automatically copied into it so the project is 100% self-contained.
- **Automatic Attribute Population & Synchronization**:
  - `REGION_ID`: Evaluated from `regons.shp` (e.g., `"2"` for Makkah Al-Mukarramah, `"1"` for Riyadh).
  - `CITY_ID`: Evaluated from `cities.shp` matching `CITY_LOOKUP` (e.g., `"2"` for Jeddah).
  - `ADDRESS`: Synthesized automatically following the official Saudi cadastral standard:
    $$\text{ADDRESS} = \text{"حي/"} + \text{NEIGHBOURH} + \text{"/ب"} + \text{CITY\_NAME} + \text{"/بمنطقة/"} + \text{REG\_NAME}$$
    *(Example: `حي/حي النموذج/بالرياض/بمنطقة/الرياض`)*.
  - **In-Memory & In-Place GDB Attribute Update**: Triggered automatically upon completion of Step 2 (Build Parcels), enforced in Step 3 (Populate Schema), and available anytime via the UI button `📍 جلب العنوان من موقع القطع`. If parcel attributes are unpopulated or modified, `MissingDataPolicy.ApplyDefaults` and `GdbFeatureClassService.UpdateParcelAttributesInFeatureClass` sync both memory records and live GDB feature class rows.

### Adapting and Customizing `helpers/adminareas`:
Developers and users can modify or enrich the spatial data in `helpers/adminareas` to meet specific municipal requirements:
1. **Adding Municipal Sub-Districts (البلديات الفرعية)**:
   - Additional polygon shapefiles (e.g., `sub_municipalities.shp` or `districts.shp`) can be placed in `helpers/adminareas/` to automatically populate sub-district attributes or filter valid neighborhood names.
2. **Updating Boundary Precision & Datums**:
   - If the default regional boundaries in `regons.shp` or `cities.shp` require alignment with updated National Geospatial-Information Authority (GEOSA) boundaries, replace or edit the `.shp` files in `helpers/adminareas/` while preserving the core attribute column names (`REGION_ID`, `REG_NAME`, `CITY_ID`, `CITY_NAME`).
3. **Adding Custom Regional Attributes**:
   - Additional columns (such as Postal Code, Amanah Code, or Sub-Municipality ID) can be added to the `.dbf` of `cities.shp` and mapped directly in `AdminAreaDetector.cs` (`Cad2Gis.AddIn/SpatialEnrichment/AdminAreaDetector.cs`).

---

## 4. Cadastral Domain Rules & 4-Cardinal Boundary Model (`example`)

Detailed analysis of the reference deliverable (`example/ملف الشيب فيل 26- 8 - 2026 معدل`) reveals vital cadastral rules codified into the engine:

### A. The 4-Cardinal Boundary Rule
- Every parcel in the official output has exactly **4 boundary records** (`BORDERNAME`: 1 = North / شمال, 2 = East / شرق, 3 = South / جنوب, 4 = West / غرب).
- When a parcel side has multiple segments, corner chamfers (شطرات), or touches multiple entities (e.g., part adjacent parcel, part street, part utility):
  - `DIMENSION` = `"Multi"`
  - `SHAPE_Leng` = Exact sum of metric lengths for that facade.
  - `BORDERDESC` = Integer rounded length (`(long)Math.Round(SHAPE_Leng)`).
  - `DESCRIPTIO` = Fluent concatenated Arabic narrative listing all touching entities:
    - *Example 1 (Multiple neighbors & street)*: `قطعة رقم 2، قطعة رقم 9، شارع عرض 30 متر`
    - *Example 2 (Street with utility)*: `شارع عرض 32 متر ، غرفة كهرباء`
    - *Example 3 (Water channel / Marina)*: `قناة مائية`
    - *Example 4 (Service road / walkway)*: `خدمات طرق وممرات`
    - *Example 5 (Walkway into garden)*: `ممر مشاه عرض 6 متر ثم حديقة`

### B. Extended BORDERTYPE Classification
In addition to standard `STR` (شارع) and `PLT` (قطعة), the engine supports the full spectrum of MOMRAH codes:
- `SRV`: خدمات طرق وممرات (Service roads / utility corridors)
- `WCH`: قناة مائية / مجرى مائي (Water channel / marina)
- `LNE`: ممر مشاة (Pedestrian walkway)
- `PRK` / `PAK`: مواقف سيارات / حديقة (Parking / Public garden)
- `MSQ` / `MSD`: مسجد (Mosque)
- `YRD`: ساحة / فناء (Open plaza / yard)

### C. Land-Use Dual-Source Harmonization
The tool supports both CAD-driven and Excel-driven attribution:
1. **CAD-Driven**: Extracted from Text/MText on Usage layers, normalized, and classified into `USAGE` code and `REMARKS` description.
2. **Excel-Driven**: Reads `helpers/PARCEs_landuse.xlsx` or `example/PARCEs_landuseexample.xlsx` matching on `PLOT_ID`. Populates:
   - `USAGE`: e.g., `Mixed Use`, `RSD`, `COM`, `RSC`, `UTL`.
   - `REMARKS`: e.g., `متعدد الاستخدام`, `سكني`, `تجاري أحياء`, `حديقة`, `غرفة كهرباء`.
   - `BLOCK_NO`: e.g., `1`, `A`, or empty if unblocked.
   - `AREA_SQM`: Cross-checked against computed CAD polygon area (flags discrepancies $> 0.05 \text{ m}^2$).

---

## 5. High-Level Architectural Pipeline (Feature Class Native + Multi-Format Export)

```mermaid
flowchart TD
    subgraph S1 [Step 1: Scan & Filter CAD Layers]
        MAP[Active Map / CAD Ingest] --> SCAN[Scan CAD Drawing & Features]
        XLS_EXP[Expected_Layers.xlsx] --> DYN_LOAD[Dynamic Layer Catalog Ingestion]
        SCAN & DYN_LOAD --> TABS[4-Category Tabs: Main, Sub, Text, Roads]
        XARAB[XArabDecoder] --> AR_PREV[Real-Time Arabic Text Preview]
    end

    subgraph S2 [Step 2: Extract Parcels 2D Planar]
        TABS --> POLY_ENGINE[Dual-Mode Polygonizer: Closed Polygons & Line to Polygon]
        SCOPE[Polygon Layer Creation Scope] --> FILTER_L[Filter Main Only, Main & Sub, or All Except Text]
        FILTER_L & POLY_ENGINE --> CURVES[True Curves Preservation & Planar 2D Enforce]
        CURVES --> PLOT_TXT[Spatial Hash Grid Text Association: PLOT_ID]
        PLOT_TXT --> FC_PARCEL_RAW[Parcels Extracted & Classified: MAIN / SUB]
    end

    subgraph S3 [Step 3: Populate Schema & Municipal Data]
        FC_PARCEL_RAW --> SRC_SEL{Schema Data Source Selection}
        SRC_SEL -- Standard Municipal --> STD_IN[Manual Metadata: Plan, District, City, Request Type]
        STD_IN --> ADM_DET[Admin Area Spatial Intersect: REGION_ID, CITY_ID, ADDRESS]
        STD_IN --> EXT_LU[Join External Land-Use Table & Validate Areas Excel]
        SRC_SEL -- Use My Own Data --> CUST_IN[Custom Data Picker: .xlsx, .shp, .gdb]
        CUST_IN --> CHK_SCH[Automated Schema Check: Identical vs Different]
        CHK_SCH --> FLD_MAP[Smart Field Mapping Table: Instant Auto-Match Synonyms]
        EXT_LU & FLD_MAP --> COMMIT_S3[Apply MOMRAH 26-Attribute Schema & Save to GDB]
        COMMIT_S3 --> FC_PARCEL[Native Feature Class: PARCEL in GDB]
    end

    subgraph S4 [Step 4: Extract 4-Cardinal Borders]
        FC_PARCEL --> DECOMPOSE[Exterior Ring Decomposition into 4 Cardinal Facades]
        DECOMPOSE --> CHAMFER[Merge Corner Chamfers / Shotar]
        CHAMFER --> SENSE[Outward Ray/Wedge Sensing: PLT, STR, WCH, SRV]
        SENSE --> ARABIC_DESC[Compose Concatenated Arabic DESCRIPTIO]
        ARABIC_DESC --> FC_BORDER[Write to GDB Feature Class: Border]
        FC_BORDER --> MAP_LAYER2[Display Live Layers on Active Map]
    end

    subgraph S5 [Step 5: Quality Assurance & Multi-Format Deliverables]
        FC_PARCEL & FC_BORDER --> QA_ENGINE[Unconditional Non-Blocking QA/QC Validation]
        QA_ENGINE --> CHK_SHP{Export to Shapefile Checked?}
        CHK_SHP -- Yes --> EXP_SHP[Write PARCEL.shp, border.shp, PARCEL_Differences.shp + UTF-8 .cpg]
        CHK_SHP -- No --> KEEP_GDB[Retain Deliverables in GDB Only]
        QA_ENGINE --> EXP_XLS[Generate Detailed QA_Audit_Report.xlsx]
        QA_ENGINE --> AUTO_DIR[Auto-Numbered Collision-Proof Delivery Folder: Plan_No_City_District_Date]
    end
```

---

## 6. English UI, DAML Ribbon Configuration & MVVM DockPane Workflow

The Add-in interface is engineered for high productivity, visual elegance, and English LTR alignment:

### Ribbon Controls (`Config.daml`):
- **Tab**: `CAD2GIS`
- **Group 1: Workspace**:
  - `Show Tool Panel`: Opens the main DockPane (`ID = Cad2Gis_Dockpane`).
- **Group 2: Pipeline Steps**:
  - `1. Scan Layers`: Activates Tab 0 and initiates CAD layer scan (`Cad2Gis_Btn_Step1`).
  - `2. Extract Parcels`: Activates Tab 1 and runs 2D parcel polygonization (`Cad2Gis_Btn_Step2`).
  - `3. Populate Schema`: Activates Tab 2 and opens Schema & Municipal Data (`Cad2Gis_Btn_Step3`).
  - `4. Extract Borders`: Activates Tab 3 and runs 4-cardinal facade decomposition (`Cad2Gis_Btn_Step4`).
  - `5. Export & QA`: Activates Tab 4 and opens final delivery export (`Cad2Gis_Btn_Step5`).
- **Group 3: Full Pipeline**:
  - `Run All (Pipeline)`: Executes steps 1 through 5 sequentially with automated error isolation.
*Technical Guarantee*: `loadOnClick="true"` and `Enabled = true` ensure instant responsiveness without assembly lazy-loading stalls.

### DockPane Stepper (`Cad2GisDockpaneView.xaml`):
- **Header**:
  - Real-time Spatial Admin Badge: `📍 Region Makkah Al-Mukarramah (2) | City Jeddah (2)`
  - Standard Address Preview: `حي/{Neighbourhood}/ب{City}/بمنطقة/{Region}` (dynamically detected)
- **Tab 1: 1. Scan Layers**:
  - CAD Drawing picker (`.dwg`, `.dxf`) + `Convert CAD to Dataset` (`CADToGeodatabase`).
  - Expected Layers Excel linker (`helpers/Expected_Layers.xlsx`) with `Load Layers from Excel`.
  - 4-Tier CAD Feature Layer resolution with 4 category tabs (`Main Parcels`, `Sub-Parcels / Utilities`, `Text & Annotations`, `Roads & References`).
  - Real-time layer count and entity counters per category.
  - XArab text decoder with live raw vs decoded Arabic preview table.
- **Tab 2: 2. Extract Parcels**:
  - **Layer Geometry Mode**: `Both (مضلعات وخطوط معاً)` (Default), `Polygon (مضلعات فقط)`, or `Line (خطوط فقط)`.
  - **Polygon Layer Creation Scope**:
    - `كل الطبقات المختارة (Main & Sub)`: Ingests all selected layers belonging to Main Parcels and Sub-Parcels.
    - `القطع الرئيسية فقط (Main Only)`: Restricts parcel polygon creation strictly to primary title deed plots.
    - `كل المختار ما عدا النصوص (All Except Text)`: Ingests all non-text drawing geometry.
  - **Plot Text Layer Selector**: Dedicated dropdown (`AvailablePlotTextLayers`) to pick the exact annotation layer for Plot IDs.
  - **Line to Polygon Engine**: `[✔] Build polygons from lines` with tolerance (meters) and shared edge grouping (`Group layers together`).
  - **Geometry Preservation**: 2D Planar coordinate enforcement (strips Z and M) with native circular arc preservation (`HasCurves` test avoids vertex explosion).
  - Buttons: `▶ Run Step 2 (Extract Parcels 2D)` and `📤 Export Step 2 Parcels`.
- **Tab 3: 3. Populate Schema**:
  - **Schema Data Source Selector**:
    - `(•) Standard Municipal Schema / MOMRAH`: Manual metadata entry (`PLAN_NO`, `NEIGHBOURH`, `REQUEST_TY`, `REPORT_NO`, `City`, `Region`), `📍 Detect Address from Main Parcels`, External Land-Use Table (`.xlsx`), and Areas Excel validation table (`.xlsx`).
    - `( ) Use My Own Data (استخدام بياناتي الخاصة)`: Dedicated custom schema panel supporting Excel (`.xlsx`, `.xls`), Shapefiles (`.shp`), and GDB Feature Classes.
      * Automated Schema Check: Instantly verifies if the file contains the 22 standard MOMRAH fields.
      * Status Banner: Green `✅ 100% Identical MOMRAH Schema` (collapsed) vs Amber `⚠️ Different Schema` (auto-expanded).
      * Smart Field Mapping DataGrid: ComboBoxes for source fields with instant auto-linking (`AutoMatchField` across exact names, normalized strings, and English/Arabic synonyms like `رقم_القطعة`, `المخطط`, `الاستخدام`, `الحي`, `المساحة`), status badges (`✔ مربوط`, `إلزامي`, `اختياري`), and `⚡ Auto-Map` button.
  - Buttons: `▶ Run Step 3 (Populate Schema)` and `📤 Export Step 3 (Populated Parcels)`.
- **Tab 4: 4. Extract Borders**:
  - 4-Cardinal boundary model configuration (North, East, South, West).
  - Automated neighbor sensing and chamfer integration (angles $\approx 45^\circ$, length $\le 7.0 \text{ m}$).
  - Fluent concatenated Arabic descriptions (`قطعة رقم 2، قطعة رقم 9، شارع عرض 30 متر`).
  - Interactive Inferred Borders table with street width suggestions and manual width overrides.
  - Buttons: `▶ Run Step 4 (Extract Borders)` and `📤 Export Step 4 Borders`.
- **Tab 5: 5. Export & QA**:
  - Target Output Directory configuration with collision-proof auto-incremented delivery folder:
    `Plan_{PlanNo}_{City}_{District}_{YYYYMMDD}` (e.g. `Plan_629_Jadahh_AlRabuah_20261021_1`).
  - Unconditional, non-blocking QA/QC validation engine generating detailed `QA_Audit_Report.xlsx`.
  - Shapefile Delivery Option: `[✔] Export to ESRI Shapefiles (PARCEL.shp & border.shp + UTF-8 .cpg)`.
  - Buttons: `💾 Run Step 5 (Export & QA)` and deliverables inventory checklist.
- **Action Bar & Status Footer**:
  - `🚀 Run All Steps Sequentially (Full Pipeline)` button with dynamic progress bar and live action console.
  - Dynamic status dot indicator (🟢 emerald for success, 🔵 pulsating cyan during processing, 🔴 crimson on error).

---

## 7. Engineering Phases, Testing & Deployment Automation

| Milestone | Scope | Deliverables & Verification |
| :--- | :--- | :--- |
| **Milestone 1: Core Engine & Lookups** | Solution setup, domain models, `XArabDecoder`, `ArabicCadDecoder`, `MunicipalLookups`, ClosedXML Excel readers, `ExpectedLayersService`, `PolygonLayerScopeService`. | All 43 initial unit and integration tests pass (`Cad2Gis.Tests`). |
| **Milestone 2: Spatial Enrichment & GDB Service** | `AdminAreaDetector` using `helpers/adminareas`, `GdbFeatureClassService` schema cloning and FeatureLayer rendering. | Point-in-polygon returns Makkah/Jeddah for sample UTM points; GDB feature classes created with 26 fields. |
| **Milestone 3: Stage 2 Parcel Generation** | 2D Planar polygonization, line-to-polygon with tolerance, layer scopes, curve densification prevention. | Generates verified parcels matching benchmark schema within $0.01 \text{ m}^2$. |
| **Milestone 4: Stage 3 Populate Schema & Field Mapping** | Custom schema ingestion (`.xlsx`, `.shp`, `.gdb`), `CheckSchema`, `AutoMatchField` Arabic synonyms, MVVM DataGrid. | 100% auto-linking on matching field names, transfers attributes to `_parcels` and commits to GDB. |
| **Milestone 5: Stage 4 Borders & Stage 5 Export/QA** | 4-cardinal facade decomposition, chamfer merging, ray sensing, non-blocking QA report, auto-numbered delivery folders. | 113 automated unit & integration tests passing at 100%. Packaged into `Cad2Gis.esriAddinX`. |

---

## 8. Risk Analysis & Mitigation Matrix

| Identified Risk | Severity | Mitigation Strategy Implemented in v2.5 |
| :--- | :--- | :--- |
| **Shapefile Metric Overwrite in GCS** | High | Planar UTM metric values (`AREA_SQM`, `Shape_Area`, `SHAPE_Leng`, `BORDERDESC`) are frozen in memory and written into the DBF table explicitly without letting GCS reprojection alter metric numbers. |
| **Arabic Encoding Corruption in DBF** | High | Every shapefile write is accompanied by a dedicated `.cpg` file containing `UTF-8`. Text fields are normalized before writing. |
| **Ribbon Buttons Grayed Out / Disabled** | Critical | Set `loadOnClick="true"`, configure relative class names in `Config.daml`, and explicitly enforce `Enabled = true` inside `OnUpdate()`. |
| **Pro SDK Threading Collisions** | High | Strict isolation: UI state and MVVM properties run on the WPF Dispatcher thread; all ArcGIS Pro SDK data/geometry access is wrapped inside `QueuedTask.Run(...)`. |

---

# Part 2 — Execution Master Prompt

## Execution Master Prompt: Build & Maintain "CAD2GIS" for ArcGIS Pro 3.3.0 (C#, ArcGIS Pro SDK for .NET)

Act as a Principal GIS Software Architect, ArcGIS Pro SDK for .NET specialist, and Saudi municipal cadastral automation expert. Maintain and extend the production-ready **CAD2GIS** ArcGIS Pro 3.3.0 Add-in in C# according to the rigorous requirements below.

```
Target Framework: .NET 8 (net8.0-windows)
Host Application: ArcGIS Pro 3.3.0 (SDK for .NET)
Architecture: x64
Solution File: Cad2Gis.sln
Add-In GUID: {907b00a7-4503-4bff-9969-268e429817bd}
```

---

### 1. Visual Studio Solution Architecture (`Cad2Gis.sln`)

The solution is divided into three focused projects:

```
Cad2Gis/
├── Cad2Gis.Core/                    (Standalone .NET 8 Class Library - No ArcGIS Pro dependencies)
│   ├── Encodings/
│   │   └── XArabDecoder.cs          (Fast Arabic SHX font translation engine)
│   ├── Lookups/
│   │   └── MunicipalLookups.cs      (68 BorderTypes, BuildingTypes, UsageTypes, CityLookups)
│   ├── Models/
│   │   ├── CadLayerInfo.cs          (Layer metadata, entity count, assignment role)
│   │   ├── ParcelRecord.cs          (26 MOMRAH municipal attributes + geometry references)
│   │   ├── BorderRecord.cs          (9 MOMRAH border attributes + direction codes)
│   │   └── ProjectMetadata.cs       (PlanNo, Neighbourhood, RequestType, Address builder)
│   ├── Services/
│   │   ├── LandUseExcelService.cs   (ClosedXML reader/writer for PARCEs_landuse.xlsx)
│   │   └── QaValidationEngine.cs    (Topology, schema, and referential integrity validator)
│   └── Utils/
│       └── ArabicTextNormalizer.cs  (Alef, Yeh, Teh-Marbuta normalization)
│
├── Cad2Gis.AddIn/                   (ArcGIS Pro Add-in Project - ArcGIS Pro SDK 3.3)
│   ├── Config.daml                  (Ribbon buttons, groups, dockpane declaration)
│   ├── Cad2GisModule.cs             (Add-in module singleton)
│   ├── DockPane/
│   │   ├── Cad2GisDockpaneView.xaml (WPF tabbed UI with English LTR presentation)
│   │   ├── Cad2GisDockpaneViewModel.cs (MVVM coordinator, asynchronous pipeline)
│   │   └── RibbonCommands.cs        (Step 1..4 and Pipeline button handlers)
│   ├── Geometry/
│   │   ├── ParcelPolygonizer.cs     (Planar polygonization & densification)
│   │   ├── FacadeDecomposer.cs      (4-cardinal direction partition & chamfer grouping)
│   │   └── OutwardRaySensor.cs      (Ray casting & Arabic narrative composition)
│   ├── IO/
│   │   ├── GdbFeatureClassService.cs (Native GDB Feature Class creation, writing & map sync)
│   │   ├── ShapefileExporter.cs     (Template cloning, UTF-8 .cpg, frozen metric DBF write)
│   │   ├── ShapefileReader.cs       (Fast shapefile geometry & attribute loader)
│   │   └── ReportGenerator.cs       (ClosedXML QA workbook & JSON audit logs)
│   └── SpatialEnrichment/
│       └── AdminAreaDetector.cs     (Point-in-polygon spatial detection on helpers/adminareas)
│
└── Cad2Gis.Tests/                   (xUnit Test Suite)
    ├── XArabDecoderTests.cs         (Font decoding & slash preservation tests)
    ├── FacadeGroupingTests.cs       (Normal azimuths & chamfer merging tests)
    ├── MunicipalLookupTests.cs      (Code mappings & building types tests)
    └── QaValidationTests.cs         (Referential integrity & metric validation tests)
```

---

### 2. Core Domain Models & Embedded Dictionaries (`Cad2Gis.Core`)

#### `ParcelRecord.cs`
Represents the 26 mandatory attributes matching `helpers/Templet/PARCEL.shp`:
```csharp
public class ParcelRecord
{
    public long InternalId { get; set; }
    public ParcelClass Class { get; set; } = ParcelClass.Main;

    public int RealestateType { get; set; } = 2;              // REALESTA_1 (أرض فضاء)
    public string Neighbourhood { get; set; } = string.Empty; // NEIGHBOURH
    public string RegionId { get; set; } = "2";               // REGION_ID (مكة المكرمة)
    public string CityId { get; set; } = "2";                 // CITY_ID (جدة)
    public string Address { get; set; } = string.Empty;       // ADDRESS
    public string Usage { get; set; } = "RSD";                // USAGE
    public int IsBuilding { get; set; } = 0;                  // ISBUILDING
    public int BuildingType { get; set; } = 1;                // BUILDINGTY
    public string Remarks { get; set; } = "سكني";             // REMARKS
    public string? Layout { get; set; }                       // LAYOUT
    public string PlotId { get; set; } = string.Empty;        // PLOT_ID
    public string? BlockNo { get; set; }                      // BLOCK_NO
    public string PlanNo { get; set; } = string.Empty;        // PLAN_NO
    public string PlotNo1 { get; set; } = string.Empty;       // PLOT_NO1
    public string? PlotNo2 { get; set; }
    public string? PlotNo3 { get; set; }
    public string? PlotNo4 { get; set; }
    public string? PlotNo5 { get; set; }
    public string? PlotNo6 { get; set; }
    public string RequestType { get; set; } = "فرز";          // REQUEST_TY
    public string RequestNo { get; set; } = string.Empty;     // REQUEST_NO
    public string ReportNo { get; set; } = string.Empty;      // REPORT_NO

    // Frozen UTM Planar Metrics
    public double AreaSqm { get; set; }                       // AREA_SQM
    public double ShapeArea { get; set; }                     // Shape_Area
    public double ShapeLength { get; set; }                   // Shape_Leng

    public double CentroidX { get; set; }
    public double CentroidY { get; set; }
    public List<BorderRecord> Borders { get; set; } = new();
}
```

#### `BorderRecord.cs`
Represents the 9 mandatory boundary attributes matching `helpers/Templet/border.shp`:
```csharp
public class BorderRecord
{
    public long InternalId { get; set; }
    public string PlotId { get; set; } = string.Empty;        // PLOT_ID
    public string PlanNo { get; set; } = string.Empty;        // PLAN_NO
    public int BorderName { get; set; }                       // BORDERNAME (1=North, 2=East, 3=South, 4=West)
    public string BorderType { get; set; } = "STR";           // BORDERTYPE (STR, PLT, SRV, WCH, LNE, PRK)
    public long BorderDesc { get; set; }                      // BORDERDESC (Integer rounded metric length)
    public string Dimension { get; set; } = "Single";         // DIMENSION ("Single" or "Multi")
    public double ShapeLength { get; set; }                   // SHAPE_Leng (Exact metric length)
    public string Descriptio { get; set; } = string.Empty;    // DESCRIPTIO / DESCRIPTO (Arabic narrative)
}
```

---

### 3. Arabic CAD Font & Annotation Decoder (`XArabDecoder.cs`)

Decodes legacy Arabic text from AutoCAD annotations (such as xArab font encodings) with alphanumeric slash protection:
```csharp
public static class XArabDecoder
{
    private static readonly Dictionary<char, string> TranslationMap = new()
    {
        ['H'] = "ا", ['h'] = "ا", ['f'] = "ب", ['F'] = "ب", ['B'] = "ب",
        ['j'] = "ت", ['J'] = "ت", ['e'] = "ث", ['E'] = "ث", ['{'] = "ج",
        ['p'] = "ح", ['o'] = "خ", [']'] = "د", ['}'] = "ذ", ['v'] = "ر",
        ['V'] = "ر", ['<'] = "ز", ['>'] = "ز", ['s'] = "س", ['a'] = "ش",
        ['w'] = "ص", ['q'] = "ض", ['Q'] = "ض", ['\''] = "ط", ['/'] = "ظ",
        ['u'] = "ع", ['U'] = "ع", ['y'] = "غ", ['t'] = "ف", ['T'] = "ف",
        ['r'] = "ق", ['R'] = "ق", [':'] = "ك", [';'] = "ك", ['g'] = "ل",
        ['G'] = "ل", ['l'] = "م", ['L'] = "م", ['k'] = "ن", ['K'] = "ن",
        ['i'] = "ه", ['I'] = "ه", [','] = "و", ['d'] = "ي", ['D'] = "ي",
        ['m'] = "ة", ['M'] = "ة", ['z'] = "ئ", ['c'] = "ؤ", ['x'] = "ء",
        ['n'] = "ى", ['b'] = "لا"
    };

    public static string Decode(string rawText, bool preserveSlash = true, bool reverseText = false)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;
        string text = rawText.Replace("\r", "").Replace("\n", "");

        if (reverseText)
        {
            char[] arr = text.ToCharArray();
            Array.Reverse(arr);
            text = new string(arr);
        }

        const string slashPlaceholder = "__PROTECTED_SLASH__";
        if (preserveSlash)
        {
            text = Regex.Replace(text, @"(?<=\w)/(?=\w)", slashPlaceholder);
        }

        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (TranslationMap.TryGetValue(c, out string rep)) sb.Append(rep);
            else if (c != '_' && c != '(' && c != ')') sb.Append(c);
        }

        string result = sb.ToString();
        if (preserveSlash) result = result.Replace(slashPlaceholder, "/");
        return Regex.Replace(result, @"\s+", " ").Trim();
    }
}
```

---

### 4. Spatial Enrichment & Admin Area Detection (`AdminAreaDetector.cs`)

Extracts administrative jurisdiction by intersecting parcel coordinates against `helpers/adminareas`:
```csharp
public class AdminAreaDetector
{
    private readonly string _adminAreasFolder;

    public AdminAreaDetector(string adminAreasFolder)
    {
        _adminAreasFolder = adminAreasFolder;
    }

    public (string regionId, string regionName, string cityId, string cityName) DetectAdminLocation(MapPoint utmCenterPoint)
    {
        string regId = "2", regName = "مكة المكرمة";
        string cityId = "2", cityName = "جدة";

        // Intersects against helpers/adminareas/regons.shp and cities.shp
        // Read REGION_ID, REG_NAME, CITY_ID, CITY_NAME
        return (regId, regName, cityId, cityName);
    }
}
```

---

### 5. Stage 2: Parcel Construction, Layer Scopes, Line Polygonization & Native GDB Feature Class (`PARCEL`)

#### 1. Authoritative CAD Map Layer Discovery (`FindCadFeatureLayersOnMap`):
- Unlike standard layers, AutoCAD drawings in ArcGIS Pro are contained within a parent `GroupLayer` / `CadLayer` named after the file (`.dwg` or `.dxf`), while child `FeatureLayer`s are generically named: `"Polygon"`, `"Polyline"`, `"Point"`, `"Annotation"`, and `"MultiPatch"`.
- The add-in resolves CAD layers through a 4-tier discovery protocol:
  1. **Hierarchical Traversal**: Recursively inspects `layer.Parent` up to the root to match the DWG filename or basename.
  2. **Data Source Connection**: Inspects `featureClass.GetPath()` for direct path matches against the active CAD drawing.
  3. **CAD Schema Fingerprinting**: Detects universal CAD fields (`Layer` + `Color`, `Linetype`, `RefName`, or `Handle`).
  4. **Dynamic Map Registration**: If the CAD drawing is not yet in the Table of Contents, it is automatically loaded using `LayerFactory.Instance.CreateLayer(new Uri(cadFilePath), map)`.

#### 2. Polygon Layer Creation Scopes (`PolygonLayerScopeService`):
Enables the user to precisely control which CAD drawing layers contribute to polygon feature generation:
1. **`كل الطبقات المختارة (Main & Sub)` (ScopeMainAndSub)**:
   - Ingests all selected layers tagged as Main Parcels (`Boundary`) or Sub-Parcels / Utilities (`Service`).
   - Default mode providing full subdivision coverage.
2. **`القطع الرئيسية فقط (Main Only)` (ScopeMainOnly)**:
   - Restricts polygon creation strictly to Main Parcels (`Category == "Main Parcels"` or `Role == Boundary`).
   - Ideal for generating primary cadastral title deed parcel boundaries while ignoring secondary easements.
3. **`كل المختار ما عدا النصوص (All Except Text)` (ScopeAllExceptText)**:
   - Ingests all selected drawing features except explicit text annotation, plot number, and dimension layers (`Text & Annotations`).
   - Captures unconventional boundary polyline layers without noise.

#### 3. Intelligent Cadastral Classification & Text Decoding:
- **Main Parcels (`Boundary`)**: Layers containing `commercial`, `comm`, `residential`, `mixed`, `سكني`, `تجاري`, `استثماري`, `الاوجه`, `الاجهه`, `واجهات`, `قطع`, `قطعة`, `اراضي`, `أراضي`.
- **Sub-Parcels / Utilities (`Service`)**: Layers containing `serv`, `util`, `mosque`, `جامع`, `مسجد`, `حديقة`, `حدائق`, `garden`, `park`, `room`, `parking`, `مواقف`, `hospital`, `مستشفى`, `school`, `مدرسة`, `حكومي`, `كهرباء`.
- **Text & Annotations**: Layers containing `text`, `txt`, `memo`, `dim`, `number`, `num`, `رقم`, `ارقام`, `ابعاد`, `نصوص`, `كتابة`, or with `GeometryType == "Text"`.
- **AutoCAD MText Stripping**: All complex AutoCAD formatting codes (e.g. `{\fArial|b0|i0;101}`) and XArab glyph encodings are cleaned to yield pure Arabic text and numeric parcel numbers.

#### 4. Dual-Mode Polygonization Engine (`LinePolygonizerService`):
1. **Closed CAD Polygons**: Ingested directly from the CAD `Polygon` feature class, verified for non-emptiness, and cleaned via `ParcelPolygonizer.DensifyAndClean(cadPoly)`.
2. **Line Polygonizer (Open Polylines & Boundary Line Networks)**:
   - When parcels in the CAD drawing are drafted as lines or open polylines (or when `Polygon` features are empty, or when `Build polygons from lines` is checked), lines from eligible layers are ingested from the CAD `Polyline` feature class.
   - Closed planar rings are synthesized using planar graph cycle traversal within the user-specified `SnapToleranceMeters` (default: 0.05 m).
   - Supports `Group layers together (shared edges)` for seamless shared boundary topology.
3. **Headless / Offline Fallback Synthesis**: If executing without an active map session, the engine generates geometrically valid planar parcels matching the detected or expected layers to ensure downstream pipeline stages remain testable and reliable.

#### 5. Land-Use Integration & Native GDB Feature Class:
- Reads `helpers/PARCEs_landuse.xlsx` matching on `PLOT_ID` to enrich `USAGE`, `REMARKS`, and `BLOCK_NO`.
- Computes frozen planar UTM metrics (`AREA_SQM`, `Shape_Area`, `Shape_Leng`).
- Clones schema from `helpers/Templet/PARCEL.shp` and writes native records to `PARCEL` Feature Class in the active `.gdb`.
- Adds/refreshes the live layer on `MapView.Active.Map`.

---

### 6. Stage 3: 4-Cardinal Facade Decomposer & GDB Feature Class (`Border`)

#### Algorithm:
1. **Exterior Ring Vectorization**: Enumerate polygon vertices and segment vectors.
2. **Azimuth Partitioning (4 Quadrants)**:
   - **North (`1`)**: $315^\circ \le \theta < 360^\circ$ or $0^\circ \le \theta < 45^\circ$
   - **East (`2`)**: $45^\circ \le \theta < 135^\circ$
   - **South (`3`)**: $135^\circ \le \theta < 225^\circ$
   - **West (`4`)**: $225^\circ \le \theta < 315^\circ$
3. **Chamfer (Shotar) Merging**: Corner cuts (angles $\approx 45^\circ$, length $\le 7.0 \text{ m}$) are merged into their adjacent facade.
4. **Outward Sensing & Arabic Narrative**:
   - Cast outward rays to detect touching neighbors (`PLT`, `STR`, `WCH`, `SRV`, `LNE`).
   - Formulate composite Arabic descriptions: `قطعة رقم 2، قطعة رقم 9، شارع عرض 30 متر`.
5. **Commit to Geodatabase Feature Class**:
   - Clones schema from `helpers/Templet/border.shp`.
   - Writes 4 border polylines per parcel into `Border` Feature Class.
   - Adds/refreshes the layer on `MapView.Active.Map`.

---

### 7. Native Geodatabase Feature Class Service (`GdbFeatureClassService.cs`)

Manages Geodatabase lifecycle, schema cloning, writing, and reading:
```csharp
public static class GdbFeatureClassService
{
    public static string GetEffectiveGdbPath(string? customOutputFolder = null)
    {
        string? projGdb = Project.Current?.DefaultGeodatabasePath;
        if (!string.IsNullOrWhiteSpace(projGdb) && (Directory.Exists(projGdb) || projGdb.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase)))
            return projGdb;

        string targetDir = !string.IsNullOrWhiteSpace(customOutputFolder) 
            ? customOutputFolder 
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CAD2GIS_Export");
        Directory.CreateDirectory(targetDir);
        return Path.Combine(targetDir, "Cad2Gis_Workspace.gdb");
    }

    public static async Task EnsureFeatureClassFromTemplateAsync(string gdbPath, string fcName, string templatePath)
    {
        await EnsureGeodatabaseAsync(gdbPath);
        // Uses Geoprocessing FeatureClassToFeatureClass_conversion if FC does not exist
    }

    public static void WriteParcelsToFeatureClass(string gdbPath, string fcName, IEnumerable<ParcelRecord> parcels, IReadOnlyDictionary<long, Polygon> geom)
    {
        // Opens Geodatabase, opens FeatureClass, clears old records, writes RowBuffer with polygon geometry
    }

    public static void WriteBordersToFeatureClass(string gdbPath, string fcName, IEnumerable<BorderRecord> borders, IReadOnlyDictionary<long, Polyline> geom)
    {
        // Opens Geodatabase, opens FeatureClass, clears old records, writes RowBuffer with polyline geometry
    }

    public static (List<ParcelRecord> Parcels, Dictionary<long, Polygon> Geometries) ReadParcelsFromFeatureClass(string gdbPath, string fcName = "PARCEL")
    {
        // Direct reader from GDB Feature Class allowing independent Step 3 and 4 execution
    }

    public static (List<BorderRecord> Borders, Dictionary<long, Polyline> Geometries) ReadBordersFromFeatureClass(string gdbPath, string fcName = "Border")
    {
        // Direct reader from GDB Feature Class allowing independent Step 4 execution
    }

    public static FeatureLayer? AddOrRefreshMapLayer(Map? map, string gdbPath, string fcName, string caption)
    {
        var uri = new Uri(Path.Combine(gdbPath, fcName));
        var layer = LayerFactory.Instance.CreateLayer(uri, map, -1, caption) as FeatureLayer;
        layer?.SetName(caption);
        return layer;
    }
}
```

---

### 8. Stage 4: Optional Shapefile Exporter & QA/QC Audit Engine

In Step 4, if `ExportToShapefile` is checked:
- Clones `helpers/Templet/PARCEL.shp` and `border.shp` to the export directory.
- Writes records and geometries with explicit UTF-8 `.cpg` files.
- Preserves frozen planar UTM metrics in DBF without GCS degree distortion.

Generates full MOMRAH audit deliverables:
- `PARCEs_landuse.xlsx` (Updated register matching `example/PARCEs_landuseexample.xlsx`).
- `QA_QC_Report.xlsx` (Comprehensive multi-sheet ClosedXML audit workbook).
- `Missing_Data_Report.csv` & `Audit_Log.json`.

---

### 9. ArcGIS Pro DAML Configuration & English MVVM DockPane UI

#### `Config.daml` (Namespace Resolution & Instant Load):
```xml
<ArcGIS defaultAssembly="Cad2Gis.AddIn.dll" defaultNamespace="Cad2Gis.AddIn" xmlns="http://schemas.esri.com/DADF/Registry" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <AddInInfo id="{907b00a7-4503-4bff-9969-268e429817bd}" version="2.5.0" author="CAD2GIS Team">
    <Name>CAD2GIS</Name>
    <Description>Cadastral ETL Add-in for ArcGIS Pro (MOMRAH / Balady Compliance)</Description>
    <Image>Images\AddinDesktop32.png</Image>
  </AddInInfo>

  <modules>
    <insertModule id="Cad2Gis_Module" className="Cad2GisModule" autoLoad="true">
      <tabs>
        <tab id="Cad2Gis_Tab" caption="CAD2GIS">
          <group refID="Cad2Gis_Group_Workspace" />
          <group refID="Cad2Gis_Group_Steps" />
          <group refID="Cad2Gis_Group_Pipeline" />
        </tab>
      </tabs>
      <groups>
        <group id="Cad2Gis_Group_Workspace" caption="Workspace">
          <button refID="Cad2Gis_Btn_ShowDockpane" size="large" />
        </group>
        <group id="Cad2Gis_Group_Steps" caption="Pipeline Steps">
          <button refID="Cad2Gis_Btn_Step1" size="middle" />
          <button refID="Cad2Gis_Btn_Step2" size="middle" />
          <button refID="Cad2Gis_Btn_Step3" size="middle" />
          <button refID="Cad2Gis_Btn_Step4" size="middle" />
        </group>
        <group id="Cad2Gis_Group_Pipeline" caption="Execution">
          <button refID="Cad2Gis_Btn_RunAll" size="large" />
        </group>
      </groups>
      <controls>
        <button id="Cad2Gis_Btn_ShowDockpane" caption="Show Tool Panel" className="ShowCad2GisDockpaneButton" loadOnClick="true" smallImage="Images\GenericButtonBlue16.png" largeImage="Images\GenericButtonBlue32.png" />
        <button id="Cad2Gis_Btn_Step1" caption="1. Scan Layers" className="ExecuteStep1Button" loadOnClick="true" smallImage="Images\GenericButtonBlue16.png" />
        <button id="Cad2Gis_Btn_Step2" caption="2. Build Parcels" className="ExecuteStep2Button" loadOnClick="true" smallImage="Images\GenericButtonBlue16.png" />
        <button id="Cad2Gis_Btn_Step3" caption="3. Build Borders" className="ExecuteStep3Button" loadOnClick="true" smallImage="Images\GenericButtonBlue16.png" />
        <button id="Cad2Gis_Btn_Step4" caption="4. Export &amp; QA" className="ExecuteStep4Button" loadOnClick="true" smallImage="Images\GenericButtonBlue16.png" />
        <button id="Cad2Gis_Btn_RunAll" caption="Run All (Pipeline)" className="ExecuteAllPipelineButton" loadOnClick="true" smallImage="Images\GenericButtonBlue16.png" largeImage="Images\GenericButtonBlue32.png" />
      </controls>
      <dockPanes>
        <dockPane id="Cad2Gis_Dockpane" caption="CAD2GIS - Cadastral Transformation" className="DockPane.Cad2GisDockpaneViewModel" dock="right">
          <content className="DockPane.Cad2GisDockpaneView" />
        </dockPane>
      </dockPanes>
    </insertModule>
  </modules>
</ArcGIS>
```

---

### 10. Automated Packaging Script (`package_addin.ps1`) — Zero Auto-Install Policy (INV-04)

Automates compilation, archiving, and staging of the `.esriAddinX` bundle into the project workspace root. In strict accordance with **INV-04**, automated scripts **must never** deploy or copy files into the user's `%USERPROFILE%\Documents\ArcGIS\AddIns\...` directory to prevent file locking and system state interference during development. Installation is performed manually by the operator via double-clicking `Cad2Gis.esriAddinX`.

```powershell
$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$binDir = Join-Path $scriptDir "Cad2Gis.AddIn\bin\Debug\net8.0-windows"
$configDaml = Join-Path $scriptDir "Cad2Gis.AddIn\Config.daml"
$outputDir = Join-Path $scriptDir "bin\Package"
$addinFile = Join-Path $outputDir "Cad2Gis.esriAddinX"
$rootAddinFile = Join-Path $scriptDir "Cad2Gis.esriAddinX"

Write-Host "Packaging CAD2GIS Add-in..." -ForegroundColor Cyan

# Stage files
$stagingDir = Join-Path $outputDir "staging"
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

Copy-Item (Join-Path $binDir "*") $stagingDir -Recurse -Force
Copy-Item $configDaml (Join-Path $stagingDir "Config.daml") -Force

$imagesDir = Join-Path $scriptDir "Cad2Gis.AddIn\Images"
if (Test-Path $imagesDir) { Copy-Item $imagesDir (Join-Path $stagingDir "Images") -Recurse -Force }

# Create .esriAddinX in bin/Package and copy to workspace root
if (Test-Path $addinFile) { Remove-Item $addinFile -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $addinFile)
Remove-Item $stagingDir -Recurse -Force

Copy-Item $addinFile $rootAddinFile -Force

Write-Host "Successfully generated Add-in package in workspace root (INV-04 Enforced):" -ForegroundColor Green
Write-Host "  -> $rootAddinFile" -ForegroundColor Yellow
Write-Host "Note: Double-click this file to install or update in ArcGIS Pro." -ForegroundColor Gray
```

---

### 11. Production Addendum (v3.2) — Unified Dark Workstation Theme & Dynamic Excel Layers Loading

#### A. Elimination of Initial Fake Layer Seeds & Real Dynamic Excel Layer Loading
1. **Zero Dummy Data at Startup:**
   - Previous versions auto-seeded placeholder layers (`PARCEL_RESIDENTIAL`, `PARCEL_COMMERCIAL`, etc.) on initial launch. In v3.2, this initial fake seed injection is permanently eliminated.
   - The CAD layers catalog starts in a completely clean, empty state until explicitly populated from a real CAD drawing or user-selected Expected Layers Excel.
2. **Dedicated Expected Layers Excel Integration:**
   - The DockPane features a dedicated card for `Expected Layers Excel (ملف إكسل الطبقات المتوقعة)` with a file picker and an explicit action button: `📥 اقرأ واستورد الطبقات من الإكسل (Load Layers from Excel)` bound to `LoadExpectedLayersCommand`.
   - Populates layer definitions from `Expected_Layers.xlsx` (supporting profiles `Example_Profile`, `MOMRAH_Standard`, `Default`) and maps them directly into the 4 category tabs.
   - Robust role parsing (`ExpectedLayersService.ParseRole`) mapping MOMRAH strings (`"Main parcel"`, `"Sub-parcel"`, `"Road"`, `"Dimensions"`, etc.) to strongly-typed `LayerRole` and routing each to its target category.
3. **Multi-Feature-Class CAD Extraction & Arabic Preview:**
   - Scans all CAD feature classes (`Polygon`, `Polyline`, `Point`, `Annotation`), cataloging geometry types, entity counts, and AutoCAD ACI colors.
   - Decodes CAD text annotations using `XArabDecoder` and presents both `RawCadText` and `DecodedArabicText` live in the table.

#### B. Unified Dark Workstation Theme (Theme Harmonization)
1. **Design System Tokens:**
   - **Canvas Background:** Deep dark workstation navy/charcoal `#0B0F19`.
   - **Cards & Outer Containers:** `#1E293B`, Borders `#334155`, Corner radius 6px.
   - **Inner Review Panels & Tables:** `#101726` with header background `#172136` and borders `#24324D`.
   - **Form Controls (`ModernTextBox`, `ComboBox`):** Background `#111827`, Foreground `#F8FAFC`, Caret/Focus `#38BDF8`, Borders `#334155`.
   - **Labels (`HeaderLabel`):** High-contrast light slate `#CBD5E1`.
   - **Stepper Tabs:** Dark tab strip `#1E293B` with active tab `#0F172A` and emerald top highlight `#00E699`.
   - **Interactive Elements:** Checkboxes/RadioButtons with `#E2E8F0` text and pointer cursors.
   - **Action Buttons:** Clear hierarchy (`#2563EB` blue action, `#0F766E` / `#0D9488` teal pipeline/Excel, `#16A34A` green approval, `#334155` secondary).
   - **Live Status Box:** `#111827` with sky blue `#38BDF8` text.
2. **Total Elimination of Visual Clashes:**
   - Both the CAD Layers table (Tab 1) and Inferred Borders Review table (Tab 3) share the identical dark styling, eliminating jarring white backgrounds or unreadable contrast.

#### C. Verification & Quality Benchmark (v3.2)
- **Unit Test Suite:** Automated unit and integration test suite (`Cad2Gis.Tests`) passing successfully.
- **Deployment:** Compiled and packaged into `Cad2Gis.esriAddinX` via `package_addin.ps1` and deployed to `%USERPROFILE%\Documents\ArcGIS\AddIns\ArcGISPro\{907b00a7-4503-4bff-9969-268e429817bd}`.

---

### 12. Production Addendum (v3.3) — Polygon Layer Creation Scopes, 4-Tier CAD Feature Layer Discovery & Line Polygonization

#### A. Three Selectable Polygon Layer Creation Scopes (`SelectedPolygonLayerScope`)
In Step 2 (`2. Parcels`), users can dynamically control which CAD drawing layers contribute to polygon generation via a dedicated UI dropdown bound to `SelectedPolygonLayerScope`:
1. **`كل الطبقات المختارة (Main & Sub)` (`PolygonLayerScopeService.ScopeMainAndSub`)**:
   - Ingests all selected layers tagged as Main Parcels (`Boundary`) or Sub-Parcels / Utilities (`Service`).
   - Default mode providing complete cadastral subdivision coverage.
2. **`القطع الرئيسية فقط (Main Only)` (`PolygonLayerScopeService.ScopeMainOnly`)**:
   - Restricts polygon creation strictly to primary cadastral title deed plot boundaries (`Category == "Main Parcels"` or `Role == Boundary`).
   - Completely ignores secondary utility easements, substations, and walkways.
3. **`كل المختار ما عدا النصوص (All Except Text)` (`PolygonLayerScopeService.ScopeAllExceptText`)**:
   - Ingests all selected drawing features excluding annotation, plot number, and dimension layers (`Text & Annotations`).
   - Ensures unclassified or bespoke boundary polyline layers are captured without text noise.

#### B. 4-Tier CAD Feature Layer Resolution on Active Map (`FindCadFeatureLayersOnMap`)
To overcome ArcGIS Pro's Table of Contents architecture — where AutoCAD drawings are mounted under a parent `GroupLayer` named after the file while child layers are generically named `"Polygon"`, `"Polyline"`, `"Point"`, `"Annotation"` — the add-in deploys an intelligent 4-tier discovery protocol:
1. **Parent Hierarchy Traversal**: Recursively climbs `layer.Parent` up to the root to detect parent `GroupLayer` or `CadLayer` nodes whose names match the DWG/DXF filename or basename.
2. **FeatureClass Connection Resolution**: Queries `featureClass.GetPath()` for direct path matches against the active CAD drawing.
3. **CAD Schema Fingerprinting**: Detects universal CAD feature class fields (`Layer` + `Color`, `Linetype`, `RefName`, or `Handle`).
4. **Dynamic Map Registration**: If the CAD drawing is not yet in the Table of Contents, it is automatically added using `LayerFactory.Instance.CreateLayer(new Uri(cadFilePath), map)` and its child feature layers are ingested immediately.

#### C. Auto-Discovery & Zero-Friction Step 2 Execution
- If a user navigates directly to Tab 2 and clicks `Run Step 2` without running Step 1, the engine does not abort.
- It dynamically queries `FindCadFeatureLayersOnMap`, scans the distinct CAD layer names from `fc.Search(new QueryFilter { SubFields = "Layer" })`, infers their cadastral roles, enriches their metadata, and populates `CadLayers` on the UI thread asynchronously.

#### D. Dual-Mode Polygonization Engine & Automatic Line Ingestion
1. **Direct Closed Polygons**: Reads closed polygons directly from the CAD `Polygon` feature class, verifies non-emptiness, and densifies and cleans geometries via `ParcelPolygonizer.DensifyAndClean(cadPoly)`.
2. **Automatic Line Polygonizer**:
   - When CAD parcels are drafted as open polylines or individual line segments (or when the CAD `Polygon` feature class returns 0 features, or when `Build polygons from lines` is checked), lines from eligible layers are ingested from the CAD `Polyline` feature class.
   - Closed planar rings are assembled using `LinePolygonizerService.Polygonize()` within the user-specified `SnapToleranceMeters` (default: 0.05 m).
   - Supports `Group layers together (shared edges)` for shared boundary topology.
3. **Headless / Offline Fallback Synthesis**: If executing without an active map session, the engine generates geometrically valid planar parcels matching the detected or expected layers to ensure downstream pipeline stages remain fully operational and testable.

#### E. Cadastral Layer Naming & Categorization Overhaul (`InferRoleFromName`)
- Correctly classifies Arabic and English cadastral layer naming conventions:
  - **Main Parcels (`Boundary`)**: `commercial`, `comm`, `residential`, `mixed`, `mixed use`, `سكني`, `تجاري`, `استثماري`, `الاوجه`, `الاجهه`, `واجهات`, `قطع`, `قطعة`, `اراضي`, `أراضي`.
  - **Sub-Parcels / Utilities (`Service`)**: `serv`, `util`, `mosque`, `جامع`, `مسجد`, `حديقة`, `حدائق`, `garden`, `park`, `room`, `parking`, `مواقف`, `hospital`, `مستشفى`, `school`, `مدرسة`, `حكومي`, `كهرباء`.
  - **Text & Annotations**: Restricted strictly to text, dimension, and annotation layers (`text`, `txt`, `memo`, `dim`, `number`, `num`, `رقم`, `ارقام`, `ابعاد`, `نصوص`, `كتابة`).
- Strips complex AutoCAD MText formatting codes (`{\fArial|b0|i0;...}`) and XArab glyph encodings to ensure pure Arabic text and numeric parcel numbers.

#### F. Discrepancy Tracking & Quality Assurance (`PARCEL_Differences`)
- Auto-detects and segregates parcels exhibiting cadastral anomalies (area difference > 0.5 m² against `Areas Excel`, missing `PLOT_ID`, unapproved `USAGE`, or incomplete cardinal boundaries) into a dedicated `PARCEL_Differences` feature class for instant visual inspection.

#### G. Verification & Quality Benchmark (v3.3)
- **Unit & Integration Test Suite:** Automated unit and integration test suite (`Cad2Gis.Tests`) passing successfully.
- **Automated Deployment:** Compiled and packaged into `Cad2Gis.esriAddinX` via `package_addin.ps1` and deployed to `%USERPROFILE%\Documents\ArcGIS\AddIns\ArcGISPro\{907b00a7-4503-4bff-9969-268e429817bd}`.

---

### 13. Production Addendum (v3.4) — High-Performance Polygonizer, Resilient Final Export & Auto-Numbered Delivery Folder

#### A. Extreme Performance Optimization in Step 2 (Building & Extracting Polygons)
1. **Curved-Only Segment Densification (`ParcelPolygonizer.DensifyAndClean`)**:
   - In previous iterations, `DensifyByLength` with 0.1 m was invoked unconditionally on all geometries, causing severe vertex explosion (up to 5,000 vertices per rectangular plot) and degrading all subsequent topological operations.
   - The engine now explicitly tests `poly2D.HasCurves`:
     * If `!poly2D.HasCurves`, densification is completely skipped for linear segments, preserving clean, lightweight geometry.
     * If `poly2D.HasCurves` is true, smooth arc densification is applied only to curved segments (`maxSegmentLength >= 0.5m`).
     * `SimplifyAsFeature` is executed conditionally only when `!IsSimpleAsFeature`.
2. **High-Speed 2D Spatial Grid Index for Annotation Matching**:
   - Replaced the $O(N \times M)$ nested loop between parcels and CAD annotation texts with a spatial hash grid index.
   - Parcels are indexed into 100-meter spatial buckets based on their planar bounding box (`Extent`).
   - For every text coordinate, candidate parcels are retrieved in $O(1)$, filtered by fast bounding box overlap, and evaluated via point-in-polygon only when necessary, terminating on the first match (`break`).
   - Result: Annotation-to-parcel matching for 2,500+ plots executes in under **0.05 seconds** (previously several minutes).
3. **Spatial Hash Grid Snapping & Adjacency in Line-to-Polygon Engine (`LinePolygonizerService`)**:
   - `SnapSegments`: Replaced $O(N^2)$ linear point searches with a spatial grid dictionary (`tolerance * 2` cell size), achieving instantaneous cluster snapping.
   - `AssembleCycles`: Built an adjacency endpoint lookup grid, allowing $O(1)$ segment connection instead of repeated $O(N)$ full-array scans.

#### B. Resilient Final Export (Unconditional Deliverable Generation)
1. **Non-Blocking QA/QC Validation**:
   - Even when strict municipal audit rules report discrepancies (e.g. 2,494 errors or missing optional fields), the tool **never blocks or aborts export**.
   - All errors and warnings are recorded in `QaStatusSummary` and documented in detail inside `QA_Audit_Report.xlsx`, while the primary deliverables (`PARCEL.shp`, `border.shp`, `PARCEL_Differences.shp`, `.cpg`, Geodatabase) are generated and delivered unconditionally.
2. **Isolated Deliverable Stages**:
   - Shapefile generation, Geodatabase creation, and Excel compliance auditing operate in isolated `try/catch` pipelines. A failure in external Geodatabase creation or file locks in Excel never prevents Shapefiles from completing.
   - `ExportToFeatureClassAsync` automatically bootstraps the destination File Geodatabase (`Export_Deliverables.gdb`) and clones official schemas from templates before inserting records.

#### C. Intelligent Delivery Folder Naming & Auto-Numbered Collision Prevention (`ExportNamingService`)
1. **Standardized Delivery Folder Hierarchy**:
   - Deliverables are systematically written into a structured subfolder inside `OutputDirectory` named:
     `Plan_{PlanNo}_{City}_{District}_{YYYYMMDD}`
     (e.g., `Plan_629_Jadahh_AlRabuah_20261020`).
2. **Auto-Increment Numbering on Existing Data**:
   - If the delivery folder already exists and contains files, the engine prevents overwriting or data loss by automatically appending an incremental sequence suffix:
     `Plan_629_Jadahh_AlRabuah_20261020_1`
     `Plan_629_Jadahh_AlRabuah_20261020_2`
   - Existing deliverables are permanently safeguarded.

#### D. Live Action Progress Bottom Console
- Upgraded the bottom pane in `Cad2GisDockpaneView.xaml` into an interactive live monitoring console:
  * Dynamic status dot indicator (🟢 emerald for success, 🔵 pulsating cyan during processing, 🔴 crimson on error).
  * Exact action title (`CurrentActionText`) and percentage badge (`ProgressPercentageText`).
  * High-visibility progress bar supporting determinate and indeterminate modes.
  * Real-time parcel-by-parcel sub-counter (`ProgressDetailsText`) and active phase badge (`ActivePhaseSummary`).

#### E. Verification & Quality Benchmark (v3.4)
- **Unit & Integration Test Suite:** 87 tests passing with 0 failures (`Cad2Gis.Tests` at 100% pass rate).
- **Automated Deployment:** Compiled, verified, and packaged into `Cad2Gis.esriAddinX` via `package_addin.ps1`.

---

### 14. Production Addendum (v3.5) — Custom Schema Ingestion, Automated Schema Verification & Instant Cadastral Field Mapping Engine

#### A. Architectural Reorganization: Relocating "Use My Own Data" to Step 3 (Populate Schema)
1. **User Requirement & Logical Separation of Concerns**:
   - In earlier versions, an option labeled `استخدام بياناتي الخاصة (Use My Own Data)` was located in Step 2 (`2. Extract Parcels`). This caused workflow ambiguity because Step 2's primary mathematical responsibility is spatial extraction, polygonization, and geometric reconstruction from CAD linework/polygons.
   - In v3.5, `Use My Own Data` is relocated to **Step 3 (`3. Populate Schema`)**, while Step 2 is dedicated purely to geometric parcel construction, True Curves arc preservation, planar 2D coordinate enforcement, and spatial plot text association.
   - Step 3 now features a top-level source selector:
     * `(•) تعبئة البيانات القياسية المعتمدة (Standard Municipal Schema / MOMRAH)`: For CAD drawings following standard municipal layouts, manual metadata entry, automated spatial administrative jurisdiction detection, and optional external land-use/areas Excel tables.
     * `( ) استخدام بياناتي الخاصة (Use My Own Data)`: For users providing external attribute tables, spreadsheets, or pre-attributed shapefiles/feature classes with custom schemas.

#### B. Standard MOMRAH 22-Target Field Definitions & Domain Synonyms (`CustomSchemaDataService.cs`)
1. **Exhaustive Target Field Catalog**:
   - `CustomSchemaDataService.MomrahTargetFields` defines 22 standard MOMRAH target fields with Arabic descriptions, mandatory flags, and known synonym dictionaries:
     * `PLOT_ID`: رقم القطعة (مفتاح الربط الأساسي) [Mandatory] — Synonyms: `plot_id`, `plotid`, `plot_no`, `plotno`, `plot`, `parcel_id`, `parcelid`, `رقم_القطعة`, `رقم القطعة`, `القطعة`, `الرقم`, `id`, `num`
     * `PLAN_NO`: رقم المخطط المعتمد [Mandatory] — Synonyms: `plan_no`, `planno`, `plan_num`, `plan`, `رقم_المخطط`, `رقم المخطط`, `المخطط`, `المخطط_المعتمد`
     * `USAGE`: كود/نوع الاستخدام [Mandatory] — Synonyms: `usage`, `land_use`, `landuse`, `use`, `الاستخدام`, `نوع_الاستخدام`, `استخدام`, `نوع الاستخدام`
     * `BLOCK_NO`: رقم البلك أو المجاورة [Optional] — Synonyms: `block_no`, `blockno`, `block`, `البلك`, `رقم_البلك`, `رقم البلك`, `بلك`, `مجاورة`
     * `NEIGHBOURH`: كود الحي البلدي [Mandatory] — Synonyms: `neighbourh`, `neighborhood`, `neighbourhood`, `district`, `الحي`, `كود_الحي`, `كود الحي`, `اسم_الحي`, `اسم الحي`
     * `ADDRESS`: نص العنوان المنظم الكامل [Optional] — Synonyms: `address`, `addr`, `العنوان`, `عنوان_العقار`, `العنوان_الوطني`
     * `CITY_ID`: كود/اسم المدينة [Mandatory] — Synonyms: `city_id`, `cityid`, `city`, `المدينة`, `كود_المدينة`, `كود المدينة`, `اسم_المدينة`
     * `REGION_ID`: كود/اسم المنطقة [Mandatory] — Synonyms: `region_id`, `regionid`, `region`, `المنطقة`, `كود_المنطقة`, `كود المنطقة`, `اسم_المنطقة`
     * `PARCEL_TYP`: تصنيف القطعة (1=رئيسية، 2=مرفق) [Optional] — Synonyms: `parcel_typ`, `parceltyp`, `parcel_type`, `نوع_القطعة`, `تصنيف_القطعة`, `النوع`
     * `REALESTA_1`: نوع العقار (2=أرض فضاء، 1=مبنى) [Optional] — Synonyms: `realesta_1`, `realestate`, `realestate_type`, `نوع_العقار`, `العقار`
     * `ISBUILDING`: هل العقار مبني (0=لا، 1=نعم) [Optional] — Synonyms: `isbuilding`, `is_building`, `building`, `مبنى`, `بناء`, `هل_مبني`
     * `BUILDINGTY`: نوع البناء (1=مسلح حديث) [Optional] — Synonyms: `buildingty`, `building_type`, `buildingtype`, `نوع_المبنى`, `نوع_البناء`
     * `REMARKS`: بيان / ملاحظات الاستخدام بالعربي [Optional] — Synonyms: `remarks`, `remark`, `notes`, `note`, `ملاحظات`, `ملاحظة`, `البيان`, `الوصف`, `وصف`
     * `LAYOUT`: المجاورة التخطيطية [Optional] — Synonyms: `layout`, `sub_layout`, `المجاورة`, `مجاورة`
     * `REQUEST_TY`: نوع الطلب البلدي [Optional] — Synonyms: `request_ty`, `request_type`, `نوع_الطلب`, `الطلب`
     * `REQUEST_NO`: رقم الطلب البلدي [Optional] — Synonyms: `request_no`, `requestno`, `رقم_الطلب`
     * `REPORT_NO`: رقم تقرير المعاينة/المخطط [Optional] — Synonyms: `report_no`, `reportno`, `رقم_التقرير`, `التقرير`
     * `AREA_SQM`: المساحة المعتمدة بالمتر المربع [Optional] — Synonyms: `area_sqm`, `areasqm`, `area`, `shape_area`, `المساحة`, `مساحة`, `المساحة_م2`
     * `PLOT_NO1`..`PLOT_NO4`: أرقام القطع المقسمة أو المدمجة [Optional].

#### C. Automated Schema Verification & Status Logic (`CheckSchema`)
- When a file path is provided in `CustomSchemaDataPath`, the system extracts all column headers/field names.
- `CheckSchema(sourceFields)` compares the extracted list against all standard MOMRAH field definitions:
  * **100% Identical Schema**:
    If all target fields are present in the source file, `IsSchemaIdentical` is set to `true`.
    Status Banner displays: `✅ السكيما مطابقة تماماً للمواصفات المعتمدة (100% Identical MOMRAH Schema) - تم العثور على جميع الحقول الـ 22.` with dark emerald styling (`#064E3B`, Border `#10B981`, Text `#34D399`).
    The Field Mapping table is **collapsed by default** (`IsFieldMappingExpanded = false`) since no manual intervention is required.
  * **Different Schema**:
    If fields are missing, named differently, or have custom naming, `IsSchemaIdentical` is set to `false`.
    Status Banner displays: `⚠️ السكيما تختلف عن الاسكيما المعتمدة (X/22 حقول معتمدة، وتم ربط Y حقلاً تلقائياً). تم فتح جدول ربط الحقول.` with amber styling (`#451A03`, Border `#F59E0B`, Text `#FCD34D`).
    The Field Mapping table is **automatically expanded** (`IsFieldMappingExpanded = true`) to immediately show users the mapping status.

#### D. Multi-Tier Intelligent Field Matcher (`AutoMatchField` — "يربط في الfield mapping عطول")
- When custom data is ingested, every target field is passed to `AutoMatchField(targetField, sourceFields)`:
  1. **Tier 1 (Exact Match)**: Case-insensitive match against target name (e.g., `PLOT_ID`, `PLAN_NO`).
  2. **Tier 2 (Normalized Match)**: Match ignoring underscores, hyphens, and whitespace (e.g. `plotid` matches `PLOT_ID`, `planno` matches `PLAN_NO`, `blockno` matches `BLOCK_NO`).
  3. **Tier 3 (Synonym & Arabic Alias Match)**: Match against comprehensive cadastral dictionaries covering both colloquial and technical English/Arabic names.
     * Example: Source field `رقم_القطعة` or `رقم القطعة` immediately binds to `PLOT_ID`.
     * Example: Source field `المخطط` immediately binds to `PLAN_NO`.
     * Example: Source field `الاستخدام` immediately binds to `USAGE`.
     * Example: Source field `الحي` immediately binds to `NEIGHBOURH`.
     * Example: Source field `المدينة` immediately binds to `CITY_ID`.
     * Example: Source field `المساحة` immediately binds to `AREA_SQM`.
- **Zero-Friction Result**: All matching fields are mapped instantly upon file selection without requiring manual selection for every row.

#### E. Cross-Format Ingestion Engine (`CustomSchemaDataHelper.cs`)
1. **Excel Workbooks (`.xlsx`, `.xls`)**:
   - `ReadExcelHeaders`: Reads header row via ClosedXML and returns unique column names.
   - `ReadExcelRows`: Ingests rows into `List<Dictionary<string, object>>` keyed by column header.
2. **ESRI Shapefiles (`.shp`)**:
   - `ExtractFields`: Opens datastore via ArcGIS Pro SDK `FileSystemDatastore`, accesses feature class definition, and filters out internal fields (`FID`, `Shape`).
   - `ReadRecords`: Iterates feature records through `fc.Search()` into dictionary rows.
3. **File Geodatabase Feature Classes / Tables (`.gdb`)**:
   - Supports paths containing `.gdb` and feature class names.
   - Opens `Geodatabase` via `FileGeodatabaseConnectionPath`, extracts fields (excluding `OBJECTID`, `Shape`, `Shape_Length`, `Shape_Area`), and streams records into dictionaries.

#### F. Reactive MVVM DockPane & Field Mapping UI
1. **`FieldMappingItem` Observable Model**:
   - Implements `PropertyChangedBase` with `TargetField`, `TargetDescription`, `IsMandatory`, and `SelectedSourceField`.
   - Each item maintains its own `AvailableSourceFields` collection containing `-- غير محدد --` plus all detected source fields.
   - Reactive status badge: `StatusText` (`✔ مربوط` in green `#10B981`, `إلزامي (غير مربوط)` in red `#EF4444`, or `اختياري` in slate `#94A3B8`).
2. **Interactive DataGrid**:
   - Styled with dark workstation theme tokens (`CadDataGridHeaderStyle`, `CadDataGridRowStyle`, background `#1E293B`, border `#334155`).
   - Four informative columns:
     * `الحقل المعتمد (Target)`: Bold cyan `#38BDF8`.
     * `الوصف بالعربي`: High-contrast Arabic descriptor `#CBD5E1`.
     * `حقل البيانات الخاصة (Source Field)`: ComboBox using `DarkComboBoxStyle` and `DarkComboBoxItemStyle`.
     * `الحالة`: Real-time color-coded mapping status indicator.
   - Action header with `⚡ إعادة الربط التلقائي (Auto-Map)` button bound to `AutoMapFieldsCommand`.

#### G. Execution Pipeline in Step 3 (`ExecuteStep3PopulateSchemaAsync`)
1. **When `IsSchemaFromCustom` is Active**:
   - Ingests records via `CustomSchemaDataHelper.ReadRecords`.
   - Resolves the source field mapped to `PLOT_ID` and creates a high-speed dictionary index `recordsByPlotId`.
   - For each parcel in `_parcels`:
     * Matches record by `PlotId` (or sequential row fallback).
     * `ApplyCustomRowToParcel`: Transcribes values for all mapped fields (`PlotId`, `PlanNo`, `Usage`, `BlockNo`, `Neighbourhood`, `Address`, `CityId`, `RegionId`, `RealestateType`, `IsBuilding`, `BuildingType`, `Remarks`, `Layout`, `RequestType`, `RequestNo`, `ReportNo`, `AreaSqm`, etc.).
     * `MissingDataPolicy.ApplyDefaults(p, _projectMeta, forceUpdate: false)`: Safely fills standard fallback defaults for unmapped or empty attributes.
   - Commits all parcels to native Geodatabase Feature Class (`PARCEL`) and refreshes live map layer.
2. **When `IsSchemaFromStandard` is Active**:
   - Preserves standard municipal flow (joining external land-use table, validating areas Excel, spatial admin jurisdiction detection, and structured address generation).

#### H. Geometric Invariant Guarantee & True Curves Preservation
- All geometries are strictly validated for 2D Planar metrics:
  * Eliminates Z and M values across all feature class writes.
  * Arc segments in CAD drawings are preserved as native circular strings without destructive linear chopping.
  * Vertex explosion is completely eliminated on straight lines (`!poly2D.HasCurves` check).

#### I. Verification & Quality Benchmark (v3.5)
- **Unit & Integration Test Suite:** 113 automated unit tests passing with 0 failures (`Cad2Gis.Tests` at 100% pass rate).
- **Dedicated Test Coverage (`CustomSchemaFieldMappingTests.cs`)**:
  * `CheckSchema_WhenAllStandardFieldsPresent_ReturnsIdenticalTrue`
  * `CheckSchema_WhenDifferentSchema_ReturnsIdenticalFalse`
  * `AutoMatchField_CorrectlyMatchesExactAndSynonyms` (18 parameterized test cases covering exact, case-insensitive, normalized, and Arabic aliases)
  * `AutoMatchField_WhenNoMatchFound_ReturnsEmpty`
  * `ReadExcelHeadersAndRows_WithCustomSchema_ReadsSuccessfully`
- **Deployment Artifact:** Successfully compiled, validated, and packaged into `Cad2Gis.esriAddinX` via `package_addin.ps1`, available at workspace root for double-click manual installation.

---

### 15. Production Addendum (v3.6) — Border Extraction Method from Main Parcel Boundaries (Part A)

#### A. Core Architectural Scope & Invariant Principles
1. **Target Population:** MAIN parcel polygons only (`PARCEL_TYP = 1` / internal class MAIN) evaluated in working planar UTM coordinates (meters).
2. **Boundary Lines Only:** Converts polygon boundary rings to lines and works strictly on those lines. Never creates artificial lines; only **split** and **merge** of original boundary pieces are permitted.
3. **True Curves Preservation:** True curves (circular arcs, cubic Béziers) remain true curves in output geometry. Zero densification, smoothing, or straightening of output geometry. Densified copies may be used internally for angle/direction measurements only and are never written to disk.
4. **Independent Hole Processing:** Every polygon ring (exterior CW, interior holes CCW) is processed independently as a separate part. Lines of different rings are never merged.
5. **Output Cardinal Topology:** At most 4 single-part polylines per ring (1 = North, 2 = East, 3 = South, 4 = West). Union of all outputs equals original boundary exactly.
6. **Repeated Directions Constraint:** Repeated directions are allowed strictly across different rings (holes). Inside any single ring, each cardinal direction appears at most once, and every line is strictly single-part.

#### B. Step 1 — Interior Angle Corner Detection, Reflex Corners & Clusters
1. **Interior Angle Definition:** Computed from inside parcel material. Split point created when interior angle $\in [A_{\min}, A_{\max}]$ (default 20°–130°).
2. **Reflex Corners (Concave Turns):** Concave corners (e.g. inside corner of an L-shaped parcel) have interior angles $> 180^\circ$. Split point created when angle $\in [360^\circ - A_{\max}, 360^\circ - A_{\min}]$ (default 230°–340°). Configurable toggle: `IncludeReflexCorners` (default ON).
3. **Virtual Chords for Curves:** For angle tests, arcs are represented by virtual chords connecting start and end points. Real arc geometry is preserved intact in output.
4. **Arc-to-Arc Joints:**
   - **Same Circle:** Identical center and radius $\rightarrow$ redundant joint, split point skipped.
   - **Smooth Compound Curve:** Tangents match within tolerance $\rightarrow$ split point skipped.
   - **Tangent Break:** Evaluated via virtual chord angles.
5. **Corner Clusters (Chamfers & Rounded Corners):** Consecutive vertices closer than $L$ (default 7 m) treated as one corner when combined interior angle falls in range. Split at both ends so chamfer/arc becomes its own short piece.

#### C. Step 2 — Outward Normal Direction & True North Alignment
1. **Outward Normal Azimuth:** Points outward from parcel material (for holes, points into the hole). Length-weighted circular mean used for bent pieces.
2. **True North Meridian Convergence:** Corrects UTM grid azimuth by meridian convergence at centroid: $\gamma = (\lambda - \lambda_0) \sin(\phi)$.
3. **4-Direction Cross Alignment:** Assigns pieces to nearest cardinal direction (1=N, 2=E, 3=S, 4=W) minimizing total angular deviation with opposite cross constraints (N $\leftrightarrow$ S, E $\leftrightarrow$ W) and deterministic tie-breaking.

#### D. Step 3 — Cyclic Sequence Merging (Single-Part Lines)
1. **Adjacent Grouping:** Adjacent pieces of identical direction are merged into a single-part polyline.
2. **Rule 1 (Absorb In-Between):** If two runs of direction X are separated by a shorter run of Y, Y merges into X if Y is shorter than both flanking runs and Y retains another run.
3. **Rule 2 (Longest Wins):** Longest run of X keeps X; shorter runs merge into adjacent line whose direction is angularly closest.
4. **Flagging Incomplete Directions:** Rings with fewer than 4 directions (triangles, 3-sided rings) are flagged with `FEWER_THAN_4_DIRECTIONS` in QA without inventing spurious lines.

#### E. A5 Blocking Geometry Preservation Checks
- Sum of output line lengths equals total perimeter within $10^{-6}\text{ m}$.
- Output lines union equals boundary (zero symmetric difference, no gaps, no overlaps).
- Number of curve segments invariant; every output polyline is single-part. Any failure blocks export.

---

### 16. Production Addendum (v3.7) — "Continue from Latest Export" Workflow & Manual Edits Preservation (Part B)

#### A. Export State Tracking (`last_export.json`)
- Writes state file to `HelperFiles\state\last_export.json` upon every successful export.
- Records run ID, timestamp, export folder, dataset paths (Shapefile/GDB), CRS, UTM zone, source CAD name, profile name, file SHA-256 hashes, file sizes, feature counts, and history of last $N$ exports (default 5).

#### B. Source Selector & Working Context
- Added `"Latest export (includes my edits)"` option to Stage 3/4 Borders, Stage 5 QA, Area Comparison, and Re-Export.
- Defaulted when a latest export exists (`continue_from_latest_export = true`).
- Status card shows working path, timestamp, and edit status with buttons: Change, Use session data, Open folder, and History dropdown.

#### C. Loading & Discrepancy Reconciliation
- Reprojects GCS export back to working UTM before computation.
- Identifies MAIN/SUB by `PARCEL_TYP` (1=MAIN, 2=SUB). Repeated directions disambiguated by `RingIndex`.
- External edits detected by file hash, modified timestamp, and feature counts. Diff table lists added, deleted, geometry changed, and attribute changed features.
- If geometry changed, compares computed UTM area/perimeter with frozen metrics (`AREA_SQM`, `Shape_Area`, `Shape_Length`). Discrepancies prompt user: (a) recompute derived fields, or (b) keep stored values with QA warning. User-typed attributes are never overwritten.
- Re-running borders over manually edited borders prompts user: Keep edited borders / Regenerate.
- Missing export path shows visible warning with selection options; never falls back silently.

---

### 17. Production Addendum (v3.8) — ArcGIS Pro SDK Non-Blocking Anti-Freeze Architecture & Threading Standards

#### A. The Deadlock Root Cause
In the ArcGIS Pro SDK, `Geoprocessing.ExecuteToolAsync` cannot run inside `QueuedTask.Run(...)` because geoprocessing methods manage their own internal queuing onto the Pro geoprocessing worker thread and require the Main CIM Thread (MCT) to be free. When a GP tool is called inside `QueuedTask.Run(...)`, the MCT thread is locked waiting for the GP tool to finish, while the GP tool is waiting for the MCT thread to become free, creating an **immediate and permanent application freeze**.

#### B. Three-Tier Threading Isolation Standard
1. **Tier 1 — Geoprocessing Tools (Background Async):**
   - Calls to `EnsureFeatureClassFromTemplateAsync`, `RunFeatureToPolygonAsync`, `ConvertCadToDatasetAsync`, and `Geoprocessing.ExecuteToolAsync` must execute **strictly OUTSIDE `QueuedTask.Run`**.
2. **Tier 2 — Geodatabase Entity Editing & Geometry Engine (MCT QueuedTask):**
   - Geodatabase operations (`OpenDataset`, `CreateRowBuffer`, `CreateInsertCursor`, `Search`, `Delete`), map layer adding/refreshing (`AddCadDatasetToMap`, `AddOrRefreshMapLayer`), and geometry builders (`PolygonBuilderEx`, `GeometryEngine`) must run **inside synchronous `QueuedTask.Run(() => { ... })`**.
   - **Prohibition of Nested QueuedTask:** Never invoke `await QueuedTask.Run(...)` inside an active `QueuedTask.Run`.
3. **Tier 3 — WPF MVVM UI Dispatching (UI Thread):**
   - Observable collection modifications (`CadLayers`, `InferredBorders`, `GeometryDiffList`) and user dialogs (`MessageBox.Show`) must run **directly on the UI thread** outside `QueuedTask.Run`.
   - **Prohibition of `Dispatcher.Invoke`:** Never invoke synchronous `Dispatcher.Invoke` from inside `QueuedTask.Run`.

#### C. Verification & Quality Benchmark (v3.8)
- **Unit & Integration Test Suite:** 131 automated unit tests passing with 0 failures (`Cad2Gis.Tests` at 100% pass rate).
- **Dedicated Coverage:**
  * `BorderExtractionMethodTests.cs`: 15 synthetic test cases verifying A7 edge cases (chords, clusters, reflex angles, true curves, cyclic invariant merging, hole into-hole normals, geometry preservation).
  * `LatestExportWorkflowTests.cs`: 7 integration test cases verifying B7 workflow (state roundtrip, history depth, external file hash change detection, diff table detection, frozen metric prompts, locked file read-only fallback, and missing path error handling).
- **Deployment Artifact:** Successfully compiled with 0 warnings, 0 errors, and packaged into `Cad2Gis.esriAddinX` via `package_addin.ps1`.

---
### 18. Production Addendum (v3.9) — Line Mode Ingestion of All Selected Layers & Universal Working Data Source Across All Steps

#### A. Guaranteed Ingestion of All Selected Layers in Line Mode
1. **The Root Filtering Problem**:
   - In previous iterations, `IsLayerEligibleForPolygonCreation` was constrained by `SelectedPolygonLayerScope` and rejected any layer with `Role == LayerRole.Ignore` (such as layers cataloged under Roads & References). When users selected `Line (خطوط فقط)` and checked multiple line layers, layers outside "Main Parcels" were discarded, causing incomplete parcel generation.
2. **Comprehensive Fix**:
   - `PolygonLayerScopeService.IsLayerEligible(layer, scope, isLineMode)` now treats **100% of user-checked layers (`layer.IsSelected == true`)** (except pure text annotations: `Category != "Text & Annotations"` and `GeometryType != "Text"`) as **fully eligible** whenever line mode is active (`LayerGeometryModeService.ShouldPolygonizeLines(SelectedGeometryMode, BuildPolygonsFromLines)`).
   - In `GdbFeatureClassService.RunFeatureToPolygonAsync`, the SQL selection uses case-insensitive matching:
     $$\text{UPPER(Layer) IN ('LAYER1', 'LAYER2', ...)}$$
     guaranteeing that layers are captured regardless of uppercase, lowercase, or mixed casing in the CAD drawing.
   - All eligible lines are fed into `management.FeatureToPolygon` and, if needed, into `LinePolygonizerService.Polygonize()` to assemble closed planar rings.

#### B. Universal Working Data Source Across Steps 2 Through 5
1. **Step-by-Step Consistency**:
   - The **Working Data Source Selector** (`Latest export`, `Session data`, `Use My Own Data`) and the **Working From Summary Bar** (`Working from: <path> — exported <timestamp> — edited after export: yes/no` with Open Folder, Change Source, and History Dropdown) are now systematically available across **all downstream steps**:
     * **Step 2 (2. Extract Parcels)**: Enables loading pre-exported parcels from the latest export (reprojected to working UTM) or custom data directly into Step 2.
     * **Step 3 (3. Populate Schema)**: Enables enriching metadata from latest export, session, or custom schemas.
     * **Step 4 (4. Borders)**: Enables border decomposition from latest export (with external edit detection banner and diff table).
     * **Step 5 (5. Export & QA)**: Enables export auditing from latest export, session, or custom data.

---
### 19. Production Addendum (v4.0) — Strict Geometry Preservation & Zero Arc-to-Line Conversion Standard

#### A. Overriding Principle: Original Geometry is the Data
Under no circumstances may the tool convert true curves (`EllipticArcSegment`, circular arcs, polyline bulges) into straight-line chord approximations in the working UTM environment. The tool may split, merge, and read original geometry pieces, but must never re-sample, smooth, densify, snap, or rebuild them from coordinate/vertex lists.

#### B. Code Audit & Fixes on Stored Geometry
1. **Elimination of Densification & Simplification on Stored Geometry**:
   - All legacy calls to `ParcelPolygonizer.DensifyAndClean` have been marked `[Obsolete]` and replaced with `ParcelPolygonizer.Clean2DPlanar(polygon)`, which strictly strips Z and M values (`HasZ = false; HasM = false;`) with 0 alterations to 2D coordinates or segment curves.
   - Banned operations on stored geometry (`Densify`, `DensifyByLength`, `DensifyByDeviation`, `Generalize`, `Simplify`) are strictly prohibited and enforced via automated static code-audit unit tests.
2. **Curve-Preserving Polygonization (`CurvePreservingPolygonizerService.cs`)**:
   - Polygons are constructed by chaining the original native `Segment` instances directly (`LineSegment`, `EllipticArcSegment`, `CubicBezierSegment`) via `PolygonBuilderEx.AddPart(IEnumerable<Segment>)`.
   - Rebuilding rings from point lists (`PolygonBuilderEx.CreatePolygon(pts)`), which silently dropped curve information, is completely eliminated.
   - Closure gaps $\le$ snap tolerance are bridged by inserting a minimal straight closing segment without moving any existing vertex. Larger gaps are flagged and reported as dangles.
3. **Mandatory Input Scope & Manifest / Coverage Check**:
   - Line polygonization ingests ALL layers selected by the user in Stage 1 without skipping.
   - Modes supported:
     * **"Group of all selected layers"** (Default): Combines line entities into a single network so shared edges across layers close rings together.
     * **"Per layer"**: Processes each selected layer separately, still processing every layer.
   - **Input Manifest**: Reports entities read, kept as-is (closed polylines), entered network, and ignored per layer. Any layer contributing 0 entities is flagged as an ERROR.
   - **Coverage Check**: Reports unused network lines (dangles/orphans) and polygons contributed per layer.
   - **Stale Invalidation**: Any change to layer selection, grouping mode, or tolerance invalidates previous results and displays a warning banner.
4. **Borders & Curve Splitting**:
   - Splitting an arc at a distance produces sub-arcs retaining the exact same circle (identical center, radius, and direction). Merging concatenates original segments.
   - Measurements (`AREA_SQM`, `Shape_Area`, `Shape_Length`) are calculated from true curve geometry analytically ($0.5 R^2(\theta - \sin\theta)$ circular cap formula).
5. **Controlled Deviation Export for Shapefile/GCS Deliverables**:
   - Shapefile and GCS projection cannot physically store true curves. Controlled deviation densification (`GeometryEngine.Instance.DensifyByDeviation`) is applied using the user-configured tolerance (`MaxCurveExportDeviationMeters`, default 0.001 m / 1 mm) instead of coarse default chord approximations.
   - Option **"Also keep an exact UTM copy with true curves"** exports a companion File Geodatabase feature class retaining 100% true curves.
   - Frozen metrics (`AREA_SQM`, etc.) are computed from exact UTM true curves BEFORE any export conversion.
6. **Geometry Preservation Audit & Blocking Stage Checks (`CurveAuditService.cs`)**:
   - `CurveAuditService` inspects segments by type (line, circular arc, elliptic arc, Bézier), center, radius, length, and analytic area.
   - Blocking checks in working UTM data ensure:
     * Curve count does not decrease.
     * Center and radius match within $10^{-6}$ m.
     * Length and area match within $10^{-6}$.
   - Writes `Geometry_Preservation_Report.txt` upon export.

#### C. Verification & Automated Test Suite (138 Tests)
- **138 Unit & Integration Tests Passing at 100%** with 0 warnings and 0 errors.
- **Dedicated Coverage in `GeometryPreservationTests.cs`**:
  * `Test1`: Parcel with corner arc & circular boundary: center, radius, length unchanged; analytic area & perimeter verified.
  * `Test2`: Circle stored as 2 arcs stays 2 arcs; splitting produces sub-arcs on identical circle.
  * `Test3`: Multi-layer line closure with 3 layers; 0-entity error flag; stale marking; per-layer mode.
  * `Test4`: Lines-to-polygons with arcs; no vertex moved; minimal gap bridging vs large gap dangle reporting.
  * `Test5`: Exact vertex coordinate preservation ($\le 10^{-9}$ m tolerance).
  * `Test6`: Controlled export deviation ($\le 0.001$ m); frozen metrics preservation from exact UTM curves.
  * `Test7`: Static code audit across all C# files enforcing zero banned alterations on stored geometry.


---

### 20. Production Addendum (v4.1) — Elimination of Fake Files & Dynamic Expected Layers Excel Sync

#### A. Zero Fake Files Policy
- `HelperFilesManager.cs` strictly forbids the automatic creation of dummy or empty template files.
- If mandatory helper files (such as layer mapping tables or administrative boundary shapefiles) are missing from `HelperFiles\`, the tool presents a clean warning alert and guides the user to supply the official files, rather than fabricating synthetic data.

#### B. Dynamic Excel Sync with `HelperFiles\Expected Layers`
- In Step 1, the layer discovery engine automatically inspects and reads the active Excel file inside `HelperFiles\Expected Layers\*.xlsx`.
- When the user selects or customizes CAD layers on the active map, clicking `Save Custom Rules to Template` dynamically updates and registers newly selected layers into the Excel workbook, maintaining a synchronized profile across sessions.

---

### 21. Production Addendum (v4.2) — ArcPy Feature To Polygon 2D Integration (`convert.md`)

#### A. ArcPy 2D Planar Geoprocessing Toggle
- Step 2 integrates the selectable option: `[✔] Use ArcPy FeatureToPolygon (2D Planar)` bound to `UseArcPyFeatureToPolygon`.
- When enabled, polygonization delegates to ArcGIS Pro's native `FeatureToPolygon` tool configured strictly with disabled elevation flags:
  ```python
  with arcpy.EnvManager(outputZFlag="Disabled", outputMFlag="Disabled"):
      arcpy.management.FeatureToPolygon(
          in_features=in_line_layers,
          out_feature_class=out_fc,
          cluster_tolerance=cluster_tolerance,
          attributes="NO_ATTRIBUTES"
      )
  ```
- Complies with [convert.md](convert.md): operates asynchronously outside the UI thread, strips all Z/M coordinates, preserves true curves, and outputs directly into the workspace geodatabase for instant Step 3 consumption.

---

### 22. Production Addendum (v4.3) — Structured Delivery Hierarchy (`Final/Final.gdb`, `Shape/`, `Reports/`) & Live Map Display

#### A. Structured Output Directory Hierarchy
Step 5 organizes all final deliverables into a structured `Final/` directory within the export destination:
1. `Final/Final.gdb`: Enterprise Geodatabase containing `PARCEL` and `BORDER` feature classes with 100% True Curves preservation.
2. `Final/Shape`: MOMRAH-compliant 2D shapefiles (`PARCEL.shp`, `border.shp`) accompanied by UTF-8 `.cpg` encoding files for Arabic typography.
3. `Final/Reports`: Comprehensive QA/QC audit logs, discrepancy spreadsheets, and validation certificates.

#### B. Immediate Live Map Layer Addition
- Upon successful execution of Step 5, `GdbFeatureClassService.AddOrRefreshMapLayer` automatically adds and refreshes the generated `PARCEL` and `BORDER` feature classes on the active ArcGIS Pro map (`MapView.Active.Map`), providing immediate visual confirmation to the operator.

---

### 23. Production Addendum (v4.4) — Curvature-Preserving Arc Extension & Unified Multi-Layer Network (148 Passing Tests)

#### A. Curvature-Preserving Arc Extension (`ExtendLinesAndCurves`)
- When unclosed gaps exist between CAD line entities:
  - Default extension distance: **0.05 m (5 cm)** (configurable).
  - **Straight Lines:** Extended along their collinear ray direction until intersection with adjoining boundaries.
  - **Circular Arcs:** Extended **strictly along their true circular curvature** ($\Delta\theta = \Delta s / R$) around the identical center $C$ and radius $R$. Tangent line extensions or chord simplifications on arcs are strictly prohibited, preserving true circle geometry 100%.

#### B. Unified Multi-Layer Network
- All active CAD layers selected in Step 1 are aggregated into a single topological network during polygonization.
- Solves the multi-layer parcel boundary problem where external outlines and internal division lines reside on different CAD layers (e.g. `BND_EXT` and `LOT_LINE`).

#### C. Verification & Automated Test Suite (148 Tests)
- All **148 automated unit and integration tests** in `Cad2Gis.Tests` pass with zero failures:
  - `Test8`: Curvature-preserving arc extension verifying center and radius preservation.
  - `Test9`: Straight line collinear extension for parcel boundary closure.
  - `Test10`: Unified multi-layer network closure combining lines and circular arcs.
  - `Test1` through `Test7`: Geometry preservation, zero banned calls audit, and MOMRAH compliance.

---

### 24. Production Addendum (v4.5) — Reviewed DockPane UI Reference

Fully aligned with `CAD2GIS_Dockpane_Buttons_Guide_Reviewed.xlsx`:
- Complete documentation of all 18 action buttons and commands across the 5 steps and bottom footer.
- Exhaustive listing of toggle options, comboboxes, and review grids.
- Complete specifications available in [gui.md](gui.md) and [CAD2GIS_Dockpane_Buttons_Guide_Reviewed.xlsx](CAD2GIS_Dockpane_Buttons_Guide_Reviewed.xlsx).

---

### 25. Production Addendum (v4.6) — Master Immutable Invariants & 10 Core Architectural Requirements

The 8 Immutable Invariants (INV-01 through INV-08) from [CHANGELOG_MODIFICATIONS.md](CHANGELOG_MODIFICATIONS.md) and the 10 Golden Rules from [promt 01010.md](promt%2001010.md) are the supreme architectural law for CAD2GIS:
1. **INV-01 (No Arc-to-Line):** Absolute ban on `Densify`, `DensifyByLength`, `Simplify`, and `Generalize` on stored geometries.
2. **INV-02 (Unified Multi-Layer Scope):** All selected layers must be polygonized together.
3. **INV-03 (Working Data Source Continuity):** Universal source selector available across Steps 2 through 5.
4. **INV-04 (No Auto-Install):** Addin package output strictly to workspace root; manual user double-click only.
5. **INV-05 (Strict 2D Planar):** `HasZ = false` and `HasM = false` enforced on all outputs.
6. **INV-06 (Analytic Curves Calculation):** Areas and perimeters computed analytically from true mathematical arcs.
7. **INV-07 (Controlled Shapefile Deviation):** Shapefile conversion kept within $\le 0.001\text{ m}$ with true mathematical source preserved in GDB.
8. **INV-08 (Anti-Freeze Architecture):** UI thread isolation with asynchronous geoprocessing and dispatching.

---
**This comprehensive specification reflects the complete, verified v4.4 codebase. All development, extensions, and automated tests adhere strictly to these architectural contracts.**





