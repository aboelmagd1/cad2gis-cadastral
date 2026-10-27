# CAD2GIS — Pipeline Architecture & Data Flow Specification (02_PIPELINE_AND_DATA_FLOW.md)

> **الملخص التنفيذي بالعربية:**
> يحدد هذا الملف المعمارية الهندسية الصارمة لخط الإنتاج الخماسي (5-Step Pipeline) وتدفق البيانات بين المراحل. يعتمد النظام مبدأ "النسخة الوحيدة على القرص" بحيث يكون ناتج كل خطوة هو نفسه مصدر العمل للخطوة التالية دون ازدواجية بين نسخ في الذاكرة ونسخ مصدرة. يدعم كل تبويب خيارين حصريين لمصدر البيانات (الخطوة السابقة أو بيانات خاصة)، مع حماية التعديلات اليدوية للمستخدم عبر سجل المسارات المعتمد (`pipeline_state.json`) وبصمة المحتوى (Content Stamp).

---

## 1. Five-Step Pipeline & Step Output Matrix

The CAD2GIS workflow is divided into five strictly sequential stages. Each step produces **exactly ONE authoritative output on disk**, which is immediately added or refreshed as a map layer in ArcGIS Pro.

```
[CAD Drawing]
     │
     ▼
┌─────────┐
│ Step 1  │ ──► Output: Cad_Dataset (Feature Dataset in GDB)
└─────────┘
     │
     ▼
┌─────────┐
│ Step 2  │ ──► Output: Cad_Constructed_Parcels (Polygon FC with Working Fields)
└─────────┘
     │
     ▼
┌─────────┐
│ Step 3  │ ──► Output: PARCEL_Approved_Schema (MOMRAH 26-Field Polygon FC)
└─────────┘
     │
     ▼
┌─────────┐
│ Step 4  │ ──► Output: BORDER_Cardinal_Approved (4-Cardinal Polyline FC)
└─────────┘
     │
     ▼
┌─────────┐
│ Step 5  │ ──► Outputs: Final.gdb (PARCEL, BORDER) + Shapefiles + Reports/
└─────────┘
```

### 1.1 Step Numbering, Method Mapping & Output Inventory

| UI Step Number | Dockpane Tab Title | Internal Method Name | Reads From | Writes Exactly ONE Output | Map Layer Name |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **Step 1** | `1. CAD & Spatial` | `ConvertCadToDatasetAsync` | Raw CAD (`.dwg` / `.dxf`) or Own Data | `Cad_Dataset` (Feature Dataset) | `Cad_Dataset` Group Layer |
| **Step 2** | `2. Extract Parcels` | `ExecuteStep2Async` | `Cad_Dataset` lines/polygons or Own Data | `Cad_Constructed_Parcels` (Feature Class) | `Cad_Constructed_Parcels` |
| **Step 3** | `3. Populate Schema` | `ExecuteStep3PopulateSchemaAsync` | `Cad_Constructed_Parcels` or Own Data | `PARCEL_Approved_Schema` (Feature Class) | `PARCEL_Approved_Schema` |
| **Step 4** | `4. Build Borders` | `ExecuteStep4BordersAsync` | `PARCEL_Approved_Schema` or Own Data | `BORDER_Cardinal_Approved` (Feature Class) | `BORDER_Cardinal_Approved` |
| **Step 5** | `5. Export & QA` | `ExecuteStep5ExportDeliverablesAsync`| `PARCEL_Approved_Schema` + `BORDER_Cardinal_Approved` | `Final/Final.gdb` (PARCEL, BORDER) + Shapefiles | `PARCEL` & `BORDER` (Final) |

> [!IMPORTANT]
> **Code Method Name Offset Note:** In the legacy codebase, methods had offsets (e.g., `ExecuteStep3Async` was used as a wrapper calling Step 4 border logic). All UI, bindings, logs, and specifications MUST strictly use the **UI Step Numbers (1 to 5)** defined in the table above.

---

## 2. Universal Two-Option Source Model (Owner Decision D02)

Every pipeline step from Step 2 to Step 5 MUST present **exactly two source radio options**, and no more:

1. **Option A: Previous step (الخطوة السابقة) [Default]**
   - Automatically resolves the input path from the verified registry entry of Step $N-1$.
   - Does NOT read from unverified in-memory session arrays.
   - If Step $N-1$ has not completed or has no verified entry, Step $N$ MUST stop execution immediately with a clear blocking error.
2. **Option B: My own data (بيانات خاصة بي)**
   - Allows the operator to browse (`OpenFileDialog` / Pro Catalog) to an external Feature Class or Shapefile.
   - Opens a field-mapping interface to map required source fields to the target pipeline schema.
   - Does NOT alter preceding steps; Step $N$ still writes its standard single output to disk so subsequent steps can continue seamlessly.

