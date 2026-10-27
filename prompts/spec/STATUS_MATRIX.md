# CAD2GIS Specification — Implementation Status & Gap Matrix (Backlog)

> **ملخص باللغة العربية (Arabic Summary):**  
> توثق هذه المصفوفة الشاملة الحالة البرمجية الراهنة لجميع متطلبات النظام (98 متطلباً) مقارنة بالكود المصدري الفعلي في مجلد العمل. تصنف المتطلبات إلى: مُنفذة بالكامل (`IMPLEMENTED`)، أو جزئية (`PARTIAL`)، أو غير مُنفذة (`NOT IMPLEMENTED`)، أو غير قابلة للتحقق إلا داخل بيئة تشغيل ArcGIS Pro الحية (`UNKNOWN`). تمثل الفجوات المحددة قائمة المهام التطويرية (Backlog) للتحديثات القادمة.

---

## 1. Summary Statistics by Functional Area

| Area | Area Code | Total REQs | IMPLEMENTED | PARTIAL | NOT IMPLEMENTED | UNKNOWN (Needs Pro) | Implementation Rate |
| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **General & Architecture** | `REQ-GEN` | 5 | 5 | 0 | 0 | 0 | 100% |
| **Pipeline & State Flow** | `REQ-PIPE` | 10 | 9 | 1 | 0 | 0 | 90% |
| **Step 1: CAD Ingestion** | `REQ-ST1` | 8 | 6 | 0 | 0 | 2 | 75% |
| **Step 2: Parcel Polygonization** | `REQ-ST2` | 9 | 8 | 1 | 0 | 0 | 89% |
| **Step 3: Attributes & Schema** | `REQ-ST3` | 10 | 10 | 0 | 0 | 0 | 100% |
| **Step 4: Cardinal Borders** | `REQ-ST4` | 20 | 19 | 1 | 0 | 0 | 95% |
| **Step 5: Export & QA** | `REQ-ST5` | 10 | 8 | 0 | 0 | 2 | 80% |
| **Data Dictionary & Lookups** | `REQ-DATA` | 8 | 8 | 0 | 0 | 0 | 100% |
| **Settings & Profiles** | `REQ-SET` | 6 | 6 | 0 | 0 | 0 | 100% |
| **User Interface (Dockpane)** | `REQ-UI` | 7 | 5 | 1 | 0 | 1 | 71% |
| **Build & Deployment** | `REQ-BLD` | 5 | 5 | 0 | 0 | 0 | 100% |
| **TOTAL** | — | **98** | **89** | **4** | **0** | **5** | **90.8%** |

---

## 2. Detailed Requirement Status Matrix

### Area: General Architecture (`REQ-GEN`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-GEN-001` | C# Add-in for Pro 3.3.0 on .NET 8.0 SDK | `IMPLEMENTED` | `Cad2Gis.Core.csproj`, `Cad2Gis.AddIn.csproj` | Fully targeting `net8.0` and `net8.0-windows`. |
| `REQ-GEN-002` | Isolated Core library (`Cad2Gis.Core`) | `IMPLEMENTED` | `Cad2Gis.Core/` (Zero Esri references) | Verified pure C# algorithms. |
| `REQ-GEN-003` | Standalone ArcPy Python Toolbox fallback | `IMPLEMENTED` | `HelperFiles/Cad2Gis_ArcPy.pyt` | Fallback tools defined and operational. |
| `REQ-GEN-004` | No silent background execution without user trigger | `IMPLEMENTED` | `Cad2GisDockpaneViewModel.cs` | All execution binds to explicit button commands. |
| `REQ-GEN-005` | Geometry preservation: True arcs remain arcs | `IMPLEMENTED` | `Cad2Gis.Core/Geometry/` | Circular arc definitions preserved in internal data structures. |

---

