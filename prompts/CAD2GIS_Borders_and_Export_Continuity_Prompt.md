# Prompt: CAD2GIS Borders and Export Continuity (Production v4.4)

Apply this specification to the ArcGIS Pro 3.3.0 Add-in (.NET 8). The approved specification files and immutable invariants in [CHANGELOG_MODIFICATIONS.md](CHANGELOG_MODIFICATIONS.md) and [promt 01010.md](promt 01010.md) are the primary sources of truth; report any conflict. Inspect existing code before modifying.

---

## Part A — Border Extraction from MAIN Parcel Boundaries (Step 4)

### A1. Hard Constraints & Core Invariants
- **Input:** **MAIN parcel polygons only** (`PARCEL_TYP = 1` / internal class `MAIN`), operating in planar UTM coordinates. Directly read the dataset resulting from Step 3 without repeating CAD conversion or recreating earlier datasets.
- Convert each polygon's boundary ring(s) to lines and work **strictly on those boundary lines**.
- **Never create artificial lines.** The only permissible geometric operations are **split** and **merge** of the original boundary components.
- **Strict Curve Preservation (INV-01):** True curves (circular arcs, Bézier curves) must remain true curves in the output (`EllipticArcSegment`). No densification, generalization, smoothing, or chord straightening is permitted on stored output geometries.
- **Planar 2D Enforcement (INV-05):** All output geometries must have `HasZ = false` and `HasM = false` to guarantee compatibility with Saudi MOMRAH / Balady standards.
- **Holes (Interior Rings):** Every ring of the polygon — the exterior ring and each interior ring — is processed independently as a separate topological unit with rules A2–A4. Hole boundaries are never merged across rings.
- **Output Structure Per Ring:** At most **4 single-part polylines**, exactly one per cardinal direction (`BORDERNAME` 1 = North, 2 = East, 3 = South, 4 = West). No line is ever multipart.

### A2. Step 1 — Corner (Split) Points by Angle
- **Angle Calculation:** At each vertex, calculate the **interior angle measured from inside the parcel** between the incoming and outgoing segments.
- **Split Interval:** A vertex is treated as a split point when the interior angle falls within **$[A_{\min}, A_{\max}]$**, default **10°–160°** (configurable in profile and Dockpane UI).
- **Convex Corners:** Have interior angles below 180° ($< 180^\circ$).
- **Reflex (Concave) Corners:** Have interior angles above 180° ($> 180^\circ$); vertices with interior angles in the mirrored range $[360^\circ - A_{\max}, 360^\circ - A_{\min}]$ (default 200°–350°) are also treated as split points.
- **Arcs / Curves & Virtual Chords:** Replace each arc temporarily with a virtual straight chord segment strictly for the angular calculation. The real arc geometry remains untouched.
- **Arc-to-Arc Joints:**
  1. *Same circle* (same center, radius, and orientation) $\rightarrow$ redundant vertex, **no split**.
  2. *Different circles, tangent-continuous* (tangents align within angular tolerance) $\rightarrow$ smooth transition, **no split**.
  3. *Tangent break* $\rightarrow$ evaluate with virtual chord angle test.
- **Corner Clusters (Chamfers / "Shotar" & Fillets):** Consecutive vertices closer than $L$ (default 7 m) are evaluated as a unified cluster. If their cumulative turning angle meets corner criteria, split at both cluster extremities so the chamfer forms its own segment.

### A3. Step 2 — Direction of Each Piece (Outward Normal)
- Direction is defined by the azimuth of the piece's outward normal pointing away from the parcel interior (pointing into holes for interior rings).
- **True North Correction:** Correct UTM grid azimuth by the meridian convergence angle computed at the parcel centroid.
- **Cardinal Assignment:** Assign each piece to the nearest cardinal direction (North, East, South, West). Near 45° ties are resolved deterministically to keep opposite facades balanced.

