# CAD2GIS — Data Dictionary & Municipal Lookups Specification (08_DATA_DICTIONARY_AND_LOOKUPS.md)

> **الملخص التنفيذي بالعربية:**
> يوثق هذا الملف قاموس البيانات الرسمي الشامل لكافة حقول طبقتي قطع الأراضي (PARCEL - 26 حقلاً) والحدود الجغرافية (BORDER - 10 حقول) والحقول التشغيلية المؤقتة. كما يربط كل قائمة أكواد بورقة العمل المعتمدة في ملف الإكسل المرجعي (`Required Fields in the Shapefile.xlsx`)، بما يضمن قراءة القيم ديناميكياً في وقت التشغيل وعدم تثبيتها كقيم نصية مسبقة البرمجة (Hardcoded).

---

## 1. PARCEL Approved Schema (26 Fields)

Source Template: `HelperFiles/templates/PARCEL.shp`  
Excel Reference: `Required Fields in the Shapefile.xlsx` $\rightarrow$ Sheet: `Parcel_Table`

| Field Name | Type | Length | Dec | Mandatory | Source / Derivation Rule |
| :--- | :--- | :---: | :---: | :---: | :--- |
| `REALESTA_1` | Long (Int32) | 10 | 0 | Yes | Real estate type: `1` = Unit, `2` = Land parcel. Default `= 2`. |
| `NEIGHBOURH` | String | 6 | - | Yes | 6-character official municipal neighbourhood code (from `AdminAreaDetector`). |
| `REGION_ID`  | String | 2 | - | Yes | 2-digit official administrative region code (from `AdminAreaDetector`). |
| `CITY_ID`    | String | 5 | - | Yes | Official city identifier from `CITY_LOOKUP` sheet (e.g. `"1"` for Riyadh). |
| `PARCEL_TYP` | Long (Int32) | 10 | 0 | Yes | Parcel classification: `1` = MAIN (قطعة رئيسية), `2` = SUB (قطعة فرعية/خدمات). |
| `ADDRESS`    | String | 254 | - | Yes | Full structured administrative address string (from `_projectMeta.BuildAddress`). |
| `USAGE`      | String | 20 | - | Yes | Approved Balady code from `Usage Types` sheet (`RSD, COM, RSC, AGR, IND, UTL`). |
| `ISBUILDING` | Long (Int32) | 10 | 0 | Yes | Building status: `0` = Vacant, `1` = Built. Default `= 0`. |
| `BUILDINGTY` | Long (Int32) | 10 | 0 | Yes | Construction type from `Building Types` sheet (`1` = Modern concrete). Default `= 1`. |
| `REMARKS`    | String | 254 | - | No | Notes/Description from Excel match, `USAGE_TXT`, or usage name. Never provenance or PlotId. |
| `LAYOUT`     | String | 50 | - | No | Approved masterplan subdivision code (from metadata or Excel). |
| `PLOT_ID`    | String | 50 | - | Yes | Plot identifier. Assigned from `PLOT_TXT` in Line with data layer mode. |
| `BLOCK_NO`   | String | 50 | - | No | Block identifier from `PARCEs_landuse.xlsx` or annotation. |
| `PLAN_NO`    | String | 50 | - | Yes | Official subdivision plan number (from metadata or Excel). |
| `PLOT_NO1`   | String | 50 | - | Yes | Primary plot number. Synchronized with `PLOT_ID`. |
| `PLOT_NO2`   | String | 50 | - | No | Secondary plot number (for consolidated parcels). |
| `PLOT_NO3`   | String | 50 | - | No | Additional consolidated plot number. |
| `PLOT_NO4`   | String | 50 | - | No | Additional consolidated plot number. |
| `PLOT_NO5`   | String | 50 | - | No | Additional consolidated plot number. |
| `PLOT_NO6`   | String | 50 | - | No | Additional consolidated plot number. |
| `REQUEST_TY` | String | 50 | - | No | Municipal transaction request type (from metadata). |
| `REQUEST_NO` | String | 50 | - | No | Municipal transaction request number (from metadata). |
| `REPORT_NO`  | String | 50 | - | No | Survey technical report number (from metadata). |
| `AREA_SQM`   | Double | 19 | 2 | Yes | Analytical parcel area in square meters calculated from native true curves. |
| `Shape_Area` | Double | 19 | 8 | Yes | ESRI standard polygon area in UTM meters squared. |
| `Shape_Leng` | Double | 19 | 8 | Yes | ESRI standard polygon perimeter in UTM meters. |

