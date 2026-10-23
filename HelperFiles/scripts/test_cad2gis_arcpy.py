# -*- coding: utf-8 -*-
"""
Automated Reproducible Tests for Cad2Gis_ArcPy.pyt
Verifies all 7 user requirements:
1. Cad2GisLineToPolygonTool: Input inspection, CRS, boundary connectivity, geoprocessing logging, geometric verification, preserving cluster tolerance.
2. Cad2GisFullPipelineTool: Real execution of all steps, real QA/QC validation without false success reports.
3. Data Flow: Feature Class chaining from Step 1 to Step 4 and export manifest tracking.
4. Direction Codes: BORDERNAME standardization (1 North, 2 East, 3 South, 4 West).
5. Cad2GisWidthInferenceTool: Ray casting, distance sensing, and classification of streets and passages.
6. Cad2GisLoadDemoTool: Synthetic demo data and Areas Excel creation with known metrics.
7. HelperFiles Resolution & JSON Error Handling: Strict resolution order and clear diagnostics on corrupted JSON.
"""

import unittest
import os
import sys
import shutil
import json
import tempfile
import arcpy

# Add HelperFiles directory to sys.path so Cad2Gis_ArcPy can be imported as module
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
HELPER_DIR = os.path.dirname(SCRIPT_DIR) if os.path.basename(SCRIPT_DIR).lower() == "scripts" else SCRIPT_DIR
from importlib.machinery import SourceFileLoader
import importlib.util
pyt_path = os.path.join(HELPER_DIR, "Cad2Gis_ArcPy.pyt")
loader = SourceFileLoader("Cad2Gis_ArcPy", pyt_path)
spec = importlib.util.spec_from_file_location("Cad2Gis_ArcPy", pyt_path, loader=loader)
pyt_mod = importlib.util.module_from_spec(spec)
loader.exec_module(pyt_mod)