> [!CAUTION]
> **Legacy "Latest Export" Mode Completely Removed:**
> There is NO "Latest export" source option. There is NO third radio button. There is NO export-history drop-down. The output of a step *is* its export and working data simultaneously.

---

## 3. Single Copy on Disk Rule

In CAD2GIS, **the output of a step on disk is the single source of truth**:
- There is NO separate "live session in-memory version" and "exported version".
- When Step 2 finishes, it writes `Cad_Constructed_Parcels` into `Cad2Gis_Workspace.gdb`. That physical dataset IS the data.
- If the operator edits polygons in ArcGIS Pro (e.g. adjusts boundaries or deletes invalid features), those edits are directly saved into `Cad_Constructed_Parcels`.
- When Step 3 begins, it reads `Cad_Constructed_Parcels` **directly from disk**. Any manual edits made by the operator are automatically ingested.
- In-memory arrays (`_parcels`, `_borders`) exist strictly as temporary analytical caches during execution and MUST NEVER be used as the input source for a subsequent step.

---

## 4. Pipeline State Registry (`pipeline_state.json`)

To link steps together reliably across ArcGIS Pro application restarts, the pipeline maintains an authoritative JSON state file at `HelperFiles/state/pipeline_state.json`.

### 4.1 Registry JSON Schema

```json
{
  "version": "1.0",
  "lastUpdated": "2026-10-27T22:30:00Z",
  "steps": {
    "1": {
      "step": 1,
      "path": "C:\\Workspace\\Cad2Gis_Workspace.gdb\\Cad_Dataset",
      "featureCount": 1420,
      "wkid": 32637,
      "writtenAt": "2026-10-27T22:10:15Z",
      "contentStamp": "1420_A8F4C2E910B45D67"
    },
    "2": {
      "step": 2,
      "path": "C:\\Workspace\\Cad2Gis_Workspace.gdb\\Cad_Constructed_Parcels",
      "featureCount": 350,
      "wkid": 32637,
      "writtenAt": "2026-10-27T22:15:30Z",
      "contentStamp": "350_9B2D8E4F1A3C5678"
    },
    "3": {
      "step": 3,
      "path": "C:\\Workspace\\Cad2Gis_Workspace.gdb\\PARCEL_Approved_Schema",
      "featureCount": 350,
      "wkid": 32637,
      "writtenAt": "2026-10-27T22:20:45Z",
      "contentStamp": "350_3F8C1D7E5A2B9041"
    },
    "4": {
      "step": 4,
      "path": "C:\\Workspace\\Cad2Gis_Workspace.gdb\\BORDER_Cardinal_Approved",
      "featureCount": 1280,
      "wkid": 32637,
      "writtenAt": "2026-10-27T22:25:00Z",
      "contentStamp": "1280_4E6A9B1C8D2F3456"
    }
  }
}
```

### 4.2 Registration Invariants
1. **Verification Before Registration:** A step MUST NOT record its output in `pipeline_state.json` until it has executed `GdbFeatureClassService.VerifyPolygonFeatureClassAsync` or `VerifyLineFeatureClassAsync` and confirmed:
   - Feature class exists on disk.
   - Feature class is readable.
   - Geometry type matches expected type (`Polygon` or `Polyline`).
   - Feature count $> 0$ and exactly matches the written count.
   - Spatial reference WKID matches the selected project UTM zone.
2. **Surviving Restarts:** `pipeline_state.json` is persisted to disk immediately upon verification. When ArcGIS Pro restarts, the Dockpane loads this registry during `InitializeAsync()`.

---

## 5. Working Fields Lifecycle

Intermediate attributes (working fields) are essential for carrying provenance, text joins, and dominant layer classification from Step 2 into Step 3. However, they are **strictly forbidden** from appearing in the final MOMRAH deliverables.

### 5.1 Working Fields Inventory

| Field Name | Type | Length | Written By | Read By | Purpose |
| :--- | :--- | :---: | :---: | :---: | :--- |
| `SRC_LAYER` | String | 100 | Step 2 | Step 3 | Dominant CAD layer that generated the parcel boundary. |
| `SRC_LAYERS`| String | 254 | Step 2 | Step 3 | All contributing CAD layers with perimeter meters (e.g. `LotLine:120.5;Road:3.2`). |
| `PARCEL_CLS`| String | 10 | Step 2 | Step 3 | Temporary parcel classification (`MAIN` vs `SUB`). |
| `PLOT_TXT`  | String | 50 | Step 2 | Step 3 | Raw plot number annotation matched from the PlotText layer. |
| `PLOT_LYR`  | String | 100 | Step 2 | Step 3 | Layer name from which the raw plot text was extracted. |
| `USAGE_TXT` | String | 254 | Step 2 | Step 3 | Raw usage/land-use text matched from the UsageText layer. |
| `USAGE_LYR` | String | 100 | Step 2 | Step 3 | Layer name from which the raw usage text was extracted. |
| `LYR_USAGE` | String | 50 | Step 2 | Step 3 | Pre-assigned usage code defined on the layer in Step 1. |
| `MATCH_MODE`| String | 20 | Step 2 | Step 3 | Spatial join method (`Contains`, `Nearest`, `Conflict`, `None`). |
| `PROV`      | String | 254 | Step 2 | Step 3 | Internal provenance description tracking construction origin. |