### Area: Pipeline & State Flow (`REQ-PIPE`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-PIPE-001` | 5 discrete sequential pipeline steps | `IMPLEMENTED` | `Cad2GisDockpaneViewModel.cs` | 5 tabs with sequential execution barriers. |
| `REQ-PIPE-002` | Strictly two source options per step (D02) | `IMPLEMENTED` | `SourceSelectionViewModel.cs`, Dockpane XAML | Previous Step vs Own Data cards implemented. |
| `REQ-PIPE-003` | Exactly one primary output per step (D03) | `IMPLEMENTED` | `GdbFeatureClassService.cs` | Standardized feature class names enforced. |
| `REQ-PIPE-004` | Registry state tracking (`pipeline_state.json`) | `IMPLEMENTED` | `PipelineStateManager.cs` | JSON state persisted on step completion. |
| `REQ-PIPE-005` | 10 working fields lifecycle & cleanup | `IMPLEMENTED` | `WorkingFieldService.cs` | Fields created in Step 2, purged in Step 3. |
| `REQ-PIPE-006` | Single authoritative copy on disk | `IMPLEMENTED` | `GdbFeatureClassService.cs` | Overwrites working feature class in place; no duplicate exports. |
| `REQ-PIPE-007` | Change detection via SHA-256 content stamp | `IMPLEMENTED` | `PipelineStateManager.cs` | Content hash computed from attributes & geometry. |
| `REQ-PIPE-008` | Explicit failure behavior (No silent fallback) | `IMPLEMENTED` | `Cad2GisDockpaneViewModel.Step*.cs` | Error dialogs and log warnings on failure; no fake success. |
| `REQ-PIPE-009` | Thread-safe anti-freeze architecture | `IMPLEMENTED` | `Cad2GisDockpaneViewModel.Step*.cs` | Pro SDK calls dispatched via `QueuedTask.Run`. |
| `REQ-PIPE-010` | Step naming harmonization (UI vs Code) | `PARTIAL` | `Cad2GisDockpaneViewModel.Step3.cs` | Internal method `ExecuteStep3Async` performs Step 4 border logic; needs clean internal method rename to match UI numbering. |

---

### Area: Step 1 CAD Ingestion (`REQ-ST1`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-ST1-001` | CAD ingestion into `Cad_Dataset` | `UNKNOWN` | `Cad2GisDockpaneViewModel.Step1.cs` | Requires live ArcGIS Pro Geoprocessing to run CAD-to-GDB conversion on actual DWG. |
| `REQ-ST1-002` | Mandatory projected UTM zones 35N–40N | `IMPLEMENTED` | `CoordinateSystemService.cs` | Validates EPSG 32635–32640 and warns on unprojected CAD. |
| `REQ-ST1-003` | 4 layer category tabs in UI | `IMPLEMENTED` | `Cad2GisDockpane.xaml`, `Step1ViewModel.cs` | Main, Sub, Text, Ignore category collections. |
| `REQ-ST1-004` | Street/Road linework classified in `[Sub-Parcels]` | `IMPLEMENTED` | `LayerCategorizationService.cs` | Default rules route road keywords to Sub-Parcels. |
| `REQ-ST1-005` | 3 geometry modes (Line, Polygon, Line with data layer) | `IMPLEMENTED` | `Step1ViewModel.cs`, `Cad2GisDockpane.xaml` | Dropdown binds 3 explicit modes. |
| `REQ-ST1-006` | Refreshable per-layer USAGE dropdown from Excel | `IMPLEMENTED` | `Cad2GisDockpaneViewModel.Step1.cs` | Populates from `Usage Types` sheet with Refresh command. |
| `REQ-ST1-007` | xArab font reverse-byte decoding with slash guard | `IMPLEMENTED` | `ArabicTextDecoder.cs` | Regex preserves digit slashes (`11/3314`). |
| `REQ-ST1-008` | `Expected_Layers.xlsx` & `Layers_Used.xlsx` handling | `UNKNOWN` | `ExpectedLayersService.cs` | Requires live CAD ingestion run to verify Excel creation on disk. |

---

