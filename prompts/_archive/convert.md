---
Status: SUPERSEDED by prompts/spec/04_STEP2_PARCELS_SPEC.md
Date: 2026-10-27
Notes: Fully superseded and consolidated into prompts/spec/04_STEP2_PARCELS_SPEC.md and 01_PRODUCT_OVERVIEW.md. Retained in archive for historical record.
---

# CAD Lines to Polygon — ArcPy Feature To Polygon Engine (2D Planar, No Z/M)

The approved specification files and immutable invariants in [CHANGELOG_MODIFICATIONS.md](CHANGELOG_MODIFICATIONS.md) and [promt 01010.md](promt 01010.md) are the primary sources of truth; report any conflict.

---

## 1. Overview & Required Behavior
In CAD2GIS (v4.4), Step 2 provides an integrated toggle:
`[✔] Use ArcPy FeatureToPolygon (2D Planar)` (Bound to `UseArcPyFeatureToPolygon`).

When activated, polygon construction executes via the native ArcGIS Pro **Feature To Polygon** geoprocessing tool with strictly disabled Z and M flags, generating pure 2D planar geometries compliant with MOMRAH / Balady standards:

```python
import arcpy

def lines_to_polygons(in_line_layers, out_fc, cluster_tolerance=None):
    # Output must not carry Z or M values (Strict 2D Planar)
    with arcpy.EnvManager(outputZFlag="Disabled", outputMFlag="Disabled"):
        arcpy.management.FeatureToPolygon(
            in_features=in_line_layers,          # List of ALL selected line layers combined
            out_feature_class=out_fc,
            cluster_tolerance=cluster_tolerance, # None unless the user sets a tolerance
            attributes="NO_ATTRIBUTES",
            label_features=None,
        )
```

---

## 2. Core Rules & Constraints

1. **Unified Multi-Layer Scope:** The input is **ALL layers** selected by the user in Step 1 (e.g. boundary outlines, lot lines, internal divisions). Never omit any selected layer; passing them together ensures shared boundaries between layers form enclosed, watertight polygons.
2. **Strict 2D Planar Output (INV-05):** Z and M coordinates are completely disabled via `outputZFlag = "Disabled"` and `outputMFlag = "Disabled"`. Even if CAD input geometries carry 3D elevation coordinates, the output feature class must have `HasZ = false` and `HasM = false`.
3. **Cluster Tolerance Policy:** `cluster_tolerance` is empty (`None`) by default. A tolerance is only applied if explicitly specified by the operator.
4. **No Manual Geometry Distortion:** Dangling or unclosed lines that fail to form closed polygons are logged with coordinate locations for review, rather than silently modified.
5. **Curve Geometry Preservation (INV-01):** True circular arcs (`EllipticArcSegment`) must be preserved as native curves and never densified or faceted into chords.
6. **Non-Destructive Processing:** Source CAD drawings and raw input feature classes are strictly read-only and never modified.
7. **Thread-Safe Asynchronous Execution (INV-08):** In the ArcGIS Pro Add-in, Geoprocessing tools are executed via `Geoprocessing.ExecuteToolAsync` outside the UI thread, ensuring the Pro interface remains completely responsive.
8. **Direct Handoff to Step 3:** The output polygon dataset produced in Step 2 is stored in the workspace Geodatabase and directly consumed by Step 3 (Populate Schema) without regenerating or re-reading CAD.

---

## 3. Workflow Sequence
1. Gather all line layers marked active in Step 1 (`SelectedCADLayers`).
2. Configure environmental flags (`outputZFlag="Disabled"`, `outputMFlag="Disabled"`).
3. Execute `FeatureToPolygon` on the combined layer collection.
4. Validate topological outputs: verify `HasZ == false`, confirm geometry validity, and verify native curve integrity.
5. Return the resulting feature class and display statistics in the dockpane progress console.

---

## 4. Acceptance Criteria
- Output polygons are strictly planar 2D without Z or M dimensions.
- Polygons are constructed from the unified network of all active layers.
- Arcs remain true mathematical curves without chord fragmentation.
- Execution completes asynchronously without blocking the ArcGIS Pro UI.
