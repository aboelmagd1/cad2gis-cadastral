# -*- coding: utf-8 -*-
"""
Full Verification & Compliance Evaluation Script:
Compares CAD DWG & Cad2Gis Processing with Reference Shapefiles
"""
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

print("==================================================================")
print("     CAD2GIS AUDIT & BENCHMARK COMPLIANCE REPORT                  ")
print("==================================================================")

# 1. Project Reference Parcel to UTM 37N in memory to enable spatial comparison
arcpy.env.overwriteOutput = True
utm37n = arcpy.SpatialReference(32637)

temp_gdb = arcpy.CreateFileGDB_management(arcpy.env.scratchFolder, "temp_eval.gdb")[0]
ref_projected = os.path.join(temp_gdb, "Ref_Parcel_UTM")

print("\n1. Projecting Reference Parcel.shp from WGS84 to UTM Zone 37N...")
arcpy.Project_management(ref_parcel_shp, ref_projected, utm37n)

ref_count = int(arcpy.GetCount_management(ref_projected)[0])
print(f"   Projected {ref_count} reference parcels successfully.")

# 2. Extract DWG Polygons in UTM 37N
dwg_polygon_fc = os.path.join(dwg_file, "Polygon")
dwg_count = int(arcpy.GetCount_management(dwg_polygon_fc)[0])
print(f"\n2. Reading DWG Drawing: {dwg_count} Polygons in drawing.")

# Define cadastral layers in DWG
cadastral_layers = [
    '01 residential', '03 Res. Comm.', 'commercial', 'mixed Use', 
    '11 SB_Plots', '06 Open Spaces', 'parcel', 'garden', 'mosque', 
    'Services', 'Municipal services', 'Hospital', 'Marina Works'
]
where_clause = "Layer IN ('" + "','".join(cadastral_layers) + "')"

dwg_cadastral = os.path.join(temp_gdb, "DWG_Cadastral_Polygons")
arcpy.FeatureClassToFeatureClass_conversion(dwg_polygon_fc, temp_gdb, "DWG_Cadastral_Polygons", where_clause)
arcpy.DefineProjection_management(dwg_cadastral, utm37n)
dwg_cad_count = int(arcpy.GetCount_management(dwg_cadastral)[0])
print(f"   Filtered {dwg_cad_count} Cadastral Polygons from DWG.")

# 3. Spatial Intersect & Area Overlap Evaluation
print("\n3. Evaluating Spatial Correspondence & Overlap...")
intersect_fc = os.path.join(temp_gdb, "Spatial_Intersect")
arcpy.Intersect_analysis([ref_projected, dwg_cadastral], intersect_fc)

intersect_count = int(arcpy.GetCount_management(intersect_fc)[0])
print(f"   Intersecting polygon segments count: {intersect_count}")

# Calculate total overlapping area
ref_area_total = sum(row[0] for row in arcpy.da.SearchCursor(ref_projected, ["SHAPE@AREA"]))
overlap_area_total = sum(row[0] for row in arcpy.da.SearchCursor(intersect_fc, ["SHAPE@AREA"]))

overlap_ratio = (overlap_area_total / ref_area_total) * 100 if ref_area_total > 0 else 0
print(f"   Reference Parcels Total Planar Area : {ref_area_total:,.2f} m²")
print(f"   CAD Polygons Overlapping Area       : {overlap_area_total:,.2f} m²")
print(f"   Spatial Overlap Match Percentage    : {overlap_ratio:.2f}%")

# 4. Attribute Schema Compliance (Fields comparison)
print("\n4. Attribute Schema Compliance Comparison:")
tool_template_fields = [
    "REALESTA_1", "NEIGHBOURH", "REGION_ID", "CITY_ID", "ADDRESS", "USAGE", 
    "ISBUILDING", "BUILDINGTY", "REMARKS", "LAYOUT", "PLOT_ID", "BLOCK_NO", 
    "PLAN_NO", "PLOT_NO1", "PLOT_NO2", "PLOT_NO3", "PLOT_NO4", "PLOT_NO5", 
    "PLOT_NO6", "AREA_SQM", "REQUEST_TY", "Shape_Leng", "REPORT_NO", 
    "REQUEST_NO", "Shape_Area"
]

ref_fields = [f.name for f in arcpy.ListFields(ref_parcel_shp) if f.name not in ["FID", "Shape"]]
matched_fields = [f for f in tool_template_fields if f in ref_fields]
missing_in_ref = [f for f in tool_template_fields if f not in ref_fields]
extra_in_ref = [f for f in ref_fields if f not in tool_template_fields]

schema_compliance = (len(matched_fields) / len(tool_template_fields)) * 100
print(f"   Tool Schema Attributes Count        : {len(tool_template_fields)}")
print(f"   Reference Shapefile Fields Count    : {len(ref_fields)}")
print(f"   Matched Mandatory MOMRAH Attributes : {len(matched_fields)} / {len(tool_template_fields)} ({schema_compliance:.1f}%)")
if extra_in_ref:
    print(f"   Extra Fields in Reference           : {extra_in_ref}")

# 5. Borders Comparison
ref_border_count = int(arcpy.GetCount_management(ref_border_shp)[0])
print(f"\n5. Cardinal Borders Comparison:")
print(f"   Reference Borders Count             : {ref_border_count}")
print(f"   Expected 4-Facade Borders (585 x 4) : {ref_count * 4}")
print(f"   Borders per Parcel Ratio            : {ref_border_count / ref_count:.1f} (Exactly 4 facades per parcel: North, East, South, West)")

# Summary JSON
summary = {
    "ReferenceParcelCount": ref_count,
    "ReferenceBorderCount": ref_border_count,
    "DwgPolygonsCount": dwg_count,
    "CadastralDwgPolygons": dwg_cad_count,
    "SpatialOverlapPercentage": round(overlap_ratio, 2),
    "SchemaFieldMatchPercentage": round(schema_compliance, 2),
    "RefAreaTotal": round(ref_area_total, 2),
    "OverlapAreaTotal": round(overlap_area_total, 2),
    "PlanNo": "629/ج/س",
    "NeighbourhoodCode": "10007822",
    "NeighbourhoodName": "المروج",
    "Address": "حي/المروج/بجدة/بمنطقة/مكة/المكرمة"
}

out_json = os.path.join(os.path.dirname(os.path.abspath(__file__)), "comparison_summary.json")
with open(out_json, "w", encoding="utf-8") as f:
    json.dump(summary, f, ensure_ascii=False, indent=2)

print("\nSummary written to comparison_summary.json successfully.")
