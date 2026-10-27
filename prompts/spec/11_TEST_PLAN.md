# CAD2GIS — Test Strategy & Automated Verification Plan (11_TEST_PLAN.md)

> **الملخص التنفيذي بالعربية:**
> تحدد هذه الوثيقة خطة الاختبار الشاملة والتحقق الآلي لنظام CAD2GIS. تشمل الوثيقة مصفوفة تتبع كاملة تربط كل متطلب وظيفي (REQ) بالاختبار البرمجي المقابل له في حزمة اختبارات الوحدة (Unit Tests)، وتصف النماذج الهندسية الوهمية (Synthetic Fixtures) ذات الإجابات الحسابية المعروفة، وتفصل بين الاختبارات المستقلة تماماً عن برمجيات إزري (Cad2Gis.Core - 240 اختباراً) وتلك التي تتطلب جلسة حية داخل ArcGIS Pro.

---

## 1. Test Architecture & Separation of Concerns

To guarantee maintainability and rapid continuous integration:
- **`Cad2Gis.Core` Test Suite:** 100% pure .NET 8 xUnit tests executing in under 3 seconds without requiring ArcGIS Pro installed, running, or licensed.
- **ArcGIS Pro Live Integration Tests:** End-to-end workflow validations requiring an active ArcGIS Pro session, live Geodatabase connections, and map layer rendering.

```
Total Automated Unit Tests: 240
Passing:                    240 (100%)
Failed:                     0
Skipped:                    0
Duration:                   ~2-3 seconds
```

---

## 2. Synthetic Test Fixtures (Fictional Datasets)

All automated tests MUST use purely synthetic, fictional datasets with mathematically exact pre-calculated answers. Testing MUST NEVER rely on client masterplans, proprietary drawings, or confidential survey coordinates.

### 2.1 Standard Fictional Parcel Geometry Fixtures

| Fixture ID | Geometric Shape | Dimensions & Coordinates | Known Mathematical Answers |
| :--- | :--- | :--- | :--- |
| `FIX-RECT-01` | Simple Rectangle | $(0,0) \rightarrow (40,0) \rightarrow (40,20) \rightarrow (0,20) \rightarrow (0,0)$ | Area = $800.00\text{ m}^2$, Perimeter = $120.00\text{ m}$, 4 right angles ($90^\circ$). |
| `FIX-TRAP-02` | Right Trapezoid | $(0,0) \rightarrow (50,0) \rightarrow (30,30) \rightarrow (0,30) \rightarrow (0,0)$ | Area = $1,200.00\text{ m}^2$, Perimeter = $146.06\text{ m}$, Angle at $(50,0)$ = $56.31^\circ$. |
| `FIX-ARC-03` | Parcel with True Arc | Rectangle with circular arc on East side ($R = 10.0\text{ m}$, Center $(30, 10)$) | Exact analytical arc length = $15.71\text{ m}$, Analytical area = $857.08\text{ m}^2$. |
| `FIX-CHAM-04` | Corner Chamfer | $30\times 30\text{ m}$ square with $5\text{ m}$ chamfer at corner $(30,30)$ | Segment length $= 7.07\text{ m}$, Chamfer angle $= 45^\circ$, Clustered into single segment. |
| `FIX-HOLE-05` | Donut Parcel | $50\times 50\text{ m}$ outer square with $10\times 10\text{ m}$ interior hole | Net Area = $2,400.00\text{ m}^2$, 2 independent rings, 8 cardinal boundary lines. |
| `FIX-TRI-06` | Triangle Parcel | $(0,0) \rightarrow (40,0) \rightarrow (20,30) \rightarrow (0,0)$ | Exactly 3 cardinal directions; flags `FEWER_THAN_4_DIRECTIONS`. |

---

## 3. Requirements Traceability Test Matrix

