# CAD2GIS — Product Overview & Architectural Blueprint (01_PRODUCT_OVERVIEW.md)

> **الملخص التنفيذي بالعربية:**
> يُعد نظام CAD2GIS أداة إضافية متقدمة (Add-in) لبيئة ArcGIS Pro 3.3.0 تم تطويرها بلغة C# ونظام .NET 8، بهدف تحويل المخططات المساحية من أوتوكاد (AutoCAD DWG/DXF) إلى طبقات جغرافية رقمية مطابقة تماماً للمواصفات والاسكيما المعتمدة لوزارة البلديات والإسكان ومنصة بلدي (MOMRAH / Balady). يضمن النظام الحفاظ الكامل على المنحنيات الحقيقية (True Curves)، وتجريد الأبعاد الثلاثية لإنتاج طبقات مسطحة 2D Planar، وحساب المقاييس بدقة، والامتثال للقواعد غير القابلة للتغيير (Immutable Invariants).

---

## 1. Executive Summary & Purpose

CAD2GIS automates the complete lifecycle of converting architectural and subdivision CAD layouts into certified municipal GIS datasets. It eliminates the manual, error-prone editing historically required to format AutoCAD drawings into the 26-field MOMRAH parcel schema and 10-field cardinal boundary schema.

### 1.1 Target Users & Operating Environment
- **Municipal Surveyors & Engineering Consultants:** Preparing cadastral masterplans for submission to Saudi Amanat (Riyadh, Makkah, Eastern Province, Jeddah, etc.).
- **GIS Operations Teams:** Validating incoming surveyor deliverables against MOMRAH / Balady data specifications.
- **Operating Environment:** Runs as a native desktop Dockpane within **ArcGIS Pro 3.3.0** on 64-bit Windows workstations, alongside an independent Python Toolbox (`Cad2Gis_ArcPy.pyt`) for automated batch or command-line execution.

---

## 2. Technical Stack & Target Platform

| Component | Specification | Rule & Enforcement |
| :--- | :--- | :--- |
| **Host Application** | ArcGIS Pro 3.3.0 (x64) | MUST run natively inside ArcGIS Pro 3.3.0. |
| **Add-in Runtime** | Microsoft .NET 8.0 Windows (`net8.0-windows`) | MUST target `net8.0-windows` with WPF enabled. |
| **Core Domain Library** | Microsoft .NET 8.0 Standard (`net8.0`) | MUST be pure C# (`net8.0`) without ArcGIS Pro SDK dependencies, enabling 100% unit-testability outside ArcGIS Pro. |
| **Pro SDK Assemblies** | ArcGIS Pro SDK for .NET 3.3.0 | References `ArcGIS.Core.dll`, `ArcGIS.Desktop.Framework.dll`, `ArcGIS.Desktop.Mapping.dll`, `ArcGIS.Desktop.Editing.dll`. |
| **Excel Parser** | ClosedXML 0.104.2 | In-memory spreadsheet parsing for templates, lookup tables, and custom schema files without requiring Microsoft Office. |
| **Packaging Format** | `.esriAddinX` (ZIP container) | Staged via `package_addin.ps1` with root `Config.daml`, `Images/`, and `Install/` assemblies. |
| **ArcPy Fallback Engine** | Python 3.9+ (ArcGIS Pro default Python environment) | `Cad2Gis_ArcPy.pyt` sharing identical helper files and lookup tables. |

---

## 3. Solution Structure & Project Responsibilities

```
Cad2Gis/
├── Cad2Gis.sln                    # Visual Studio 2022 solution file
├── package_addin.ps1              # Zero-click build & packaging script
├── Cad2Gis.Core/                  # PURE C# CORE ENGINE (.NET 8)
│   ├── Models/                    # ParcelRecord, BorderRecord, CandidateAnnotation, etc.
│   ├── Services/                  # CurvePreservingPolygonizer, FacadeDecomposer, QaValidationEngine...
│   ├── Geometry/                  # 2D planar math, angles, meridian convergence, ray-casting
│   └── Lookups/                   # MunicipalLookups, ArabicTextNormalizer, ProfileExcelService
├── Cad2Gis.AddIn/                 # ARCGIS PRO 3.3 ADD-IN (.NET 8 WPF)
│   ├── Config.daml                # Ribbon layout, groups, buttons, dockpane registration
│   ├── Cad2GisModule.cs           # Primary Add-in module singleton
│   ├── DockPane/                  # Cad2GisDockpaneView (XAML) & Cad2GisDockpaneViewModel (MVVM)
│   ├── IO/                        # GdbFeatureClassService, ShapefileExporter, ExportStateManager
│   └── SpatialEnrichment/         # AdminAreaDetector (spatial overlay with admin boundaries)
├── Cad2Gis.Tests/                 # AUTOMATED TEST SUITE (xUnit, .NET 8)
│   └── *.cs                       # 240+ unit tests with synthetic fictional test geometries
HelperFiles/                       # EXTERNAL RUNTIME HELPERS & SCHEMAS
├── templates/                     # Master blank Shapefile templates (PARCEL.shp, border.shp)
├── lookups/                       # JSON dictionaries, BorderType_Keywords.csv, Usage_Keywords.csv
├── expected_layers/               # Expected_Layers.xlsx
├── adminareas/                    # Administrative boundary shapefiles for auto-location
├── state/                         # pipeline_state.json (authoritative pipeline registry)
├── Cad2Gis_ArcPy.pyt              # Python Toolbox fallback
└── settings.json                  # Workstation configuration settings
```

