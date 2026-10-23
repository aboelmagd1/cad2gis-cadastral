# -*- coding: utf-8 -*-
"""
CAD2GIS ArcPy Line to Polygon Tool (Production v3.3.0)
Converts 2D line networks and polyline boundary rings to clean cadastral parcels
adhering to MOMRAH / Balady standards (2D Planar, Z/M Disabled).
"""

import sys
import os
import json
import argparse

def main():
    parser = argparse.ArgumentParser(description="CAD2GIS ArcPy Line to Polygon Tool")
    parser.add_argument("--gdb", required=True, help="Path to File Geodatabase (.gdb)")
    parser.add_argument("--dataset", default="Cad_Dataset", help="Feature dataset name containing lines")
    parser.add_argument("--lines_fc", default="", help="Specific lines feature class (optional)")
    parser.add_argument("--out_fc", default="PARCEL_Lines_Polygons", help="Output polygon feature class name or path")
    parser.add_argument("--tolerance", type=float, default=0.05, help="Cluster tolerance in meters")
    parser.add_argument("--layers", default="", help="Comma-separated list of CAD layer names to include")
    parser.add_argument("--utm", type=int, default=32637, help="UTM WKID (e.g. 32637 for UTM 37N)")

    args = parser.parse_args()

    result = {
        "success": False,
        "inputLineCount": 0,
        "outputPolygonCount": 0,
        "totalAreaSqm": 0.0,
        "outputFcPath": "",
        "gpMessages": "",
        "diagnostic": ""
    }

    try:
        import arcpy
    except ImportError as e:
        result["diagnostic"] = f"ArcPy is not installed or not available in the current Python environment: {e}"
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(1)

    try:
        gdb_path = os.path.abspath(args.gdb)
        if not arcpy.Exists(gdb_path):
            result["diagnostic"] = f"Geodatabase does not exist at '{gdb_path}'."
            print(json.dumps(result, ensure_ascii=False))
            sys.exit(1)

        arcpy.env.workspace = gdb_path
        arcpy.env.overwriteOutput = True
        arcpy.env.outputZFlag = "Disabled"
        arcpy.env.outputMFlag = "Disabled"

        if args.utm > 0:
            sr = arcpy.SpatialReference(args.utm)
            arcpy.env.outputCoordinateSystem = sr

        # Locate lines feature class
        in_lines = ""
        if args.lines_fc and arcpy.Exists(args.lines_fc):
            in_lines = args.lines_fc
        else:
            cand1 = os.path.join(gdb_path, args.dataset, "Polyline")
            cand2 = os.path.join(gdb_path, "Polyline")
            cand3 = os.path.join(gdb_path, args.dataset, "Line")
            if arcpy.Exists(cand1):
                in_lines = cand1
            elif arcpy.Exists(cand2):
                in_lines = cand2
            elif arcpy.Exists(cand3):
                in_lines = cand3
            else:
                # Search feature classes in dataset
                arcpy.env.workspace = os.path.join(gdb_path, args.dataset) if arcpy.Exists(os.path.join(gdb_path, args.dataset)) else gdb_path
                fcs = arcpy.ListFeatureClasses(feature_type="Polyline") or []
                if fcs:
                    in_lines = os.path.join(arcpy.env.workspace, fcs[0])

        if not in_lines or not arcpy.Exists(in_lines):
            result["diagnostic"] = f"Lines Feature Class not found in '{gdb_path}\\{args.dataset}'."
            print(json.dumps(result, ensure_ascii=False))
            sys.exit(1)

        # Prepare layer with SQL filter if specific layers provided
        temp_layer = "in_memory\\filtered_lines_temp"
        if arcpy.Exists(temp_layer):
            arcpy.management.Delete(temp_layer)

        where_clause = ""
        if args.layers:
            layer_names = [l.strip() for l in args.layers.split(",") if l.strip()]
            if layer_names:
                escaped = ["'" + l.replace("'", "''") + "'" for l in layer_names]
                where_clause = f"Layer IN ({','.join(escaped)})"

        arcpy.management.MakeFeatureLayer(in_lines, temp_layer, where_clause)
        in_count = int(arcpy.management.GetCount(temp_layer)[0])
        result["inputLineCount"] = in_count

        if in_count == 0:
            result["diagnostic"] = f"No input lines matched the layer filter ({args.layers})."
            print(json.dumps(result, ensure_ascii=False))
            sys.exit(0)

        # Determine target output FC
        out_fc = args.out_fc
        if not os.path.isabs(out_fc):
            out_fc = os.path.join(gdb_path, args.out_fc)

        cluster_tol = f"{args.tolerance} Meters"

        # Execute Feature to Polygon
        arcpy.management.FeatureToPolygon(temp_layer, out_fc, cluster_tol)

        gp_messages = arcpy.GetMessages()
        result["gpMessages"] = gp_messages
        result["outputFcPath"] = out_fc

        if arcpy.Exists(out_fc):
            out_count = int(arcpy.management.GetCount(out_fc)[0])
            result["outputPolygonCount"] = out_count

            total_area = 0.0
            with arcpy.da.SearchCursor(out_fc, ["SHAPE@AREA"]) as cursor:
                for row in cursor:
                    if row[0] is not None:
                        total_area += float(row[0])
            result["totalAreaSqm"] = round(total_area, 2)

            if out_count > 0:
                result["success"] = True
                result["diagnostic"] = f"تم بنجاح تحويل {in_count} خط إلى {out_count} مضلع بمساحة إجمالية {result['totalAreaSqm']} م² عبر ArcPy."
            else:
                result["success"] = False
                if "000117" in gp_messages or "WARNING" in gp_messages:
                    result["diagnostic"] = "أداة ArcPy FeatureToPolygon لم تُنشئ أي مضلعات: خطوط الحدود غير مغلقة طوبولوجياً وتوجد فجوات (Dangling lines / Gaps)."
                else:
                    result["diagnostic"] = "لم يتم تكوين أي مضلعات من الخطوط المدخلة عبر أداة ArcPy."
        else:
            result["success"] = False
            result["diagnostic"] = f"فشل إنشاء Feature Class للمضلعات في '{out_fc}'.\n{gp_messages}"

        print(json.dumps(result, ensure_ascii=False))

    except Exception as ex:
        err_msgs = ""
        try:
            import arcpy
            err_msgs = arcpy.GetMessages(2)
        except Exception:
            pass
        result["success"] = False
        result["diagnostic"] = f"خطأ أثناء تشغيل ArcPy Line to Polygon: {str(ex)}\n{err_msgs}"
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(1)

if __name__ == "__main__":
    main()
