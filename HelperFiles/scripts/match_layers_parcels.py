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
ref_parcel_shp = os.path.join(ref_folder, "Parcel.shp")

print("=== CHECKING DWG POLYGON LAYERS VS REFERENCE PARCELS ===")

dwg_polygon_fc = os.path.join(dwg_file, "Polygon")

# Count by layer in DWG Polygon
layer_counts = {}
with arcpy.da.SearchCursor(dwg_polygon_fc, ['Layer']) as cursor:
    for row in cursor:
        layer_counts[row[0]] = layer_counts.get(row[0], 0) + 1

print("\nAll Layers in DWG Polygon FC:")
for l, c in sorted(layer_counts.items(), key=lambda x: x[1], reverse=True):
    print(f"  {l:<35} : {c} polygons")

# Project a sample of Parcel.shp to UTM 37N to compare areas
utm37n = arcpy.SpatialReference(32637)

# Check total area of Reference Parcels vs DWG Polygons
ref_total_area = 0.0
ref_areas = []
with arcpy.da.SearchCursor(ref_parcel_shp, ['AREA_SQM']) as cursor:
    for row in cursor:
        ref_total_area += row[0]
        ref_areas.append(row[0])

print(f"\nReference Parcel Total Area: {ref_total_area:,.2f} m² across {len(ref_areas)} parcels")
print(f"Min Area: {min(ref_areas):.2f} m², Max Area: {max(ref_areas):.2f} m², Avg Area: {sum(ref_areas)/len(ref_areas):.2f} m²")

# Check layers that match parcel roles:
parcel_candidate_layers = ['01 residential', '03 Res. Comm.', 'commercial', 'mixed Use', '11 SB_Plots', '06 Open Spaces', 'parcel']
candidate_total = sum(layer_counts.get(l, 0) for l in parcel_candidate_layers)
print(f"\nCandidate Parcel Layers Count in DWG: {candidate_total} ({', '.join(f'{l}: {layer_counts.get(l, 0)}' for l in parcel_candidate_layers)})")
