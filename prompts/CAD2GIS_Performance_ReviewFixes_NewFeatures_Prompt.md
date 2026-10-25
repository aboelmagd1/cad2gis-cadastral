# CAD2GIS — Prompt: Step 4 performance, review fixes, and the new "Line with data layer" mode

Work on the current repository (the Add-in is split into `Cad2GisDockpaneViewModel.Step1..Step5.cs` / `.Pipeline.cs`). Read the real code before changing it, make surgical changes, keep the working behaviour, and never claim a test passed that you did not run. `Cad2Gis.AddIn` needs ArcGIS Pro; run `dotnet test` for what builds without Pro (`Cad2Gis.Core`) and state clearly what could not be built or run.

Priority: **Part A** (performance), **Part B** (confirmed defects), **Part C** (new feature).

---

## Part A — Step 4 (borders) takes far too long

### A.1 Root cause (confirmed by reading the code)
`ExecuteStep4BordersAsync` loops over every MAIN parcel and, for each one, calls `OutwardRaySensor.SenseAndEnrichBorders(p, cardinalDict, parcelTuples, ...)` where `parcelTuples` is **the list of all parcels**. Inside, there is no spatial filter, so the work is **O(N²)** geometry-engine calls (N = number of parcels, for example 585):

1. **Obstacle building (`OutwardRaySensor.cs`, first loop):** for every other parcel it calls `GeometryEngine.Instance.Distance(currentGeom, other.Geometry)` and then `other.Geometry.Points` and creates a `Segment2D` for **every vertex pair of every parcel in the dataset**, for every parcel. That is about N² exact polygon-distance calls and N × (total vertices) object allocations, and it is done even if no border of that parcel needs a width measurement.
2. **Neighbour check (second loop):** for each of the 4 borders and for **each** other parcel it calls `GeometryEngine.Instance.Buffer(otherGeom, snapTolerance)`, then `Intersects`, then `Intersection`. That is about **4 × N² polygon buffers** (more than 1.3 million for 585 parcels), and the buffer of the same neighbour is recomputed again and again.
3. Minor: `ReportProgress` is called for every parcel with string formatting, and curve geometry is read through `.Points` (vertices only) each time.

### A.2 Required fix (same results, far less work)
1. **Build a spatial index once**, before the parcel loop in Step 4: a uniform grid (hash of cells) over parcel envelopes, with cell size about the maximum search distance (100 m, configurable). Precompute per parcel, once: `Envelope`, the outer-ring segments as `Segment2D[]` (read from the real segments, not only `.Points`), and a lazily created, cached **buffered neighbour polygon** (`Dictionary<long, Polygon>`; buffer once per parcel, never per border).
2. **Neighbour candidates:** for each border, query the index for parcels whose envelope intersects the border's envelope expanded by `snapTolerance`. Only those candidates go through `Intersects` / `Intersection` (usually fewer than 10), using the cached buffers.
3. **Minimum facing gap:** before calling the exact `Distance`, compare envelopes; call `Distance` only for parcels whose envelope is within `MinFacingGap` of the current parcel. Parcels farther than that are valid facing obstacles without any exact distance call.
4. **Obstacle segments lazily and locally:** build the obstacle list for a parcel only if at least one of its borders has no eligible neighbour (needs width inference), and take segments only from parcels whose envelopes intersect that border's envelope expanded by `MaxSearchDistance`, using the precomputed `Segment2D[]`.
5. **Throttle progress** to at most one update every ~250 ms or 1 %.
6. **Timing log:** a `Stopwatch` per phase (decompose, neighbour check, width inference, writing) and totals, written to the run log, so the improvement is measurable. Support cancellation (`CancelableProgressorSource`).
7. Do **not** parallelise in this pass (thread-safety of the geometry engine must be verified separately); mention it as a later option.

**Before / after example (neighbour check):**
```csharp
// BEFORE: every border x every parcel, buffer recomputed each time
foreach (var other in allParcels)
{
    var neighborBuffered = GeometryEngine.Instance.Buffer(other.Geometry, snapTolerance) as Polygon;
    if (GeometryEngine.Instance.Intersects(borderGeom, neighborBuffered)) { /* shared length ... */ }
}

// AFTER: index query + cached buffer, same shared-length logic
foreach (var other in index.Query(borderGeom.Extent, expandBy: snapTolerance))
{
    var neighborBuffered = index.GetOrCreateBuffered(other.Parcel.InternalId, snapTolerance);
    if (GeometryEngine.Instance.Intersects(borderGeom, neighborBuffered)) { /* shared length ... */ }
}
```
**Acceptance:** identical borders, `BORDERTYPE`, descriptions, and widths on the same input before and after (compare on a synthetic grid of at least 400 parcels with streets, sub-parcels, and a gap case), and a logged before/after timing. Add a Core-level test for the index (candidate sets equal the brute-force result).