### 5.2 Strict Schema Cleansing in Step 3
When Step 3 writes `PARCEL_Approved_Schema`, it passes `includeWorkingFields: false` to `EnsureFeatureClassFromTemplateAsync`.
Before registering Step 3 output, it executes `VerifyExactTemplateSchemaAsync`. If any of the 10 working fields exist in `PARCEL_Approved_Schema`, a blocking warning is logged:
`"QA Warning: Extraneous working fields leaked into PARCEL_Approved_Schema: [list]"`

---

## 6. Manual Edit Detection & Protection

Because each step writes to a single output on disk, re-running Step $N$ will overwrite its previous output. To protect manual edits made by operators in ArcGIS Pro:

1. **Content Stamp Generation:** At write time, a SHA-256 content stamp is generated from:
   $$\text{Stamp} = \text{FeatureCount} + \text{SHA256}\left(\sum_{i} \text{OID}_i + \text{PointCount}_i + \text{CentroidX}_i + \text{CentroidY}_i\right)$$
2. **Edit Detection (`PipelineStateManager.IsEditedSinceWritten`):**
   - Before executing Step $N$, the system computes the current content stamp of Step $N$'s output.
   - If the current stamp differs from the stamp recorded in `pipeline_state.json`, manual edits are flagged (`editedSinceWritten = true`).
3. **Explicit User Confirmation Prompt:**
   If manual edits are detected, execution pauses and displays a modal confirmation dialog:
   ```
   تم رصد تعديلات يدوية على طبقة الخطوة N (اسم الطبقة): [تفاصيل التعديل]
   هل ترغب في استبدال هذه التعديلات وإعادة كتابة الطبقة؟
   Manual edits were detected on Step N output. Overwrite and discard manual edits?
   [Yes / نعم] -> Overwrites output and updates registry stamp.
   [No / لا]  -> Cancels step execution and preserves manual edits.
   ```
4. **Locked Datasets & Unsaved Edits:**
   - If the target feature class has unsaved edits in the active ArcGIS Pro edit session, the step stops with: `"يرجى حفظ أو إلغاء التعديلات المعلقة في خريطة ArcGIS Pro أولاً."`
   - If the feature class is locked by another process (schema lock), the step aborts without corrupting data.

---

## 7. Failure & Stop Policy

- **No Silent Fallbacks:** If an input dataset is missing, empty, or fails spatial reference checks, the step MUST halt immediately. It MUST NOT fall back to older exports, session memory, or default geometries.
- **No False Success:** Progress reporting MUST NOT report $100\%$ or `"اكتملت العملية بنجاح"` if any verification check failed.
- **Trace Logging:** Every step records operational telemetry into `HelperFiles/logs/stepN_trace.log` with timestamps, phases, parcel counts, and error details.

---

## 8. Functional Requirements: Pipeline & Data Flow

- **`REQ-PIPE-001` (Sequential Hierarchy):** The pipeline MUST enforce linear execution: Step 1 $\rightarrow$ Step 2 $\rightarrow$ Step 3 $\rightarrow$ Step 4 $\rightarrow$ Step 5.
- **`REQ-PIPE-002` (Single Output Per Step):** Each step MUST write exactly one verified feature class to disk as specified in Section 1.1.
- **`REQ-PIPE-003` (Two Sources Only):** Steps 2 through 5 MUST provide only two source options: `PreviousStep` and `OwnData`.
- **`REQ-PIPE-004` (Single Disk Copy):** The output on disk MUST serve as both the working dataset and export; session memory MUST NOT be used across steps.
- **`REQ-PIPE-005` (Registry Persistence):** `pipeline_state.json` MUST be updated only after geometric and tabular verification succeeds.
- **`REQ-PIPE-006` (Surviving Restarts):** Registry states MUST persist across ArcGIS Pro application restarts.
- **`REQ-PIPE-007` (Working Fields Scope):** Working fields MUST exist in `Cad_Constructed_Parcels` and MUST be purged from `PARCEL_Approved_Schema`.
- **`REQ-PIPE-008` (Edit Detection):** Steps MUST check for post-write manual edits via content stamps and require confirmation before overwriting.
- **`REQ-PIPE-009` (Active Map Display):** Outputs MUST be automatically added to the active map with layer names matching the feature class name.
- **`REQ-PIPE-010` (Strict Stop On Failure):** Any validation failure MUST block pipeline execution without silent fallback.