| Requirement ID | Specification Area | Automated Test Class | Test Method Name | Execution Environment |
| :--- | :--- | :--- | :--- | :---: |
| **`REQ-GEN-002`** | Pure Core Library | `PipelineIntegrationTests` | `VerifyCoreHasNoProDependencies` | Standalone (.NET 8) |
| **`REQ-GEN-005`** | Invariants INV-01..08 | `GeometryPreservationTests` | `StrictArcPreservation_NoDensification` | Standalone (.NET 8) |
| **`REQ-PIPE-002`**| Single Output Per Step | `WorkingDataSourceTests` | `EachStepProducesExactlyOneVerifiedOutput` | Standalone (.NET 8) |
| **`REQ-PIPE-003`**| Two Sources Only | `WorkingDataSourceTests` | `StepSourceEnum_HasExactlyTwoValues` | Standalone (.NET 8) |
| **`REQ-PIPE-005`**| Registry State | `PipelineStoppageAndDemoTests`| `RegistryPersistenceAndValidation` | Standalone (.NET 8) |
| **`REQ-PIPE-008`**| Edit Detection | `PerformanceAndReviewFixesTests`| `ContentStamp_DetectsManualModifications` | Standalone (.NET 8) |
| **`REQ-ST1-002`**| UTM Zones 35N-40N | `UtmZonesAndPlausibilityTests`| `ValidatesAllSaudiUtmZones` | Standalone (.NET 8) |
| **`REQ-ST1-005`**| Geometry Modes | `CadExtractionAndExpectedLayersTests`| `LayerGeometryModeService_ValidatesModes`| Standalone (.NET 8) |
| **`REQ-ST1-006`**| Dynamic Usage Types | `MunicipalLookupsTests` | `LoadsUsageTypesDynamicallyFromExcel` | Standalone (.NET 8) |
| **`REQ-ST1-007`**| Slash Preservation | `XArabDecoderAdvancedTests` | `PreservesSlashInNumericStrings` | Standalone (.NET 8) |
| **`REQ-ST2-004`**| Dominant Layer 60% | `CadExtractionAndExpectedLayersTests`| `DominantLayerMatcher_Applies60PercentRule`| Standalone (.NET 8) |
| **`REQ-ST2-005`**| Mixed Layers Flag | `CadExtractionAndExpectedLayersTests`| `DominantLayerMatcher_AssignsMixedLayers` | Standalone (.NET 8) |
| **`REQ-ST2-006`**| Text Joins & Conflict | `Step2AndStep3FixesTests` | `Part2_ThreeRectangles_AdheresToCadastral` | Standalone (.NET 8) |
| **`REQ-ST3-002`**| PLOT_ID from PLOT_TXT | `PlotIdFromPlotTextTests` | `Test1_ThreeParcels_AssignsPlotId` | Standalone (.NET 8) |
| **`REQ-ST3-003`**| Trimming & Slashes | `PlotIdFromPlotTextTests` | `Test2_TrimSurroundingSpacesAndKeepSlash` | Standalone (.NET 8) |
| **`REQ-ST3-004`**| Empty PLOT_TXT Untouched| `PlotIdFromPlotTextTests` | `Test1_ThreeParcels_AssignsPlotId` | Standalone (.NET 8) |
| **`REQ-ST3-005`**| Missing Field Resilience| `PlotIdFromPlotTextTests` | `Test5_ModeLineWithDataLayer_NoPlotTxtField`| Standalone (.NET 8) |
| **`REQ-ST3-006`**| Duplicate PLOT_ID Warn | `PlotIdFromPlotTextTests` | `Test3_DuplicatePlotTextValues_KeptAndReported`| Standalone (.NET 8) |
| **`REQ-ST3-007`**| USAGE Precedence | `PerformanceAndReviewFixesTests`| `UsagePrecedence_HonorsPriorityOrder` | Standalone (.NET 8) |
| **`REQ-ST3-008`**| Usage Whitelist | `BorderTypeAndDimensionTests` | `RejectsUsageCodesOutsideApprovedSheet` | Standalone (.NET 8) |
| **`REQ-ST3-009`**| Clean REMARKS | `PerformanceAndReviewFixesTests`| `RemovesProvenanceFromRemarks` | Standalone (.NET 8) |
| **`REQ-ST4-002`**| MAIN Parcels Only | `BorderExtractionMethodTests` | `DecomposesOnlyMainParcels` | Standalone (.NET 8) |
| **`REQ-ST4-004`**| Max 4 Cardinal Lines | `BorderExtractionMethodTests` | `RingsProduceAtMost4SinglePartLines` | Standalone (.NET 8) |
| **`REQ-ST4-006`**| Interior Angles 10°-160°| `BorderExtractionMethodTests` | `SplitPoints_CalculatedFromInteriorAngle` | Standalone (.NET 8) |
| **`REQ-ST4-008`**| Chamfer 7m Clustering | `BorderExtractionMethodTests` | `CornerClustering_GroupsVerticesBelow7m` | Standalone (.NET 8) |
| **`REQ-ST4-009`**| True North Correction | `BorderExtractionMethodTests` | `CorrectsAzimuthForMeridianConvergence` | Standalone (.NET 8) |
| **`REQ-ST4-011`**| Shared Segment >= 0.5m | `BorderTypeAndDimensionTests` | `SharedSegment_RequiresHalfMeterOverlap` | Standalone (.NET 8) |
| **`REQ-ST4-012`**| MAIN Neighbour = PLT | `BorderTypeAndDimensionTests` | `MainNeighbour_AssignsPLTDirectly` | Standalone (.NET 8) |
| **`REQ-ST4-014`**| Single Midpoint Ray | `WidthInferenceAdvancedTests`| `CastsSingleRayFromMidpoint` | Standalone (.NET 8) |
| **`REQ-ST4-015`**| Facing Parcel Gap >= 1m | `WidthInferenceAdvancedTests`| `IgnoresParcelsCloserThan1Meter` | Standalone (.NET 8) |
| **`REQ-ST4-017`**| Passage/Street Cutoff 10m| `WidthInferenceAdvancedTests`| `Applies10MeterCutoffForStreetVsPassage` | Standalone (.NET 8) |
| **`REQ-ST4-019`**| DIMENSION 15° Deflection| `BorderTypeAndDimensionTests` | `DeflectionAbove15Deg_AssignsMulti` | Standalone (.NET 8) |
| **`REQ-ST5-001`**| Deliverables Structure | `ExportNamingTests` | `GeneratesStandardFinalFolderHierarchy` | Standalone (.NET 8) |
| **`REQ-ST5-003`**| 1mm Densification Guard | `GeometryPreservationTests` | `ShapefileExport_DeviationWithin1Millimeter`| Standalone (.NET 8) |
| **`REQ-ST5-004`**| Frozen Metrics | `MetricPreservationAndToleranceTests`| `FreezesAnalyticalMetricsBeforeGcsExport`| Standalone (.NET 8) |
| **`REQ-ST5-005`**| UTF-8 CPG File | `LatestExportWorkflowTests` | `CreatesUtf8CpgFilesWithEveryShapefile` | Standalone (.NET 8) |
| **`REQ-ST5-008`**| QA Validation Engine | `QaValidationTests` | `ValidatesMandatoryBaladyRules` | Standalone (.NET 8) |
| **`REQ-SET-002`**| Dynamic Path Resolution| `HelperFilesManagerTests` | `ResolvesHelperFolderWithoutHardcodedPaths`| Standalone (.NET 8) |

