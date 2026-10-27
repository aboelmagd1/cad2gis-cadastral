# CAD2GIS — Settings, Profiles & Bootstrap Specification (09_SETTINGS_AND_PROFILES.md)

> **الملخص التنفيذي بالعربية:**
> تحدد هذه الوثيقة مواصفات إعدادات النظام وتفضيلات محطة العمل المخزنة في (`settings.json`) ونموذج ملفات التوصيف المخصصة للمكاتب الاستشارية (Consultant Profiles). وتفصّل هيكلية مجلد المساعدة (`HelperFiles/`) وقواعد التهيئة التلقائية (Bootstrapping) عند أول تشغيل، وآلية التوافق العكسي مع الملفات القديمة، ومنع تثبيت أي مسارات برمجية ثابتة خاصة بمطوري النظام.

---

## 1. Workstation Global Settings (`HelperFiles/settings.json`)

Global preferences for the ArcGIS Pro workstation are persisted in `HelperFiles/settings.json`.

### 1.1 Complete Settings Dictionary

| Parameter Name | Data Type | Default | Unit | Valid Range | Description |
| :--- | :---: | :---: | :---: | :---: | :--- |
| `helperFolderPath` | String | `""` | Path | Valid directory | Custom override for `HelperFiles` root path (empty = auto-locate). |
| `sharedProfilesFolderPath` | String | `""` | Path | Valid directory | Network/shared directory for consultant profiles. |
| `uiLanguage` | String | `"ar"` | Code | `"ar"`, `"en"` | Primary language for Dockpane labels, tooltips, and console. |
| `lastUsedProfile` | String | `"Default"`| Name | Existing profile | Profile automatically loaded at application startup. |
| `lastOutputFolder` | String | `""` | Path | Valid directory | Most recently used delivery output folder. |
| `defaultUtmZone` | Int32 | `32637` | WKID | 32635–32640 | Default projected coordinate system (32637 = Zone 37N). |
| `defaultClosureToleranceMeters` | Double | `0.05` | Meters | 0.001 – 0.50 | Geometric closure tolerance for line polygonization. |
| `defaultPassageStreetThresholdMeters`| Double | `10.0` | Meters | 3.0 – 30.0 | Cutoff width separating Passage (`LNE`) from Street (`STR`). |
| `defaultMaxRayDistanceMeters` | Double | `100.0` | Meters | 20.0 – 500.0 | Maximum search distance for facing street width rays. |
| `defaultRayCount` | Int32 | `1` | Count | 1 or 3 | Street width ray mode: `1` = Midpoint ray, `3` = 3-ray median. |
| `areaToleranceSqm` | Double | `0.5` | $\text{m}^2$| 0.01 – 10.0 | Absolute area tolerance when comparing CAD with Excel. |
| `areaTolerancePercent` | Double | `0.1` | % | 0.01 – 5.0 | Percentage area tolerance when comparing CAD with Excel. |
| `chamferThresholdMeters` | Double | `7.0` | Meters | 1.0 – 20.0 | Maximum segment length for corner chamfer clustering ($L$). |
| `chamferAngleDeg` | Double | `45.0` | Degrees| 15.0 – 75.0 | Nominal chamfer diagonal deflection angle. |
| `chamferAngleToleranceDeg` | Double | `15.0` | Degrees| 5.0 – 30.0 | Angular tolerance for detecting chamfer segments. |
| `runAllStepsMode` | Boolean | `true` | Flag | `true`, `false` | Enables single-click sequential execution of Steps 1 to 5. |
| `pauseAtReviewTables` | Boolean | `true` | Flag | `true`, `false` | Pauses pipeline after Step 2 and Step 4 to allow manual grid edits. |
| `logRetentionDays` | Int32 | `30` | Days | 1 – 365 | Automatic purge threshold for old diagnostic log files. |
| `aMinDeg` | Double | `10.0` | Degrees| 5.0 – 45.0 | Minimum interior angle for corner split point ($A_{\min}$). |
| `aMaxDeg` | Double | `160.0` | Degrees| 135.0 – 175.0 | Maximum interior angle for corner split point ($A_{\max}$). |
| `multiSegmentAngleDeviationDeg`| Double | `15.0` | Degrees| 5.0 – 45.0 | Deflection threshold for `DIMENSION = "Multi"`. |
| `includeReflexCorners` | Boolean | `true` | Flag | `true`, `false` | Enables split points at reflex corners ($[200^\circ, 350^\circ]$). |
| `arcCenterToleranceMeters` | Double | `0.05` | Meters | 0.001 – 0.20 | Tolerance for matching center of concentric arcs. |
| `arcRadiusToleranceMeters` | Double | `0.05` | Meters | 0.001 – 0.20 | Tolerance for matching radius of concentric arcs. |
| `arcTangentToleranceDeg` | Double | `2.0` | Degrees| 0.1 – 10.0 | Tangent alignment tolerance for smooth arc transitions. |
| `clusterLengthMeters` | Double | `7.0` | Meters | 1.0 – 20.0 | Cluster grouping length for consecutive corner vertices. |
| `rule1LengthRatioGuard` | Double | `1.0` | Ratio | 0.1 – 5.0 | Length ratio threshold for boundary merge Rule 1. |
| `trueNorthCorrection` | Boolean | `true` | Flag | `true`, `false` | Applies meridian convergence correction to boundary azimuths. |

---

## 2. Consultant Profile Schema (`ConsultantProfileModel`)

Consultant profiles enable engineering firms to store layer-naming conventions, layer-to-usage assignments, and geometric tolerances per client or municipality:
- Stored as `.json` or `.xlsx` files in `HelperFiles/profiles/`.
- Managed via `ProfileExcelService`.