---

## Part B — Defects found while reviewing the business-rules changes

### B.1 Step 3 uses the wrong text for REMARKS and USAGE (`LandUseExcelService.EnrichParcels`)
**What the code does:** in the fallback branch it calls `MunicipalLookups.ClassifyUsageFromText(p.PlotId)` and sets `p.Remarks = p.PlotId` when `hasChosenText`. But `PlotId` is the **plot number** text. In Step 2 the usage/description text chosen in Step 1 is joined into `p.Usage` (for example "سكني" or "حديقة"). `EnrichParcels` then overwrites `p.Usage` with a code (`UTL`, `RSD`, or empty) **before** REMARKS reads it, so the original text is lost.
**Example:** a MAIN parcel with plot text `12` and usage text `سكني`. Expected: `USAGE = RSD`, `REMARKS = سكني`. Actual: classification runs on `"12"` (no match → empty USAGE) and `REMARKS = "12"`.
**Fix:**
1. Add an internal (not exported) field on `ParcelRecord`, `UsageText`, set in Step 2 from the joined usage/description annotation (and, in Part C, from the layer-level data). Never overwrite it.
2. `EnrichParcels`: classify `USAGE` from `UsageText`; `REMARKS` = `UsageText` when valid, else the final `USAGE` code, else empty. Never use `PlotId` as text.
3. **Precedence for `USAGE` (decided by the owner: the Excel has first priority):** (1) a value the user entered or edited manually, which is protected and is never overwritten without explicit approval; then (2) a **valid match in `PARCEs_landuse.xlsx`** (the plot is found and its `USAGE` is a code in `Usage Types`); then (3) the **layer-level USAGE** chosen in the Step 1 drop-down (Part C); then (4) the fallback rules (`SUB → UTL`; MAIN from the usage text via the usage keyword table, else empty). A lower source fills only what the higher sources left empty. If the Excel row exists but its `USAGE` is empty or not in `Usage Types`, fall through to the layer value. Keep the final check that `USAGE` is in the `Usage Types` sheet. Record which source supplied each `USAGE` (`Manual`, `Excel`, `Layer`, `Fallback`) in the QA report.
**Tests:** the example above; no usage text → `REMARKS` = `USAGE` or empty; `PlotId` is never copied into `REMARKS`; a user-entered `USAGE` is kept; Excel `RSD` with layer value `COM` → `USAGE = RSD` (source `Excel`); no Excel match with layer value `COM` → `COM` (source `Layer`); Excel row with empty or invalid `USAGE` → falls through to the layer value.

### B.2 `BorderType_Keywords.csv` contains usage rows that are not border types
The file maps `سكني`→`RSD`, `سكني تجاري`/`تجاري سكني`→`RSC`, `زراعي`→`AGR`, `صناعي`→`IND`. These codes belong to `Usage Types`, not `Border Types` (67 official border codes), so they are dropped by validation and produce warnings on every run.
**Fix:** keep `BorderType_Keywords.csv` for Border Types only (every code must exist in the `Border Types` sheet). Move the usage rows to a separate `Usage_Keywords.csv` (columns as in the border table, codes validated against `Usage Types`) used by `MunicipalLookups.ClassifyUsageFromText`. Startup validation must warn only about real mistakes. (Note: the border codes `MSQ`, `PRK`, `ROD`, `PNT` in that file do exist in the sheet; `MSD` does not, and is no longer used.)

### B.3 Provenance text can leak into REMARKS
Step 2 sets `Remarks = "From Dataset layer [...]"` / `"From FeatureToPolygon [...]"`. When an Excel row matches but has no remarks, `EnrichParcels` leaves that string in `REMARKS`, and it can reach the exported data.
**Fix:** store provenance in an internal field (`Provenance`) or in the run log, never in `REMARKS`. Add a QA check that no exported `REMARKS` starts with `From `.

---

## Part C — New: USAGE per layer in Step 1 and the "Line with data layer" mode

