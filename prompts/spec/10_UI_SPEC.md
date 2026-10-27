# CAD2GIS — Dockpane User Interface & Interaction Specification (10_UI_SPEC.md)

> **الملخص التنفيذي بالعربية:**
> تحدد هذه الوثيقة مواصفات واجهة المستخدم التفاعلية (DockPane UI/UX) للوحة تحكم CAD2GIS في ArcGIS Pro 3.3.0. تشمل الوثيقة أبعاد اللوحة القياسية (450px)، والسمات اللونية الداكنة المتناسقة (Modern Slate Dark Theme)، وترتيب التبويبات الخمسة، وكافة عناصر التحكم والأزرار، وبطاقات اختيار مصدر البيانات الموحدة، ومخطط تدفق كونسول التقدم الحي، والكتالوج الثنائي اللغات (العربية والإنجليزية) لكافة الرسائل التنبيهية والتحذيرية.

---

## 1. Visual Design System & Palette Tokens

The Dockpane adheres to an ultra-clean, high-contrast Dark Workstation theme designed for extended GIS operational sessions:

| Token Name | Hex Code | Visual Role | Application in UI |
| :--- | :---: | :---: | :--- |
| **Deep Obsidian** | `#0B0F19` | Background | Primary window background, progress console container. |
| **Slate Navy 800** | `#1E293B` | Surface / Card | Process cards, grouped option panels, tab strips. |
| **Dark Slate 900** | `#0F172A` | Inset / Input | TextBoxes, ComboBoxes, DataGrid cell backgrounds. |
| **Action Blue** | `#2563EB` | Primary Action | Main execution buttons ("Extract Parcels", "Browse"). |
| **Hover Blue** | `#1D4ED8` | Interactive State | Mouse hover over primary command buttons. |
| **Electric Sky** | `#38BDF8` | Accent & Headers | Card titles, percentage indicators, highlighted entity counts. |
| **Success Emerald** | `#10B981` | Positive Feedback | Verified state badges, 100% progress indicators, valid audits. |
| **Warning Amber** | `#F59E0B` | Warning / Review | Duplicate ID notices, missing Excel warnings, manual edit alerts. |
| **Critical Crimson** | `#EF4444` | Error State | Missing file blocks, invalid geometry alerts, process halts. |
| **Muted Slate** | `#94A3B8` | Secondary Text | Helper descriptions, watermarks, unselected tab labels. |

- **Dockpane Dimensions:** `d:DesignWidth = "450"` pixels; `d:DesignHeight = "850"` pixels; Docked by default on the right side of the active ArcGIS Pro layout (`dock="right"`).

---

## 2. Layout Structure & Regional Zones

```
┌────────────────────────────────────────────────────────┐
│ ZONE 1: Header & Spatial Badges                        │ (Title, Active Profile, UTM WKID)
├────────────────────────────────────────────────────────┤
│ ZONE 2: 5-Step Pipeline Tabbed Stepper                 │
│   [1. CAD] [2. Parcels] [3. Schema] [4. Borders] [5. QA]│
├────────────────────────────────────────────────────────┤
│                                                        │
│ ZONE 3: Active Tab Scrollable Work Area                │ (Card containers, controls, DataGrids)
│                                                        │
├────────────────────────────────────────────────────────┤
│ ZONE 4: Footer — Run All Pipeline & Live Progress      │ (Run All, Progress Bar, Status Text)
└────────────────────────────────────────────────────────┘
```

---

## 3. Detailed Tab Specifications

### 3.1 Tab 1: `1. CAD & Spatial`
- **CAD File Card:**
  - `TextBox` with browse button (`📁 Browse CAD...`) filtering for `*.dwg;*.dxf`.
  - Action button: `🔄 تحويل الكاد إلى داتاسيت (Convert CAD to GDB Dataset)`.
  - Secondary button: `🧪 Load Demo Project` (loads fictional synthetic demo layout).
- **Coordinate System (CRS) Card:**
  - Dropdown populated with UTM Zones 35N to 40N and Ain el Abd.
  - Active spatial reference badge displaying EPSG code and regional coverage.
- **Expected Layers Excel Card:**
  - Path selector for `Expected_Layers.xlsx`.
  - Button: `📖 قراءة ملف الإكسل (Load Expected Layers)`.