### 2.1 Profile JSON Structure

```json
{
  "profileName": "Amanat_Riyadh_Standard",
  "description": "Standard layer mapping and tolerances for Riyadh Municipality layouts",
  "version": "4.4",
  "createdAt": "2026-10-20T08:00:00Z",
  "geometryMode": "Line with data layer (خطوط مع طبقة البيانات)",
  "selectedPlotTextLayer": "PLOT_NUMBERS",
  "selectedUsageTextLayer": "LAND_USE_TEXT",
  "mainLayers": ["PARCEL_RESIDENTIAL", "PARCEL_COMMERCIAL", "LOT_LINES"],
  "subLayers": ["UTILITY_ELEC", "UTILITY_MOSQUE", "GARDEN_PARK", "ROAD_NETWORK"],
  "plotTextLayers": ["PLOT_NUMBERS", "PARCEL_ID"],
  "usageTextLayers": ["LAND_USE_TEXT", "REMARKS_TEXT"],
  "ignoreLayers": ["DIMS", "HATCH_CONCRETE", "BORDER_SHEET"],
  "layerUsageMappings": {
    "PARCEL_RESIDENTIAL": "RSD",
    "PARCEL_COMMERCIAL": "COM",
    "UTILITY_ELEC": "UTL",
    "UTILITY_MOSQUE": "UTL",
    "GARDEN_PARK": "UTL"
  },
  "closureToleranceMeters": 0.05,
  "passageStreetThresholdMeters": 10.0,
  "maxRayDistanceMeters": 100.0,
  "rayCount": 1,
  "aMinDeg": 10.0,
  "aMaxDeg": 160.0,
  "multiSegmentAngleDeviationDeg": 15.0,
  "includeReflexCorners": true
}
```

### 2.2 Backward Compatibility Invariants
When deserializing profiles created by earlier software versions:
1. **Legacy `ContinueFromLatestExport`:** Silently ignored. Does not throw an exception; the property is discarded.
2. **Legacy Geometry Modes:**
   - `"Both (مضلعات وخطوط معاً)"` $\rightarrow$ normalized to `"Line with data layer (خطوط مع طبقة البيانات)"`.
   - `"Polygon line"` $\rightarrow$ normalized to `"Line with data layer (خطوط مع طبقة البيانات)"`.
   - `"Lines"` $\rightarrow$ normalized to `"Line (خطوط فقط)"`.
   - `"Polygon"` $\rightarrow$ normalized to `"Polygon (مضلعات فقط)"`.

---

## 3. `HelperFiles` Hierarchy & Auto-Bootstrapping Rules

To guarantee zero-configuration deployment across new developer or surveyor workstations, `HelperFilesManager` implements an automated initialization and self-healing sequence.

### 3.1 Folder Resolution Sequence
`HelperFilesManager.ResolveHelperFilesDirectory()` locates the active helper folder using this priority:
1. Path specified in `settings.json` (`helperFolderPath`).
2. Directory named `HelperFiles` located adjacent to the active ArcGIS Pro project (`Project.Current.HomeFolderPath\HelperFiles`).
3. Directory named `HelperFiles` located in the Add-in assembly install root (`AppDomain.CurrentDomain.BaseDirectory\HelperFiles`).
4. Workspace root directory (`d:\workking on\shafik\create tool 02\HelperFiles`).

### 3.2 Auto-Bootstrapping Engine
If `HelperFiles/` or any of its mandatory sub-folders are missing at runtime:
1. **Directory Tree Creation:**
   Automatically creates:
   - `HelperFiles/state/`
   - `HelperFiles/logs/`
   - `HelperFiles/lookups/`
   - `HelperFiles/profiles/`
   - `HelperFiles/templates/`
   - `HelperFiles/adminareas/`
   - `HelperFiles/expected_layers/`
2. **Default State Files:**
   - Creates blank `state/pipeline_state.json` initialized with `{"version": "1.0", "steps": {}}`.
   - Creates `settings.json` with standard parameters from Section 1.1.
3. **Lookup Extraction:**
   - Extracts default embedded `BORDERTYPE.json`, `BORDERNAME.json`, `USAGE.json`, `BUILDINGTY.json`, `standard_widths.json`, `BorderType_Keywords.csv`, and `Usage_Keywords.csv` from Add-in assembly resources if missing.
4. **No Hard-Coded Developer Paths:**
   - Source code MUST NEVER contain hard-coded local absolute paths (e.g. `C:\Users\username\...` or `D:\workking on\...`). All paths MUST resolve dynamically via `_helperManager`.

---

## 4. Functional Requirements: Settings & Profiles

- **`REQ-SET-001` (Settings Persistence):** Global settings MUST be persisted to `HelperFiles/settings.json`.
- **`REQ-SET-002` (Dynamic Path Resolution):** `HelperFilesManager` MUST resolve directories dynamically without hardcoded machine paths.
- **`REQ-SET-003` (Self-Healing Bootstrapping):** Missing helper folders and lookup files MUST be automatically created at runtime.
- **`REQ-SET-004` (Profile Schema):** Profiles MUST store layer classifications, layer usage mappings, geometry mode, and angular tolerances.
- **`REQ-SET-005` (Backward Compatibility):** Deserializers MUST ignore legacy `ContinueFromLatestExport` and normalize legacy mode strings.
- **`REQ-SET-006` (Log Retention Purge):** Trace logs older than `logRetentionDays` (default 30) MUST be automatically deleted during initialization.
