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
ref_border_shp = os.path.join(ref_folder, "Border.shp")

print("=== DEEP COMPARISON OF REFERENCE PARCEL.SHP VS DWG ===")

# Check extent and coordinates of reference shapefile
desc_shp = arcpy.Describe(ref_parcel_shp)
ext_shp = desc_shp.extent
print(f"Reference Parcel.shp Extent: XMin={ext_shp.XMin}, YMin={ext_shp.YMin}, XMax={ext_shp.XMax}, YMax={ext_shp.YMax}")
print(f"Spatial Ref: {desc_shp.spatialReference.name} (FactoryCode: {desc_shp.spatialReference.factoryCode})")

# Check extent and coordinates of DWG
dwg_polygon_fc = os.path.join(dwg_file, "Polygon")
desc_dwg = arcpy.Describe(dwg_polygon_fc)
ext_dwg = desc_dwg.extent
print(f"DWG Polygon Extent: XMin={ext_dwg.XMin}, YMin={ext_dwg.YMin}, XMax={ext_dwg.XMax}, YMax={ext_dwg.YMax}")
print(f"DWG Spatial Ref: {desc_dwg.spatialReference.name} (FactoryCode: {desc_dwg.spatialReference.factoryCode})")

# Sample 10 PLOT_IDs and USAGE from reference Parcel.shp
print("\nSample 10 Parcels from Reference Parcel.shp:")
with arcpy.da.SearchCursor(ref_parcel_shp, ['FID', 'PLOT_ID', 'BLOCK_NO', 'USAGE', 'AREA_SQM', 'NEIGHBOURH', 'ADDRESS']) as cursor:
    for i, row in enumerate(cursor):
        if i >= 10: break
        print(f"  FID={row[0]}: PLOT_ID='{row[1]}', BLOCK='{row[2]}', USAGE='{row[3]}', AREA={row[4]:.2f}, NEIGHBOURH='{row[5]}', ADDRESS='{row[6]}'")

# Check Border.shp fields and sample
desc_b = arcpy.Describe(ref_border_shp)
print(f"\nReference Border.shp Count: {arcpy.GetCount_management(ref_border_shp)[0]}")
fields_b = [f.name for f in arcpy.ListFields(ref_border_shp)]
print(f"Border Fields: {fields_b}")
print("Sample 10 Borders from Reference Border.shp:")
with arcpy.da.SearchCursor(ref_border_shp, ['FID', 'PLOT_ID', 'BORDERNAME', 'BORDERTYPE', 'BORDERDESC', 'DIMENSION', 'DESCRIPTIO', 'SHAPE_Leng']) as cursor:
    for i, row in enumerate(cursor):
        if i >= 10: break
        print(f"  FID={row[0]}: PLOT_ID='{row[1]}', BORDERNAME='{row[2]}', TYPE='{row[3]}', DESC='{row[4]}', DIM='{row[5]}', DESCRIPTIO='{row[6]}', LEN={row[7]:.2f}")
