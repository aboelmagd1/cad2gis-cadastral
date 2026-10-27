# CAD2GIS — Step 1: CAD Ingestion, Layer Inspection & Classification Specification (03_STEP1_LAYERS_SPEC.md)

> **الملخص التنفيذي بالعربية:**
> تحدد هذه الوثيقة متطلبات المرحلة الأولى (Step 1) لتحويل مخططات الكاد (AutoCAD DWG/DXF) إلى قاعدة بيانات جغرافية، وتعيين الإسقاط المتري (UTM Zones 35N–40N). تشمل الوثيقة تقسيم الطبقات آلياً إلى أربعة تصنيفات رئيسية (Main, Sub, Text, Ignore)، وتحديد نمط المعالجة الهندسية (Geometry Modes)، وتفعيل قائمة أنواع الاستخدام المعتمدة على مستوى الطبقة (Usage Types)، وفك تشفير النصوص العربية المعكوسة (xArab Decoding) مع حماية أرقام القطع التي تحوي فواصل وكسور، وإدارة ملفات الطبقات المتوقعة وملف الطبقات المستخدمة.

---

## 1. Goal & Operational Scope

Step 1 transforms external AutoCAD drawings (`.dwg` or `.dxf`) into a standardized ESRI Feature Dataset named `Cad_Dataset` inside `Cad2Gis_Workspace.gdb`, projected into the user's selected WGS 84 UTM zone. It classifies every layer into functional cadastral roles, enabling automated parcel extraction in Step 2.

---

## 2. Spatial Reference & Metric Coordinate System

Calculations in CAD2GIS MUST occur in projected metric coordinates. The operator selects the appropriate UTM zone for the project from the dropdown:

| Zone Name | EPSG Code | Geographic Coverage (Saudi Arabia) |
| :--- | :---: | :--- |
| **WGS 1984 UTM Zone 37N** [Default] | `32637` | Riyadh, Makkah, Madinah, Qassim, Hail, Tabuk |
| **WGS 1984 UTM Zone 38N** | `32638` | Riyadh East, Eastern Province, Dammam, Jubail, Khobar |
| **WGS 1984 UTM Zone 39N** | `32639` | Far Eastern Province, Al-Ahsa, Salwa |
| **WGS 1984 UTM Zone 36N** | `32636` | Red Sea Coast, Jeddah, Yanbu, Duba |
| **WGS 1984 UTM Zone 35N** | `32635` | Far Western Islands, Tiran |
| **Ain el Abd 1970 / Aramco** | `20499` | Legacy oil concession layouts (automatically reprojected) |

---

## 3. CAD Ingestion & `Cad_Dataset` Structure

When the operator executes `ConvertCadToDatasetCommand`:
1. The CAD file is ingested using ArcGIS Pro's `CADToGeodatabase` geoprocessing tool with target spatial reference set to the selected UTM WKID.
2. The output is a single Feature Dataset:
   `[WorkspaceGDB]\Cad_Dataset`
3. It contains native feature classes:
   - `Cad_Dataset\Point`: CAD blocks, points, survey nodes.
   - `Cad_Dataset\Polyline`: All lines, 2D/3D polylines, arcs, circles, boundaries.
   - `Cad_Dataset\Polygon`: Closed lightweight polylines and hatches.
   - `Cad_Dataset\Annotation`: CAD text strings, MText, and dimensions.

---

## 4. Layer Categorization & Tab Structure

CAD layers are categorized into 4 operational tabs in the Dockpane DataGrid:

```
┌────────────────────────────────────────────────────────────────────────┐
│ [Main Parcels]  │  [Sub-Parcels / Utilities]  │  [Text & Annotations]  │  [Ignore Layers] │
└────────────────────────────────────────────────────────────────────────┘
```

1. **Tab 1: `[Main Parcels]`**
   - Boundary outlines, lot lines, subdivision blocks for residential, commercial, or investment properties.
   - These form `MAIN` parcels (`PARCEL_TYP = 1`), receive plot numbers, and undergo Step 4 boundary extraction.
2. **Tab 2: `[Sub-Parcels / Utilities]`**
   - Public facilities, utilities, electrical rooms (`ECR`), mosques (`MSQ`), parks (`PAK`), parking (`PRK`), and walkways (`LNE`).
   - **Roadway / Street Rule:** Any layer representing streets or passages (`Street`, `Roads`) MUST be placed here. They are treated as sub-parcels so they do NOT undergo 4-cardinal boundary decomposition.
3. **Tab 3: `[Text & Annotations]`**
   - Layers containing parcel numbers (`PlotText`) and land use/remarks (`UsageText`).
   - The user selects the primary `PlotText` layer and primary `UsageText` layer from dropdowns populated by this tab.
4. **Tab 4: `[Ignore Layers]`**
   - Dimensions, hatching, title blocks, contour lines, grid marks. Ignored during parcel construction.

---

## 5. Geometry Processing Modes (Owner Decision D06)

The Dockpane provides a `GeometryMode` selector with three authoritative options:

