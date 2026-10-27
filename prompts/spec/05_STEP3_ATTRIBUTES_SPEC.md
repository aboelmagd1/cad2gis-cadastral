# CAD2GIS — Step 3: Municipal Schema & Attribute Population Specification (05_STEP3_ATTRIBUTES_SPEC.md)

> **الملخص التنفيذي بالعربية:**
> تحدد هذه الوثيقة متطلبات المرحلة الثالثة (Step 3) لتعبئة الاسكيما البلدية الرسمية المعتمدة من وزارة الشؤون البلدية والقروية والإسكان (MOMRAH 26-Field Schema) وحذف كافة الحقول التشغيلية المؤقتة. وتفصّل الوثيقة قاعدة اشتقاق رقم القطعة (`PLOT_ID`) من حقل `PLOT_TXT` في نمط "خطوط مع طبقة البيانات"، وسلّم أسبقية تحديد الاستخدام (`USAGE` Precedence)، وقواعد الملاحظات (`REMARKS`)، والاستكشاف المكاني للتبعية الإدارية (Admin Jurisdiction)، وسياسة إكمال البيانات الإلزامية.

---

## 1. Goal & Operational Scope

Step 3 reads `Cad_Constructed_Parcels` produced by Step 2 (or the user's Own Data), enriches it with cadastral attributes and municipal jurisdiction metadata, conforms it strictly to the 26-field MOMRAH template schema, and produces **exactly ONE output feature class**:
`[WorkspaceGDB]\PARCEL_Approved_Schema`

---

## 2. Derivation of `PLOT_ID` from `PLOT_TXT` (Line with Data Layer Mode)

When the active geometry mode is **Line with data layer** (`LayerGeometryModeService.IsLineWithDataLayer(SelectedGeometryMode)`), Step 3 executes `PlotIdFromPlotTextService.Apply(_parcels)` immediately in the load phase **before any component consumes the plot identifier**:

```
Read Parcels from Cad_Constructed_Parcels
                    │
                    ▼
   [Is Line with data layer mode?]
      │                    │
     Yes                   No ──► Keep existing PLOT_ID (Identical to today)
      │
      ▼
   [Has PLOT_TXT field?]
      │                    │
     Yes                   No ──► Log missing field, keep existing PLOT_ID
      │
      ▼
For each parcel:
  text = PLOT_TXT.Trim()
  If text not empty:
     PLOT_ID  = text
     PLOT_NO1 = text (if previously empty or derived from PLOT_ID)
  Else:
     Leave PLOT_ID untouched
```

### 2.1 Rules & Invariants
1. **Execution Point:** Runs immediately after parcels are read from disk and verified non-empty, strictly BEFORE `PARCEs_landuse.xlsx` lookup, `LandUseExcelService.EnrichParcels`, QA validation, and boundary linkage.
2. **Text Preservation:** Slashes, hyphens, and numeric systems (Arabic-Indic `١٠١/٣٣` and Latin `11/3314`) MUST be preserved exactly as matched. No alteration other than trimming surrounding whitespace.
3. **Empty Text Guard:** If `PLOT_TXT` is empty or null, the existing `PLOT_ID` (`P1`, `P2`, etc.) is left completely untouched. The system NEVER fabricates a random number or copies from other attributes.
4. **Missing Field Resilience:** If the input dataset does not contain a `PLOT_TXT` field, the step MUST NOT fail. It logs:
   `PLOT_TXT field not found in <path>; PLOT_ID left unchanged`
5. **Duplicate Plot Numbers:** If several parcels receive the same `PLOT_ID`, none are renamed or deleted. They are assigned as-is, a warning is logged listing up to 10 duplicates, and the duplicates are reported in QA.
6. **Log Telemetry:** One comprehensive log line is recorded:
   `[Step3] PLOT_ID from PLOT_TXT: assigned={Assigned}, emptyText={EmptyText}, changed={Changed}, duplicates={DuplicateCount} | Examples: [P1 → 101, P2 → 102]`

---

## 3. `USAGE` Precedence Hierarchy (Owner Decision D08)

Every parcel's `USAGE` field MUST contain an official Balady usage code from the `Usage Types` worksheet. Any code not present in that sheet (including `PLT`) is strictly prohibited.

The field is resolved using a 4-tier strict precedence:

```
┌────────────────────────────────────────────────────────┐
│ Priority 1: User Manual Edit                           │ (Verified against approved list)
└──────────────────────────┬─────────────────────────────┘
                           │ If empty / unedited
                           ▼
┌────────────────────────────────────────────────────────┐
│ Priority 2: Excel Match (PARCEs_landuse.xlsx)          │ (Matched by normalized PLOT_ID)
└──────────────────────────┬─────────────────────────────┘
                           │ If no Excel match / empty
                           ▼
┌────────────────────────────────────────────────────────┐
│ Priority 3: Pre-Assigned Layer Usage (LYR_USAGE)       │ (From dominant CAD layer in Step 1)
└──────────────────────────┬─────────────────────────────┘
                           │ If unassigned
                           ▼
┌────────────────────────────────────────────────────────┐
│ Priority 4: Default Fallback Engine                    │
│   • SUB parcel  ──► "UTL"                              │
│   • MAIN parcel ──► Inferred from Usage_Keywords.csv   │
│                    or left blank (QA Warning)          │
└────────────────────────────────────────────────────────┘
```

### 3.1 Approved Usage Codes
- `RSD`: Residential (سكني)
- `COM`: Commercial (تجاري)
- `RSC`: Residential-Commercial (سكني تجاري)
- `AGR`: Agricultural (زراعي)
- `IND`: Industrial (صناعي)
- `UTL`: Utilities / Facilities (مرافق)

### 3.2 MAIN Parcel Usage Fallback (`Usage_Keywords.csv`)
If Priority 1, 2, and 3 are empty, `LandUseExcelService` analyzes `USAGE_TXT` against `HelperFiles/lookups/Usage_Keywords.csv`:
- Contains `"سكني"` $\rightarrow$ `RSD`
- Contains `"تجاري"` $\rightarrow$ `COM`
- Contains `"سكني تجاري"` or `"تجاري سكني"` $\rightarrow$ `RSC`
- Contains `"زراعي"` $\rightarrow$ `AGR`
- Contains `"صناعي"` $\rightarrow$ `IND`
- Contains `"مرافق"`, `"مرفق"`, or `"خدمات"` $\rightarrow$ `UTL`
If no match occurs, `USAGE` is left empty and flagged in the QA audit log.

---

## 4. `REMARKS` Rules (Owner Decision D09)

The `REMARKS` field stores municipal notes and textual descriptions:
1. **Excel Remarks:** If `PARCEs_landuse.xlsx` matched by `PLOT_ID` and contains non-empty `Remarks`, it takes first priority.
2. **CAD Usage Text:** If Excel is absent or empty, the parcel's raw matched `USAGE_TXT` is used.
3. **Usage Description Fallback:** If `USAGE_TXT` is empty, the Arabic description of the parcel's `USAGE` code is assigned (e.g. `RSD` $\rightarrow$ `"سكني"`).
4. **Strict Provenance Ban:** Internal tracking text (e.g. `"From Layer [XYZ]"` or `"From FeatureToPolygon"`) MUST NEVER be stored in `REMARKS`. Any legacy provenance string found in `REMARKS` is automatically moved to the internal `Provenance` property and cleared from `REMARKS`.

---

## 5. Administrative Jurisdiction Detection (`AdminAreaDetector`)

Step 3 performs a point-in-polygon spatial overlay between the parcel centroid and administrative boundary shapefiles in `HelperFiles/adminareas/`:

1. **Hierarchy:** Region $\rightarrow$ Governorate/Amanah $\rightarrow$ Municipality $\rightarrow$ City $\rightarrow$ Sub-Municipality $\rightarrow$ Neighbourhood.
2. **Attributes Populated:**
   - `REGION_ID`: 2-digit official administrative region code (e.g. `"01"` for Riyadh).
   - `CITY_ID`: Official city identifier from `CITY_LOOKUP` sheet (e.g. `"1"` for Riyadh City).
   - `NEIGHBOURH`: Official 6-character municipal neighbourhood code.
   - `ADDRESS`: Fully structured standard address string:
     `"المملكة العربية السعودية - المنطقة الوسطى - أمانة منطقة الرياض - حي الملقا"`

---

## 6. Missing Data Policy (`MissingDataPolicy.cs`)

To ensure deliverables comply with Balady database validation without manual data entry:
- `REALESTA_1`: Default `= 2` (Land parcel / أرض).
- `ISBUILDING`: Default `= 0` (Vacant / لا يوجد بناء).
- `BUILDINGTY`: Default `= 1` (Modern reinforced concrete / سكني مسلح حديث).
- `PLAN_NO`: Populated from project metadata (or Excel `PLAN_NO`).
- `PLOT_NO1`: Synchronized with `PLOT_ID`.
- `REQUEST_TY`, `REQUEST_NO`, `REPORT_NO`: Populated from project metadata.

---

## 7. Strict Template Schema Enforcement & Working Field Purge

Step 3 output MUST match the 26 approved MOMRAH template fields exactly:
1. `GdbFeatureClassService.EnsureFeatureClassFromTemplateAsync` is invoked with `includeWorkingFields: false`.
2. Before registering the output, `VerifyExactTemplateSchemaAsync` checks that none of the 10 working fields (`SRC_LAYER`, `SRC_LAYERS`, `PARCEL_CLS`, `PLOT_TXT`, `PLOT_LYR`, `USAGE_TXT`, `USAGE_LYR`, `LYR_USAGE`, `MATCH_MODE`, `PROV`) leaked into `PARCEL_Approved_Schema`.

---

## 8. Worked Example (Fictional Data)

**Input Parcel from Step 2:**
- `InternalId = 1`, `PlotId = "P1"`, `PlotText = "101"`, `Class = ParcelClass.Main`, `AreaSqm = 625.50`
- `SRC_LAYER = "LOT_RESIDENTIAL"`, `LYR_USAGE = "RSD"`, `USAGE_TXT = "سكني خاص"`

**Step 3 Processing:**
1. **PlotIdFromPlotTextService:**
   - Detects `PlotText = "101"`.
   - Updates: `PlotId = "101"`, `PlotNo1 = "101"`.
   - Logged as: `P1 → 101`.
2. **Excel Lookup:**
   - Searches `PARCEs_landuse.xlsx` for key `"101"`.
   - Found row: `BLOCK_NO = "42"`, `REMARKS = "فيلا سكنية"`.
   - Applies: `BlockNo = "42"`, `Remarks = "فيلا سكنية"`.
3. **Usage Determination:**
   - Priority 1 (Manual): None.
   - Priority 2 (Excel): Blank.
   - Priority 3 (LYR_USAGE): `"RSD"`.
   - Validated against `Usage Types` sheet $\rightarrow$ `"RSD"` accepted.
4. **Admin Jurisdiction:**
   - Centroid falls in Riyadh $\rightarrow$ `RegionId = "01"`, `CityId = "1"`, `Neighbourhood = "010105"`.
5. **Schema Commitment:**
   - Written to `PARCEL_Approved_Schema` with working fields excluded.

---

## 9. Functional Requirements: Step 3

- **`REQ-ST3-001` (Single Output):** Step 3 MUST write solely to `PARCEL_Approved_Schema`.
- **`REQ-ST3-002` (PLOT_ID from PLOT_TXT):** In Line with data layer mode, `PLOT_ID` MUST be updated from `PLOT_TXT` before Excel lookup.
- **`REQ-ST3-003` (Trimming & Digits):** `PLOT_TXT` MUST be trimmed of outer whitespace while preserving internal slashes and digits.
- **`REQ-ST3-004` (Empty PLOT_TXT):** If `PLOT_TXT` is empty, existing `PLOT_ID` MUST remain unchanged.
- **`REQ-ST3-005` (Missing PLOT_TXT Field):** If `PLOT_TXT` field is absent from the feature class, `PLOT_ID` MUST remain unchanged with a logged notice.
- **`REQ-ST3-006` (Duplicate PLOT_IDs):** Duplicate `PLOT_ID`s MUST NOT be renamed or deleted; they MUST be reported as warnings.
- **`REQ-ST3-007` (USAGE Precedence):** `USAGE` MUST adhere to the 4-tier precedence hierarchy.
- **`REQ-ST3-008` (Usage Code Whitelist):** `USAGE` MUST only contain codes from `Usage Types` (`RSD, COM, RSC, AGR, IND, UTL`).
- **`REQ-ST3-009` (REMARKS Rules):** `REMARKS` MUST NOT contain `PlotId` or provenance strings.
- **`REQ-ST3-010` (Working Field Purge):** `PARCEL_Approved_Schema` MUST NOT contain any working fields.