---

## 4. Saudi Municipal Context (MOMRAH / Balady Compliance)

The Ministry of Municipal and Rural Affairs & Housing (MOMRAH) establishes strict topological and attribution standards for cadastral digital submissions:

1. **Two Core Layers:**
   - **PARCEL:** Closed polygon feature class containing exactly 26 approved attributes (`REALESTA_1`, `PARCEL_TYP`, `PLOT_ID`, `USAGE`, `REMARKS`, `AREA_SQM`, etc.).
   - **BORDER:** Single-part polyline feature class containing exactly 10 approved attributes (`BORDERNAME`, `BORDERTYPE`, `DESCRIPTO`, `DIMENSION`, `LENGTH`, etc.).
2. **Cardinal Facade Model:** Each parcel's perimeter must be decomposed into at most four primary cardinal directions:
   - `1` = North (شمال)
   - `2` = East (شرق)
   - `3` = South (جنوب)
   - `4` = West (غرب)
3. **Strict 2D Planar Geometry:** Elevation ($Z$) and Measure ($M$) flags are strictly forbidden on final deliverables.
4. **Projected Metric Plane:** All engineering and analytical calculations MUST occur in projected WGS 84 UTM coordinates (zones 35N to 40N) to ensure distance and area metrics match ground reality.

---

## 5. Master Immutable Invariants (INV-01 to INV-08)

These eight invariants represent non-negotiable architectural constraints established in `CHANGELOG_MODIFICATIONS.md`. Any code modification violating these rules is rejected:

| ID | Invariant Title | Technical Definition | Rationale |
| :--- | :--- | :--- | :--- |
| **INV-01** | **Strict Curve Preservation (No Arc-to-Line)** | No densification, generalization, smoothing, or vertex-stripping on stored Geodatabase features. Circular arcs (`EllipticArcSegment`) MUST remain native mathematical curves. | Balady rejects faceted arc approximations; geometric area and arc length must be exact. |
| **INV-02** | **Multi-Layer Ingestion Scope** | Step 2 parcel polygonization MUST process all layers selected by the user in Step 1 simultaneously as a unified line network. | Individual layer processing fails to close polygons where shared boundary lines reside on different layers. |
| **INV-03** | **Working Data Source Continuity** | Each step (2 to 5) MUST offer a consistent source selection mechanism (Previous Step vs My Own Data) based on a single verified output on disk. | Prevents operator confusion between in-memory session caches and disk deliverables. |
| **INV-04** | **No Auto-Install Policy** | Packaging scripts MUST NOT silently copy `.esriAddinX` into `%USERPROFILE%\Documents\ArcGIS\AddIns\...`. The file is saved locally in the workspace root for manual installation. | Prevents file-locking conflicts and breaking developer workstations during active builds. |
| **INV-05** | **Strict 2D Planar Output** | All generated geometries MUST have `HasZ = false` and `HasM = false`. | MOMRAH cadastral portal rejects 3D geometries or features with $Z/M$ coordinate channels. |
| **INV-06** | **Analytical True Metric Calculation** | `AREA_SQM`, `Shape_Area`, `Shape_Leng`, and border lengths MUST be calculated analytically from true mathematical curves in UTM meters. | Approximating curves with straight lines decreases calculated parcel area and perimeter. |
| **INV-07** | **Controlled Export Densification** | When exporting to Shapefile format (which cannot store mathematical arcs), arc densification MUST NOT exceed a deflection deviation of $0.001\text{ m}$ ($1\text{ mm}$). | Preserves sub-millimeter geometric accuracy while honoring ESRI Shapefile format limitations. |
| **INV-08** | **Anti-Freeze Threading Architecture** | Heavy Geoprocessing tools MUST execute asynchronously off the UI thread; ArcGIS Pro geometry operations MUST run inside `QueuedTask.Run`; UI updates MUST dispatch asynchronously. | Prevents the ArcGIS Pro application interface from freezing or deadlocking during heavy operations. |

---

## 6. Functional Requirements: General & Architectural

- **`REQ-GEN-001` (Host Platform):** The solution MUST execute as an ArcGIS Pro 3.3.0 Add-in running on .NET 8.0 x64.
- **`REQ-GEN-002` (Pure Core Architecture):** `Cad2Gis.Core` MUST NOT reference any ArcGIS Pro SDK libraries and MUST compile against pure `.NET 8.0`.
- **`REQ-GEN-003` (Local Source of Truth):** The local workspace directory MUST serve as the sole authoritative repository of code, prompts, and lookups.
- **`REQ-GEN-004` (Packaging):** The Add-in MUST be packaged into `Cad2Gis.esriAddinX` via `package_addin.ps1` with `Config.daml` and `Images/` at root, and assemblies inside `Install/`.
- **`REQ-GEN-005` (Immutable Invariants):** All implementations MUST strictly enforce invariants `INV-01` through `INV-08`.