---

## 2. BORDER Approved Schema (10 Fields)

Source Template: `HelperFiles/templates/border.shp`  
Excel Reference: `Required Fields in the Shapefile.xlsx` $\rightarrow$ Sheet: `Border_Table`

| Field Name | Type | Length | Dec | Mandatory | Source / Derivation Rule |
| :--- | :--- | :---: | :---: | :---: | :--- |
| `PARCEL_ID`  | String | 50 | - | Yes | Parent parcel `PLOT_ID` linking border to its originating MAIN parcel. |
| `BORDERNAME` | Long (Int32) | 10 | 0 | Yes | Cardinal direction code: `1` = North, `2` = East, `3` = South, `4` = West. |
| `BORDERTYPE` | String | 20 | - | Yes | Approved code from `Border Types` sheet (`PLT, STR, LNE, MSQ, PAK, ECR, UTL...`). |
| `DESCRIPTO`  | String | 254 | - | Yes | Descriptive text (e.g. `"قطعة رقم 12"`, `"شارع بعرض 20 م"`, `"ممر بعرض 6 م"`). |
| `DIMENSION`  | String | 20 | - | Yes | Linearity class: `"Single"` (deflection $\le 15^\circ$) or `"Multi"` (deflection $> 15^\circ$). |
| `LENGTH`     | Double | 19 | 2 | Yes | Boundary line length in ground meters calculated from native true curves. |
| `SUB_MUNIC`  | String | 50 | - | No | Sub-municipality name/code from administrative boundary lookup. |
| `SURVEY_DAT` | Date | 8 | - | No | Date of survey / processing timestamp. |
| `SURVEYOR`   | String | 50 | - | No | Survey office or operator identifier. |
| `NOTES`      | String | 254 | - | No | Technical notes or corner cluster descriptions. |

> [!NOTE]
> **Field Name Invariant (`DESCRIPTO` vs `DESCRIPTIO`):** The field name defined in the official Shapefile template and Excel sheet `Border_Table` is `DESCRIPTO` (9 characters). In some legacy ESRI documentation it was cited as `DESCRIPTIO` (10 characters). The codebase dynamically reads `def.FindField("DESCRIPTO")` with fallback to `"DESCRIPTIO"`.

---

## 3. Temporary Working Fields (Dropped in Step 3)

| Field Name | Type | Length | Lifecycle | Purpose |
| :--- | :--- | :---: | :--- | :--- |
| `SRC_LAYER`  | String | 100 | Step 2 $\rightarrow$ Step 3 | Dominant CAD layer name forming the boundary. |
| `SRC_LAYERS` | String | 254 | Step 2 $\rightarrow$ Step 3 | Detailed layer perimeters (e.g. `LotLine:85.4;Road:12.1`). |
| `PARCEL_CLS` | String | 10 | Step 2 $\rightarrow$ Step 3 | Temporary class string (`MAIN` vs `SUB`). |
| `PLOT_TXT`   | String | 50 | Step 2 $\rightarrow$ Step 3 | Raw plot number matched from PlotText layer. |
| `PLOT_LYR`   | String | 100 | Step 2 $\rightarrow$ Step 3 | Source layer name of raw plot text. |
| `USAGE_TXT`  | String | 254 | Step 2 $\rightarrow$ Step 3 | Raw usage/land-use text matched from UsageText layer. |
| `USAGE_LYR`  | String | 100 | Step 2 $\rightarrow$ Step 3 | Source layer name of raw usage text. |
| `LYR_USAGE`  | String | 50 | Step 2 $\rightarrow$ Step 3 | Pre-assigned usage code from Step 1 layer dropdown. |
| `MATCH_MODE` | String | 20 | Step 2 $\rightarrow$ Step 3 | Text match method (`Contains`, `Nearest`, `Conflict`, `None`). |
| `PROV`       | String | 254 | Step 2 $\rightarrow$ Step 3 | Provenance trace of parcel polygon construction. |

