---
Status: SUPERSEDED by prompts/spec/
Date: 2026-10-27
Notes: Fully superseded and consolidated into prompts/spec/02_PIPELINE_AND_DATA_FLOW.md through 07_STEP5_EXPORT_QA_SPEC.md. Fixes verified and recorded in code. Retained in archive for historical record.
---

# CAD2GIS — Detailed Fix Prompt (from the code review of `cad2gis-cadastral-main.zip`)

You are fixing confirmed defects in the CAD2GIS ArcGIS Pro 3.3 Add-in (`Cad2Gis/Cad2Gis.AddIn`, `Cad2Gis.Core`, `Cad2Gis.Tests`). Every issue below was found by reading the actual code; file names and line numbers refer to the reviewed snapshot (they may have drifted — search by the quoted code). For each issue you get: the **problem with a concrete example**, the **root cause**, the **required fix with a before/after example**, and a **test**.

## Ground rules
- Fix only what is listed (surgical, minimal changes). Keep working behaviour intact; no broad refactors.
- Read the surrounding code before editing; do not assume.
- `Cad2Gis.AddIn` needs the ArcGIS Pro SDK and a live Pro session. Run `dotnet test` for everything that builds without Pro (the `Cad2Gis.Core` logic and its tests); for AddIn-only changes, say exactly what you could not build or run. **Never claim a test passed unless you ran it.**
- Never silently switch a data source, never report success when a step failed, never update a "latest" reference before the output is verified.
- All user-facing messages stay bilingual-friendly (Arabic UI text is fine, keep existing style).

Priority: P1–P3 first (they break the workflow), then P4–P6.

---

## P1 — Step 4 reads the wrong source (stale Latest Export instead of Step 3's Feature Class)

**Where:** `Cad2GisDockpaneViewModel.cs` — `WorkingSourceMode` (one string shared by Steps 2, 3, 4; `Stage3SourceMode` just proxies it), `ExecuteStep4BordersAsync` (branches on `WorkingSourceMode`), profile apply (`if (p.ContinueFromLatestExport && HasLatestExport) WorkingSourceMode = "LatestExport";`), `ConsultantProfileModel.ContinueFromLatestExport = true` (default), `UseLatestExportCommand`, `OnHistoryItemSelected`, `IsSchemaFromStandard` (sets `LatestExport` whenever `HasLatestExport`).

**Problem (example):** The user finishes a full run once (Step 5 writes `last_export.json`). Next day they open a new CAD, run Steps 1–3 and export Step 3 to `Cad_Dataset.gdb\PARCEL_Approved_Schema`. They apply their consultant profile (default `ContinueFromLatestExport = true`). `WorkingSourceMode` becomes `"LatestExport"`. They press Step 4. The code takes the `else if (WorkingSourceMode == "LatestExport")` branch and reads `state.ParcelPath` — **yesterday's final export** — and never looks at `Step3ExportedParcelsFeatureClassPath`. Borders are extracted from the wrong parcels with no warning.

**Root cause:** one global source mode for all steps + a profile default that forces `LatestExport` + the Step 3 output reference lives only in memory (`Step3ExportedParcelsFeatureClassPath` is a plain property; it is empty after Pro restarts).

**Fix:**
1. Per-step source mode. Keep XAML bindings working, but store the mode per step:
```csharp
public enum StepSource { FromPreviousStep, LatestExport, UserOwnData }
private readonly Dictionary<int, StepSource> _sourceByStep = new() { [2]=StepSource.FromPreviousStep, [3]=StepSource.FromPreviousStep, [4]=StepSource.FromPreviousStep };
public StepSource GetSource(int step) => _sourceByStep.TryGetValue(step, out var s) ? s : StepSource.FromPreviousStep;
public void SetSource(int step, StepSource s) { _sourceByStep[step] = s; NotifyPropertyChanged(null); }
// WorkingSourceMode stays only as the proxy for the step tab currently shown in the UI
```
`ExecuteStep4BordersAsync` must use `GetSource(4)`, not `WorkingSourceMode` (a "Run all" can execute Step 4 while another tab is visible).
2. Default for every step is `FromPreviousStep`. `LatestExport` is used **only when the user explicitly chose it for that step**. Change `ContinueFromLatestExport` default to `false`; if a profile has it `true`, show a banner ("Latest export is available — use it for this step?") instead of switching silently. This replaces the earlier "latest export is the default" rule for Steps 2–4 (the audit rule "no stale export as a silent source" wins).
3. Persist the verified Step 3 output reference in `HelperFiles\state\pipeline_state.json` (`{ step: 3, path, featureCount, wkid, timestamp, runId }`) and load it at startup, so Step 4 still finds it after Pro restarts.
4. In Step 4's default branch keep the existing validation, and extend it: existence, readable, `GeometryType.Polygon`, count > 0, valid spatial reference, **and** that the count equals the recorded count (warn if different). Log the resolved path and source mode at the start of the run.