---

## 4. Tests Requiring Live ArcGIS Pro Session

The following features involve native ArcGIS Pro runtime hosts and MUST be validated manually or via Pro UI integration tests:
1. **Ribbon & Dockpane Rendering:** Loading `Cad2Gis_Dockpane` via `Config.daml` in ArcGIS Pro 3.3.0.
2. **CADToGeodatabase Ingestion:** Native ESRI CAD driver importing complex Autodesk Civil 3D proxy entities.
3. **QueuedTask Threading Behavior:** Verifying UI responsiveness when processing large datasets ($> 5,000$ parcels).
4. **Active Map Layer Manipulation:** Automatic addition of `Cad_Constructed_Parcels`, `PARCEL_Approved_Schema`, and `BORDER_Cardinal_Approved` to the active TOC (`MapView.Active.Map`).
5. **ArcGIS Pro Native Edit Session Detection:** Detecting uncommitted map edits via `HasUnsavedEdits`.

---

## 5. Test Execution Protocol

```bash
# Execute full automated test suite from solution directory
cd Cad2Gis
dotnet test Cad2Gis.Tests --verbosity normal
```

**Acceptance Gate:**
- Total tests executed: $\ge 240$.
- Failures: $0$.
- Skipped: $0$.
- Execution time: $< 10$ seconds.