class TestCad2GisArcPy(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.test_root = tempfile.mkdtemp(prefix="cad2gis_test_")
        cls.toolbox_path = os.path.join(HELPER_DIR, "Cad2Gis_ArcPy.pyt")
        cls.tb = arcpy.ImportToolbox(cls.toolbox_path)

    @classmethod
    def tearDownClass(cls):
        try:
            shutil.rmtree(cls.test_root, ignore_errors=True)
        except Exception:
            pass

    def test_01_direction_codes_standardization(self):
        """Requirement 4: 1 North, 2 East, 3 South, 4 West."""
        self.assertEqual(pyt_mod.CARDINAL_NORTH, 1)
        self.assertEqual(pyt_mod.CARDINAL_EAST, 2)
        self.assertEqual(pyt_mod.CARDINAL_SOUTH, 3)
        self.assertEqual(pyt_mod.CARDINAL_WEST, 4)

        # Check lookup JSON file
        lookup_data = pyt_mod.load_lookup("BORDERNAME.json")
        self.assertEqual(lookup_data["1"]["name"], "North")
        self.assertEqual(lookup_data["1"]["arabic"], "شمال")
        self.assertEqual(lookup_data["2"]["name"], "East")
        self.assertEqual(lookup_data["2"]["arabic"], "شرق")
        self.assertEqual(lookup_data["3"]["name"], "South")
        self.assertEqual(lookup_data["3"]["arabic"], "جنوب")
        self.assertEqual(lookup_data["4"]["name"], "West")
        self.assertEqual(lookup_data["4"]["arabic"], "غرب")

        # Test azimuth classification to cardinal directions
        # North: [315..360) U [0..45)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(0.0), 1)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(350.0), 1)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(30.0), 1)
        # East: [45..135)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(45.0), 2)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(90.0), 2)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(120.0), 2)
        # South: [135..225)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(135.0), 3)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(180.0), 3)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(210.0), 3)
        # West: [225..315)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(225.0), 4)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(270.0), 4)
        self.assertEqual(pyt_mod.classify_azimuth_to_cardinal(300.0), 4)

    def test_02_helper_files_resolution_and_json_error_handling(self):
        """Requirement 7: Search order and clear messages on JSON error."""
        resolved = pyt_mod.resolve_helper_folder()
        self.assertTrue(os.path.isdir(resolved), f"Resolved helper folder does not exist: {resolved}")

        # Test corrupted JSON reading
        corrupted_json_path = os.path.join(self.test_root, "bad_syntax.json")
        with open(corrupted_json_path, "w", encoding="utf-8") as f:
            f.write("{ invalid json content: 123, }")

        # Should log clear warning and return default without crashing
        data = pyt_mod.load_json(corrupted_json_path, default_val={"safe": True})
        self.assertEqual(data, {"safe": True})

    def test_03_load_demo_tool(self):
        """Requirement 6: Generate synthetic demo dataset and Areas Excel."""
        demo_out_dir = os.path.join(self.test_root, "demo_output")
        tool = pyt_mod.Cad2GisLoadDemoTool()
        
        # Create parameters
        params = tool.getParameterInfo()
        params[0].value = demo_out_dir
        params[1].value = 32637
        
        tool.execute(params, None)

        # Check generated deliverables on disk
        parcel_shp = os.path.join(demo_out_dir, "PARCEL_Demo.shp")
        obstacle_shp = os.path.join(demo_out_dir, "Facing_Obstacles_Demo.shp")
        cad_lines_shp = os.path.join(demo_out_dir, "CAD_Lines_Demo.shp")
        excel_path = os.path.join(demo_out_dir, "Areas_Demo.xlsx")
        csv_path = os.path.join(demo_out_dir, "Areas_Demo.csv")

        self.assertTrue(os.path.isfile(parcel_shp), "PARCEL_Demo.shp missing")
        self.assertTrue(os.path.isfile(obstacle_shp), "Facing_Obstacles_Demo.shp missing")
        self.assertTrue(os.path.isfile(cad_lines_shp), "CAD_Lines_Demo.shp missing")
        self.assertTrue(os.path.isfile(excel_path) or os.path.isfile(csv_path), "Areas Demo Excel/CSV missing")

        # Verify geometric contents
        parcel_count = int(arcpy.management.GetCount(parcel_shp)[0])
        self.assertEqual(parcel_count, 4, "Should have 2 Main parcels + 2 Sub-parcels")

        areas = []
        with arcpy.da.SearchCursor(parcel_shp, ["PLOT_ID", "AREA_SQM", "PARCEL_CLS"]) as cursor:
            for row in cursor:
                areas.append((row[0], row[1], row[2]))

        # Main parcels 101 and 102 are 600m² each
        main_areas = [a[1] for a in areas if a[2] == "MAIN"]
        self.assertEqual(len(main_areas), 2)
        for a in main_areas:
            self.assertAlmostEqual(a, 600.0, places=1)

        # Sub parcels Garden (400m²) and Corridor (180m²)
        sub_areas = sorted([a[1] for a in areas if a[2] == "SUB"])
        self.assertEqual(sub_areas, [180.0, 400.0])

    def test_04_line_to_polygon_tool_and_connectivity_audit(self):
        """Requirement 1: Input inspection, CRS, connectivity (dangles), and output verification."""
        demo_dir = os.path.join(self.test_root, "demo_output")
        cad_lines = os.path.join(demo_dir, "CAD_Lines_Demo.shp")
        out_polys = os.path.join(self.test_root, "lines_to_polys.shp")

        # 1. Connectivity audit directly on unclosed lines
        conn = pyt_mod.inspect_boundary_connectivity(cad_lines, tolerance=0.01)
        # Should detect dangles because of deliberate 0.03m gap and dangle line
        self.assertGreater(conn["dangle_count"], 0, "Should detect dangling line ends when tolerance < gap")

        # 2. Run Cad2GisLineToPolygonTool with exact cluster tolerance 0.05m (which bridges the 0.03m gap)
        tool = pyt_mod.Cad2GisLineToPolygonTool()
        params = tool.getParameterInfo()
        params[0].value = cad_lines
        params[1].value = out_polys
        params[2].value = 0.05  # Cluster tolerance
        params[3].value = "EPSG:32637 (37N)"

        tool.execute(params, None)

        self.assertTrue(os.path.isfile(out_polys), "Output polygon FC was not created")
        count = int(arcpy.management.GetCount(out_polys)[0])
        self.assertEqual(count, 1, "Expected 1 closed polygon from closed quadrilateral with tolerance")

        # Verify geometry metrics
        ok, count_verified, area = pyt_mod.verify_output_geometry(out_polys)
        self.assertTrue(ok)
        self.assertAlmostEqual(area, 100.0, delta=1.0)  # 10x10m square = 100m²

    def test_05_width_inference_tool(self):
        """Requirement 5: Perpendicular ray sensing, distance measurement, street/passage classification."""
        demo_dir = os.path.join(self.test_root, "demo_output")
        parcel_shp = os.path.join(demo_dir, "PARCEL_Demo.shp")
        obstacle_shp = os.path.join(demo_dir, "Facing_Obstacles_Demo.shp")

        # Create test border features for parcel 101:
        # South edge: (500000, 2400000) to (500020, 2400000), facing 12m street -> Expected STR (>= 10m)
        # West edge: (499994, 2400000) to (499994, 2400030), facing 6m corridor -> Expected LNE (< 10m)
        sr = arcpy.SpatialReference(32637)
        test_borders_shp = os.path.join(self.test_root, "test_borders_inference.shp")
        arcpy.management.CreateFeatureclass(self.test_root, "test_borders_inference.shp", "POLYLINE", spatial_reference=sr)
        arcpy.management.AddField(test_borders_shp, "PLOT_ID", "TEXT", field_length=50)
        arcpy.management.AddField(test_borders_shp, "BORDERNAME", "SHORT")
        arcpy.management.AddField(test_borders_shp, "BORDERTYPE", "TEXT", field_length=20)
        arcpy.management.AddField(test_borders_shp, "DESCRIPTIO", "TEXT", field_length=100)

        line_south = arcpy.Polyline(arcpy.Array([arcpy.Point(500000, 2400000), arcpy.Point(500020, 2400000)]), sr)
        line_west = arcpy.Polyline(arcpy.Array([arcpy.Point(499994, 2400000), arcpy.Point(499994, 2400030)]), sr)

        with arcpy.da.InsertCursor(test_borders_shp, ["SHAPE@", "PLOT_ID", "BORDERNAME", "BORDERTYPE", "DESCRIPTIO"]) as cur:
            cur.insertRow([line_south, "101", 3, "STR", "شارع"])  # South = 3
            cur.insertRow([line_west, "101", 4, "STR", "شارع"])   # West = 4

        # Execute WidthInferenceTool
        tool = pyt_mod.Cad2GisWidthInferenceTool()
        params = tool.getParameterInfo()
        params[0].value = parcel_shp
        params[1].value = test_borders_shp
        params[2].value = obstacle_shp
        params[3].value = 10.0  # Threshold
        params[4].value = 3     # Ray count
        params[5].value = 100.0 # Max dist

        tool.execute(params, None)

        # Verify updated attributes
        results = {}
        with arcpy.da.SearchCursor(test_borders_shp, ["BORDERNAME", "BORDERTYPE", "DESCRIPTIO"]) as cur:
            for row in cur:
                results[row[0]] = (row[1], row[2])

        # South border (3) faces 12m obstacle -> STR with description "شارع بعرض 12 م"
        self.assertIn(3, results)
        self.assertEqual(results[3][0], "STR")
        self.assertIn("12", results[3][1])

        # West border (4) faces 6m obstacle -> LNE with description "ممر بعرض 6 م"
        self.assertIn(4, results)
        self.assertEqual(results[4][0], "LNE")
        self.assertIn("6", results[4][1])

    def test_06_full_pipeline_tool_data_flow_and_genuine_qa_qc(self):
        """Requirements 2 & 3: Real pipeline execution, feature class data flow, export manifest, and true QA/QC."""
        demo_dir = os.path.join(self.test_root, "demo_output")
        parcel_shp = os.path.join(demo_dir, "PARCEL_Demo.shp")
        pipeline_out_dir = os.path.join(self.test_root, "pipeline_output")

        tool = pyt_mod.Cad2GisFullPipelineTool()
        params = tool.getParameterInfo()
        params[0].value = parcel_shp
        params[1].value = "Default_Consultant_Profile"
        params[2].value = "EPSG:32637 (37N)"
        params[3].value = pipeline_out_dir
        params[4].value = "ESRI Shapefiles (PARCEL.shp & border.shp + UTF-8 .cpg)"

        tool.execute(params, None)

        # 1. Verify deliverables
        final_parcel = os.path.join(pipeline_out_dir, "PARCEL.shp")
        final_border = os.path.join(pipeline_out_dir, "border.shp")
        parcel_cpg = os.path.join(pipeline_out_dir, "PARCEL.cpg")
        border_cpg = os.path.join(pipeline_out_dir, "border.cpg")
        manifest_file = os.path.join(pipeline_out_dir, "last_export.json")

        self.assertTrue(os.path.isfile(final_parcel), "PARCEL.shp missing in deliverable")
        self.assertTrue(os.path.isfile(final_border), "border.shp missing in deliverable")
        self.assertTrue(os.path.isfile(parcel_cpg), "PARCEL.cpg UTF-8 missing")
        self.assertTrue(os.path.isfile(border_cpg), "border.cpg UTF-8 missing")
        self.assertTrue(os.path.isfile(manifest_file), "last_export.json manifest missing")

        # 2. Verify export manifest data flow
        with open(manifest_file, "r", encoding="utf-8") as f:
            manifest = json.load(f)

        self.assertTrue(manifest["qaQcPassed"], "QA/QC should pass on clean demo dataset")
        self.assertEqual(manifest["qaErrorCount"], 0)
        self.assertIn("step1PolygonOutput", manifest)
        self.assertIn("step2ClassificationOutput", manifest)
        self.assertIn("step3BordersOutput", manifest)
        self.assertIn("step4WidthInferenceOutput", manifest)

        # 3. Verify borders were extracted with approved cardinal codes (1 North, 2 East, 3 South, 4 West)
        border_dirs = set()
        with arcpy.da.SearchCursor(final_border, ["BORDERNAME", "PLOT_ID"]) as cur:
            for row in cur:
                border_dirs.add(row[0])
                # Border must belong to a MAIN parcel (101 or 102), NEVER a sub-parcel
                self.assertIn(str(row[1]), ["101", "102"])

        self.assertEqual(border_dirs, {1, 2, 3, 4}, "All 4 cardinal directions must be present")

    def test_07_qa_qc_failure_detection(self):
        """Requirement 2: Ensure QA/QC does NOT falsely declare success on defective data."""
        # Create defective parcel dataset: MAIN parcel with empty PLOT_ID
        defective_dir = os.path.join(self.test_root, "defective_dataset")
        os.makedirs(defective_dir, exist_ok=True)
        sr = arcpy.SpatialReference(32637)
        defective_shp = os.path.join(defective_dir, "defective_parcels.shp")
        arcpy.management.CreateFeatureclass(defective_dir, "defective_parcels.shp", "POLYGON", spatial_reference=sr)
        arcpy.management.AddField(defective_shp, "PLOT_ID", "TEXT", field_length=50)
        arcpy.management.AddField(defective_shp, "PLAN_NO", "TEXT", field_length=50)
        arcpy.management.AddField(defective_shp, "PARCEL_CLS", "TEXT", field_length=20)
        arcpy.management.AddField(defective_shp, "AREA_SQM", "DOUBLE")

        pts = [arcpy.Point(500000, 2400000), arcpy.Point(500020, 2400000), arcpy.Point(500020, 2400030), arcpy.Point(500000, 2400030), arcpy.Point(500000, 2400000)]
        poly = arcpy.Polygon(arcpy.Array(pts), sr)
        with arcpy.da.InsertCursor(defective_shp, ["SHAPE@", "PLOT_ID", "PLAN_NO", "PARCEL_CLS", "AREA_SQM"]) as cur:
            # Defect: MAIN parcel with empty PLOT_ID and empty PLAN_NO
            cur.insertRow([poly, "", "", "MAIN", 600.0])

        out_defective_pipeline = os.path.join(self.test_root, "pipeline_defective_out")
        tool = pyt_mod.Cad2GisFullPipelineTool()
        params = tool.getParameterInfo()
        params[0].value = defective_shp
        params[1].value = "Default_Consultant_Profile"
        params[2].value = "EPSG:32637 (37N)"
        params[3].value = out_defective_pipeline
        params[4].value = "ESRI Shapefiles (PARCEL.shp & border.shp + UTF-8 .cpg)"

        tool.execute(params, None)

        manifest_file = os.path.join(out_defective_pipeline, "last_export.json")
        with open(manifest_file, "r", encoding="utf-8") as f:
            manifest = json.load(f)

        # MUST NOT declare success!
        self.assertFalse(manifest["qaQcPassed"], "QA/QC must fail when MAIN parcel lacks PLOT_ID/PLAN_NO")
        self.assertGreater(manifest["qaErrorCount"], 0, "Errors must be logged in manifest")

if __name__ == "__main__":
    unittest.main()