**Before / after (Step 4 source resolution):**
```csharp
// BEFORE: one shared mode decides for every step
else if (WorkingSourceMode == "LatestExport") { var state = ExportStateManager.LoadLatestExport(...); step4SourcePath = state!.ParcelPath; }
else { /* Step3ExportedParcelsFeatureClassPath */ }

// AFTER: the Step 4 mode is its own value; default reads the verified Step 3 reference
var mode = GetSource(4);
string step4SourcePath = mode switch
{
    StepSource.UserOwnData   => ResolveUserOwnData(),                       // explicit user choice
    StepSource.LatestExport  => ResolveLatestExportOrFail(),                // explicit user choice, banner shown
    _                        => ResolveVerifiedStep3OutputOrFail()          // pipeline_state.json / session; fail loudly if missing
};
Log($"[Step4] source={mode} path={step4SourcePath}");
```
**Tests:** (a) a latest export exists and the profile has `ContinueFromLatestExport = true`; Step 3 output exists → Step 4 reads Step 3's path. (b) Restart simulation: load `pipeline_state.json` → Step 4 finds Step 3's output. (c) Step 3 path missing → Step 4 stops with a message, no fallback. (d) User picks LatestExport for Step 4 only → Steps 2/3 unaffected.

---

## P2 — Step 3 registers its output before verifying it, and reports success after a failed export

**Where:** `Cad2GisDockpaneViewModel.cs` ~line 3079 (`Step3ExportedParcelsFeatureClassPath = Path.Combine(GdbPath, fcParcelName);` right after `WriteParcelsToFeatureClass`) and ~line 3879 inside `ExportStep3` (registration inside a `try`, but the `catch (Exception gdbEx)` only adds a "[GDB Notice]" warning and the summary still says "Step 3 Export Complete").

**Problem (example):** The GDB write fails (locked gdb, schema error). The catch adds a warning, the registered path from a *previous* run is still set (or the new path is set before anything was verified), and the UI says "Step 3 Export Complete: Successfully exported 585 parcels". Step 4 then reads a missing or stale Feature Class.

**Fix:** add a verification helper and use it before registering:
```csharp
// GdbFeatureClassService
public static (bool ok, string reason) VerifyPolygonFeatureClass(string fcPath, int expectedCount, int expectedWkid)
{
    try {
        using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(Path.GetDirectoryName(fcPath)!)));
        using var fc = gdb.OpenDataset<FeatureClass>(Path.GetFileName(fcPath));
        var def = fc.GetDefinition();
        if (def.GetShapeType() != GeometryType.Polygon) return (false, "geometry type is not Polygon");
        long n = fc.GetCount();
        if (n == 0 || n != expectedCount) return (false, $"feature count {n} != expected {expectedCount}");
        if (def.GetSpatialReference()?.Wkid != expectedWkid) return (false, "spatial reference mismatch");
        return (true, "");
    } catch (Exception ex) { return (false, ex.Message); }
}
```
```csharp
// BEFORE
Step3ExportedParcelsFeatureClassPath = Path.Combine(GdbPath, fcParcelName);   // registered unverified
// AFTER
var path = Path.Combine(GdbPath, fcParcelName);
var (ok, reason) = GdbFeatureClassService.VerifyPolygonFeatureClass(path, _parcels.Count, SelectedUtmZone);
if (!ok) { ReportError($"Step 3 export failed verification: {reason}"); return; }   // keep the previous reference untouched
RegisterVerifiedStep3Output(path, _parcels.Count, SelectedUtmZone);                 // updates property + pipeline_state.json
```
In `ExportStep3`, a failed GDB export must end the step with an **error** (not a warning + "Complete"). Shapefile failure may stay a warning if the GDB succeeded, but the summary must say which part failed.
**Tests:** failed GDB write → previous registered path unchanged, message says failed; count mismatch → not registered; success → registered and persisted.

