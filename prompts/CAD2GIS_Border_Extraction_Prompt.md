# Prompt: CAD2GIS Border Extraction and Continue from Latest Export (v4.4)

Use the skills: `cad-border-extraction`, `cad2gis-neighbor-description-inference`, `cad2gis-export-and-resume`, `preserve-curve-geometry`, `gis-project-engineering-standards`.
The approved specification files and immutable invariants in [CHANGELOG_MODIFICATIONS.md](CHANGELOG_MODIFICATIONS.md) and [promt 01010.md](promt%2001010.md) are the primary sources of truth; report any conflict. Inspect existing code before modifying.

---

## Part A — Border Extraction (Step 4)

Implement and maintain border extraction adhering strictly to the following engineering requirements:
1. **Input Data Source:** Process **MAIN parcels only** (`PARCEL_TYP = 1` / internal class `MAIN`), operating in planar UTM coordinates (meters). Directly consume the output of **Step 3 (Populate Schema)** without re-reading CAD or recreating parcels.
2. **Cardinal Direction Boundaries:** Per ring (exterior ring and each interior hole ring evaluated separately): generate at most 4 single-part polylines, exactly one per cardinal direction:
   - `BORDERNAME 1`: North (شمال)
   - `BORDERNAME 2`: East (شرق)
   - `BORDERNAME 3`: South (جنوب)
   - `BORDERNAME 4`: West (غرب)
3. **Corner Detection & Split Points:**
   - **Angle definition:** At each vertex, compute the **interior angle measured from inside the parcel**, between the incoming and outgoing pieces.
   - A vertex is a **split point** when this angle lies within **$[A_{\min}, A_{\max}]$**, default **10°–160°** (configurable, saved in the profile and dockpane UI).
   - **Convex corners** have interior angles below 180° ($< 180^\circ$).
   - **Concave (reflex) corners** have interior angles above 180° ($> 180^\circ$), evaluated in the mirrored range $[360^\circ - A_{\max}, 360^\circ - A_{\min}]$ (default 200°–350°).
   - **Corner clusters (chamfers / rounded fillets):** vertices closer than threshold (default 7 m) with cumulative turning angle matching corner limits are split at cluster ends.
4. **True Curves & Arc Preservation (INV-01):**
   - Tangent-continuous arcs or identical circles do not split.
   - Tangent breaks use a temporary virtual chord solely for angle estimation.
   - True arcs (`EllipticArcSegment`, circular arcs) are **never straightened, densified, or flattened into line segments**.
5. **Direction Assignment:** Outward normal direction relative to True North (corrected for UTM meridian convergence at parcel centroid). Near 45° ties follow the deterministic optimization rule specified in the master prompt.
6. **Contiguous Single-Part Merging & Multi-Segment Border Classification:**
   - Merge only adjacent pieces of the same direction.
   - Rule 1: Absorb an in-between shorter piece of different direction if flanked by same direction.
   - Rule 2: Longest contiguous piece keeps direction; shorter pieces merge into nearest neighbor.
   - Output must **never contain multipart lines**.
   - **Multi vs Single Border Definition:** After merge borders, define whether a border is "Multi" or "Single" based on the angle between consecutive segments: it is NOT one segment ("Multi") if the angle between consecutive segments deviates from 180° by more than **`MultiSegmentAngleDeviationDeg`** (default **10°**, i.e. angle $< 170^\circ$ or $> 190^\circ$). Configurable in profile Excel, settings JSON, and Dockpane UI.
7. **Quality Assurance & Topology:**
   - Parcels with fewer than 4 cardinal directions are flagged with `FEWER_THAN_4_DIRECTIONS` in QA without inventing artificial lines.
   - Parcels with interior rings (holes): each ring is processed independently, with directions repeating at most once per ring.

---

## Part B — Border Description (التوصيف واستشعار الشوارع)

1. **Neighbor Identification:** Dimensions adjoining neighbor parcels inherit parcel description / plot ID.
2. **Ray-Based Width Inference (Sides without Neighbor):**
   - Cast perpendicular outward rays (default 3 rays at 1/4, 1/2, and 3/4 length; median distance taken).
   - Maximum search distance = 100 m.
   - Distance $< 10\text{ m}$ $\rightarrow$ `"ممر بعرض W م"` (`PASS`).
   - Distance $\ge 10\text{ m}$ $\rightarrow$ `"شارع بعرض W م"` (`STR`).
   - Sides with no hit within 100 m are marked `UNKNOWN` for operator review.
3. **Protection of Manual Edits:** Manually entered descriptions, lengths, and types are locked and preserved across runs unless the user explicitly clicks `Regenerate`.

---

## Part C — Export Continuity & Working Data Source

1. **Working State Tracking:** Persist state into `HelperFiles\state\last_export.json` on each export (run ID, timestamp, parcel/border paths, UTM zone, feature counts, hash).
2. **Universal Data Selector:** Present in every step (Steps 2 through 5) offering:
   - `Latest export`: Automatically loads the most recent deliverables.
   - `From previous step`: Passes in-memory / session outputs directly.
   - `Use my own data`: Allows browsing external GDB Feature Classes or Shapefiles.
3. **Structured Final Delivery:**
   - All final deliverables must be organized inside `Final/`:
     - `Final/Final.gdb`: Feature classes with True Curves intact.
     - `Final/Shape`: Shapefiles (`PARCEL.shp`, `border.shp` + UTF-8 `.cpg`).
     - `Final/Reports`: Full QA/QC audit summaries and validation logs.
