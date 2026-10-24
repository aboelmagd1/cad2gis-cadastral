# CAD2GIS — Prompt: Working Data Source (each step builds on the previous step)

**Scope:** change only how each step chooses and reads its input data (the "working data source"). Do not change the algorithms inside the steps. This prompt **replaces** the earlier "Latest export" source mode, the "continue from latest export" default, and the export-history selector.

## 1. The rule (from the product owner)

1. **Every step builds on the step before it.** The steps run in sequence: 1 → 2 → 3 → 4 → 5.
2. Each step has **exactly two source options, no more**:
   - **A. Previous step** (default): the step reads the output of the previous step.
   - **B. My own data**: the user picks his own dataset for that step.
3. **The previous step's output *is* its export. There is one copy of the data, not two.** When the user presses the button that exports a step's result, the exported Feature Class is the working data. There is no separate "live" (in-memory/session) version and "exported" version. When the user edits the exported data (for example in ArcGIS Pro), the next step reads exactly that edited data.

The purpose: the user must never wonder "am I editing the live data or the exported data?" There is only one.

## 2. Data flow (single source of truth = the step's output Feature Class on disk)

| Step (UI numbering) | Reads | Writes (this is also its export) |
| :--- | :--- | :--- |
| 1 Dataset / CAD conversion | the CAD file (or **My own data**) | `Cad_Dataset` in the geodatabase |
| 2 Parcels | Step 1's `Cad_Dataset` (or **My own data**) | one Step 2 parcels Feature Class |
| 3 Parcel schema / export | Step 2's parcels Feature Class (or **My own data**) | **one** Step 3 parcels Feature Class (the existing `PARCEL_Approved_Schema`) |
| 4 Borders | Step 3's Feature Class (or **My own data**) | one Step 4 borders Feature Class |
| 5 Final export | Step 3's parcels + Step 4's borders (or **My own data** for either) | `Final/Final.gdb`, `Final/Shape`, `Final/Reports` (deliverables) |

- Note the existing code names methods with an offset (for example `ExecuteStep3Async` calls `ExecuteStep4BordersAsync`). Follow the **UI step numbers** above and do not rename the public commands unless needed.
- Step 3 currently registers its output in **two places** (right after the populate/write step, ~line 3079, and again inside the export step, ~line 3879, with two different Feature Class names `PARCEL` and `PARCEL_Approved_Schema`). Make Step 3 have **one** output Feature Class; its export button writes or refreshes that one and registers it once.
- The Step 5 `Final` folder is a **deliverable**, not a source for earlier steps. It is not a third option anywhere. If the user wants to work from it, he picks it through **My own data**.

## 3. What to remove

Search by name and remove (or make inert, keeping profile/state files loadable):
- The `"LatestExport"` mode and every branch that reads it: `WorkingSourceMode == "LatestExport"`, `UseLatestExportCommand`, `OnHistoryItemSelected` (export-history selection), the `HasLatestExport`-driven switch inside `IsSchemaFromStandard`, the profile-apply line `if (p.ContinueFromLatestExport && HasLatestExport) WorkingSourceMode = "LatestExport";`, and the "latest export" banner/diff UI that exists only for this mode.
- The shared string `WorkingSourceMode` that all steps use (and `Stage3SourceMode`, which just proxies it).
- `ConsultantProfileModel.ContinueFromLatestExport`: stop reading it and stop showing it; old profile files that contain it must still load without error (ignore the value).
- Any **in-memory session copy as a source** between steps: `_parcels` / `_borders` must not be the thing the next step uses. (They can stay as a cache; see rule 4.2.)

## 4. What to implement

### 4.1 Per-step source with two values
```csharp
public enum StepSource { PreviousStep, OwnData }          // nothing else

private readonly Dictionary<int, StepSource> _sourceByStep = new()
    { [2] = StepSource.PreviousStep, [3] = StepSource.PreviousStep,
      [4] = StepSource.PreviousStep, [5] = StepSource.PreviousStep };
private readonly Dictionary<int, string> _ownDataPathByStep = new();   // only used when OwnData

public StepSource GetSource(int step) => _sourceByStep.TryGetValue(step, out var s) ? s : StepSource.PreviousStep;
```
Each step reads `GetSource(step)` — **never** a value shared with other steps, and not the tab that happens to be visible (a "Run all" may execute Step 4 while another tab is shown).

### 4.2 The step registry (`pipeline_state.json`)
A small file in the helper folder (`HelperFiles\state\pipeline_state.json`) that records, per step, the **verified output** of that step:
```json
{ "step": 3, "path": "<gdb>\\PARCEL_Approved_Schema", "featureCount": 585, "wkid": 32638,
  "writtenAt": "2026-10-24T10:15:00", "contentStamp": "<hash or last-edit marker>" }
```
- A step records its output **only after** it verified the Feature Class (exists, readable, expected geometry type, count > 0, valid spatial reference, count equals what was written).
- The next step resolves its input path **from this registry** (step N reads the entry of step N−1). It must survive restarting ArcGIS Pro. The registry is the only thing that links steps together: not layer names in the map, not fixed paths, not a leftover export.
- **Always read from the Feature Class on disk at the start of a step.** If an in-memory cache exists, use it only when its `contentStamp` equals the one in the registry; otherwise reload. Edits the user made in the Feature Class (in Pro or any tool) are therefore always picked up.