---

## 4. Municipal Code Lists & Reference Worksheets

### 4.1 Usage Types (`Usage Types` Sheet)
Source: `HelperFiles/Required Fields in the Shapefile.xlsx` $\rightarrow$ Sheet: `Usage Types` (7 rows, 6 active codes)

| Code | Arabic Description | English Meaning | Applicable Parcel Class |
| :---: | :--- | :--- | :---: |
| `RSD` | سكني | Residential | MAIN |
| `COM` | تجاري | Commercial | MAIN |
| `RSC` | سكني تجاري | Residential-Commercial Mixed | MAIN |
| `AGR` | زراعي | Agricultural | MAIN |
| `IND` | صناعي | Industrial | MAIN |
| `UTL` | مرافق | Public Utilities & Facilities | SUB / Service |

### 4.2 Building Construction Types (`Building Types` Sheet)
Source: `HelperFiles/Required Fields in the Shapefile.xlsx` $\rightarrow$ Sheet: `Building Types` (5 rows, 4 codes)

| Code | Arabic Description | English Meaning |
| :---: | :--- | :--- |
| `1` | سكني مسلح حديث | Modern Reinforced Concrete |
| `2` | سكني مسلح قديم | Older Reinforced Concrete |
| `3` | سكني شعبي | Traditional / Mud / Brick Masonry |
| `4` | سكني مهدم | Dilapidated / Demolished Structure |

### 4.3 Border Types (`Border Types` Sheet)
Source: `HelperFiles/Required Fields in the Shapefile.xlsx` $\rightarrow$ Sheet: `Border Types` (68 codes).  
JSON Mirror: `HelperFiles/lookups/BORDERTYPE.json`

Key representative codes:
- `PLT`: القطعة (Adjoining Main Parcel)
- `STR`: شارع (Street $\ge 10\text{ m}$)
- `LNE`: ممر مشاه (Pedestrian Passage $< 10\text{ m}$)
- `MSQ`: مسجد (Mosque - Note: code is `MSQ`, NOT `MSD`)
- `PAK`: حديقة (Park / Public Garden - Note: code is `PAK`, NOT `PRK`)
- `PRK`: مواقف سيارات (Parking Lot)
- `ECR`: غرفة كهرباء (Electrical Substation Room)
- `TFR`: غرفة محولات (Transformer Room)
- `UTL`: مرافق (Public Utility / Amenity)
- `HLW`: ممر (Corridor / Hallway)
- `SRV`: خدمات (Services)
- `ROD`: طريق (Highway / Major Road)
- `YRD`: ساحة (Open Courtyard / Plaza)
- `LAN`: أرض فضاء (Vacant Land)

---

## 5. Keyword Mapping Tables

### 5.1 Border Type Keywords (`BorderType_Keywords.csv`)
Path: `HelperFiles/lookups/BorderType_Keywords.csv`  
Structure: `Keyword,AppliesTo,MatchType,BorderTypeCode,Priority,Notes`

Sample rules:
```csv
ممر مشاة,BOTH,contains,LNE,1,ممر مشاة
ممر مشاه,BOTH,contains,LNE,1,ممر مشاه
حديقة,BOTH,contains,PAK,1,حديقة
مسجد,BOTH,contains,MSQ,1,مسجد
جامع,BOTH,contains,MSQ,1,جامع
مصلى,BOTH,contains,MSQ,1,مصلى
ممر,BOTH,contains,HLW,2,ممر
خدمات,BOTH,contains,SRV,2,خدمات
مرفق,BOTH,contains,UTL,2,مرفق
مرافق,BOTH,contains,UTL,2,مرافق
شارع,BOTH,contains,STR,2,شارع
مواقف,BOTH,contains,PRK,2,مواقف سيارات
غرفة كهرباء,BOTH,contains,ECR,2,غرفة كهرباء
غرفة محولات,BOTH,contains,TFR,2,غرفة المحولات
ساحة,BOTH,contains,YRD,2,ساحة
أرض فضاء,BOTH,contains,LAN,2,أرض فضاء
```