### C.1 Step 1 — optional USAGE drop-down per selected layer
- In the layer-selection tables (Main Parcels and Sub-Parcels tabs) add a column **"USAGE"** with a drop-down for each selected layer. Selection is **optional** (empty by default).
- The list is read from the `Usage Types` sheet of `HelperFiles/Required Fields in the Shapefile.xlsx` (currently `RSD سكني`, `COM تجاري`, `RSC سكني تجاري`, `AGR زراعي`, `IND صناعي`, `UTL مرافق`), shown as `RSD — سكني`. It is **re-read from the Excel** every time the pane opens and on a "Refresh list" button, so a code the user adds to the sheet appears without a code change.
- Store the choice per layer name in the profile and in the project state; old profiles without it must load unchanged. If a stored code is no longer in the sheet, show a warning and clear it (never keep an invalid code).
- Default when empty: no layer-level USAGE (the existing rules in B.1 apply).

### C.2 Step 2 — rename the third mode to "Line with data layer"
- Current modes: Line, Polygon, and "Polygon line". Keep the first two as they are. **Rename the third to "Line with data layer"** (UI text, enum value, and profile value; the old stored value must still load and map to the new one).
- Idea: the parcels are built exactly like the Lines mode, plus a new sub-step that finds **which layer created each parcel** and takes that layer's USAGE.

### C.3 Algorithm for "Line with data layer"
1. **Source and selection:** same as the Lines mode: the `Polyline` feature class of `Cad_Dataset` (written by Step 1), selection `UPPER(Layer) IN (...)` of the layers selected in Step 1 (Main and Sub groups together, one combined line network). No fallback to unselected layers.
2. **Split at vertices:** run `management.SplitLine` (Split Line At Vertices) on that selection into a **temporary** feature class (scratch workspace; never a Step 2 output) so each segment keeps its `Layer` value. Z/M disabled in the environment.
3. **Build parcels:** run Feature To Polygon (Z disabled) on the same selection, as in the Lines mode, producing the single Step 2 output `Cad_Constructed_Parcels`.
4. **Attribute each parcel to a layer:** for every parcel, take the segments that lie **on its boundary** (segments whose midpoint is within `LAYER_MATCH_TOLERANCE` = 0.05 m of the parcel boundary, configurable; use a spatial index so it is not O(N²)), sum the **length per layer**, and take the layer with the **largest total length** as the parcel's layer.
5. **Assign from that layer:** `USAGE` = the layer's Step 1 drop-down value (if set; validated against `Usage Types`); the parcel class MAIN/SUB = the group of the dominant layer (Main tab / Sub tab); `UsageText` = the layer-level usage description if set.
6. **Flags and report:** if the dominant layer covers less than `DOMINANT_LAYER_MIN_SHARE` (default 60 %) of the boundary → flag `MIXED_LAYERS`; ties → deterministic (longer total, then layer name) and flagged; no segments found → flag and leave the layer fields empty; a dominant layer with no USAGE selected → no USAGE (existing rules apply). Write a per-parcel table (layer → meters, dominant layer, share, assigned USAGE) to the QA report.
7. **Cleanup:** delete the temporary segment feature class and temporary feature layers, also on failure. Only `Cad_Constructed_Parcels` remains and is on the map.
8. The layer-level `USAGE` assigned here is only the **third** source: in Step 3 a valid Excel match **overrides** it (the Excel has first priority), and a manual edit overrides both. The layer value is kept when the Excel has no match or no valid `USAGE` for the plot (precedence in B.1). Keep the layer-derived value in a separate internal field (for example `LayerUsage`) until Step 3 resolves the final `USAGE`, so the order of operations cannot lose it.

### C.4 Tests (fictional data)
1. Two closed rectangles drawn on layer `A` (USAGE `RSD`) and one on layer `B` (USAGE `COM`) → each parcel gets the USAGE of its layer.
2. A parcel bounded by 3 sides of layer `A` (30 m each) and 1 side of layer `B` (10 m) → layer `A`, share ≈ 90 %.
3. A parcel with 50/50 sides → `MIXED_LAYERS`, deterministic tie handling.
4. Layer without a USAGE selection → USAGE empty, no error.
5. A USAGE code removed from the Excel after being stored → warning and cleared, never written.
6. The drop-down list updates after adding a row to the `Usage Types` sheet and pressing Refresh.
7. Old profile with the previous mode name loads as "Line with data layer".
8. Temporary segments feature class is gone after success and after a forced failure.
9. A layer in the Sub group dominates → parcel class SUB.
10. Layer value `COM` and an Excel match with `RSD` for the same plot → final `USAGE = RSD`; no Excel match → `COM`.

## Final report (required)
For each part: what was changed (files and methods), the tests added with their actual results, the before/after timing for Part A, and what still needs a live ArcGIS Pro session (the geoprocessing calls, map layers, and the timing on real data).