- **Layer Mapping DataGrid:**
  - 4 Category sub-tabs: `[Main Parcels]`, `[Sub-Parcels / Utilities]`, `[Text & Annotations]`, `[Ignore Layers]`.
  - DataGrid Columns:
    1. Checkbox: `IsSelected` (with Select All header checkbox).
    2. Color Swatch: Small rounded rectangle showing CAD RGB layer color.
    3. Layer Name: Bold string with text trimming.
    4. Entities: Numeric count of elements on layer.
    5. Geom Type: `Polyline`, `Polygon`, `Point`, `Text`.
    6. USAGE Dropdown: Dynamic list from `Usage Types` worksheet (`RSD`, `COM`, `RSC`, `AGR`, `IND`, `UTL`).
  - Context Menu on rows: `Move to [Main Parcels]`, `Move to [Sub-Parcels]`, `Move to [Text]`, `Move to [Ignore]`.
  - Action Toolbar: `🎯 تحديد هذه الطبقة فقط`, `✔ تحديد الكل`, `❌ إلغاء الكل`, `🔄 تحديث الاستخدام`.

### 3.2 Tab 2: `2. Extract Parcels`
- **Universal Working Source Selector Card:**
  - Radio 1: `◉ الخطوة السابقة (Previous step: Step 1 Cad_Dataset)` [Default].
  - Radio 2: `○ بيانات خاصة بي (My own data)`.
  - Read-only Status Banner:
    `Reads from: [Path] — Features: [N] — Written: [Timestamp] — Edited: [Yes/No]`
  - Action buttons: `📂 فتح المجلد`, `🗺️ إضافة للخريطة`.
- **Polygon Construction Parameters Card:**
  - Geometry Mode Dropdown: `Line with data layer` [Default], `Line`, `Polygon`.
  - Closure Tolerance: Numeric TextBox (default `0.05` meters).
  - Checkboxes: `[✔] تمديد الخطوط والأقواس (Extend Lines & Arcs)`, `[✔] بناء المضلعات من الخطوط عبر ArcPy`.
- **Primary Execution Button:** `🔨 بناء واستخراج قطع الأراضي (Extract Parcels)`.
- **Review DataGrid:** Displays constructed parcels with `InternalId`, `PlotId`, `Class`, `AreaSqm`, `SourceLayer`.

### 3.3 Tab 3: `3. Populate Schema`
- **Universal Working Source Selector Card:**
  - Previous step (`Cad_Constructed_Parcels`) vs My own data.
  - Status Banner with feature count and edit stamp.
- **Tabular Enrichment Card:**
  - Toggle: `◉ نموذج الوزارة المعتمد (MOMRAH Standard)` vs `○ اسكيما خاصة (Custom Schema)`.
  - Excel Path: `HelperFiles/PARCEs_landuse.xlsx`.
  - Areas Excel Path: `Areas.xlsx` (optional for area discrepancy comparison).
  - Main Usage Mode Dropdown: `InferFromText` [Default], `LeaveBlank`.
- **Primary Execution Button:** `📋 تعبئة الاسكيما والبيانات البلدية (Populate Schema)`.
- **Review DataGrid:** Displays enriched parcels with MOMRAH attributes (`PLOT_ID`, `USAGE`, `REMARKS`, `NEIGHBOURH`, `ADDRESS`, `AREA_SQM`).

### 3.4 Tab 4: `4. Build Borders`
- **Universal Working Source Selector Card:**
  - Previous step (`PARCEL_Approved_Schema`) vs My own data.
  - Status Banner with feature count and edit stamp.
- **Border Extraction Parameters Card:**
  - Interior Split Angle Range: $A_{\min} = 10^\circ$, $A_{\max} = 160^\circ$.
  - Deflection Threshold: $15^\circ$ (for `DIMENSION = "Multi"`).
  - Street Width Ray Mode: `◉ شعاع واحد (Single Ray)` [Default] vs `○ 3 أشعة (3-Ray Median)`.
  - Max Ray Distance: `100` meters.
  - Passage vs Street Cutoff: `10` meters.
  - Chamfer Cluster Length: `7` meters.
- **Primary Execution Button:** `📐 استخراج وتوصيف الحدود واستشعار الشوارع (Build Borders)`.
- **Review DataGrid:** Displays extracted borders with `PARCEL_ID`, `BORDERNAME`, `BORDERTYPE`, `DESCRIPTO`, `DIMENSION`, `LENGTH`.

### 3.5 Tab 5: `5. Export & QA`
- **Universal Working Source Selector Card:**
  - Previous step (`PARCEL_Approved_Schema` + `BORDER_Cardinal_Approved`) vs My own data.