### 5.2 Usage Keywords (`Usage_Keywords.csv`)
Path: `HelperFiles/lookups/Usage_Keywords.csv`  
Structure: `Keyword,AppliesTo,MatchType,UsageCode,Priority,Notes`

Sample rules:
```csv
سكني,BOTH,contains,RSD,1,سكني
تجاري,BOTH,contains,COM,1,تجاري
سكني تجاري,BOTH,contains,RSC,1,سكني تجاري
تجاري سكني,BOTH,contains,RSC,1,تجاري سكني
زراعي,BOTH,contains,AGR,1,زراعي
صناعي,BOTH,contains,IND,1,صناعي
مرافق,BOTH,contains,UTL,1,مرافق
مرفق,BOTH,contains,UTL,1,مرفق
خدمات,BOTH,contains,UTL,2,خدمات
```

---

## 6. Standard Street & Passage Widths (`standard_widths.json`)

Path: `HelperFiles/lookups/standard_widths.json`  
Values (meters):
`[4.0, 5.0, 6.0, 8.0, 10.0, 12.0, 15.0, 18.0, 20.0, 25.0, 30.0, 36.0, 40.0, 50.0, 60.0]`

---

## 7. UTM Zones & Meridian Convergence Guidance

| UTM Zone | EPSG Code | Central Meridian ($\lambda_0$) | Longitude Range | Saudi Municipal Guidance |
| :---: | :---: | :---: | :---: | :--- |
| **Zone 35N** | `32635` | $27^\circ\text{ E}$ | $24^\circ\text{ E} - 30^\circ\text{ E}$ | Far Western borders, Red Sea islands |
| **Zone 36N** | `32636` | $33^\circ\text{ E}$ | $30^\circ\text{ E} - 36^\circ\text{ E}$ | Jeddah, Tabuk, Yanbu, Duba, Al-Wajh |
| **Zone 37N** | `32637` | $39^\circ\text{ E}$ | $36^\circ\text{ E} - 42^\circ\text{ E}$ | Makkah, Madinah, Riyadh West, Qassim, Hail |
| **Zone 38N** | `32638` | $45^\circ\text{ E}$ | $42^\circ\text{ E} - 48^\circ\text{ E}$ | Riyadh City, Eastern Province, Jubail, Khobar |
| **Zone 39N** | `32639` | $51^\circ\text{ E}$ | $48^\circ\text{ E} - 54^\circ\text{ E}$ | Al-Ahsa, Salwa, Rub' al Khali East |
| **Zone 40N** | `32640` | $57^\circ\text{ E}$ | $54^\circ\text{ E} - 60^\circ\text{ E}$ | Far Southeastern borders (Oman/UAE border) |

---

## 8. Functional Requirements: Data Dictionary & Lookups

- **`REQ-DATA-001` (Dynamic Excel Lookups):** All usage, border type, and city lookups MUST be read dynamically at runtime from Excel/JSON files.
- **`REQ-DATA-002` (PARCEL 26-Field Template):** The PARCEL layer MUST contain exactly the 26 fields specified in Section 1.
- **`REQ-DATA-003` (BORDER 10-Field Template):** The BORDER layer MUST contain exactly the 10 fields specified in Section 2.
- **`REQ-DATA-004` (Working Fields Purge):** The 10 working fields MUST be purged prior to final MOMRAH export.
- **`REQ-DATA-005` (Usage Whitelist):** `USAGE` values MUST strictly match one of the 6 codes in `Usage Types`.
- **`REQ-DATA-006` (BorderType Whitelist):** `BORDERTYPE` values MUST strictly match one of the 68 codes in `Border Types`.
- **`REQ-DATA-007` (Keyword Fallback):** Unmatched text annotations MUST be resolved via `Usage_Keywords.csv` and `BorderType_Keywords.csv`.
- **`REQ-DATA-008` (Standard Widths Catalog):** Street suggestion scores MUST reference `standard_widths.json`.
