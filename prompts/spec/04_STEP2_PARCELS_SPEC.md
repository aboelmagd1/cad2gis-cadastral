# CAD2GIS — Step 2: Parcel Construction & Attribution Specification (04_STEP2_PARCELS_SPEC.md)

> **الملخص التنفيذي بالعربية:**
> تحدد هذه الوثيقة متطلبات المرحلة الثانية (Step 2) لبناء مضلعات قطع الأراضي من خطوط أو مضلعات الكاد، مع الالتزام التام بالقواعد الهندسية الثابتة لمنع تخشين المنحنيات (Strict True Curves) وتجريد قيم Z/M. وتفصّل الوثيقة خوارزمية "خطوط مع طبقة البيانات" (Line with data layer) لتحديد الطبقة السائدة لكل قطعة، وإجراء المطابقة المكانية للنصوص (Spatial Text Joins) لاستخراج رقم القطعة الأولي (`PLOT_TXT`) وتوليد تقرير الربط النصي (Text Join Report) وكتابة مخرجات `Cad_Constructed_Parcels`.

---

## 1. Goal & Operational Scope

Step 2 takes `Cad_Dataset` produced in Step 1 (or the operator's Own Data) and constructs closed, topologically watertight parcel polygons. It writes **exactly ONE output feature class**:
`[WorkspaceGDB]\Cad_Constructed_Parcels`

---

## 2. Geometry Modes & Construction Engines

```
Selected Geometry Mode
       │
       ├──► Line (خطوط فقط) ────────────────► FeatureToPolygon (All Selected Layers Combined)
       │
       ├──► Polygon (مضلعات فقط) ───────────► Read Closed Polygons + 2D Planar Strip Z/M
       │
       └──► Line with data layer ───────────► Split Lines + FeatureToPolygon + Layer Attribution
```

### 2.1 Mode 1: Line (خطوط فقط)
1. Selects all polylines from `Cad_Dataset\Polyline` where the `Layer` attribute matches any layer checked in the `Main Parcels` or `Sub-Parcels` tabs.
2. Combines them into a single unified network (INV-02) and invokes `FeatureToPolygon` via ArcPy or the native Geoprocessing service:
   - `outputZFlag = "Disabled"`, `outputMFlag = "Disabled"` (INV-05).
   - `attributes = "NO_ATTRIBUTES"`.
   - `cluster_tolerance = null` (unless explicitly configured).
3. **Strict No-Fallback Policy:** If the selected lines do not form closed polygons, the engine MUST NOT silently pull lines from unchecked layers. Unclosed loops are logged with coordinate locations for operator inspection.

### 2.2 Mode 2: Polygon (مضلعات فقط)
1. Reads native CAD closed lightweight polygons from `Cad_Dataset\Polygon`.
2. Strips elevation and measure data via `ParcelPolygonizer.Clean2DPlanar()` (`HasZ = false; HasM = false`).
3. Computes analytical area (`AREA_SQM`) and perimeter (`Shape_Leng`) from true geometry.

### 2.3 Mode 3: Line with data layer (خطوط مع طبقة البيانات) [Default]
This mode builds polygons from lines and attributes each resulting polygon based on the CAD layer that formed its boundary:

```
[Selected Polylines] ──► Split Line at Vertices ──► Feature To Polygon ──► Coincidence Buffer (0.05m)
                                                                                   │
                                                                                   ▼
                                                                 Perimeter Share Calculation
                                                                 (Dominant Layer >= 60%)
```

#### Detailed Dominant Layer Algorithm:
1. **Split Line at Vertices:** Selected polyline features are split at all vertex intersections so each segment is associated with a single CAD layer name.
2. **Feature To Polygon:** Closed polygons are constructed from the split segments.
3. **Boundary Buffer Analysis:** For each constructed polygon:
   - A boundary strip buffer of radius $\text{COINCIDENCE\_TOLERANCE} = 0.05\text{ m}$ is evaluated against the split line segments.
   - The coincident perimeter length contributed by each layer is summed:
     $$\text{Perimeter}(L_k) = \sum \text{Length of segments from Layer } L_k \text{ coincident with boundary}$$
4. **Dominant Layer Selection:**
   - The layer contributing the greatest boundary length is identified as the candidate dominant layer:
     $$\text{Share}(L_{\text{dom}}) = \frac{\text{Length}(L_{\text{dom}})}{\text{Total Boundary Length}}$$
   - **60% Dominance Guard:** If $\text{Share}(L_{\text{dom}}) \ge 0.60$ (60%), then:
     `SRC_LAYER = L_dom`
   - If no single layer achieves $60\%$ of the perimeter, the parcel is flagged as mixed:
     `SRC_LAYER = "MIXED_LAYERS"`
5. **Class & Usage Assignment:**
   - If $L_{\text{dom}}$ belonged to the `Main Parcels` tab $\rightarrow$ `PARCEL_CLS = "MAIN"`, `Class = ParcelClass.Main`.
   - If $L_{\text{dom}}$ belonged to the `Sub-Parcels` tab $\rightarrow$ `PARCEL_CLS = "SUB"`, `Class = ParcelClass.Sub`.
   - If $L_{\text{dom}}$ has an assigned layer-level usage code (from Step 1) $\rightarrow$ `LYR_USAGE = AssignedUsage`.
6. **Formatted Provenance String:**
   `SRC_LAYERS = "LayerA:120.5;LayerB:3.2"` (recording all contributing layers and their boundary meters).

---

## 3. Spatial Text Joins (`PLOT_TXT` & `USAGE_TXT`)

Step 2 executes spatial joins between the constructed parcel polygons and text annotations from `Cad_Dataset\Annotation`:

### 3.1 Plot Number Join (`PLOT_TXT`)
- Candidate annotations are restricted strictly to the user-selected **`PlotText` layer**.
- **Precedence Hierarchy:**
  1. **Contains:** An annotation point whose coordinates fall strictly inside the 2D polygon boundary.
  2. **Nearest (Proximity Fallback - Optional):** If enabled and 0 annotations fall inside, the closest annotation within $15.0\text{ m}$ of the parcel centroid is assigned.
  3. **Conflict:** If 2 or more annotations from the PlotText layer fall inside a single parcel, the parcel is flagged with `MATCH_MODE = "Conflict"`. The annotation closest to the polygon centroid is assigned as provisional `PLOT_TXT`, and the conflict is logged in the QA report.
  4. **None:** If 0 annotations match, `PLOT_TXT = ""` and `MATCH_MODE = "None"`.
- **Provisional `PLOT_ID` Assignment:**
  - If `PLOT_TXT` is resolved: `PlotId = PLOT_TXT`.
  - If `PLOT_TXT` is empty: `PlotId = $"P{InternalId}"` (e.g. `P1`, `P2`, `P3`).

### 3.2 Land Use Text Join (`USAGE_TXT`)
- Candidate annotations are restricted to the user-selected **`UsageText` layer**.
- If annotations fall inside the parcel:
  - If multiple distinct text values exist, they are concatenated:
    `USAGE_TXT = string.Join(" - ", distinctTexts)`
  - `USAGE_LYR = string.Join(";", distinctLayers)`
  - `Remarks = USAGE_TXT`

---

## 4. Working Fields Output Table

Step 2 outputs `Cad_Constructed_Parcels` containing standard geometry fields, template fields, and the 10 temporary working fields:

| Field Name | Type | Sample Value | Description |
| :--- | :---: | :--- | :--- |
| `PLOT_ID` | String(50) | `101` or `P1` | Provisional plot identifier. |
| `PLOT_NO1` | String(50) | `101` or `P1` | Main plot number (kept synchronized with PLOT_ID). |
| `SRC_LAYER` | String(100) | `PARCEL_BOUNDARY` | Dominant CAD layer name. |
| `SRC_LAYERS`| String(254) | `PARCEL_BOUNDARY:85.4;ROAD:12.1` | Contributing layer perimeters. |
| `PARCEL_CLS`| String(10) | `MAIN` or `SUB` | Temporary classification. |
| `PLOT_TXT` | String(50) | `11/3314` | Raw matched plot text (unaltered). |
| `PLOT_LYR` | String(100) | `CAD_TEXT_PLOTS` | Source layer of plot text. |
| `USAGE_TXT` | String(254) | `سكني` | Raw matched usage text. |
| `USAGE_LYR` | String(100) | `CAD_TEXT_LANDUSE` | Source layer of usage text. |
| `LYR_USAGE` | String(50) | `RSD` | Pre-assigned usage code from Step 1. |
| `MATCH_MODE`| String(20) | `Contains` | Spatial join status (`Contains`, `Nearest`, `Conflict`, `None`). |
| `PROV` | String(254) | `Line with data layer: [PARCEL_BOUNDARY]` | Provenance trace. |

---

## 5. Text Join Report (`text_join_report.txt`)

Upon completing Step 2, `PlotIdExtractorService` generates an audit summary saved to:
`[Workspace]/HelperFiles/logs/text_join_report.txt` and `[OutputDirectory]/Reports/text_join_report.txt`

### 5.1 Report Structure
- Total parcels constructed.
- Parcels with matched plot text (`Contains`: count, `Nearest`: count).
- Conflicted parcels with multiple plot numbers (listing Parcel ID, candidate numbers, and chosen winner).
- Parcels without plot text (provisional `P{ID}` assigned).
- Parcels with duplicate plot numbers across different parcels (listing duplicate IDs and counts).

---

## 6. Worked Example (Fictional Data)

**Scenario:** A residential block with 3 parcels constructed via *Line with data layer* mode.

1. **Parcel 1:**
   - Boundary: $40\text{ m} \times 20\text{ m}$ rectangle (Perimeter = $120.0\text{ m}$).
   - $115.0\text{ m}$ of boundary formed by layer `LOT_LINES` (Main Parcels tab, USAGE = `RSD`).
   - $5.0\text{ m}$ formed by layer `STREET` (Sub-Parcels tab).
   - $\text{Share}(\text{LOT\_LINES}) = \frac{115.0}{120.0} = 95.8\% \ge 60\% \rightarrow \text{Dominant}$.
   - Annotation inside: `"101"` on layer `PLOT_NUMBERS`.
   - Result:
     `PLOT_ID = "101"`, `PLOT_NO1 = "101"`, `PLOT_TXT = "101"`, `PARCEL_CLS = "MAIN"`, `SRC_LAYER = "LOT_LINES"`, `LYR_USAGE = "RSD"`, `MATCH_MODE = "Contains"`.

2. **Parcel 2:**
   - Boundary: Perimeter = $100.0\text{ m}$.
   - $100.0\text{ m}$ formed by layer `LOT_LINES`.
   - Annotation inside: None.
   - Result:
     `PLOT_ID = "P2"`, `PLOT_NO1 = "P2"`, `PLOT_TXT = ""`, `PARCEL_CLS = "MAIN"`, `SRC_LAYER = "LOT_LINES"`, `LYR_USAGE = "RSD"`, `MATCH_MODE = "None"`.

3. **Parcel 3:**
   - Boundary: Perimeter = $60.0\text{ m}$.
   - $55.0\text{ m}$ formed by layer `ELEC_ROOM` (Sub-Parcels tab, USAGE = `UTL`).
   - Annotation inside: `"SEC-01"` on layer `SERVICES_TEXT`.
   - Result:
     `PLOT_ID = "SEC-01"`, `PLOT_TXT = "SEC-01"`, `PARCEL_CLS = "SUB"`, `SRC_LAYER = "ELEC_ROOM"`, `LYR_USAGE = "UTL"`.

---

## 7. Functional Requirements: Step 2

- **`REQ-ST2-001` (Single Output):** Step 2 MUST write its output solely to `Cad_Constructed_Parcels`.
- **`REQ-ST2-002` (Line Mode Scope):** Line mode MUST combine all selected layers without fallback to unselected layers.
- **`REQ-ST2-003` (2D Planar Output):** Output polygons MUST have $Z$ and $M$ flags disabled (`HasZ = false; HasM = false`).
- **`REQ-ST2-004` (Dominant Layer Determination):** In Line with data layer mode, the dominant layer MUST be calculated via 60% perimeter threshold.
- **`REQ-ST2-005` (Mixed Layers Flag):** If no layer reaches 60% perimeter share, `SRC_LAYER` MUST be set to `"MIXED_LAYERS"`.
- **`REQ-ST2-006` (PlotText Matching):** Plot numbers MUST be extracted from the user-selected PlotText layer.
- **`REQ-ST2-007` (Provisional Plot ID):** If no text is matched, `PlotId` MUST default to `$"P{InternalId}"`.
- **`REQ-ST2-008` (Working Fields Inclusion):** `Cad_Constructed_Parcels` MUST carry all 10 working fields.
- **`REQ-ST2-009` (Text Join Report):** A comprehensive text join audit report MUST be saved to `Reports/text_join_report.txt`.