---

## P3 — Lines → Polygons (`GdbFeatureClassService.RunFeatureToPolygonReportAsync`)

### P3.1 Silent fallback ignores the user's layer selection
**Problem (example):** The user ticked layers `PLOT_LINES` and `SERVICE_LINES` in Step 1. `MakeFeatureLayer` with the `where` clause fails (for instance a quoting problem). The code retries **without** the clause, so Feature To Polygon runs on **all** polylines (frames, dimensions, grid lines) and the report still says "success … from the selected layers".
```csharp
// BEFORE
if (lyrResult.IsFailed && !string.IsNullOrWhiteSpace(whereClause))
{   makeLyrArgs = Geoprocessing.MakeValueArray(actualPolylinePath, tempLyr);   // silently drops the filter
    lyrResult = await Geoprocessing.ExecuteToolAsync("management.MakeFeatureLayer", makeLyrArgs); }
// AFTER
if (lyrResult.IsFailed)
{   report.Success = false;
    report.DiagnosticMessage = "Could not select the chosen layers (MakeFeatureLayer failed):\n" + MessagesText(lyrResult);
    return report; }                                    // never widen the input silently
```
**Test:** simulate a failing where-clause → report fails with a message and no polygons are built.

### P3.2 Layer attribution is lost (every polygon gets the first layer's name)
**Problem (example):** Two layers are selected, `MAIN_PARCELS` and `PASSAGES`. The tool runs with `NO_ATTRIBUTES`, so the output has no `Layer` field; the read-back code does `layerVal = layerList[0]` for every polygon. All polygons are labelled `MAIN_PARCELS`, so passages become main parcels.
**Fix:** attribute each constructed polygon from the geometry: for every polygon, measure the share of its boundary length that coincides (within a tiny tolerance) with the lines of each selected layer, and set `LayerName` to the dominant layer, plus `LayerNames` (all contributing layers, flag `MIXED_LAYERS` when more than one layer contributes more than 10 %). In "Per layer" mode run the tool per layer and keep that layer name. Remove the `layerList[0]` fallback.
**Test:** three layers, two closed rectangles on layer A and one on layer B → each polygon gets its own layer; a polygon bounded by lines of A and B is flagged `MIXED_LAYERS`.

### P3.3 Zero-polygon diagnosis guesses the cause
**Problem (example):** The tool finished with 0 polygons because the wrong layers were selected (all lines are on a layer that was not ticked) — the message still says "the lines do not form closed rings … dangles".
**Fix:** compute the diagnosis from the data: line count and closed-ring count per layer; endpoints that have degree 1 (dangles) with the smallest gap distance between dangle pairs; spatial reference and linear unit of the input; whether the selected layers contain lines at all. Report only what was measured, e.g. `"12 dangling endpoints; smallest gap between two endpoints: 0.032 m; tolerance used: 0 m"`. If the cause cannot be determined, say "cause not determined" and include the full Geoprocessing messages. Do **not** raise the cluster tolerance automatically.
**Test:** closed rectangle → 1 polygon; rectangle with a 0.03 m gap → 0 polygons and a message containing the measured gap; empty selection → "no lines in selected layers"; two disconnected closed rings → 2 polygons.

### P3.4 Curves and moved vertices
The tool may move vertices when `cluster_tolerance` is above zero and may not preserve true arcs in its output (verify in a real Pro session and record the result). Required: if the output of `FeatureToPolygon` shows fewer curve segments than the input arcs (use `NativeCurveAuditHelper` / `CurveAuditService`), build the output ring with `CurvePreservingPolygonizerService` (chain the original segments) and use Feature To Polygon only to detect connectivity. Log the tolerance actually used and the number of vertices moved.

