import os
import sys
import json
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side

sys.stdout.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')

_script_dir = os.path.dirname(os.path.abspath(__file__))
ws_root = os.path.abspath(os.path.join(_script_dir, "..", ".."))
if not os.path.exists(os.path.join(ws_root, "HelperFiles")):
    ws_root = os.path.abspath(os.path.join(_script_dir, "..", "..", ".."))
root_helper = os.path.join(ws_root, "HelperFiles")

print(f"Target HelperFiles: {root_helper}")

# 1. Update settings.json in both locations
full_settings = {
  "helperFolderPath": "",
  "sharedProfilesFolderPath": "",
  "uiLanguage": "en",
  "lastUsedProfile": "Default_Consultant_Profile",
  "lastOutputFolder": "",
  "defaultUtmZone": 32637,
  "defaultClosureToleranceMeters": 0.05,
  "defaultPassageStreetThresholdMeters": 10.0,
  "defaultMaxRayDistanceMeters": 100.0,
  "defaultRayCount": 3,
  "areaToleranceSqm": 0.5,
  "areaTolerancePercent": 0.1,
  "chamferThresholdMeters": 7.0,
  "chamferAngleDeg": 45.0,
  "chamferAngleToleranceDeg": 15.0,
  "runAllStepsMode": True,
  "pauseAtReviewTables": True,
  "logRetentionDays": 30,

  "aMinDeg": 10.0,
  "aMaxDeg": 160.0,
  "multiSegmentAngleDeviationDeg": 10.0,
  "includeReflexCorners": True,
  "arcCenterToleranceMeters": 0.05,
  "arcRadiusToleranceMeters": 0.05,
  "arcTangentToleranceDeg": 2.0,
  "clusterLengthMeters": 7.0,
  "rule1LengthRatioGuard": 1.0,
  "trueNorthCorrection": True,

  "continueFromLatestExport": True,
  "maxExportHistoryCount": 10
}

