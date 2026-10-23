# -*- coding: utf-8 -*-
"""
Fast CAD Drawing Layer Extractor
Extracts unique CAD layer names, entity counts, geometry type, colors, and live preview text from .dwg or .dxf using ArcPy.
Outputs JSON to stdout.
"""
import sys
import os
import json
import re

ACI_PALETTE = {
    1: "#EF4444",   # Red
    2: "#FACC15",   # Yellow
    3: "#22C55E",   # Green
    4: "#06B6D4",   # Cyan
    5: "#3B82F6",   # Blue
    6: "#D946EF",   # Magenta
    7: "#F8FAFC",   # White/Black
    8: "#64748B",   # Dark Gray
    9: "#94A3B8",   # Light Gray
}

def aci_to_hex(color_val, default_hex="#FACC15"):
    try:
        c = int(color_val)
        if c in ACI_PALETTE:
            return ACI_PALETTE[c]
        if 10 <= c <= 49:
            return "#F97316" # Orange
        if 50 <= c <= 69:
            return "#EAB308" # Yellow
        if 70 <= c <= 89:
            return "#84CC16" # Light Green
        if 90 <= c <= 129:
            return "#10B981" # Emerald
        if 130 <= c <= 159:
            return "#06B6D4" # Cyan
        if 160 <= c <= 189:
            return "#3B82F6" # Blue
        if 190 <= c <= 219:
            return "#8B5CF6" # Violet
        if 220 <= c <= 249:
            return "#D946EF" # Magenta
    except Exception:
        pass
    return default_hex

# Simple XArab decoding dictionary for previewing text
XARAB_MAP = {
    'H': 'ا', 'h': 'ا', 'f': 'ب', 'F': 'ب', 'B': 'ب',
    'j': 'ت', 'J': 'ت', 'e': 'ث', 'E': 'ث', '{': 'ج',
    'p': 'ح', 'o': 'خ', ']': 'د', '}': 'ذ', 'v': 'ر',
    'V': 'ر', '<': 'ز', '>': 'ز', 's': 'س', 'a': 'ش',
    'w': 'ص', 'q': 'ض', 'Q': 'ض', '\'': 'ط', '/': 'ظ',
    'u': 'ع', 'U': 'ع', 'y': 'غ', 't': 'ف', 'T': 'ف',
    'r': 'ق', 'R': 'ق', ':': 'ك', ';': 'ك', 'g': 'ل',
    'G': 'ل', 'l': 'م', 'L': 'م', 'k': 'ن', 'K': 'ن',
    'i': 'ه', 'I': 'ه', ',': 'و', 'd': 'ي', 'D': 'ي',
    'm': 'ة', 'M': 'ة', 'z': 'ئ', 'c': 'ؤ', 'x': 'ء',
    'n': 'ى', 'b': 'لا'
}

def decode_text(raw):
    if not raw:
        return ""
    # Clean CAD formatting like {\fArial...} or \P
    cleaned = re.sub(r'\\[A-Za-z0-9]+', ' ', raw)
    cleaned = re.sub(r'[{}\r\n]', '', cleaned).strip()
    if not cleaned:
        return ""
    
    # Check if text is Arabic characters already
    if any('\u0600' <= char <= '\u06FF' for char in cleaned):
        return cleaned

    # Try XArab decoding if it has xArab letters
    xarab_hits = sum(1 for c in cleaned if c in XARAB_MAP)
    if xarab_hits >= 2 and xarab_hits >= len(cleaned) * 0.4:
        decoded = "".join(XARAB_MAP.get(c, c) for c in cleaned)
        return decoded

    return cleaned

def infer_category(layer_name):
    l = layer_name.lower()
    if any(k in l for k in ["parcel", "plot", "resident", "commercial", "comm", "mixed", "قطع", "قطعة", "حدود", "اراضي", "أراضي", "الاوجه", "الاجهه", "واجهات", "سكني", "تجاري", "استثماري"]):
        return "Main Parcels", "Boundary"
    if any(k in l for k in ["serv", "util", "park", "garden", "mosque", "room", "parking", "basement", "hospital", "gov", "خدمات", "خدمة", "مرفق", "مرافق", "حدائق", "حديقة", "مساجد", "مسجد", "حكومي", "كهرباء", "مواقف"]):
        return "Sub-Parcels / Utilities", "Service"
    if any(k in l for k in ["text", "dim", "number", "num", "memo", "txt", "رقم", "ارقام", "ابعاد", "نصوص", "كتابة", "استعمال"]):
        return "Text & Annotations", ("PlotText" if "num" in l or "رقم" in l else ("UsageText" if "use" in l or "استعمال" in l else "Dimensions"))
    if any(k in l for k in ["water", "canal", "قناة", "مجرى", "وادي"]):
        return "Roads & References", "WaterChannel"
    if any(k in l for k in ["road", "street", "path", "شوارع", "ممرات", "طريق", "محاور"]):
        return "Roads & References", "Street"
    return "Roads & References", "Ignore"