| Mode Name | Technical Behavior | When to Use |
| :--- | :--- | :--- |
| **Line (خطوط فقط)** | Reads polylines from `Cad_Dataset\Polyline` matching selected layers. Runs `FeatureToPolygon` with $Z/M$ disabled. | When CAD contains clean, closed polyline linework without filled boundary polygons. |
| **Polygon (مضلعات فقط)** | Ingests native CAD closed polygons from `Cad_Dataset\Polygon`. Ignores lines unless forced. | When CAD designer delivered pre-closed 2D polygons/LWPOLYLINES. |
| **Line with data layer (خطوط مع طبقة البيانات)** [Recommended] | Builds polygons from lines, then analyzes the perimeter of each parcel against `Split Line at Vertices` to determine which CAD layer created each parcel boundary. Assigns dominant layer `USAGE` and `ParcelClass`. | When different layers represent different land-use categories and boundaries touch. |

> [!NOTE]
> **Normalization Aliases:** Legacy profiles containing `"Both (مضلعات وخطوط معاً)"` or `"Polygon line"` are automatically normalized by `LayerGeometryModeService.NormalizeMode()` to `"Line with data layer (خطوط مع طبقة البيانات)"`.

---

## 6. Per-Layer USAGE Dropdown (Owner Decision D07)

In the Dockpane Step 1 DataGrid, each layer row displays an optional **USAGE** dropdown:
- **Source Values:** Populated dynamically at runtime from the `Usage Types` worksheet in `HelperFiles/Required Fields in the Shapefile.xlsx`.
- **Approved Codes:** `RSD` (سكني), `COM` (تجاري), `RSC` (سكني تجاري), `AGR` (زراعي), `IND` (صناعي), `UTL` (مرافق).
- **Refreshable:** Clicking `🔄 تحديث الاستخدام` (`RefreshUsageTypesCommand`) re-reads the Excel file immediately without restarting ArcGIS Pro.
- **Role:** If assigned on a layer, any parcel dominated by that layer inherits this `USAGE` code (priority 3 in USAGE precedence).

---

## 7. Arabic CAD Font & Text Decoding (`XArabDecoder`)

AutoCAD drawings created with legacy Arabic tools (such as xArab, Al-Rassam, or FastCAD) store characters inverted or in non-standard ASCII encodings:

1. **Reversal Correction:** Reverses inverted character strings to standard Right-to-Left Arabic presentation forms.
2. **Slash Protection Invariant:** When parcel numbers contain slashes (e.g., `11/3314`, `101/A`, or `١٠١/٣٣`), the slash and the adjacent digits **MUST NOT be swapped or flipped**:
   - `"4133/11"` correctly decodes to `"11/3314"`.
   - Arabic-Indic digits (`٠-٩`) and Latin digits (`0-9`) MUST be preserved exactly as drawn.
3. **Normalization:** Removes trailing diacritics (Tashkeel) and Tatweel/Kashida (`ـ`) characters while preserving semantic text.

---

## 8. Expected Layers & Layers Used Excel Synchronization

### 8.1 Expected Layers Ingestion (`Expected_Layers.xlsx`)
- Stored at `HelperFiles/expected_layers/Expected_Layers.xlsx`.
- When a CAD file is loaded, `HelperFilesManager` reads the spreadsheet. If CAD layer names match rules in the sheet, they are automatically categorized into Main, Sub, Text, or Ignore tabs, and assigned pre-configured USAGE codes.

### 8.2 Layers Used Output Report
On every run of Step 1, the system generates an audit spreadsheet:
`[OutputDirectory]/Reports/Layers_Used_[Timestamp].xlsx`
- Columns: `LayerName`, `EntityCount`, `CategoryTab`, `AssignedUsage`, `GeometryType`, `ColorHex`.

---

## 9. Error & Diagnostic Catalog

| Condition | Arabic Message | English Message | Severity |
| :--- | :--- | :--- | :---: |
| CAD file path missing or not found | يرجى تحديد مسار ملف الكاد أولاً. | CAD drawing file path not specified or not found. | Blocking Error |
| GDB Workspace invalid | تعذر فتح قاعدة البيانات الجغرافية المحددة. | Unable to open or create target Geodatabase workspace. | Blocking Error |
| 0 Layers selected for Main or Sub | لم يتم تحديد أي طبقات لبناء المضلعات. | No layers selected under Main Parcels or Sub-Parcels. | Blocking Error |
| PlotText layer missing annotations | الطبقة المختارة لأرقام القطع لا تحتوي على نصوص. | Selected PlotText layer contains 0 text annotations. | Warning |

---

## 10. Functional Requirements: Step 1

- **`REQ-ST1-001` (CAD Ingestion):** Step 1 MUST ingest `.dwg` and `.dxf` into `Cad_Dataset` with spatial reference matching the selected UTM zone.
- **`REQ-ST1-002` (Supported UTM Zones):** Supported zones MUST include EPSG 32635 through 32640 and Ain el Abd EPSG 20499.
- **`REQ-ST1-003` (4-Category Tabs):** The UI MUST classify layers into Main Parcels, Sub-Parcels/Utilities, Text & Annotations, and Ignore Layers.
- **`REQ-ST1-004` (Street Layer Rule):** Streets and roadways MUST be assigned to Sub-Parcels/Utilities.
- **`REQ-ST1-005` (Geometry Modes):** The system MUST support Line, Polygon, and Line with data layer modes.
- **`REQ-ST1-006` (Usage Dropdown):** The layer-level USAGE dropdown MUST populate dynamically from `Usage Types` worksheet.
- **`REQ-ST1-007` (Slash Preservation):** `XArabDecoder` MUST preserve slashes between digits without character transposition.
- **`REQ-ST1-008` (Layers Used Export):** Every run of Step 1 MUST write a `Layers_Used.xlsx` report under `Reports/`.