for base in [root_helper]:
    p = os.path.join(base, "settings.json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump(full_settings, f, indent=2, ensure_ascii=False)
    print(f"Updated: {p}")

# 2. Update Example_Profile.json in both locations
full_profile_json = {
  "profileName": "Default_Consultant_Profile",
  "description": "Standard cadastral transformation profile with fictional defaults",
  "boundaryLayer": "PARCEL_BOUNDARY",
  "plotNoTextLayer": "PLOT_NUMBERS",
  "usageTextLayer": "LAND_USE",
  "subParcelLayer": "SUB_PARCELS",
  "roadLayer": "ROADS",
  "dimensionsLayer": "DIMENSIONS",
  "directionSystem": "MOMRAH",
  "passageStreetThresholdMeters": 10.0,
  "maxRaySearchDistanceMeters": 100.0,
  "rayCount": 3,
  "rayMethod": "Median",
  "snapToleranceMeters": 0.05,
  "mergeChamfers": True,
  "chamferThresholdMeters": 7.0,
  "chamferAngleDeg": 45.0,
  "chamferAngleToleranceDeg": 15.0,
  "areaToleranceSqm": 0.5,
  "areaTolerancePercent": 0.1,
  "buildPolygonsFromLines": False,
  "selectedPolygonLayerScope": "MainAndSub",
  "defaultPlanNo": "0000/0",
  "defaultNeighbourhood": "الرياض",
  "defaultRequestType": "فرز",
  "defaultReportNo": "0",
  "utmEpsg": 32637,
  "useArabicXArabDecoder": True,
  "reverseArabicText": False,

  "aMinDeg": 10.0,
  "aMaxDeg": 160.0,
  "multiSegmentAngleDeviationDeg": 10.0,
  "includeReflexCorners": True,
  "arcCenterToleranceMeters": 0.05,
  "arcRadiusToleranceMeters": 0.05,
  "arcTangentToleranceDeg": 2.0,
  "clusterLengthMeters": 7.0,
  "rule1LengthRatioGuard": 1.0,
  "trueNorthCorrection": True,

  "continueFromLatestExport": True,
  "maxExportHistoryCount": 10
}

for base in [root_helper]:
    p = os.path.join(base, "profiles", "Example_Profile.json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump(full_profile_json, f, indent=2, ensure_ascii=False)
    print(f"Updated: {p}")

# 3. Update Example_Profile.xlsx in both locations
additional_rows = [
    ("aMinDeg", "10.0", "الزاوية الداخلية الصغرى للأركان", "الزاوية الداخلية الصغرى بالدرجات لاعتبار الرأس نقطة كسر للركن", "10.0 إلى 45.0", "معالجة الحدود"),
    ("aMaxDeg", "160.0", "الزاوية الداخلية القصوى للأركان", "الزاوية الداخلية القصوى بالدرجات لاعتبار الرأس نقطة كسر للركن", "90.0 إلى 160.0", "معالجة الحدود"),
    ("multiSegmentAngleDeviationDeg", "10.0", "تسامح تمييز الحدود المتعددة (Multi)", "انحراف الزاوية بين القطع المتتالية عن 180° لاعتبار الحد Multi (أقل من 170° أو أكثر من 190°)", "5.0 إلى 30.0", "معالجة الحدود"),
    ("includeReflexCorners", "True", "تضمين الأركان المنعكسة المقعرة", "كسر الأركان المنعكسة المقعرة (الزاوية > 180 مثل حرف L) في النطاق المعكوس [360-A_max, 360-A_min]", "True أو False", "معالجة الحدود"),
    ("arcCenterToleranceMeters", "0.05", "تسامح مركز القوس (متر)", "التسامح بالأمتار بين مركزي القوسين لاعتبارهما قوساً متصلاً لنفس الدائرة وتجاوز كسر المفصل", "0.01 إلى 0.20", "معالجة الحدود"),
    ("arcRadiusToleranceMeters", "0.05", "تسامح نصف قطر القوس (متر)", "التسامح بالأمتار في نصف القطر بين القوسين المتصلين", "0.01 إلى 0.20", "معالجة الحدود"),
    ("arcTangentToleranceDeg", "2.0", "تسامح المماس للأقواس (درجة)", "أقصى فرق مسموح به بين مماسي القوسين عند نقطة الالتقاء لاعتباره اتصالاً ناعماً بدون كسر", "0.5 إلى 5.0", "معالجة الحدود"),
    ("clusterLengthMeters", "7.0", "طول عنقود الشطفات والأركان (متر)", "الحد الأقصى للمسافة بين الرؤوس المتتالية لمعاملتها كشطفة أو ركن مدمج L", "3.0 إلى 12.0", "معالجة الحدود"),
    ("rule1LengthRatioGuard", "1.0", "معامل أمان دمج القطع البينية (Rule 1)", "الحد الأقصى لنسبة طول القطعة البينية مقارنة بالقطعتين المحيطتين لدمجها تلقائياً", "0.5 إلى 2.0", "معالجة الحدود"),
    ("trueNorthCorrection", "True", "تصحيح الشمال الحقيقي", "تصحيح زاوية الشمال بانحراف خط الطول (Meridian Convergence) عند مركز القطعة", "True أو False", "معالجة الحدود"),
    ("continueFromLatestExport", "True", "المتابعة من آخر تصدير", "تفعيل خيار المتابعة من آخر تصدير مع الحفاظ على التعديلات اليدوية افتراضياً", "True أو False", "استمرارية العمل"),
    ("maxExportHistoryCount", "10", "الحد الأقصى لسجل التصديرات", "عدد التصديرات السابقة المتاحة في القائمة المنسدلة للتراجع والتبديل", "3 إلى 30", "استمرارية العمل"),
    ("selectedPolygonLayerScope", "MainAndSub", "نطاق طبقات المضلعات", "MainAndSub (كل الطبقات المختارة رئيسي وفرعي) أو MainOnly (الرئيسية فقط)", "MainAndSub أو MainOnly", "استخراج المضلعات")
]

thin_border = Border(
    left=Side(style='thin', color='CBD5E1'),
    right=Side(style='thin', color='CBD5E1'),
    top=Side(style='thin', color='CBD5E1'),
    bottom=Side(style='thin', color='CBD5E1')
)
reg_font = Font(name="Segoe UI", size=10)
alt_fill = PatternFill(start_color="F8FAFC", end_color="F8FAFC", fill_type="solid")
white_fill = PatternFill(start_color="FFFFFF", end_color="FFFFFF", fill_type="solid")
center_align = Alignment(horizontal="center", vertical="center")
left_align = Alignment(horizontal="left", vertical="center")

for base in [root_helper]:
    xlsx_path = os.path.join(base, "profiles", "Example_Profile.xlsx")
    wb = openpyxl.load_workbook(xlsx_path)
    ws = wb['Profile_Settings'] if 'Profile_Settings' in wb.sheetnames else wb.active
    
    # Check existing keys
    existing_keys = set()
    for r in range(2, ws.max_row + 1):
        k = ws.cell(r, 1).value
        if k:
            existing_keys.add(str(k).strip())
            
    cur_row = ws.max_row + 1
    for row_data in additional_rows:
        key = row_data[0]
        if key in existing_keys:
            continue
        fill = alt_fill if cur_row % 2 == 0 else white_fill
        for c_idx, val in enumerate(row_data, 1):
            cell = ws.cell(row=cur_row, column=c_idx, value=val)
            cell.font = reg_font
            cell.fill = fill
            cell.border = thin_border
            if c_idx in [1, 2, 5, 6]:
                cell.alignment = center_align
            else:
                cell.alignment = left_align
        ws.row_dimensions[cur_row].height = 20
        cur_row += 1
        
    wb.save(xlsx_path)
    print(f"Updated xlsx: {xlsx_path} (now {ws.max_row} rows)")

# 4. Update profiles/README.md in both locations
readme_content = """# دليل بروفايلات المكاتب الاستشارية (CAD2GIS Consultant Profiles)

يحتوي هذا المجلد على بروفايلات الإعدادات المخصصة للمكاتب الاستشارية والمساحية في ملفات **Excel (.xlsx)** و **JSON**. 
يمكنك فتح ملف **[Example_Profile.xlsx](Example_Profile.xlsx)** مباشرة في برنامج Excel وتعديل أسماء الطبقات والمعايير بما يناسب ملفات الأوتوكاد الخاصة بمشروعك.

---

## 📑 هيكل ملف الإكسل `Example_Profile.xlsx`

يحتوي ملف الإكسل على ورقتي عمل (Sheets):

### 1. ورقة الإعدادات الرئيسية: `Profile_Settings`
تحتوي على الأعمدة التالية:
- **`SettingKey`**: مفتاح الإعداد البرمجي بالإنجليزية (يرجى عدم تغيير هذا العمود).
- **`SettingValue`**: القيمة الفعلية القابلة للتعديل حسب مشروعك.
- **`DisplayName_Ar`**: اسم الإعداد بالعربي.
- **`Description_Ar`**: شرح تفصيلي وافٍ لوظيفة الإعداد وكيفية اختياره.
- **`AllowedValues`**: القيم أو الخيارات المسموح بها.
- **`Category`**: تصنيف الإعداد (طبقات الكاد، معالجة الهندسة، استشعار الشوارع، معالجة الحدود، استمرارية العمل).

#### جدول مفاتيح الإعدادات الكامل:

| SettingKey | القيمة الافتراضية | الوصف بالعربي | التصنيف |
| :--- | :--- | :--- | :--- |
| `profileName` | `Default_Consultant_Profile` | اسم البروفايل لتمييزه في القائمة المنسدلة | عام |
| `description` | بروفايل معالجة المخططات الكاداسترالية ومطابقة بلدي | وصف موجز للغرض من البروفايل | عام |
| `boundaryLayer` | `PARCEL_BOUNDARY` | اسم طبقة حدود القطع الرئيسية في الكاد | طبقات الكاد |
| `plotNoTextLayer` | `PLOT_NUMBERS` | اسم طبقة نصوص أرقام القطع | طبقات الكاد |
| `usageTextLayer` | `LAND_USE` | اسم طبقة نصوص نوع الاستعمال والنشاط | طبقات الكاد |
| `subParcelLayer` | `SUB_PARCELS` | اسم طبقة القطع والخدمات الفرعية | طبقات الكاد |
| `roadLayer` | `ROADS` | اسم طبقة مضلعات وخطوط الشوارع | طبقات الكاد |
| `dimensionsLayer` | `DIMENSIONS` | اسم طبقة خطوط الأبعاد والشطفات | طبقات الكاد |
| `directionSystem` | `MOMRAH` | نظام ترقيم الاتجاهات: `MOMRAH` (1=شمال، 2=شرق، 3=جنوب، 4=غرب) | معالجة الحدود |
| `passageStreetThresholdMeters` | `10.0` | الحد الفاصل بين الممر (`LNE`) والشارع (`STR`) بالأمتار | استشعار الشوارع |
| `maxRaySearchDistanceMeters` | `100.0` | أقصى مسافة لأشعة استشعار الحد المقابل للشارع بالأمتار | استشعار الشوارع |
| `rayCount` | `3` | عدد الأشعة العمودية المنطلقة لحساب عرض الشارع | استشعار الشوارع |
| `rayMethod` | `Median` | طريقة حساب عرض الشارع (`Median` أو `Average`) | استشعار الشوارع |
| `snapToleranceMeters` | `0.05` | تسامح التوصيل لبناء المضلعات من الخطوط المفتوحة (5 سم) | هندسة |
| `mergeChamfers` | `True` | دمج أضلاع الشطفات مع الضلع الرئيسي الملاصق | هندسة |
| `chamferThresholdMeters` | `7.0` | الحد الأقصى لطول ضلع الشطفة بالأمتار | هندسة |
| `buildPolygonsFromLines` | `False` | تفعيل وضع بناء المضلعات من الخطوط | استخراج المضلعات |
| `selectedPolygonLayerScope` | `MainAndSub` | نطاق الطبقات: كل الطبقات المختارة (`MainAndSub`) أو الرئيسية فقط (`MainOnly`) | استخراج المضلعات |
| `aMinDeg` | `20.0` | الزاوية الداخلية الصغرى لنقطة كسر الركن | معالجة الحدود (Part A) |
| `aMaxDeg` | `130.0` | الزاوية الداخلية القصوى لنقطة كسر الركن | معالجة الحدود (Part A) |
| `includeReflexCorners` | `True` | تضمين الأركان المقعرة المنعكسة في النطاق [360-A_max, 360-A_min] | معالجة الحدود (Part A) |
| `arcCenterToleranceMeters` | `0.05` | تسامح تطابق مركزي القوسين بالأمتار لتجاوز الكسر الزائف | معالجة الحدود (Part A) |
| `arcRadiusToleranceMeters` | `0.05` | تسامح تطابق نصف قطر القوسين بالأمتار | معالجة الحدود (Part A) |
| `arcTangentToleranceDeg` | `2.0` | تسامح مماس اتصال الأقواس بالدرجات | معالجة الحدود (Part A) |
| `clusterLengthMeters` | `7.0` | طول عنقود الشطفات والأركان المتتالية L بالأمتار | معالجة الحدود (Part A) |
| `rule1LengthRatioGuard` | `1.0` | معامل أمان دمج القطع البينية Rule 1 | معالجة الحدود (Part A) |
| `trueNorthCorrection` | `True` | تصحيح الشمال الحقيقي بانحراف خط الطول UTM | معالجة الحدود (Part A) |
| `continueFromLatestExport` | `True` | تفعيل المتابعة التلقائية من آخر تصدير مع حفظ التعديلات اليدوية | استمرارية العمل (Part B) |
| `maxExportHistoryCount` | `10` | الحد الأقصى لسجل التصديرات المحفوظة للتبديل والتراجع | استمرارية العمل (Part B) |

---

### 2. ورقة دليل أدوار الطبقات: `Expected_Layers_Guide`
توضح الأدوار الكاداسترالية القياسية:
- **`Boundary`**: مضلعات قطع الأراضي الرئيسية المصدرة في `PARCEL.shp`.
- **`SubParcel`**: القطع الفرعية (ممرات، حدائق، ارتدادات).
- **`PlotText`**: نصوص أرقام القطع.
- **`UsageText`**: نصوص نوع الاستعمال والنشاط.
- **`Street`**: مضلعات ومحاور الشوارع.
- **`Dimensions`**: خطوط الأبعاد والشطفات.
- **`Service`**: المرافق العامة.
"""

for base in [root_helper]:
    p = os.path.join(base, "profiles", "README.md")
    with open(p, "w", encoding="utf-8") as f:
        f.write(readme_content)
    print(f"Updated README: {p}")

# 5. Create state/ directory and README.md in both locations
state_readme = """# CAD2GIS Export State Management (Part B)

This directory maintains runtime continuity state records for the CAD2GIS Add-in.

## Key Files:
- `last_export.json`: Pointer to the most recent successful export run, containing:
  - Run ID, timestamp, and profile used
  - Working UTM zone and export CRS
  - Exported shapefile / feature class paths
  - Dataset hashes, modification timestamps, and record counts
- `export_history.json`: Rolling history of the last N exports (configurable, default 10) allowing users to switch back to older exports.
"""

for base in [root_helper]:
    state_dir = os.path.join(base, "state")
    os.makedirs(state_dir, exist_ok=True)
    p = os.path.join(state_dir, "README.md")
    with open(p, "w", encoding="utf-8") as f:
        f.write(state_readme)
    print(f"Created state dir and README: {p}")

# 6. Create valid demo Areas_Demo.xlsx in both locations
def create_demo_areas_excel(path):
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = "Parcels Area"
    headers = ["PARCEL_NO", "AREA_SQM", "LAND_USE", "NOTES"]
    ws.append(headers)
    
    header_fill = PatternFill(start_color="1E293B", end_color="1E293B", fill_type="solid")
    header_font = Font(name="Segoe UI", size=11, bold=True, color="FFFFFF")
    for col in range(1, 5):
        cell = ws.cell(1, col)
        cell.fill = header_fill
        cell.font = header_font
        cell.alignment = center_align
        
    demo_data = [
        ("101", 600.0, "RSD", "قطعة سكنية رئيسية"),
        ("102", 600.0, "COM", "قطعة تجارية رئيسية"),
        ("103", 400.0, "PAK", "حديقة فرعية"),
        ("104", 180.0, "SRV", "ممر خدمات")
    ]
    for row_idx, record in enumerate(demo_data, 2):
        for col_idx, val in enumerate(record, 1):
            cell = ws.cell(row=row_idx, column=col_idx, value=val)
            cell.font = reg_font
            cell.border = thin_border
            cell.alignment = center_align if col_idx in [1, 3] else left_align
            
    ws.column_dimensions['A'].width = 18
    ws.column_dimensions['B'].width = 16
    ws.column_dimensions['C'].width = 16
    ws.column_dimensions['D'].width = 30
    wb.save(path)
    print(f"Created Demo Areas Excel: {path}")

for base in [root_helper]:
    p = os.path.join(base, "demo", "Areas_Demo.xlsx")
    create_demo_areas_excel(p)

print("\nHelperFiles synchronization and update complete!")