### Area: Step 2 Parcel Polygonization (`REQ-ST2`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-ST2-001` | Line mode combined linework polygonization | `IMPLEMENTED` | `GeometryEngine2D.cs` | Planar polygonization from polyline segments. |
| `REQ-ST2-002` | Polygon mode boundary cleaning & ingestion | `IMPLEMENTED` | `GeometryEngine2D.cs` | Direct polygon topological ingestion. |
| `REQ-ST2-003` | Line with data layer mode (Split line, 60% dominant) | `IMPLEMENTED` | `LineWithDataLayerEngine.cs` | Split lines, $0.05\text{ m}$ buffer, dominant attribution. |
| `REQ-ST2-004` | Spatial text join: point containment & nearest fallback | `IMPLEMENTED` | `SpatialTextJoinEngine.cs` | Contains first, then nearest within search radius. |
| `REQ-ST2-005` | Conflict detection: `MIXED_LABELS` & `MIXED_LAYERS` | `IMPLEMENTED` | `SpatialTextJoinEngine.cs`, `LineWithDataLayerEngine.cs` | Flags set when multiple distinct annotations or split ratios occur. |
| `REQ-ST2-006` | Provisional parcel identification (`P{ID}`) | `IMPLEMENTED` | `SpatialTextJoinEngine.cs` | Generates unique sequential `P1, P2...` when text missing. |
| `REQ-ST2-007` | Population of all 10 working fields | `IMPLEMENTED` | `WorkingFieldService.cs` | Populates `_CLASS, _LAYER, _PLOT_TXT, _USAGE_TXT, etc.` |
| `REQ-ST2-008` | `text_join_report.txt` generation | `IMPLEMENTED` | `TextJoinReportService.cs` | Diagnostic text file written to working directory. |
| `REQ-ST2-009` | Missing annotation notification in UI | `PARTIAL` | `Cad2GisDockpaneViewModel.Step2.cs` | Logged to console; modal banner alert could be enhanced. |

---

### Area: Step 3 Attributes & Schema (`REQ-ST3`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-ST3-001` | Ingest `PLOT_ID` from `PLOT_TXT` (Line with data layer) | `IMPLEMENTED` | `Cad2GisDockpaneViewModel.Step3.cs` | Trims, strips invalid characters, preserves digit slashes. |
| `REQ-ST3-002` | 4-tier `USAGE` precedence resolver | `IMPLEMENTED` | `UsagePrecedenceResolver.cs` | Manual > Excel > Layer USAGE > Fallback. |
| `REQ-ST3-003` | Strict official land-use codes (`RSD, COM, RSC, AGR, IND, UTL`) | `IMPLEMENTED` | `UsagePrecedenceResolver.cs` | Rejects `PLT` or unauthorized codes. |
| `REQ-ST3-004` | `REMARKS` generation rules (No PlotId, no provenance) | `IMPLEMENTED` | `RemarksGenerator.cs` | Usage text > USAGE > Empty. Provenance text excluded. |
| `REQ-ST3-005` | Admin jurisdiction spatial overlay | `IMPLEMENTED` | `AdminAreaDetector.cs` | Point-in-polygon overlay with administrative boundaries. |
| `REQ-ST3-006` | Missing-data policy (Leave empty; no "UNKNOWN") | `IMPLEMENTED` | `MissingDataPolicy.cs` | Enforces null/empty on unassigned fields. |
| `REQ-ST3-007` | Template-driven 26-field schema enforcement | `IMPLEMENTED` | `TemplateSchemaEnforcer.cs` | Enforces exact field names and types from template. |
| `REQ-ST3-008` | Purge all 10 working fields from `PARCEL_Approved_Schema` | `IMPLEMENTED` | `WorkingFieldService.cs` | Working fields stripped prior to final feature class write. |
| `REQ-ST3-009` | Trace log generation (`Step3_Trace_Log.txt`) | `IMPLEMENTED` | `Step3Tracer.cs` | Per-parcel resolution tracing written to disk. |
| `REQ-ST3-010` | Manual edit protection via content hash check | `IMPLEMENTED` | `Cad2GisDockpaneViewModel.Step3.cs` | Protects manual attribute overrides on re-execution. |

