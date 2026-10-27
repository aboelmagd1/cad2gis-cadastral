# CAD2GIS — Step 5: Deliverables Export & QA Auditing Specification (07_STEP5_EXPORT_QA_SPEC.md)

> **الملخص التنفيذي بالعربية:**
> تحدد هذه الوثيقة متطلبات المرحلة الخامسة والأخيرة (Step 5) لحزم مخرجات المشروع الرسمية وتدقيق الجودة الشامل (QA/QC). وتفصّل هيكلية مجلد التسليم النهائي (`Final/`) متضمناً قاعدة البيانات الجغرافية (`Final.gdb`) مع المنحنيات الحقيقية، وملفات الـ Shapefile بترميز UTF-8 وانحراف تخشين لا يتجاوز 1 مليمتر، وتجميد المقاييس الهندسية (Frozen Metrics)، وفحص أطوال النصوص بالبايت في ملفات DBF، وتوليد تقارير التدقيق الفني المعتمدة لمنصة بلدي.

---

## 1. Goal & Operational Scope

Step 5 takes `PARCEL_Approved_Schema` from Step 3 and `BORDER_Cardinal_Approved` from Step 4 (or the operator's Own Data), executes rigorous cadastral quality assurance audits, and packages deliverables into the standardized MOMRAH delivery hierarchy:
`[OutputDirectory]/Final/`

---

## 2. Deliverables Folder Hierarchy (Owner Decision D03)

Final deliverables are structured into three standardized sub-containers:

```
[Output Directory]/Final/
├── Final.gdb/                         # Native File Geodatabase with True Curves
│   ├── PARCEL                         # Approved 26-field polygon feature class
│   └── BORDER                         # Approved 10-field polyline feature class
├── Shape/                             # MOMRAH-Compliant 2D Planar Shapefiles
│   ├── PARCEL.shp, .dbf, .shx, .prj   # Parcel polygon shapefile
│   ├── PARCEL.cpg                     # UTF-8 code page definition
│   ├── border.shp, .dbf, .shx, .prj   # Border polyline shapefile
│   └── border.cpg                     # UTF-8 code page definition
└── Reports/                           # QA Validation & Audit Reports
    ├── QA_Audit_Summary.json          # Machine-readable QA summary
    ├── QA_Audit_Summary.txt           # Human-readable Arabic/English audit certificate
    ├── Geometry_Preservation_Report.txt# Millimeter deviation & curve count log
    └── Area_Discrepancies.txt         # Discrepancies vs Areas Excel (if provided)
```

> [!NOTE]
> **Shapefile Folder Path Rule (Interim Default D03):** To support existing submission scripts while maintaining structured output, shapefiles are written into `Final/Shape/`. If an external submission portal specifically requires files in the root `Final/` directory, the operator may configure this via `settings.json` (`exportShapefileToRoot: true`).

---

## 3. Four Live Map Outputs (Owner Decision D03)

Immediately upon successful export verification, Step 5 automatically adds four distinct layers to the active ArcGIS Pro map contents (`MapView.Active.Map`):
1. `PARCEL (Final GDB)`: Layer pointing to `Final.gdb\PARCEL`.
2. `BORDER (Final GDB)`: Layer pointing to `Final.gdb\BORDER`.
3. `PARCEL (Final Shapefile)`: Layer pointing to `Final/Shape/PARCEL.shp`.
4. `BORDER (Final Shapefile)`: Layer pointing to `Final/Shape/border.shp`.

---

## 4. Frozen Metrics & Controlled GCS Export (Owner Decisions D04, D05, INV-06, INV-07)

ESRI Shapefile format does not support true mathematical circular arcs (`EllipticArcSegment`), and geographic coordinates (WGS 84 EPSG:4326) measure in angular degrees rather than ground meters. To guarantee 100% metric fidelity:

### 4.1 Frozen Metrics Protocol (INV-06)
Before transforming geometries to GCS or exporting to Shapefile:
1. `AREA_SQM`: Analytically calculated from true mathematical arcs in projected UTM meters.
2. `Shape_Area`: Analytically calculated in UTM meters.
3. `Shape_Leng`: Analytically calculated perimeter in UTM meters.
4. `LENGTH`: Exact boundary line length calculated in UTM meters.
5. These values are committed as static frozen numeric attributes in the record buffer. They are NEVER recalculated after reprojecting to GCS.

### 4.2 Controlled Densification Standard (INV-07)
When writing to Shapefile format:
- Mathematical arcs are converted to dense vertices with a maximum chord deflection deviation:
  $$\text{Max Deflection Deviation} \le 0.001\text{ m} \quad (1\text{ millimeter})$$
- The total perimeter difference between the true arc and the densified polyline MUST NOT exceed $0.001\text{ m}$.
- The native Geodatabase feature classes in `Final.gdb` retain 100% of mathematical curves without densification.

---

## 5. DBF Byte-Length & Arabic Text Guard (Owner Decision D15)

In ESRI Shapefile DBF tables, field lengths represent total byte capacity, not character counts. In UTF-8 encoding, standard Arabic characters require 2 bytes each:

$$\text{Byte Count} = \text{Encoding.UTF8.GetByteCount}(\text{Text})$$

1. **Safety Truncation with Warning:**
   - If `GetByteCount(text) > FieldDefinition.Length`:
     - The text is truncated at the last complete UTF-8 character boundary that fits within the byte limit.
     - Truncation NEVER splits a multi-byte character sequence (which causes file corruption).
     - A blocking warning is logged in `QA_Audit_Summary.txt`:
       `"Warning: Text in field '{fieldName}' for Parcel '{plotId}' exceeded {len} bytes and was truncated."`
2. **Mandatory `.cpg` Files:**
   Every shapefile MUST be accompanied by an identical basename `.cpg` file containing the plain text `"UTF-8"`.

---

## 6. Automated QA/QC Validation Engine (`QaValidationEngine`)

`QaValidationEngine.ValidateAll()` executes an automated audit across all parcels and borders:

```
[PARCEL & BORDER Records]
            │
            ├──► 1. Parcel Validation (Mandatory fields, positive area, MAIN PLOT_ID)
            │
            ├──► 2. Border Validation (4 cardinal directions, approved BORDERTYPE)
            │
            ├──► 3. Topology Validation (Symmetric difference, gaps, overlaps)
            │
            ├──► 4. Area Excel Comparison (Absolute & percentage tolerances)
            │
            └──► 5. Schema Exactness (Zero extraneous working fields)
```

### 6.1 Audit Rules Matrix

| Check Name | Target Layer | Rule / Threshold | Severity |
| :--- | :--- | :--- | :---: |
| **MAIN Parcel PLOT_ID** | PARCEL | MAIN parcel MUST have non-empty `PLOT_ID`. | Error |
| **Duplicate MAIN PLOT_ID** | PARCEL | MAIN parcels MUST have unique `PLOT_ID`s. | Error |
| **Positive Metric Area** | PARCEL | `AREA_SQM` MUST be $> 0.001\text{ m}^2$. | Error |
| **Positive Perimeter** | PARCEL | `Shape_Leng` MUST be $> 0.001\text{ m}$. | Error |
| **Mandatory PLAN_NO** | PARCEL | `PLAN_NO` MUST not be empty or whitespace. | Error |
| **Approved USAGE Code** | PARCEL | `USAGE` MUST be one of: `RSD, COM, RSC, AGR, IND, UTL`. | Warning |
| **No Provenance in REMARKS**| PARCEL | `REMARKS` MUST NOT start with `"From "`. | Error |
| **Approved BORDERTYPE** | BORDER | `BORDERTYPE` MUST exist in the 68 codes of `Border Types`. | Error |
| **Cardinal Name Range** | BORDER | `BORDERNAME` MUST be an integer in $\{1, 2, 3, 4\}$. | Error |
| **Non-Empty DESCRIPTO** | BORDER | `DESCRIPTO` MUST not be empty on valid borders. | Warning |
| **Area Tolerance Check** | PARCEL | Area difference vs Excel $\le 0.5\text{ m}^2$ and $\le 0.1\%$. | Warning |
| **Working Fields Purge** | PARCEL | 0 working fields allowed in approved schema. | Warning |

---

## 7. QA Audit Reports

### 7.1 `QA_Audit_Summary.json`
Structured JSON payload containing:
- `timestamp`, `utmZone`, `totalParcels`, `mainParcels`, `subParcels`, `totalBorders`.
- `totalErrors`, `totalWarnings`, `passedAudit`.
- `errorList` and `warningList` detailing Layer, Entity ID, Field, and Issue Description.

### 7.2 `QA_Audit_Summary.txt`
Formal bilingual certification report:
```
================================================================================
              تقرير التدقيق الفني والرقابة الهندسية (QA/QC Audit Certificate)
================================================================================
تاريخ التدقيق: 2026-10-27 22:30:00
النطاق المتري المعتمد: WGS 1984 UTM Zone 37N (EPSG:32637)
حالة الاعتماد النهائي: معتمد ومطابق للاشتراطات البلدية (PASSED)

إحصائيات المشروع:
  - إجمالي قطع الأراضي: 350 (قطع رئيسية: 320 | قطع فرعية وخدمات: 30)
  - إجمالي حدود الواجهات: 1280 حد جغرافي
  - المساحة الإجمالية للمخطط: 254,120.50 م²

نتائج الفحص الهندسي والوصفي:
  [✔] فحص خلو القطع الرئيسية من أرقام مكررة أو فارغة: تم بنجاح.
  [✔] مطابقة أكواد الاستخدام (USAGE) لقائمة بلدي المعتمدة: تم بنجاح.
  [✔] فحص أكواد الحدود (BORDERTYPE) لـ 68 كوداً معتمداً: تم بنجاح.
  [✔] التحقق من سلامة الأقواس الحقيقية (True Curves Preservation): 100% مطابقة.
  [✔] التحقق من خلو الاسكيما من الحقول التشغيلية المؤقتة: تم بنجاح.
================================================================================
```

---

## 8. State Commitment Invariant

`pipeline_state.json` is updated for Step 5 **only after**:
1. `Final.gdb` and shapefiles exist on disk.
2. Feature counts match written counts.
3. QA validation completes with 0 blocking errors.
4. If errors occur, the step logs: `"Step 5 Export verification failed: [reason]"` and halts without marking success.

---

## 9. Functional Requirements: Step 5

- **`REQ-ST5-001` (Deliverables Hierarchy):** Step 5 MUST package deliverables into `Final/Final.gdb`, `Final/Shape`, and `Final/Reports`.
- **`REQ-ST5-002` (Native True Curves):** `Final.gdb` feature classes MUST retain mathematical true curves without densification.
- **`REQ-ST5-003` (Controlled Densification):** Shapefile export MUST NOT exceed $0.001\text{ m}$ chord deviation.
- **`REQ-ST5-004` (Frozen Metrics):** `AREA_SQM` and lengths MUST be frozen before shapefile or GCS export.
- **`REQ-ST5-005` (Mandatory CPG Files):** Every shapefile MUST have a matching `.cpg` file containing `"UTF-8"`.
- **`REQ-ST5-006` (DBF Byte Validation):** String attributes MUST be validated against DBF byte limits without character corruption.
- **`REQ-ST5-007` (Four Map Layers):** Step 5 MUST add both GDB and Shapefile layers directly to the active map.
- **`REQ-ST5-008` (QA Validation):** All outputs MUST undergo automated auditing via `QaValidationEngine`.
- **`REQ-ST5-009` (Audit Reports):** Step 5 MUST output both `QA_Audit_Summary.json` and `QA_Audit_Summary.txt`.
- **`REQ-ST5-010` (Verified State Commitment):** Pipeline registry MUST commit Step 5 only after full verification succeeds.
