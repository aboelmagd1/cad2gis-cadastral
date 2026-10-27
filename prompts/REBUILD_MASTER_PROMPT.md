# CAD2GIS — Rebuild Master Prompt for Fresh Agent

> **Mission Statement:**  
> You are an autonomous software engineering agent tasked with rebuilding the **CAD2GIS** cadastral transformation platform from scratch or performing targeted enhancements within this repository. The local workspace is the single source of truth. Your goal is to deliver an ArcGIS Pro 3.3.0 Add-in (.NET 8.0, C#) with an ArcPy fallback that processes municipal AutoCAD drawings (DWG/DXF) into fully compliant Saudi MOMRAH cadastral GIS datasets (PARCEL and BORDER) with cardinal boundary decomposition and street corridor width sensing.

---

## 1. Operating Rules & Constraints

1. **Authoritative Specification Precedence:**
   - The authoritative specification is located exclusively under `prompts/spec/`.
   - The specification hierarchy is:
     1. Owner Decisions (D01–D20 in `prompts/spec/13_DECISIONS_LOG.md`).
     2. Official MOMRAH Data Dictionary (`prompts/spec/08_DATA_DICTIONARY_AND_LOOKUPS.md` & `HelperFiles/Required Fields in the Shapefile.xlsx`).
     3. Functional Pipeline Specs (`prompts/spec/02_PIPELINE_AND_DATA_FLOW.md` through `07_STEP5_EXPORT_QA_SPEC.md`).
     4. Settings & UI Specifications (`09_SETTINGS_AND_PROFILES.md`, `10_UI_SPEC.md`).
     5. Build & Packaging Rules (`12_BUILD_DEPLOY.md`).
   - If any ambiguity arises, **never guess**. Consult `prompts/spec/13_DECISIONS_LOG.md` for the documented interim default and flag the item as `OPEN`.

2. **Immutable System Invariants:**
   - **INV-01 (Single Output Per Step):** Each step produces exactly one output feature class on disk and one layer on the map (`Cad_Constructed_Parcels`, `PARCEL_Approved_Schema`, `BORDER_Cardinal_Approved`, Step 5 deliverables).
   - **INV-02 (Two Source Options Only):** Strictly two source radio options per step: Previous Step live output or User Own Data. "Latest Export" source mode is strictly forbidden.
   - **INV-03 (Manual Edit Protection):** Any manual edit made in review DataGrids must never be silently overwritten on re-execution without explicit user confirmation.
   - **INV-04 (No Silent Auto-Install):** Packaging must build `.esriAddinX` for manual user installation. Never write directly to `%LOCALAPPDATA%\ESRI\ArcGISPro\AssemblyCache\` during build.
   - **INV-05 (Zero Geometry Generalization):** Circular arcs and true curves must remain arcs in File Geodatabase. Densification ($\le 0.001\text{ m}$) is permitted only for Shapefile and GCS export.
   - **INV-06 (No Hard-Coded Lookups):** All land-use, border, and building codes must be read dynamically at runtime from `Required Fields in the Shapefile.xlsx`.
   - **INV-07 (DBF UTF-8 Byte Enforcement):** Shapefile text attributes must never exceed DBF field byte limits in UTF-8.
   - **INV-08 (Anti-Freeze Threading):** No sync-over-async (`.Result`, `.Wait()`) on the UI thread. All geoprocessing must run in background tasks or via `QueuedTask.Run`.

---

## 2. Specification Reading Order

Before writing or editing code, read the specification files in this exact sequence:
1. [`00_INDEX.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/00_INDEX.md) & [`00_SOURCES.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/00_SOURCES.md): System index, glossary, and source inventory.
2. [`01_PRODUCT_OVERVIEW.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/01_PRODUCT_OVERVIEW.md): Architecture overview and core invariants.
3. [`02_PIPELINE_AND_DATA_FLOW.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/02_PIPELINE_AND_DATA_FLOW.md): Pipeline steps, state transitions, and working fields.
4. [`08_DATA_DICTIONARY_AND_LOOKUPS.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/08_DATA_DICTIONARY_AND_LOOKUPS.md): 26-field PARCEL and 10-field BORDER schemas.
5. [`03_STEP1_LAYERS_SPEC.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/03_STEP1_LAYERS_SPEC.md) through [`07_STEP5_EXPORT_QA_SPEC.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/07_STEP5_EXPORT_QA_SPEC.md): Algorithmic specifications for Steps 1 to 5.
6. [`09_SETTINGS_AND_PROFILES.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/09_SETTINGS_AND_PROFILES.md) & [`10_UI_SPEC.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/10_UI_SPEC.md): Dockpane UX and configuration management.
7. [`11_TEST_PLAN.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/11_TEST_PLAN.md) & [`14_REBUILD_PLAYBOOK.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/14_REBUILD_PLAYBOOK.md): Acceptance testing criteria and milestone sequence.
8. [`12_BUILD_DEPLOY.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/12_BUILD_DEPLOY.md) & [`13_DECISIONS_LOG.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/13_DECISIONS_LOG.md): Deployment rules and architectural conflict log.