---

### Area: Step 4 Cardinal Borders (`REQ-ST4`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-ST4-001` | Border decomposition on MAIN parcels only | `IMPLEMENTED` | `BorderExtractionEngine.cs` | SUB parcels skipped during decomposition. |
| `REQ-ST4-002` | Independent ring and hole processing | `IMPLEMENTED` | `BorderExtractionEngine.cs` | Holes evaluated separately; ring-level direction assignment. |
| `REQ-ST4-003` | Max 4 single-part lines per ring (No multipart) | `IMPLEMENTED` | `BorderExtractionEngine.cs` | Merges into 1 North, 2 East, 3 South, 4 West. |
| `REQ-ST4-004` | Interior angle split $[10^\circ, 160^\circ]$ & $[200^\circ, 350^\circ]$ | `IMPLEMENTED` | `CornerDetector.cs` | Angular split points detected with reflex angle mirror. |
| `REQ-ST4-005` | Arc virtual chord evaluation (True curves unsegmented) | `IMPLEMENTED` | `CornerDetector.cs` | True arcs treated as single continuous curve entity. |
| `REQ-ST4-006` | $7\text{ m}$ corner cluster chamfer merging | `IMPLEMENTED` | `CornerClusterEngine.cs` | Merges chamfered corner lines into adjacent facades. |
| `REQ-ST4-007` | Outward normal & true north meridian correction | `IMPLEMENTED` | `NormalVectorEngine.cs` | Corrects grid azimuth for central meridian convergence. |
| `REQ-ST4-008` | Facade merge Rule 1 & Rule 2 | `IMPLEMENTED` | `FacadeMergeEngine.cs` | Merges collinear adjacent segments into cardinal facades. |
| `REQ-ST4-009` | Zero geometry deformation during border creation | `IMPLEMENTED` | `GeometryEngine2D.cs` | No generalization or unconstrained vertex insertion. |
| `REQ-ST4-010` | Shared segment detection ($0.05\text{ m}$ buffer, $\ge 0.5\text{ m}$) | `IMPLEMENTED` | `SharedSegmentAnalyzer.cs` | Identifies coincident parcel boundaries. |
| `REQ-ST4-011` | Neighbor classification: MAIN neighbor $\rightarrow$ `PLT` | `IMPLEMENTED` | `BorderTypeResolver.cs` | Assigns `PLT` for common parcel boundary. |
| `REQ-ST4-012` | Neighbor classification: SUB neighbor keyword lookup | `IMPLEMENTED` | `BorderTypeResolver.cs` | Looks up SUB usage/remarks in `BorderType_Keywords.csv`. |
| `REQ-ST4-013` | Multiple neighbors: Longest type + combined list | `IMPLEMENTED` | `BorderTypeResolver.cs` | Assigns type of longest neighbor; lists all in description. |
| `REQ-ST4-014` | Description text formatting (`DESCRIPTO`) | `IMPLEMENTED` | `DescriptionFormatter.cs` | Formats neighbor and street descriptions. |
| `REQ-ST4-015` | Single ray midpoint street width sensing | `IMPLEMENTED` | `StreetWidthSensor.cs` | Casts perpendicular ray; measures facing gap $1.0\text{ m}$–$100.0\text{ m}$. |
| `REQ-ST4-016` | Record 3 width values (Measured, Rounded, Suggested) | `IMPLEMENTED` | `StreetWidthSensor.cs` | Records all 3 metrics; sets `INFERRED` flag. |
| `REQ-ST4-017` | Passage vs. Street cutoff ($10.0\text{ m}$) | `IMPLEMENTED` | `StreetWidthSensor.cs` | Classifies $< 10.0\text{ m}$ as passage (`PAS`), $\ge 10.0\text{ m}$ as street (`ST`). |
| `REQ-ST4-018` | `DIMENSION` attribute ($15^\circ$ deflection threshold) | `IMPLEMENTED` | `DimensionEvaluator.cs` | Assigns `Single` vs `Multi` based on $15^\circ$ test. |
| `REQ-ST4-019` | DBF UTF-8 byte truncation validation | `IMPLEMENTED` | `DescriptionFormatter.cs` | Validates text length against 254-byte DBF limit. |
| `REQ-ST4-020` | Spatial index performance optimization | `PARTIAL` | `BorderExtractionEngine.cs` | Envelope index implemented; obstacle candidate caching can be further tuned for 10k+ parcels. |

