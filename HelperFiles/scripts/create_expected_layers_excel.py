import os
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.utils import get_column_letter

def create_expected_layers_excel(out_path):
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = "Expected Layers"

    # Ensure RTL for Arabic readability while keeping headers clear
    ws.views.sheetView[0].rightToLeft = False

    headers = ["Profile", "LayerName", "Role", "MatchType", "Notes"]
    ws.append(headers)

    # Professional styling
    header_font = Font(name="Segoe UI", size=11, bold=True, color="FFFFFF")
    header_fill = PatternFill(start_color="1E293B", end_color="1E293B", fill_type="solid")
    center_align = Alignment(horizontal="center", vertical="center")
    left_align = Alignment(horizontal="left", vertical="center")
    
    thin_border = Border(
        left=Side(style='thin', color='CBD5E1'),
        right=Side(style='thin', color='CBD5E1'),
        top=Side(style='thin', color='CBD5E1'),
        bottom=Side(style='thin', color='CBD5E1')
    )

    for col_num, header in enumerate(headers, 1):
        cell = ws.cell(row=1, column=col_num)
        cell.font = header_font
        cell.fill = header_fill
        cell.alignment = center_align

    # Standard CAD layer entries matching both MOMRAH standards and real CAD drawings
    data = [
        # Example_Profile / MOMRAH Standard
        ("Example_Profile", "01 residential", "Main parcel", "exact", "قطع أراضي سكنية رئيسية (Main Parcels)"),
        ("Example_Profile", "commercial", "Main parcel", "exact", "قطع أراضي تجارية رئيسية (Main Parcels)"),
        ("Example_Profile", "03 Res. Comm.", "Main parcel", "exact", "قطع سكني تجاري مختلط (Main Parcels)"),
        ("Example_Profile", "mixed Use", "Main parcel", "exact", "قطع متعددة الاستخدام (Main Parcels)"),
        ("Example_Profile", "parcel", "Main parcel", "exact", "حدود قطع الأراضي (Parcels Boundary)"),
        ("Example_Profile", "الاوجه التجارية", "Main parcel", "exact", "واجهات وأراضي تجارية (Commercial Frontages)"),
        ("Example_Profile", "11 SB_Plots", "Sub-parcel", "exact", "قطع خدمات ومرافق فرعية (Sub-parcels)"),
        ("Example_Profile", "06 Open Spaces", "Sub-parcel", "exact", "مساحات مفتوحة وحدائق (Open Spaces / Sub-parcels)"),
        ("Example_Profile", "garden", "Sub-parcel", "exact", "حدائق ومناطق خضراء (Gardens / Sub-parcels)"),
        ("Example_Profile", "Municipality Park", "Sub-parcel", "exact", "حدائق ومتنزهات البلدية (Municipality Parks)"),
        ("Example_Profile", "mosque", "Sub-parcel", "exact", "مساجد ومصليات (Mosques / Sub-parcels)"),
        ("Example_Profile", "Services", "Sub-parcel", "exact", "خدمات ومرافق عامة (Public Services)"),
        ("Example_Profile", "Municipal services", "Sub-parcel", "exact", "خدمات بلدية ومرافق حكومية"),
        ("Example_Profile", "Hospital", "Sub-parcel", "exact", "مستشفيات ومراكز صحية"),
        ("Example_Profile", "Substation", "Sub-parcel", "exact", "غرف ومحطات الكهرباء (Substations)"),
        ("Example_Profile", "Electromechanical", "Sub-parcel", "exact", "غرف كهروميكانيكية وخدمات"),
        ("Example_Profile", "10 Parking_Basement_Area", "Sub-parcel", "exact", "مواقف سيارات وقبو"),
        ("Example_Profile", "parking", "Sub-parcel", "exact", "مواقف سيارات عامة"),
        ("Example_Profile", "water canals", "Water Channel", "exact", "قنوات ومجاري مائية (Water Canals)"),
        ("Example_Profile", "boundary roads", "Road", "exact", "حدود الطرق والشوارع الرئيسية (Road Boundaries)"),
        ("Example_Profile", "pathes", "Road", "exact", "ممرات المشاة والطرق الداخلية (Walkways / Passages)"),
        ("Example_Profile", "DIM", "Dimensions", "exact", "أبعاد وأطوال الأضلاع والشطفات (Dimensions)"),
        ("Example_Profile", "number", "Plot-number text", "exact", "أرقام القطع المعتمدة (Parcel Numbers)"),
        ("Example_Profile", "Text", "Plot-number text", "exact", "نصوص ومسميات المخطط (Text & Annotations)"),
        ("Example_Profile", "Commercial_Text", "Plot-number text", "exact", "ترقيم الأنشطة والقطع التجارية"),
        ("Example_Profile", "Buffer_Zone", "Other", "exact", "مناطق الحماية والمصدات"),
        ("Example_Profile", "Setback", "Other", "exact", "خطوط التنظيم والارتدادات"),
        ("Example_Profile", "Deed", "Other", "exact", "حدود الصكوك الملكية"),
        ("Example_Profile", "0", "Other", "exact", "الطبقة الصفرية الافتراضية"),
        
        # Arabic MOMRAH Standard Profile
        ("MOMRAH_Standard", "حدود_القطع", "Main parcel", "exact", "حدود القطع السكنية والتجارية"),
        ("MOMRAH_Standard", "ارقام_القطع", "Plot-number text", "exact", "نصوص أرقام القطع"),
        ("MOMRAH_Standard", "استعمالات_الاراضي", "Usage text", "exact", "نصوص الاستعمالات والأنشطة"),
        ("MOMRAH_Standard", "شوارع", "Road", "exact", "حدود الشوارع المحيطة"),
        ("MOMRAH_Standard", "ممرات", "Road", "exact", "ممرات مشاة نافذة"),
        ("MOMRAH_Standard", "ابعاد", "Dimensions", "exact", "أبعاد الأضلاع والواجهات"),
        ("MOMRAH_Standard", "شطفات", "Dimensions", "exact", "أبعاد وزوايا الشطفات"),
        ("MOMRAH_Standard", "حدائق", "Sub-parcel", "exact", "حدائق ومساحات خضراء فرعية"),
        ("MOMRAH_Standard", "مساجد", "Sub-parcel", "exact", "مساجد ومصليات فرعية"),
        ("MOMRAH_Standard", "خدمات", "Sub-parcel", "exact", "مرافق وخدمات عامة"),
        ("MOMRAH_Standard", "قنوات_مائية", "Water Channel", "exact", "قنوات ومجاري سيول"),

        # General Wildcard Matches
        ("Default", "*PARCEL*", "Main parcel", "wildcard", "أي طبقة تحتوي على PARCEL"),
        ("Default", "*PLOT*", "Main parcel", "wildcard", "أي طبقة تحتوي على PLOT"),
        ("Default", "*ROAD*", "Road", "wildcard", "أي طبقة تحتوي على ROAD"),
        ("Default", "*STREET*", "Road", "wildcard", "أي طبقة تحتوي على STREET"),
        ("Default", "*DIM*", "Dimensions", "wildcard", "أي طبقة تحتوي على DIM")
    ]

    regular_font = Font(name="Segoe UI", size=10)
    alt_fill = PatternFill(start_color="F8FAFC", end_color="F8FAFC", fill_type="solid")
    white_fill = PatternFill(start_color="FFFFFF", end_color="FFFFFF", fill_type="solid")

    for row_idx, record in enumerate(data, 2):
        fill = alt_fill if row_idx % 2 == 0 else white_fill
        for col_idx, value in enumerate(record, 1):
            cell = ws.cell(row=row_idx, column=col_idx, value=value)
            cell.font = regular_font
            cell.fill = fill
            cell.border = thin_border
            if col_idx in [1, 3, 4]:
                cell.alignment = center_align
            else:
                cell.alignment = left_align

    # Column auto-fit with minimum widths
    min_widths = [18, 30, 20, 14, 45]
    for col_idx, min_w in enumerate(min_widths, 1):
        col_letter = get_column_letter(col_idx)
        max_len = max(len(str(ws.cell(row=r, column=col_idx).value or '')) for r in range(1, len(data) + 2))
        ws.column_dimensions[col_letter].width = max(max_len + 3, min_w)

    ws.row_dimensions[1].height = 28
    for r in range(2, len(data) + 2):
        ws.row_dimensions[r].height = 20

    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    wb.save(out_path)
    print(f"Created: {out_path} ({len(data)} rules)")

if __name__ == '__main__':
    _script_dir = os.path.dirname(os.path.abspath(__file__))
    _ws_root = os.path.abspath(os.path.join(_script_dir, "..", ".."))
    targets = [
        os.path.join(_ws_root, "HelperFiles", "expected_layers", "Expected_Layers.xlsx")
    ]
    for t in targets:
        create_expected_layers_excel(t)