---

## 3. Step-by-Step Rebuild Milestones

Execute the rebuild according to the milestone roadmap defined in [`14_REBUILD_PLAYBOOK.md`](file:///d:/workking%20on/shafik/create%20tool%2002/prompts/spec/14_REBUILD_PLAYBOOK.md):

### Milestone M1: Solution Skeleton & Settings
- Scaffold `Cad2Gis.sln` with `Cad2Gis.Core` (`net8.0`), `Cad2Gis.AddIn` (`net8.0-windows`), and `Cad2Gis.Tests` (`net8.0`).
- Implement `SettingsManager` and `ProfileManager` reading/writing `%APPDATA%/CAD2GIS/settings.json`.
- **Acceptance:** Solution compiles; unit tests verify atomic persistence and self-healing defaults (`REQ-SET-001..006`).

### Milestone M2: Lookups & Excel Engine
- Implement `ClosedXmlService` and `LookupService` to parse `Required Fields in the Shapefile.xlsx` dynamically.
- Parse `Usage Types` (6 codes), `Border Types` (67 codes), and keyword lookup CSVs.
- **Acceptance:** Zero hard-coded lists; unit tests verify extraction of all lookup codes (`REQ-DATA-001..008`).

### Milestone M3: Step 1 CAD Ingestion & Font Decoding
- Implement `CadDatasetReader`, layer categorization into 4 tabs, and xArab reverse-byte font decoder with slash preservation (`11/3314`).
- Implement `Expected_Layers.xlsx` reading and `Layers_Used.xlsx` auto-creation.
- **Acceptance:** Reverse Arabic text decoded properly without corrupting parcel numbers (`REQ-ST1-001..008`).

### Milestone M4: Step 2 Parcel Polygonization
- Implement `GeometryEngine2D` supporting Line, Polygon, and Line with data layer modes.
- Implement vertex splitting, $0.05\text{ m}$ buffer, and 60% dominant boundary length attribution (`_CLASS, _LAYER, _DOM_PCT`).
- Implement spatial text join (contains $\rightarrow$ nearest fallback $\rightarrow$ provisional `P{ID}`).
- **Acceptance:** Synthetic linework fixture correctly polygonizes and assigns all 10 working fields (`REQ-ST2-001..009`).

### Milestone M5: Step 3 Municipal Attributes & Schema
- Implement `Step3MunicipalSchemaService` enforcing 26-field MOMRAH schema.
- In Line with data layer mode, pull `PLOT_ID` from Step 2 `PLOT_TXT`.
- Implement 4-tier `USAGE` precedence and `REMARKS` generation rules.
- Purge all 10 working fields from `PARCEL_Approved_Schema`.
- **Acceptance:** Output feature class matches official schema with zero working fields remaining (`REQ-ST3-001..010`).

### Milestone M6: Step 4 Cardinal Borders
- Implement `BorderExtractionEngine` decomposing MAIN parcel rings into max 4 single-part lines (1 North, 2 East, 3 South, 4 West).
- Detect corners via $[10^\circ, 160^\circ]$ and $[200^\circ, 350^\circ]$ interior angles; evaluate virtual chords for true arcs; merge $7\text{ m}$ chamfer clusters.
- Compute outward normal with true north meridian convergence correction.
- Detect shared segments ($\ge 0.5\text{ m}$, $0.05\text{ m}$ buffer); classify neighbor (`PLT` vs SUB keyword match).
- Sensor street width via single midpoint ray ($1.0\text{ m}$–$100.0\text{ m}$ facing gap); classify passage vs street at $10.0\text{ m}$.
- Evaluate `DIMENSION` (Single vs Multi at $15^\circ$ deflection threshold).
- **Acceptance:** All 6 synthetic geometry fixtures (`FIX-RECT-01` to `FIX-TRI-06`) produce exact expected borders (`REQ-ST4-001..020`).

### Milestone M7: Step 5 Export Packaging & QA
- Implement `ExportPackagingService` producing `Final.gdb` (native arcs) and `Final/Shape/` (controlled densification $\le 0.001\text{ m}$).
- Generate mandatory UTF-8 `.cpg` files and validate DBF byte lengths.
- Freeze metric attributes (`PARCEL_ARE`, `LENGTH_M`) before projecting to GCS EPSG:4326.
- Implement `QaValidationEngine` executing all 8 municipal validation rules; output bilingual `.json` and `.txt` reports.
- **Acceptance:** Output packages conform to delivery folder layout; zero fatal errors in QA summary (`REQ-ST5-001..010`).

### Milestone M8: UI Dockpane, Anti-Freeze Integration & Packaging
- Implement `Cad2GisDockpane.xaml` and ViewModels with throttled progress logging and review DataGrids.
- Wrap all Pro SDK calls in `QueuedTask.Run` and geoprocessing in background workers.
- Execute `package_addin.ps1` to assemble `Cad2Gis.esriAddinX`.
- **Acceptance:** Clean manual installation via Esri Add-In Manager; dockpane launches and executes without UI lockup (`REQ-UI-001..007`, `REQ-BLD-001..005`).

---

## 4. Test Isolation and Environment Guidance

| Test Category | Target Suite / Tool | Can Run Without Pro? | Verification Method |
| :--- | :--- | :---: | :--- |
| **Unit & Algorithmic Tests** | `Cad2Gis.Tests` | **YES** | Run `dotnet test Cad2Gis.Tests` from CLI. All 240+ tests must be green. |
| **Geometry Computation** | `Cad2Gis.Core/Geometry` | **YES** | Standalone xUnit tests using synthetic coordinate fixtures. |
| **Excel & Lookup Parsing** | `Cad2Gis.Core/Services` | **YES** | OpenXml unit tests parsing test `.xlsx` workbooks. |
| **Add-In UI & Data Binding** | `Cad2Gis.AddIn` | **YES** (Partial) | XAML compilation and ViewModel unit tests. |
| **Active Map & Pro Geodatabase** | ArcGIS Pro 3.3.0 Session | **NO** | Requires launching ArcGIS Pro, adding test CAD DWG, and running pipeline tabs. |
| **ArcPy Fallback Toolbox** | `Cad2Gis_ArcPy.pyt` | **NO** | Run inside Pro Python window or via `propy`. |

---

## 5. Change Protocol for Future Modifications

Whenever modifying the CAD2GIS codebase, follow this strict four-step protocol:
1. **Update the Specification First:**
   - Modify the relevant spec document under `prompts/spec/`.
   - If introducing an architectural decision or resolving an open question, record it in `13_DECISIONS_LOG.md`.
2. **Implement Code Changes:**
   - Make the minimal required changes to `Cad2Gis.Core`, `Cad2Gis.AddIn`, or `HelperFiles`.
   - Adhere to the established patterns, preserving existing docstrings, comments, and exception guards.
3. **Update and Run Tests:**
   - Add or update corresponding unit tests in `Cad2Gis.Tests`.
   - Run `dotnet test Cad2Gis/Cad2Gis.Tests/Cad2Gis.Tests.csproj` and ensure 100% pass rate.
4. **Update the Status Matrix:**
   - Update `prompts/spec/STATUS_MATRIX.md` to reflect the new implementation status and code locations.

---

## 6. Final Report Format

Upon completing any rebuild phase or major modification task, your final report must include:
1. **List of Files Created and Modified** (with clickable markdown file links).
2. **Requirement Implementation Summary** (table of REQ counts by area and status).
3. **Decisions & Conflict Resolutions Applied** (references to `13_DECISIONS_LOG.md`).
4. **Status of Open Items** (any pending questions for the owner).
5. **Cold-Start Verification Assessment** (confirming no undocumented assumptions exist).
6. **Live Pro Verification Checklist** (items requiring manual verification in an active ArcGIS Pro session).