---

### Area: Step 5 Export & QA (`REQ-ST5`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-ST5-001` | Export `Final.gdb` with native true arcs | `IMPLEMENTED` | `ExportPackagingService.cs` | Copies PARCEL and BORDER to File Geodatabase. |
| `REQ-ST5-002` | Export shapefiles with controlled densification ($\le 0.001\text{ m}$) | `IMPLEMENTED` | `ExportPackagingService.cs` | Exports Shapefile with arc densification limit. |
| `REQ-ST5-003` | Mandatory UTF-8 `.cpg` file generation | `IMPLEMENTED` | `ExportPackagingService.cs` | Writes `UTF-8` code page file for every shapefile. |
| `REQ-ST5-004` | Add 4 final deliverables to active map | `UNKNOWN` | `Cad2GisDockpaneViewModel.Step5.cs` | Requires live ArcGIS Pro session with active MapView. |
| `REQ-ST5-005` | Freeze metric attributes before GCS EPSG:4326 export | `IMPLEMENTED` | `GcsExportService.cs` | Area and length metrics frozen before coordinate transformation. |
| `REQ-ST5-006` | DBF byte-length validation prior to shapefile write | `IMPLEMENTED` | `DbfByteValidator.cs` | Checks multi-byte string capacity. |
| `REQ-ST5-007` | Comprehensive QA validation engine (8 rules) | `IMPLEMENTED` | `QaValidationEngine.cs` | Validates geometry, schema, mandatory fields, ranges. |
| `REQ-ST5-008` | Bilingual QA audit reports (`.json` and `.txt`) | `IMPLEMENTED` | `QaReportWriter.cs` | Outputs structured JSON and formatted audit text. |
| `REQ-ST5-009` | Registry commit only upon successful QA | `IMPLEMENTED` | `PipelineStateManager.cs` | Commits state hash only after zero fatal errors. |
| `REQ-ST5-010` | Delivery directory structure integrity | `IMPLEMENTED` | `ExportPackagingService.cs` | Creates `Final/Final.gdb`, `Final/Shape/`, `Final/Reports/`. |

---

### Area: Data Dictionary & Lookups (`REQ-DATA`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-DATA-001` | Complete MOMRAH 26-field parcel schema definition | `IMPLEMENTED` | `Required Fields in the Shapefile.xlsx` | Tabular schema verified. |
| `REQ-DATA-002` | Complete MOMRAH 10-field border schema definition | `IMPLEMENTED` | `Required Fields in the Shapefile.xlsx` | Tabular schema verified (`DESCRIPTO`). |
| `REQ-DATA-003` | 10 internal working fields specification | `IMPLEMENTED` | `WorkingFieldService.cs` | `_CLASS, _LAYER, _PLOT_TXT, etc.` |
| `REQ-DATA-004` | Dynamic reading of `Usage Types` sheet | `IMPLEMENTED` | `ClosedXmlService.cs` | Reads 6 official codes at runtime. |
| `REQ-DATA-005` | Dynamic reading of `Border Types` sheet | `IMPLEMENTED` | `ClosedXmlService.cs` | Reads 67 official codes at runtime. |
| `REQ-DATA-006` | Dynamic reading of `Building Types` sheet | `IMPLEMENTED` | `ClosedXmlService.cs` | Reads building codes at runtime. |
| `REQ-DATA-007` | External keyword lookup tables (`.csv`) | `IMPLEMENTED` | `LookupService.cs` | Loads `BorderType_Keywords.csv` and `Usage_Keywords.csv`. |
| `REQ-DATA-008` | UTM zones 35N–40N meridian guidance | `IMPLEMENTED` | `CoordinateSystemService.cs` | Maps zones to central meridians and administrative regions. |

