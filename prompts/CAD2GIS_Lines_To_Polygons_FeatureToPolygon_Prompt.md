# CAD2GIS — Prompt: Lines → Parcel Polygons (Selection + Feature To Polygon, Z disabled)

**Scope:** change only the Step 2 branch that runs when the user chooses the **Lines** geometry mode (the code path that currently calls `GdbFeatureClassService.RunFeatureToPolygonReportAsync` from `Cad2GisDockpaneViewModel`). Do not touch the other geometry modes, the text joins, Step 1, or Steps 3–5.

## 1. Required behaviour (exactly this, nothing else)

When the geometry mode is **Lines**, parcels are created like this:

1. **Source = the `Polyline` feature class that Step 1 wrote to the geodatabase**, inside `Cad_Dataset` (`<GdbPath>\Cad_Dataset\Polyline`; resolve the real path the same way Step 1 registered it). No re-conversion of the CAD file and no other source.
2. **Selection = the layers the user selected in Step 1.** Select the features of that Feature Class whose `Layer` field equals one of the selected CAD layer names. Use every selected layer, no more and no fewer.
3. **Feature To Polygon geoprocessing tool** (`management.FeatureToPolygon`) on that selection, with **Z disabled** (the geoprocessing environment `outputZFlag = Disabled`; also disable M).
4. The resulting polygons go into the existing Step 2 pipeline unchanged (text joins, area, perimeter, and so on).

Any failure in steps 1–3 **stops the step with a clear message**. There is **no fallback**: not to "all polylines", not to another tool, not to another data source.

## 2. Pre-flight checks (before any geoprocessing)

Stop with a specific message if any check fails:
- `Cad_Dataset` exists in the geodatabase and contains the `Polyline` feature class.
- The feature class has a `Layer` field and its geometry type is Polyline.
- The feature class has a spatial reference with a linear unit; log the WKID and unit. (Check this before applying any tolerance.)
- At least one layer is selected in Step 1; log the selected layer names.
- Log how many polylines each selected layer has in the feature class. A selected layer with zero polylines → warning naming the layer. Zero polylines in total → stop, and list the distinct `Layer` values that do exist in the feature class so the user can see the mismatch.

## 3. Building the selection (be strict)

Build one SQL expression from the selected layer names, doubling every single quote inside a name, and compare in upper case so that case differences in CAD layer names do not matter:
```csharp
// BEFORE (problem): several spelling variants listed by hand, and a silent retry without the filter
// whereClause = "Layer IN ('Plot','PLOT','plot')";
// if (MakeFeatureLayer failed) { MakeFeatureLayer(fc, tempLyr); }     // <- selects EVERYTHING

// AFTER
static string BuildLayerWhere(IEnumerable<string> layers)
{
    var items = layers.Select(n => "'" + n.Replace("'", "''").ToUpperInvariant() + "'");
    return $"UPPER(Layer) IN ({string.Join(",", items)})";
}
// Example: layers PLOT_LINES and Service's  ->  UPPER(Layer) IN ('PLOT_LINES','SERVICE''S')
```
Run `management.MakeFeatureLayer` with this expression. **If it fails, stop** and show the geoprocessing messages; never run again without the expression. After it succeeds, count the selected features and compare with the per-layer counts from the pre-flight step; they must add up. (Verify in a real Pro session that `UPPER()` is accepted for a file geodatabase; if it is not, use an exact-match list of the layer names exactly as stored in the feature class, taken from the distinct values read in the pre-flight step, never hand-written variants.)

## 4. Running Feature To Polygon (Z disabled)

```csharp
var env = Geoprocessing.MakeEnvironmentArray(
    workspace: gdbPath,
    overwriteoutput: true,
    outputZFlag: "Disabled",
    outputMFlag: "Disabled");           // verify the parameter names in the SDK version in use

string outFc = Path.Combine(gdbPath, "Cad_Constructed_Parcels");
var args = Geoprocessing.MakeValueArray(
    tempLyr,                             // the selected-layers feature layer
    outFc,
    clusterTolerance,                    // "" (empty) by default, see below
    "NO_ATTRIBUTES",
    "");                                 // no label features
var result = await Geoprocessing.ExecuteToolAsync("management.FeatureToPolygon", args, env);
```
- **Cluster tolerance:** default is **none** (empty = tool default). Use a value only if the user set one in the UI, formatted with the verified linear unit (for example `"0.01 Meters"`). **Never raise it automatically and never retry with a bigger one.**
- Delete an existing output with the same name before running (or rely on `overwriteoutput`), and always delete the temporary feature layer afterwards, also on failure.
- Capture **all geoprocessing messages** (`result.Messages`) and write them to the run log.
- Z handling: the environment disables Z; additionally check that the **output** has no Z (`HasZ == false`) and log if the input polylines had Z.

## 5. Verifying the result (report facts, not guesses)

- Fail if `result.IsFailed`.
- Count polygons in the output. For each polygon check it is not empty and has positive area. Log: selected line count, polygon count, tolerance used, linear unit.
- If the output has **zero polygons**, stop and report only measured facts plus the geoprocessing messages, for example: `"Selected layers: PLOT_LINES (418 lines), SERVICE_LINES (36 lines). Feature To Polygon produced 0 polygons. Tolerance used: none. Spatial reference: WKID 32638, unit Meter. Geoprocessing messages: ..."`. **Do not state a cause** (such as "unclosed lines") unless you measured it; you may add measured facts such as the number of line endpoints that touch no other line. Do not increase the tolerance, and do not delete or change any line.

## 6. Class assignment (MAIN / SUB) without losing the layer name

`NO_ATTRIBUTES` drops the `Layer` field, so do not read a layer name back from the output and do **not** assign the first selected layer to every polygon. Instead run the steps above **once per class group**: first with the layers selected in the Main Parcels tab, then (if any are selected) with the layers selected in the Sub-Parcels tab. Each run uses all the layers of its group, writes its own output (`Cad_Constructed_MAIN`, `Cad_Constructed_SUB`), and every polygon of an output gets the class of its group. If only one group has layers, there is one run.

## 7. Logging

Log, at the start and end of the branch: geometry mode = Lines, source path, selected layers, WHERE expression, selected counts, tolerance, spatial reference, tool messages, polygon count, output path.

## 8. Tests (add to `Cad2Gis.Tests`; use fictional data)

Tests that need ArcGIS Pro must say so. Cover:
1. A closed rectangle drawn on one selected layer → 1 polygon.
2. A rectangle whose four sides are split between layer `A` and layer `B`: with both selected → 1 polygon; with only `A` selected → 0 polygons and a message listing `A` and the layers that exist.
3. A frame/dimension layer that is not selected is not used (its lines create no polygons).
4. Layer names differing only in case (`plot_lines` vs `PLOT_LINES`) → matched; a name containing a single quote → no SQL error.
5. A failing `MakeFeatureLayer` → the step stops with the messages; no polygons from "all lines".
6. Input polylines with Z values → output has no Z and the polygon is built.
7. Empty selection / empty feature class → a clear message, not a success.
8. Main group and Sub group selected → two runs, each polygon carries its own group's class.
9. A gap smaller than the tolerance → 0 polygons without a tolerance (report with the log numbers) and 1 polygon when the user sets a tolerance larger than the gap.

## 9. Final report (required)
What changed (files and methods), the exact tool call and environment used, the tests added and their **actual** results, and what could not be verified without a live ArcGIS Pro session (in particular: the `UPPER()` selection syntax on a file geodatabase, the `MakeEnvironmentArray` parameter names, and whether the output keeps true arcs — if arcs become straight segments, report it; fixing that is a separate task).