---

## P4 — Border extraction edge cases (`Cad2Gis.Core/Geometry/BorderExtractionCore.cs`, `ProcessRing`)

### P4.1 A ring with fewer than 4 segments (or a single split point) produces **no pieces**
**Problem (example):** A circular parcel drawn as two arcs (`segCount = 2`) has no corner. The code sets `splitIndices = {0}`. Then `startVtx = 0` and `endVtx = splitIndices[(0+1) % 1] = 0`, so `while (currSeg != endVtx)` never executes: the ring yields **no border at all** (only the perimeter-mismatch warning shows). The same happens for a ring with exactly one split point.
**Fix:** when there is one split index, make one piece that walks the whole ring starting at that index; when there are no split points and `segCount < 4`, split at the segment boundaries (up to `segCount` pieces) deterministically and log it.
```csharp
// BEFORE
while (currSeg != endVtx) { piece.Segments.Add(segments[currSeg]); currSeg = (currSeg + 1) % segCount; }
// AFTER: a single split point must yield one full-ring piece
int walked = 0;
do
{
    piece.Segments.Add(segments[currSeg]);
    currSeg = (currSeg + 1) % segCount;
    walked++;
}
while (walked < segCount && (currSeg != endVtx || splitIndices.Count == 1));
```

**Test:** a circle stored as two arcs → pieces are created, total length equals the circumference, the integrity check passes, directions are assigned; a ring with one corner → one full-ring piece.

### P4.2 Ring orientation uses chord endpoints only
**Problem (example):** `signedArea` is the shoelace sum over `segments[i].Start/End`. For a circle stored as two arcs, the chord polygon has only two distinct points, so the signed area is 0; `insideIsOnRight` becomes arbitrary and every outward normal may be reversed — North and South (and East/West) swap.
**Fix:** include each arc's contribution to the signed area (circular segment area `0.5·r²·(θ − sin θ)`, signed by the arc's turning direction), or take the ring orientation from the SDK polygon (rings are normalized: exterior clockwise, interior counter-clockwise). Add a unit test with a two-arc circle and a half-moon shape, checking that an arc facing north is classified North.

