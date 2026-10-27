---
Status: SUPERSEDED by prompts/spec/
Date: 2026-10-27
Notes: Fully superseded and consolidated into prompts/spec/02_PIPELINE_AND_DATA_FLOW.md through 07_STEP5_EXPORT_QA_SPEC.md. Overrides incorporated into decisions log (13_DECISIONS_LOG.md). Retained in archive for historical record.
---

# CAD2GIS — Addendum Prompt (Priority Requirements v4.4)

Apply the requirements below on top of the CAD2GIS plan/prompt for ArcGIS Pro 3.3.0 (.NET 8). Where this addendum conflicts with earlier baseline documentation, **this addendum wins**. Items updating earlier specifications are marked **[OVERRIDES]**.

---

## 1. 5-Step Pipeline Architecture & Working Data Continuity

**[OVERRIDES]** earlier 4-step designs. The authoritative workflow comprises 5 dedicated, sequential stages:
1. **Step 1 — CAD & Layers (استيراد الكاد والطبقات):** Import CAD, auto-discover layers, and synchronize with dynamic Excel profile in `HelperFiles\Expected Layers\*.xlsx` (reading expected layers and automatically registering newly selected layers).
2. **Step 2 — Extract Parcels (بناء واستخراج المضلعات):** Process parcels directly from Step 1 CAD dataset. Group all selected layers into a **Unified Multi-Layer Network**, apply **Curvature-Preserving Arc Extension (5 cm default)**, and provide an optional **ArcPy Feature To Polygon** engine operating strictly in planar 2D (`outputZFlag="Disabled"`, `outputMFlag="Disabled"`).
3. **Step 3 — Populate Schema (تعبئة الاسكيما والبيانات البلدية):** Directly consume Step 2 parcel geometry without re-reading CAD. Ingest administrative context, auto-match MOMRAH 22-attribute schema, and support custom external data ingestion with schema validation.
4. **Step 4 — Build Borders (توصيف واستخراج الحدود):** Extract 4-cardinal direction boundary lines for **MAIN parcels only**, preserving true curves and executing ray-based street width inference. Protect manual edits against silent overwrites.
5. **Step 5 — Export & QA (التدقيق والتصدير النهائي):** Comprehensive QA/QC verification and delivery packaging into the structured `Final/` directory.

---

## 2. Structured Delivery Packaging inside `Final/`

**[OVERRIDES]** earlier flat export outputs. Step 5 must package deliverables into a dedicated `Final/` subdirectory within the export location:
- `Final/Final.gdb`: Enterprise geodatabase containing feature classes `PARCEL` and `BORDER` with 100% True Curves preservation.
- `Final/Shape`: MOMRAH-compliant ESRI Shapefiles (`PARCEL.shp` and `border.shp`) accompanied by UTF-8 `.cpg` files.
- `Final/Reports`: Quality assurance summaries, discrepancy logs, and audit trails (`QA_Audit_Summary.json`, `audit_log.txt`).
- **Immediate Map Display:** Successfully exported GDB feature classes are instantly added to the active ArcGIS Pro map contents via `GdbFeatureClassService.AddOrRefreshMapLayer`.

---

## 3. Strict 2D Planar Enforcement (INV-05)

- Every output parcel polygon and boundary polyline must be strictly planar 2D: `HasZ = false` and `HasM = false`.
- If CAD source entities or intermediary feature classes contain Z coordinates, Z values are completely stripped during polygonization and export.
- Frozen metric values (`AREA_SQM`, `BORDERDESC`, `SHAPE_Leng`) are stored in dedicated schema attributes and never confused with geodatabase-managed GCS shape fields.

---

## 4. Curvature-Preserving Arc & Line Extension (v4.4)

- When constructing polygons from open line networks, gaps are resolved via the **Extend Dangling Lines & Curves** engine with a default tolerance of **0.05 m (5 cm)**:
  - **Straight Lines:** Extended along their collinear ray direction until intersection.
  - **Circular Arcs:** Extended **strictly along their circular curvature** around the exact original center $C$ and radius $R$ ($\Delta\theta = \Delta s / R$). Arcs are never extended with straight tangent lines, preserving true circle geometry 100%.

---

## 5. Temporary MAIN / SUB Class Field

- Every parcel is identified as either `MAIN` (`PARCEL_TYP = 1`) or `SUB`.
- The temporary classification field is strictly dropped during final delivery packaging to ensure the exported shapefile and geodatabase conform 100% to the official MOMRAH schema template.
- Every MAIN parcel must possess a valid `PLOT_ID`. Sub-parcels act as spatial neighbor candidates.

---

## 6. 4-Cardinal Boundary Model & Ray-Based Street Width Inference

- Dimensions and border records are generated for **MAIN parcels only**.
- Each ring produces at most 4 single-part polylines: North (شمال), East (شرق), South (جنوب), West (غرب).
- For sides with no adjoining neighbor:
  - Cast outward normal rays (default 3 rays; median hit distance used).
  - Maximum search distance: 100 m.
  - Distance $< 10\text{ m}$ $\rightarrow$ `"ممر بعرض W م"` (`PASS`).
  - Distance $\ge 10\text{ m}$ $\rightarrow$ `"شارع بعرض W م"` (`STR`).
- Manual modifications to descriptions and dimensions are locked and protected.

---

## 7. Zero Auto-Install Policy (INV-04)

- The compiled Add-in package `Cad2Gis.esriAddinX` is placed exclusively in the workspace root:
  `./Cad2Gis.esriAddinX`
- Automated scripts are strictly forbidden from copying files to the user's personal ArcGIS Pro AddIns directory (`%USERPROFILE%\Documents\ArcGIS\AddIns\...`). Installation is executed manually by the user via double-click.

---

## 8. Verification & Quality Benchmark (148 Automated Tests)

- Before considering any delivery complete, all 148 automated unit tests in `Cad2Gis.Tests` must pass with zero errors (`Passed: 148, Failed: 0`).
- Static code audits enforce the strict ban on `Densify`, `DensifyByLength`, `Simplify`, and `Generalize` on stored geometries.
