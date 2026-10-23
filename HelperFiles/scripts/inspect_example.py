# -*- coding: utf-8 -*-
import os
import sys
import json
import arcpy

sys.stdout.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')

_script_dir = os.path.dirname(os.path.abspath(__file__))
_ws_root = os.path.abspath(os.path.join(_script_dir, "..", ".."))
if os.path.basename(_ws_root).lower() == "cad2gis":
    _ws_root = os.path.abspath(os.path.join(_ws_root, ".."))
ref_folder = os.path.join(_ws_root, "example", "reference_shapefiles")
dwg_file = os.path.join(_ws_root, "example", "sample_cad.dwg")
example_dir = os.path.join(_ws_root, "example")
if os.path.isdir(example_dir):
    for candidate in os.listdir(example_dir):
        if candidate.lower().endswith(('.dwg', '.dxf')):
            dwg_file = os.path.join(example_dir, candidate)
            break

print("==================================================")
print("  INSPECTING REFERENCE SHAPEFILES & DWG DRAWING   ")
print("==================================================")

# 1. Reference Shapefiles
for name in ['Parcel.shp', 'Border.shp']:
    shp = os.path.join(ref_folder, name)
    if not os.path.exists(shp):
        print(f"File not found: {shp}")
        continue
    print(f"\n--- {name} ---")
    desc = arcpy.Describe(shp)
    print(f"Spatial Reference: {desc.spatialReference.name} (EPSG: {desc.spatialReference.factoryCode})")
    print(f"Shape Type: {desc.shapeType}")
    count = int(arcpy.GetCount_management(shp)[0])
    print(f"Total Feature Count: {count}")
    
    fields = arcpy.ListFields(shp)
    field_names = [f.name for f in fields]
    print(f"Fields ({len(fields)}): {', '.join(field_names)}")
    
    # Analyze unique values of key fields
    key_fields = [f for f in ['PLAN_NO', 'NEIGHBOURH', 'ADDRESS', 'REGION_ID', 'CITY_ID', 'USAGE', 'REALESTA_1', 'REQUEST_TY', 'BORDER_NO', 'BORDER_TYP', 'BORDER_LEN', 'BORDER_DES'] if f in field_names]
    
    print("\nSample records (first 5):")
    with arcpy.da.SearchCursor(shp, key_fields) as cursor:
        for i, row in enumerate(cursor):
            if i >= 5: break
            d = dict(zip(key_fields, row))
            print(f"  [{i+1}] {json.dumps(d, ensure_ascii=False)}")
            
    # Distinct values summary
    for kf in ['PLAN_NO', 'NEIGHBOURH', 'REGION_ID', 'CITY_ID', 'USAGE', 'REALESTA_1']:
        if kf in field_names:
            vals = set()
            with arcpy.da.SearchCursor(shp, [kf]) as cursor:
                for row in cursor:
                    vals.add(str(row[0]))
            print(f"Distinct {kf} ({len(vals)}): {list(vals)[:8]}")

# 2. Inspect the DWG file structure
print("\n--- DWG Drawing Inspection ---")
print(f"DWG Path: {dwg_file}")
arcpy.env.workspace = dwg_file
dwg_fcs = arcpy.ListFeatureClasses() or []
print(f"Feature Classes in DWG: {dwg_fcs}")

for fc in dwg_fcs:
    fc_path = os.path.join(dwg_file, fc)
    fc_desc = arcpy.Describe(fc_path)
    fc_count = int(arcpy.GetCount_management(fc_path)[0])
    print(f"\nDWG FC: {fc} (Shape: {fc_desc.shapeType}, Count: {fc_count})")
    
    # Layer distribution
    layer_counts = {}
    with arcpy.da.SearchCursor(fc_path, ['Layer']) as cursor:
        for row in cursor:
            l = row[0]
            layer_counts[l] = layer_counts.get(l, 0) + 1
    top_layers = sorted(layer_counts.items(), key=lambda x: x[1], reverse=True)[:10]
    print(f"  Top Layers in {fc}: {top_layers}")