### A4. Step 3 — Merge Into Single-Part Lines & Dimension Definition (Multi vs Single)
- Group contiguous adjacent pieces having the same direction around the cyclic sequence.
- **Rule 1 (Absorb Short Intermediate Run):** If two runs of direction $X$ are separated by a shorter run of different direction $Y$, absorb $Y$ into $X$, provided $Y$ maintains at least one other distinct run elsewhere.
- **Rule 2 (Longest Wins):** If Rule 1 does not apply, the longest run of direction $X$ retains direction $X$. Shorter runs of $X$ merge into their adjacent neighbors.
- **Multi vs Single Border Parameter:** After merging borders, define whether a border is "Multi" or "Single" based on the angle between consecutive segments: it is NOT one segment ("Multi") if the angle between consecutive segments deviates from 180° by more than **`MultiSegmentAngleDeviationDeg`** (default **10°**, i.e. angle $< 170^\circ$ or $> 190^\circ$). Configurable in profile and UI.
- If fewer than 4 cardinal directions remain (e.g., triangular parcels), do not fabricate artificial lines; flag `FEWER_THAN_4_DIRECTIONS` in QA.

### A5. Geometry & Topology Preservation Checks (Blocking)
- Sum of output line lengths must equal total ring perimeter (tolerance $\le 10^{-6}\text{ m}$).
- Geometric union of outputs must equal original polygon boundary exactly with zero symmetric difference.
- True curve count must remain intact without chord approximation.

---

## Part B — Dimensioning & Border Descriptions (التوصيف)

1. **Neighbor Identification:** Adjoining parcels supply the parcel number / land-use description.
2. **Ray-Based Width Inference for Streets & Passages:**
   - For sides without neighbors, cast outward normal rays (default 3 rays at 1/4, 1/2, and 3/4 length; compute median hit distance).
   - Maximum ray distance: 100 m.
   - Distance $< 10\text{ m}$ $\rightarrow$ `"ممر بعرض W م"` (`BORDERTYPE = PASS`).
   - Distance $\ge 10\text{ m}$ $\rightarrow$ `"شارع بعرض W م"` (`BORDERTYPE = STR`).
3. **Protection of Manual Edits:**
   - Any manual modifications to border descriptions, lengths, or types are locked.
   - Re-running Step 4 will never overwrite manual edits unless the user explicitly selects `Regenerate` and confirms the warning dialog.

---

## Part C — Continue from Latest Export & Working Data Source

1. **State Persistence (`HelperFiles\state\last_export.json`):**
   - Save run ID, timestamp, parcel/border paths, UTM coordinate system, CAD source path, profile settings, feature counts, and geometry hashes upon every successful export.
2. **Universal Data Selector (Steps 2 through 5):**
   - `Latest export`: Loads the most recent deliverables from `Final/Final.gdb`.
   - `From previous step`: Consumes live in-memory / session datasets.
   - `Use my own data`: Allows selecting an external GDB Feature Class or Shapefile.
3. **Discrepancy Reconciliation & Diff Inspection:**
   - Compare current working geometry and attributes against the recorded state, highlighting added, deleted, or modified parcels/borders.
   - Freeze geometric metrics (`AREA_SQM`, `BORDERDESC`) and prompt before recomputing.

---

## Part D — Final Export Packaging Structure (Step 5)

Final export deliverables must strictly adhere to the structured hierarchy within `Final/`:
1. `Final/Final.gdb`:
   - Feature class `PARCEL`: Fully attributed with MOMRAH schema and True Curves intact.
   - Feature class `BORDER`: 4-cardinal direction boundary lines.
2. `Final/Shape`:
   - `PARCEL.shp`: Planar 2D polygon shapefile with matching schema.
   - `border.shp`: Planar 2D polyline shapefile.
   - Accompanying UTF-8 `.cpg` files for Arabic text support.
3. `Final/Reports`:
   - `QA_Audit_Summary.json` & `.txt`.
   - Discrepancy logs and verification certificates.
4. **Immediate Map Layer Display:** Upon export completion, the resulting GDB Feature Classes must be automatically added to the active ArcGIS Pro map contents.