---

### Area: Settings & Profiles (`REQ-SET`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-SET-001` | Centralized parameter validation in `settings.json` | `IMPLEMENTED` | `SettingsManager.cs` | Validates ranges and units on load. |
| `REQ-SET-002` | Multiple consultant profiles support | `IMPLEMENTED` | `ProfileManager.cs` | Loads and switches `.json` profiles. |
| `REQ-SET-003` | Profile backward-compatibility migration | `IMPLEMENTED` | `ProfileManager.cs` | Fills missing keys with defaults without crashing. |
| `REQ-SET-004` | Dynamic `HelperFiles` discovery hierarchy | `IMPLEMENTED` | `HelperFilesDiscoveryService.cs` | 3-tier lookup: Project $\rightarrow$ Profile $\rightarrow$ Install. |
| `REQ-SET-005` | Self-healing bootstrap of missing helper files | `IMPLEMENTED` | `HelperFilesDiscoveryService.cs` | Copies embedded defaults if directory missing. |
| `REQ-SET-006` | Transactional settings persistence | `IMPLEMENTED` | `SettingsManager.cs` | Atomic write via temporary file prevents corruption. |

---

### Area: User Interface (`REQ-UI`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-UI-001` | ArcGIS Pro Dockpane layout (450px, dark theme) | `IMPLEMENTED` | `Cad2GisDockpane.xaml` | Structured WPF Dockpane layout. |
| `REQ-UI-002` | Tabbed navigation for 5 pipeline steps | `IMPLEMENTED` | `Cad2GisDockpane.xaml` | TabControl with step headers and icons. |
| `REQ-UI-003` | Working data source selector cards (2 options) | `IMPLEMENTED` | `Cad2GisDockpane.xaml` | Visual RadioButton cards for source options. |
| `REQ-UI-004` | Review DataGrids with manual edit capabilities | `IMPLEMENTED` | `Cad2GisDockpane.xaml` | DataGrids bound to step preview collections. |
| `REQ-UI-005` | Throttled live progress console | `IMPLEMENTED` | `LogConsoleViewModel.cs` | Throttled batch dispatch prevents UI stutter. |
| `REQ-UI-006` | Bilingual message catalog (Arabic/English) | `IMPLEMENTED` | `LocalizationService.cs` | Keyed bilingual message dictionary. |
| `REQ-UI-007` | Seamless ArcGIS Pro theme integration (Dark/Light) | `UNKNOWN` | `Cad2GisDockpane.xaml` | Uses Pro brush resources; requires visual validation across Pro theme toggle. |

---

### Area: Build & Deployment (`REQ-BLD`)
| Requirement ID | Specification | Status | Verified Code Location | Gap / Technical Debt Description |
| :--- | :--- | :---: | :--- | :--- |
| `REQ-BLD-001` | Target framework .NET 8.0 for all projects | `IMPLEMENTED` | `Cad2Gis.*.csproj` | Confirmed `net8.0` and `net8.0-windows`. |
| `REQ-BLD-002` | Automated PowerShell packaging script (`package_addin.ps1`) | `IMPLEMENTED` | `package_addin.ps1` | Assembles valid `.esriAddinX` archive. |
| `REQ-BLD-003` | Prohibit silent auto-installation (INV-04) | `IMPLEMENTED` | `package_addin.ps1` | Packages archive for manual double-click install. |
| `REQ-BLD-004` | Complete isolation of Core library from Pro SDK | `IMPLEMENTED` | `Cad2Gis.Core.csproj` | Zero references to `ArcGIS.Desktop.*` or `ArcGIS.Core`. |
| `REQ-BLD-005` | Bundle `HelperFiles` seed copy into package | `IMPLEMENTED` | `package_addin.ps1` | Copies helper templates into staging archive. |