- **Deliverables Configuration Card:**
  - Output Folder Selector: Default `[Workspace]/Final/`.
  - Checkboxes:
    - `[✔] إنشاء قاعدة بيانات التسليم (Final.gdb with True Curves)`
    - `[✔] تصدير ملفات Shapefile بترميز UTF-8`
    - `[✔] توليد تقارير التدقيق الفني (QA/QC Reports)`
- **Primary Execution Button:** `📦 تصدير مخرجات التسليم والتدقيق الفني (Export & QA)`.
- **Audit Results Card:** Displays total errors, warnings, and passed certification badge.
- **Convenience Buttons:** `📂 فتح مجلد التسليم (Open Final Folder)`, `🗺️ إضافة المخرجات للخريطة (Add Final to Map)`.

### 3.6 Footer: Pipeline Console & Progress
- **Pipeline Runner Button:** `🚀 تشغيل المسار بالكامل (Run All Pipeline)`.
- **ProgressBar:** Smooth progress tracking ($0\%$ to $100\%$) with electric sky fill.
- **Telemetry Indicators:**
  - Phase Text (e.g. `"المرحلة 3 من 5: تعبئة الاسكيما البلدية"`).
  - Status Message with character ellipsis for long descriptions.
  - Spinner indicator active during background task processing.

---

## 4. Bilingual Message Catalog (Arabic / English)

| Message Key | Arabic String | English String | Message Type |
| :--- | :--- | :--- | :---: |
| `ERR_NO_CAD` | يرجى تحديد مسار ملف الكاد أولاً. | Please select CAD drawing file path first. | Error Modal |
| `ERR_NO_PREV_STEP` | الخطوة {0} ليس لها مخرجات معتمدة. يرجى تشغيلها أولاً أو اختيار 'بيانات خاصة'. | Step {0} has no verified output. Run Step {0} first, or choose 'My own data'. | Error Modal |
| `WARN_MANUAL_EDITS` | تم رصد تعديلات يدوية على طبقة {0}. هل ترغب في استبدالها؟ | Manual edits detected on {0}. Do you wish to overwrite them? | Confirm Dialog |
| `WARN_UNSAVED_EDITS` | توجد تعديلات معلقة في خريطة ArcGIS Pro. يرجى حفظها أولاً. | Unsaved edits exist in ArcGIS Pro map session. Please save edits first. | Warning Banner |
| `INFO_PLOT_ID_FROM_TXT` | تم تحديث PLOT_ID لـ {0} قطعة من حقل PLOT_TXT. | Updated PLOT_ID for {0} parcels from PLOT_TXT field. | Console Info |
| `WARN_PLOT_TXT_MISSING` | حقل PLOT_TXT غير موجود في {0}؛ تم ترك PLOT_ID كما هو. | PLOT_TXT field not found in {0}; PLOT_ID left unchanged. | Console Warning |
| `WARN_DUP_PLOTS` | تحذير: تم رصد أرقام قطع مكررة ({0} قيمة): {1} | Warning: Duplicate PLOT_IDs detected ({0} values): {1} | Console Warning |
| `SUCCESS_EXPORT` | اكتمل التصدير بنجاح: تم حفظ المخرجات في مجلد Final. | Export completed successfully: Deliverables saved to Final folder. | Success Banner |
| `ERR_QA_FAILED` | فشل التحقق من جودة المخرجات: تم رصد {0} أخطاء مانعة. | QA audit failed: {0} blocking errors detected. | Error Modal |

---

## 5. Functional Requirements: User Interface

- **`REQ-UI-001` (Dockpane Registration):** The UI MUST register as `Cad2Gis_Dockpane` via `Config.daml`.
- **`REQ-UI-002` (Dark Workstation Theme):** The UI MUST implement the color tokens specified in Section 1.
- **`REQ-UI-003` (Two Source Radios):** Steps 2 through 5 MUST display exactly two radio source options.
- **`REQ-UI-004` (Source Status Banner):** Steps 2 through 5 MUST display a read-only source status banner.
- **`REQ-UI-005` (Non-Blocking UI):** Long-running tasks MUST execute off the UI thread with active progress updates (INV-08).
- **`REQ-UI-006` (Bilingual Message Catalog):** All alerts and dialogs MUST reference strings from Section 4.
- **`REQ-UI-007` (Dynamic Usage Refresh):** Step 1 MUST provide a button to refresh layer USAGE options dynamically.
