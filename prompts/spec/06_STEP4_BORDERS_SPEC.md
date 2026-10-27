# CAD2GIS — Step 4: Cardinal Boundary Extraction & Street Sensing Specification (06_STEP4_BORDERS_SPEC.md)

> **الملخص التنفيذي بالعربية:**
> تحدد هذه الوثيقة متطلبات المرحلة الرابعة (Step 4) لتفكيك حدود القطع الرئيسية (MAIN Parcels) إلى أربعة اتجاهات جغرافية رئيسية (شمال، شرق، جنوب، غرب) بحد أقصى أربعة خطوط مفردة لكل حلقة. وتشمل تفاصيل خوارزمية تحديد نقاط الكسر بالزوايا الداخلية (10°–160° والانعكاس 200°–350°)، وحماية الأقواس والمنحنيات الحقيقية (INV-01)، وتصحيح الشمال الجغرافي، وقواعد دمج الخطوط، وتحديد الأجزاء المشتركة مع الجيران (Shared Segments)، واستشعار عروض الشوارع والممرات بشعاع واحد (Single Ray)، وقاعدة تصنيف الأبعاد (`Single` مقابل `Multi` عند زاوية انحراف 15°).

---

## 1. Goal & Operational Scope

Step 4 reads `PARCEL_Approved_Schema` produced by Step 3 (or the operator's Own Data), extracts cardinal boundaries for all **MAIN parcels**, senses adjoining neighbours or facing street widths, and writes **exactly ONE output feature class**:
`[WorkspaceGDB]\BORDER_Cardinal_Approved`

> [!IMPORTANT]
> **MAIN Parcels Only:** Sub-parcels (`PARCEL_TYP = 2`, e.g. utilities, mosques, parks, streets) DO NOT undergo boundary extraction. Their boundaries serve exclusively as neighbours to MAIN parcels.

---

## 2. Ring & Hole Decomposition Architecture

```
[MAIN Parcel Polygon]
         │
         ├──► Exterior Ring (Clockwise) ───────► Decompose into max 4 Cardinal Lines (N, E, S, W)
         │
         └──► Interior Ring 1..K (Counter-CW) ──► Decompose into max 4 Cardinal Lines independently
```

1. **Independent Ring Processing:** The exterior ring and each interior ring (hole) are processed independently as distinct topological loops. Boundary lines are NEVER merged across rings.
2. **Cardinal Quota Per Ring:** Each ring produces at most **4 single-part polylines**, exactly one per cardinal direction (`BORDERNAME`: 1=North, 2=East, 3=South, 4=West).
3. **Strict No-Multipart Invariant:** Every border feature MUST be a single-part polyline (`PathCount == 1`). Multipart lines are strictly prohibited.
4. **Conservation of Boundary:** The geometric union of output borders MUST equal the original polygon perimeter exactly. The system MUST NEVER create artificial shortcut lines.

---

## 3. Split Point Identification by Interior Angle (Owner Decision D11)

A polygon boundary is traversed cyclically. At each vertex $V_i$, the **interior angle $\alpha$ measured from inside the parcel** between incoming vector $\vec{V}_{i-1}V_i$ and outgoing vector $\vec{V_i}V_{i+1}$ is computed:

```
                  V_i+1
                  /
                 / 
     Inside     /  Interior Angle α
     Parcel    /  
              /
   V_i-1 ──── V_i
```

### 3.1 Angle Thresholds
1. **Convex Split Interval:** If $\alpha \in [A_{\min}, A_{\max}]$, $V_i$ is a split point:
   - Default: $[10^\circ, 160^\circ]$ (configurable).
2. **Reflex (Concave) Split Interval:** If $\alpha > 180^\circ$ and $\alpha \in [360^\circ - A_{\max}, 360^\circ - A_{\min}]$, $V_i$ is a split point:
   - Default: $[200^\circ, 350^\circ]$ (mirror of $10^\circ$–$160^\circ$).
3. **Flat Vertices:** Vertices with $\alpha \in (160^\circ, 200^\circ)$ are collinear transitions ($180^\circ \pm 20^\circ$) and are NOT split points.

### 3.2 Arc & True Curve Handling (INV-01)
- **Virtual Chord Substitution:** When calculating angles at arc endpoints, the arc is temporarily represented by a straight virtual chord between its start and end points. The true mathematical arc (`EllipticArcSegment`) remains untouched.
- **Arc-to-Arc Joints:**
  - *Same Circle:* Same center and radius within $0.05\text{ m}$ tolerance $\rightarrow$ Redundant vertex, NO split.
  - *Tangent-Continuous:* Tangents align within $2.0^\circ$ $\rightarrow$ Smooth curve, NO split.
  - *Tangent Break:* Evaluated with virtual chord angle test.

### 3.3 Corner Clustering (Chamfers / "Shotar" & Fillets)
Consecutive vertices separated by segment lengths $< L_{\text{cluster}}$ (default $7.0\text{ m}$) are grouped as a unified corner cluster:
- If the cumulative angular deviation across the cluster meets corner criteria, splits are placed at both cluster extremities.
- This isolates the chamfer into an independent segment so it does not skew cardinal orientation.

---

## 4. Direction Assignment & True North Correction

For each boundary segment $S_k$:
1. **Outward Normal Calculation:** Compute the vector perpendicular to $S_k$ pointing away from the parcel interior (pointing inward for hole boundaries).
2. **True North Correction (Meridian Convergence):**
   Projected UTM grid azimuth differs from True Astronomic North by the meridian convergence angle $\gamma$ computed at parcel centroid $(\lambda, \phi)$:
   $$\gamma = (\lambda - \lambda_0) \sin(\phi)$$
   where $\lambda_0$ is the central meridian of the UTM zone.
   $$\text{Azimuth}_{\text{True}} = (\text{Azimuth}_{\text{Grid}} + \gamma) \pmod{360^\circ}$$
3. **Cardinal Sector Mapping:**
   - **North (1):** $[315^\circ, 45^\circ)$
   - **East (2):** $[45^\circ, 135^\circ)$
   - **South (3):** $[135^\circ, 225^\circ)$
   - **West (4):** $[225^\circ, 315^\circ)$
   - Ties occurring near exact $45^\circ$ bisectors are resolved deterministically to balance opposite facades.

---

## 5. Line Merging Algorithms (Rule 1 & Rule 2)

Adjacent segments with the same cardinal direction are joined. When alternating directions occur, two rules resolve the topology:

```
Traverse Segments Around Ring
             │
             ▼
     [Rule 1 Applicable?]
     Two runs of X separated by short run of Y,
     and Y exists elsewhere on ring?
            / \
          Yes  No
          /     \
   Absorb Y      ▼
   into X   [Rule 2: Longest Wins]
            Longest run of X retains X;
            Shorter runs of X merge into adjacent.
```

- **Rule 1 (Absorb Short Intermediate Run):** If two runs of direction $X$ are separated by a shorter run of direction $Y$, and direction $Y$ maintains at least one other distinct run elsewhere on the ring, the intermediate $Y$ run is absorbed into $X$.
- **Rule 2 (Longest Wins):** If Rule 1 does not apply, the longest run of direction $X$ retains direction $X$. Shorter duplicate runs of $X$ merge into their adjacent neighbours.
- **Fewer Than 4 Directions:** If a triangular parcel naturally yields only 3 directions, the system MUST NOT invent a 4th line. It flags `FEWER_THAN_4_DIRECTIONS` in the QA log.

---

## 6. Shared Segment Identification & `BORDERTYPE` (Owner Decision D12)

A boundary line is evaluated against all neighbouring parcel polygons in `PARCEL_Approved_Schema`:

```
For each boundary line L:
  Buffer L by SHARED_COINCIDENCE_TOLERANCE (0.05 m)
  Intersect buffer with neighbouring parcel boundaries
  Sum coincident length for each neighbour
  Eligible neighbours = neighbours with Coincident Length >= 0.5 m
```

### 6.1 Eligible Neighbour Rules
1. **Length Threshold:** $\text{Coincident Length} \ge \text{MIN\_SHARED\_SEGMENT\_LENGTH} = 0.5\text{ m}$.
2. **Coincidence Buffer:** $\text{SHARED\_COINCIDENCE\_TOLERANCE} = 0.05\text{ m}$ ($5\text{ cm}$).
3. **Attribution:**
   - **MAIN Neighbour:** Assigned directly:
     `BORDERTYPE = "PLT"` (القطعة), `DESCRIPTO = $"قطعة رقم {NeighbourPlotId}"`
   - **SUB Neighbour:** Inspects neighbour's `REMARKS` and `USAGE` against `HelperFiles/lookups/BorderType_Keywords.csv`:
     - Matches `"مسجد"` or `"جامع"` $\rightarrow$ `BORDERTYPE = "MSQ"`, `DESCRIPTO = "مسجد"`
     - Matches `"حديقة"` $\rightarrow$ `BORDERTYPE = "PAK"`, `DESCRIPTO = "حديقة"`
     - Matches `"ممر مشاة"` $\rightarrow$ `BORDERTYPE = "LNE"`, `DESCRIPTO = "ممر مشاة"`
     - Matches `"مرفق"` or `USAGE = UTL` $\rightarrow$ `BORDERTYPE = "UTL"`, `DESCRIPTO = "مرفق"`
   - **Validation:** `BORDERTYPE` MUST exist in the 68 approved codes of the `Border Types` sheet. Unapproved codes (e.g. `MSD`) are strictly forbidden.
4. **Multiple Neighbours on One Boundary Line:**
   - The boundary line remains a single polyline.
   - `BORDERTYPE` is taken from the neighbour with the **longest shared segment**.
   - `DESCRIPTO` lists all eligible neighbours in order:
     `"قطعة رقم 12 و13 وممر مشاة"`

---

## 7. Width Inference via Single-Ray Casting (Owner Decisions D13 & D20)

If a boundary line has NO eligible neighbour ($\ge 0.5\text{ m}$), it represents a street or passage:

```
Boundary Midpoint ──► Outward Normal Ray ──► Hits Facing Parcel (Gap >= 1.0 m, Dist <= 100 m)
                                                          │
                                                          ▼
                                               Euclidean Distance (d)
                                               Rounded Width = Round(d, AwayFromZero)
                                                          │
                                         ┌────────────────┴────────────────┐
                                         ▼                                 ▼
                             Rounded Width < 10.0 m            Rounded Width >= 10.0 m
                             Passage (ممر)                     Street (شارع)
                             BORDERTYPE = "LNE"                BORDERTYPE = "STR"
                             DESCRIPTO = "ممر بعرض W م"         DESCRIPTO = "شارع بعرض W م"
```

### 7.1 Single-Ray Specifications
1. **Ray Origin:** Cast from the exact midpoint ($50\%$ length) of the boundary line.
2. **Ray Direction:** Perpendicular outward normal vector.
3. **Ray Offset:** An offset of $0.05\text{ m}$ is applied to clear the originating parcel boundary.
4. **Valid Target Parcel:**
   - A polygon from the same parcel feature class.
   - MUST NOT touch the originating parcel.
   - MUST be separated by a gap $\ge \text{MIN\_FACING\_GAP} = 1.0\text{ m}$ (parcels closer than $1\text{ m}$ are adjacent neighbours, not facing street frontages).
5. **Maximum Search Distance:** $\text{MAX\_RAY\_DISTANCE} = 100.0\text{ m}$.
6. **Corner Deflection Retries:** If the ray grazes an external corner within $0.1\text{ m}$, two micro-shifted rays at $48\%$ and $52\%$ are tested.
7. **Three Recorded Values:**
   - **Measured:** Raw geometric Euclidean distance.
   - **Rounded:** Nearest whole integer using `MidpointRounding.AwayFromZero` (e.g. $14.5\text{ m} \rightarrow 15\text{ m}$, $9.4\text{ m} \rightarrow 9\text{ m}$).
   - **Suggested:** Nearest standard width from `standard_widths.json` if within $\pm 1.0\text{ m}$.
   - **No Auto-Snapping:** The value written to `DESCRIPTO` is strictly the **Rounded** value.
8. **Passage vs Street Cutoff (10 m):**
   - $\text{Rounded Width} < 10.0\text{ m} \rightarrow$ Passage (`BORDERTYPE = "LNE"`, `DESCRIPTO = "ممر بعرض {W} م"`).
   - $\text{Rounded Width} \ge 10.0\text{ m} \rightarrow$ Street (`BORDERTYPE = "STR"`, `DESCRIPTO = "شارع بعرض {W} م"`).
   - Marked with internal property `IsInferred = true`.
9. **No Target Found ($> 100\text{ m}$):**
   - `BORDERTYPE = ""`, `DESCRIPTO = ""`.
   - Displayed as `UNKNOWN` in the review table.
   - **Rule:** The literal word `"UNKNOWN"` MUST NEVER be written into the shapefile/GDB attributes.

---

## 8. Dimension Field Definition (`DIMENSION`) (Owner Decision D14)

The `DIMENSION` attribute classifies boundary line linearity:
- **`Single`:** Assigned if the boundary consists of a single straight segment or a single continuous circular arc, OR if all internal deflection angles between adjacent segments are $\le 15.0^\circ$.
- **`Multi`:** Assigned if the deflection angle $|\Delta\text{azimuth}|$ between any two adjacent segments exceeds $\text{DIMENSION\_DEFLECTION\_THRESHOLD} = 15.0^\circ$.

$$\text{Deflection Angle } \theta = |\text{Azimuth}_2 - \text{Azimuth}_1| \pmod{360^\circ}$$
$$\text{If } \theta > 180^\circ \implies \theta = 360^\circ - \theta$$
$$\text{If } \exists \theta > 15.0^\circ \implies \text{DIMENSION} = \text{"Multi"}; \quad \text{Else } \text{DIMENSION} = \text{"Single"}$$

---

## 9. Performance Architecture (Spatial Caching)

Step 4 processes thousands of boundary interactions efficiently through four performance safeguards (INV-08):
1. **R-Tree / Spatial Index:** Parcel geometries are indexed into bounding-box spatial trees, eliminating full-table scans during shared-segment and ray intersections.
2. **Cached Buffers:** The $0.05\text{ m}$ boundary buffers are computed once per parcel.
3. **Lazy Obstacle Assembly:** Ray-casting collision obstacles are assembled dynamically per geographic cluster.
4. **Throttled Progress Dispatching:** UI progress updates are throttled to at most once every $250\text{ ms}$.

---

## 10. Worked Example (Fictional Data)

**Parcel 101 Boundary Decomposition:**
- Exterior Ring Perimeter = $110.0\text{ m}$.
- 4 primary vertices at $(0,0)$, $(30,0)$, $(30,25)$, $(0,25)$ with interior angles $= 90^\circ$ (within $[10^\circ, 160^\circ]$) $\rightarrow$ 4 split points.

1. **North Boundary (Line 1):**
   - From $(30,25)$ to $(0,25)$, length $= 30.0\text{ m}$, True Azimuth $= 0^\circ$ $\rightarrow$ North (`BORDERNAME = 1`).
   - Shared segment analysis: Touches Parcel 102 along $30.0\text{ m} \ge 0.5\text{ m}$ (MAIN parcel).
   - Result: `BORDERTYPE = "PLT"`, `DESCRIPTO = "قطعة رقم 102"`, `DIMENSION = "Single"`, `LENGTH = 30.0`.
2. **East Boundary (Line 2):**
   - From $(30,0)$ to $(30,25)$, length $= 25.0\text{ m}$, True Azimuth $= 90^\circ$ $\rightarrow$ East (`BORDERNAME = 2`).
   - Shared segment analysis: 0 neighbours $\ge 0.5\text{ m}$.
   - Ray cast from $(30, 12.5)$ along normal $(1, 0)$ hits facing commercial parcel at distance $19.8\text{ m}$.
   - Rounded width $= 20\text{ m} \ge 10\text{ m}$ $\rightarrow$ Street.
   - Result: `BORDERTYPE = "STR"`, `DESCRIPTO = "شارع بعرض 20 م"`, `DIMENSION = "Single"`, `LENGTH = 25.0`.
3. **South Boundary (Line 3):**
   - From $(0,0)$ to $(30,0)$, length $= 30.0\text{ m}$, True Azimuth $= 180^\circ$ $\rightarrow$ South (`BORDERNAME = 3`).
   - Shared segment analysis: 0 neighbours. Ray hits facing parcel at $5.7\text{ m}$.
   - Rounded width $= 6\text{ m} < 10\text{ m}$ $\rightarrow$ Passage.
   - Result: `BORDERTYPE = "LNE"`, `DESCRIPTO = "ممر بعرض 6 م"`, `DIMENSION = "Single"`, `LENGTH = 30.0`.
4. **West Boundary (Line 4):**
   - From $(0,25)$ to $(0,0)$, length $= 25.0\text{ m}$, True Azimuth $= 270^\circ$ $\rightarrow$ West (`BORDERNAME = 4`).
   - Shared segment analysis: Touches electrical room (SUB parcel) with `REMARKS = "غرفة كهرباء"`.
   - Keyword match $\rightarrow$ `BORDERTYPE = "ECR"`, `DESCRIPTO = "غرفة كهرباء"`.

---

## 11. Functional Requirements: Step 4

- **`REQ-ST4-001` (Single Output):** Step 4 MUST write solely to `BORDER_Cardinal_Approved`.
- **`REQ-ST4-002` (MAIN Parcels Only):** Borders MUST only be extracted for parcels where `Class == ParcelClass.Main`.
- **`REQ-ST4-003` (Rings & Holes Independence):** Interior rings MUST be decomposed independently from exterior rings.
- **`REQ-ST4-004` (Cardinal Line Quota):** Each ring MUST produce at most 4 single-part polylines.
- **`REQ-ST4-005` (No Multipart):** Output border lines MUST NOT be multipart.
- **`REQ-ST4-006` (Angle Split Points):** Vertices with interior angles in $[10^\circ, 160^\circ]$ or $[200^\circ, 350^\circ]$ MUST be split points.
- **`REQ-ST4-007` (True Curve Angle Chords):** Circular arcs MUST use virtual chords for angle computation without altering true geometry.
- **`REQ-ST4-008` (Corner Clustering):** Vertices closer than $7.0\text{ m}$ forming turning corners MUST be clustered.
- **`REQ-ST4-009` (True North Correction):** Cardinal directions MUST be corrected for UTM meridian convergence.
- **`REQ-ST4-010` (Merge Rule 1 & 2):** Alternating directional segments MUST be resolved via Rule 1 and Rule 2.
- **`REQ-ST4-011` (Shared Segment Threshold):** Shared segments MUST have length $\ge 0.5\text{ m}$ within a $0.05\text{ m}$ buffer.
- **`REQ-ST4-012` (MAIN Neighbour BORDERTYPE):** A shared MAIN neighbour MUST assign `BORDERTYPE = "PLT"`.
- **`REQ-ST4-013` (SUB Neighbour Matching):** A shared SUB neighbour MUST resolve `BORDERTYPE` via `BorderType_Keywords.csv` from approved codes.
- **`REQ-ST4-014` (Single Ray Default):** Width sensing MUST cast a single outward normal ray from the midpoint.
- **`REQ-ST4-015` (Facing Parcel Separation):** Facing parcels MUST NOT touch and MUST be separated by $\ge 1.0\text{ m}$.
- **`REQ-ST4-016` (Max Ray Search):** Maximum ray search distance MUST be $100.0\text{ m}$.
- **`REQ-ST4-017` (Passage vs Street Cutoff):** Width $< 10.0\text{ m}$ MUST be classified as passage (`LNE`); $\ge 10.0\text{ m}$ as street (`STR`).
- **`REQ-ST4-018` (No UNKNOWN in Data):** Unresolved widths MUST leave `BORDERTYPE` and `DESCRIPTO` blank.
- **`REQ-ST4-019` (DIMENSION 15° Deflection):** Lines with segment deflection $> 15.0^\circ$ MUST be assigned `DIMENSION = "Multi"`; otherwise `"Single"`.
- **`REQ-ST4-020` (UTF-8 Length Guard):** `DESCRIPTO` byte length MUST be validated against template width before writing.