### P4.3 Stale documentation of defaults
`BorderExtractionOptions` XML comments say the reflex range defaults to 230°–340°, but the defaults in the same class are 10°/160° (mirror = 200°–350°), and `prompts/CAD2GIS_Border_Extraction_Prompt.md` says 10°–160°. Correct the comments so code, comments, and prompt agree (do not change the defaults without the owner's confirmation).

---

## P5 — Width inference and description output (`WidthInferenceService.cs`, `OutwardRaySensor.cs`)

### P5.1 The literal text "UNKNOWN" is written into the deliverable
**Problem (example):** No obstacle within 100 m → the code sets `border.BorderType = "UNKNOWN"` and `border.Descriptio = "UNKNOWN"`. Those strings go into `BORDERTYPE` (a short code field, 3 characters in the official schema) and `DESCRIPTIO` of the exported shapefile/GDB — either truncated to `UNK` or exported as a fake value.
**Fix:** keep the state in a flag, not in the data. Add `IsUnknown` (and `ReviewStatus`) to `BorderRecord`; leave `BorderType` and `Descriptio` empty (or apply the missing-data policy); show "UNKNOWN" only in the review table and the QA report; update `QaValidationEngine` (`b.Descriptio == "UNKNOWN"` at ~line 193) to test the flag.
```csharp
// BEFORE
border.BorderType = "UNKNOWN"; border.Descriptio = "UNKNOWN";
// AFTER
border.IsUnknown = true; border.BorderType = ""; border.Descriptio = "";   // exported blank; flagged for review
```
Apply the same change in `OutwardRaySensor.cs` (~line 242). **Test:** export a border with no hit → the exported DESCRIPTIO/BORDERTYPE are empty and the QA report lists it as `UNKNOWN`.

### P5.2 Banker's rounding flips results
**Problem (example):** `Math.Round(12.5) = 12`, `Math.Round(9.5) = 10`, `Math.Round(10.5) = 10` (round-half-to-even). A measured 9.5 m becomes 10 → "شارع بعرض 10 م", while 8.5 becomes 8. Results at x.5 depend on whether the integer part is even.
**Fix:** `Math.Round(value, MidpointRounding.AwayFromZero)` everywhere a width is rounded. **Test:** 6.5→7, 9.5→10, 10.5→11, 12.5→13.

### P5.3 Median of an even number of hits, and the ray offset
- With 3 rays, if one misses, `sorted[Count/2]` picks the larger of the two remaining values. Define the median properly (mean of the two middle values for an even count) and document it.
- The ray starts 0.05 m outside the boundary and the hit distance is measured from there, so the width is short by 0.05 m. Add the start offset back (or measure from the boundary).
**Test:** hits `[11.9, 12.1]` → median 12.0; a 12.00 m gap measured with the offset → 12.00 (not 11.95).

### P5.4 Two implementations of ray sensing
`Cad2Gis.AddIn/Geometry/OutwardRaySensor.cs` and `Cad2Gis.Core/Services/WidthInferenceService.cs` both contain width-inference logic. Find which one Step 4 calls, make the other call it (or delete it), so the two cannot diverge (they already differ in how "UNKNOWN" is handled).

---

## P6 — Step 5 export: "Latest Export" updated without verifying the outputs, and failures hidden

**Where:** `Cad2GisDockpaneViewModel.cs` ~lines 3690–3760 (building `ExportStateRecord`), `ExportStateManager.SaveExportState` (catches exceptions internally), the outer `catch (Exception stateEx) { Debug.WriteLine(...) }`.

**Problem (example):** The shapefile for BORDER was not written (locked file). The code still saves `last_export.json` with only the PARCEL dataset (it does `if (File.Exists(expectedBorderShp)) {...}` and moves on), sets `LatestExportPath = finalDir`, and prints "تم تصدير المخرجات النهائية بنجاح". Next run "continues from the latest export" and finds no borders. If `SaveExportState` itself fails, only `Debug.WriteLine` records it, and the UI still shows success.
**Fix:**
1. Before saving the state, verify **all** deliverables: `Final.gdb` PARCEL and BORDER readable with counts equal to the in-memory counts; `Shape\PARCEL.shp` and `BORDER.shp` with `.shp/.shx/.dbf/.prj/.cpg`; the report files exist. Any missing required output → export marked **failed**, `last_export.json` untouched, `LatestExportPath` unchanged.
2. `SaveExportState` must **throw or return false** on failure; the caller shows an error and does not print the success summary.
3. The success summary lists exactly what was verified (counts and paths), not a fixed text.
```csharp
// BEFORE
ExportStateManager.SaveExportState(file, state, maxHist);   // errors swallowed
LatestExportPath = finalDir; ...                              // always updated
// AFTER
var v = VerifyFinalExport(finalDir, _parcels.Count, _borders.Count);       // gdb counts + shapefiles + reports
if (!v.Ok) { ReportError("Final export failed verification:\n" + v.Report); return; }
if (!ExportStateManager.TrySaveExportState(file, state, maxHist, out var err)) { ReportError("Could not record the latest export: " + err); return; }
LatestExportPath = finalDir; HasLatestExport = true;
```
**Tests:** missing BORDER shapefile → no state update, error shown; state file not writable → error shown, no success text; full success → state updated with both datasets and their hashes.

---

## Not reviewed (please check as part of this pass, report findings separately; do not rewrite)
`CurvePreservingPolygonizerService.cs`, `FacadeDecomposer.cs`, `GdbFeatureClassService.cs` writers/readers (`ReadParcelsFromDataset`, `ReadBordersFromDataset`), `ShapefileExporter.cs` (confirm `DensifyByDeviation` is applied only to the GCS/shapefile copy, never to the UTM working or GDB data), the ArcPy toolbox `HelperFiles/Cad2Gis_ArcPy.pyt`, and the XAML bindings for the per-step source selectors once P1 is done.

## Final report (required)
For each issue (P1–P6): problem, root cause, files/methods changed, fix, tests added and their **actual** results, and what still needs a live ArcGIS Pro session or real CAD data to verify.