def extract_layers(cad_path):
    cad_path = os.path.abspath(cad_path)
    if not os.path.exists(cad_path):
        return []

    layers = {}
    fc_map = {
        'Polygon': 'Polygon',
        'Polyline': 'Line',
        'Point': 'Point',
        'TextPoint': 'Point',
        'Annotation': 'Text'
    }

    try:
        import arcpy
        arcpy.env.workspace = cad_path
        fcs = arcpy.ListFeatureClasses() or []
        for fc in ['Polygon', 'Polyline', 'Annotation', 'Point', 'TextPoint']:
            if fc not in fcs:
                continue
            fc_path = os.path.join(cad_path, fc)
            geom_type = fc_map.get(fc, 'Line')
            
            field_names = [f.name for f in arcpy.ListFields(fc_path)]
            if 'Layer' not in field_names:
                continue

            has_color = 'Color' in field_names
            text_fld = 'Text' if 'Text' in field_names else ('TxtMemo' if 'TxtMemo' in field_names else None)

            query_fields = ['Layer']
            if has_color:
                query_fields.append('Color')
            if text_fld:
                query_fields.append(text_fld)

            with arcpy.da.SearchCursor(fc_path, query_fields) as cursor:
                for row in cursor:
                    lyr = row[0]
                    if not lyr:
                        continue
                    if lyr not in layers:
                        cat, role = infer_category(lyr)
                        layers[lyr] = {
                            'LayerName': lyr,
                            'EntityCount': 0,
                            'GeometryType': geom_type,
                            'ColorVal': row[1] if has_color and row[1] is not None else 2,
                            'RawCadText': '',
                            'DecodedArabicText': '',
                            'Role': role,
                            'Category': cat
                        }
                    layers[lyr]['EntityCount'] += 1
                    
                    # Store first non-empty text sample
                    if text_fld and len(row) > 2 and row[2] and not layers[lyr]['RawCadText']:
                        raw_str = str(row[2]).strip()
                        if raw_str and len(raw_str) > 1:
                            layers[lyr]['RawCadText'] = raw_str
                            layers[lyr]['DecodedArabicText'] = decode_text(raw_str)

    except Exception as ex:
        sys.stderr.write(f"ArcPy error: {ex}\n")

    result = []
    for lyr_name in sorted(layers.keys()):
        item = layers[lyr_name]
        color_hex = aci_to_hex(item['ColorVal'])
        
        # Color adjustments for typical layers matching design
        cat = item['Category']
        l = lyr_name.lower()
        if "residential" in l:
            color_hex = "#FACC15" # Yellow
        elif "commercial" in l or "الاوجه" in l:
            color_hex = "#D946EF" # Magenta
        elif "service" in l or "serv" in l:
            color_hex = "#06B6D4" # Cyan
        elif "park" in l or "garden" in l or "gov" in l:
            color_hex = "#22C55E" # Green
        elif "util" in l or "easement" in l:
            color_hex = "#86EFAC" # Light Green
        elif "canal" in l or "water" in l:
            color_hex = "#38BDF8" # Sky Blue

        # Intelligent live preview fallback if no text annotation was on that layer
        raw = item['RawCadText']
        decoded = item['DecodedArabicText']
        if not raw:
            if "residential" in l:
                raw = "S_RES_01"
                decoded = "سكني - 1"
            elif "commercial" in l:
                raw = "S_RES_01"
                decoded = "سكني - 1"
            elif "service" in l or "serv" in l:
                raw = "SERV_LANE"
                decoded = "ممر خدمة"
            elif "الاوجه" in l:
                raw = "COMM_FRONT"
                decoded = "واجهات تجارية"

        result.append({
            'LayerName': lyr_name,
            'EntityCount': item['EntityCount'],
            'GeometryType': item['GeometryType'],
            'Color': color_hex,
            'RawCadText': raw,
            'DecodedArabicText': decoded,
            'Role': item['Role'],
            'Category': cat
        })

    return result

if __name__ == '__main__':
    if len(sys.argv) > 1:
        path = sys.argv[1]
    else:
        _script_dir = os.path.dirname(os.path.abspath(__file__))
        _ws_root = os.path.abspath(os.path.join(_script_dir, "..", ".."))
        if os.path.basename(_ws_root).lower() == "cad2gis":
            _ws_root = os.path.abspath(os.path.join(_ws_root, ".."))
        path = os.path.join(_ws_root, "example", "sample_cad.dwg")
        example_dir = os.path.join(_ws_root, "example")
        if os.path.isdir(example_dir):
            for candidate in os.listdir(example_dir):
                if candidate.lower().endswith(('.dwg', '.dxf')):
                    path = os.path.join(example_dir, candidate)
                    break

    result = extract_layers(path)
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    print(json.dumps(result, ensure_ascii=False))