### 4.3 Reading the input (both options use the same validation)
Before a step starts, validate the chosen input: exists, readable, expected geometry type, feature count > 0, valid spatial reference, required fields present. If anything fails: **stop with a clear message and do not fall back to another source** (not to the other option, not to memory, not to an older export). Messages:
- Previous step output missing → `"Step N−1 has no verified output. Run Step N−1 first, or choose 'My own data'."`
- Own data invalid → the specific reason (geometry type, no features, no spatial reference, missing fields).
Log the resolved source path and the option used at the start of every step.

### 4.4 Option B — My own data
- The user browses to a Feature Class or shapefile for that step. Validate as above; show a **field-mapping panel** for the fields the step needs (for example plot id, parcel type) and keep the choice in the profile.
- Calculations are never done in GCS. If the data is in GCS, offer to project a copy to the chosen UTM zone first and log it; do not proceed silently.
- Choosing own data affects **that step's input only**. The step still writes its own output Feature Class (and registers it), so the following steps keep building on this step normally.

### 4.5 Re-running a step and manual edits (protect the single copy)
Because the output is the working data, re-running a step overwrites it. Before overwriting a step's output:
- Detect whether it was edited after the step wrote it (compare `contentStamp` / feature-level edit tracking with the registry).
- If it was edited, show what changed and ask for explicit confirmation (overwrite / keep and cancel). Never overwrite silently.
- If the output layer is in the map with **unsaved edits**, ask the user to save them first. If the Feature Class is locked, stop with a message.
- Never overwrite a user's attribute edits when only derived metrics are updated (keep the existing rule).

### 4.6 UI
For each step show exactly **two radio buttons**: "Previous step" and "My own data", plus a read-only line:
`Reads from: <full path> — <feature count> features — written <time> — edited since written: yes/no`
with buttons **Open folder** and **Add to map**. For "My own data" add a Browse button. Remove any third option, the export-history dropdown, and any "Latest export" label. Use the same wording in the Arabic UI.

## 5. Before / after example

**Problem today (example):** the user exports Step 3 to `PARCEL_Approved_Schema`, then edits two parcels in Pro. A profile with `ContinueFromLatestExport = true` is applied, so `WorkingSourceMode` becomes `"LatestExport"` and Step 4 reads yesterday's final export instead; or Step 4 reads the in-memory `_parcels`. The two edits are ignored and the user cannot tell which copy is the real one.
```csharp
// BEFORE: one shared mode, three sources (session memory, step 3 export, latest final export)
if (WorkingSourceMode == "LatestExport") { var s = ExportStateManager.LoadLatestExport(...); src = s!.ParcelPath; }
else { src = Step3ExportedParcelsFeatureClassPath; }        // in-memory property, lost after restart
var parcels = _parcels;                                      // memory copy used elsewhere
```
```csharp
// AFTER: one source of truth per step, two options only
var src = GetSource(4) == StepSource.OwnData
        ? ResolveOwnDataOrFail(4)
        : PipelineState.GetVerifiedOutputOrFail(step: 3);    // registry entry written by Step 3, survives restarts
ValidateInputOrFail(src);                                    // exists, polygon, count > 0, SR, fields
var parcels = GdbFeatureClassService.ReadParcelsFromDataset(src, utmWkid); // always read from disk
Log($"[Step4] option={GetSource(4)} source={src}");
```
**Result:** whatever the user edited in `PARCEL_Approved_Schema` is exactly what Step 4 reads.

## 6. Tests (fictional data; mark tests that need ArcGIS Pro)
1. Step 3 export exists; a final export from an older run also exists; profile with the old `ContinueFromLatestExport = true` → Step 4 still reads Step 3's Feature Class; no mode switch happens.
2. Edit a parcel attribute and a geometry in the Step 3 Feature Class, then run Step 4 → Step 4 uses the edited values.
3. Restart simulation (new view-model instance, registry file present) → Step 4 resolves Step 3's output.
4. Step 3 output missing → Step 4 stops with the "run Step 3 first or choose My own data" message; nothing falls back.
5. Option B with invalid data (no features / wrong geometry type / GCS) → specific message; GCS offers projection.
6. Option B for Step 4 does not change what Steps 2 and 3 read.
7. Re-run Step 3 after a manual edit → confirmation required; "cancel" leaves the edited output untouched.
8. Step 3 has one registered output (no second Feature Class name); the registry entry is written only after verification.
9. Old profile/state files containing `ContinueFromLatestExport` or history entries load without errors and change nothing.
10. UI/view-model: each step exposes exactly two source values.

## 7. Final report (required)
List what was removed, what was added, the files and methods changed, the tests added with their **actual** results, and what still needs a live ArcGIS Pro session to verify (feature-class edits in Pro, schema locks, unsaved-edit prompts).
