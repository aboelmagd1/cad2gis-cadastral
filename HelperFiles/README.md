# CAD2GIS HelperFiles 📁🗺️

This directory contains runtime helper assets, templates, lookups, and scripts required by the CAD2GIS ArcGIS Pro Add-in.

## Structure
- `templates/`: Official ESRI Shapefile templates (`PARCEL.shp`, `border.shp`) with full 26-field and 9-field municipal schemas.
- `adminareas/`: Administrative boundaries (`cities.shp`, `regons.shp`) used for point-in-polygon spatial jurisdiction detection.
- `Required Fields in the Shapefile.xlsx`: Master municipal specification for required fields, `Usage Types`, and `Border Types` domain lookups.
- `expected_layers/`: Layer classification and mapping definitions (`Expected_Layers.xlsx`).
- `PARCEs_landuse.xlsx`: External land-use register used in Step 3 for priority enrichment of `USAGE`, `REMARKS`, and `BLOCK_NO` by `PLOT_ID`.
- `lookups/`:
  - `Usage_Keywords.csv`: Semantic mapping of Arabic keywords to official MOMRAH usage types (`RSD`, `COM`, `RSC`, `AGR`, `IND`, `UTL`).
  - `BorderType_Keywords.csv`: Keyword mapping rules for neighbor and facade identification (`PLT`, `STR`, `LNE`, `PAK`, `HLW`, `SRV`, `UTL`).
  - `BORDERTYPE.json`, `cities.json`, `BUILDINGTY.json`, `USAGE.json`: JSON lookup tables.
- `profiles/`: Consultant configuration profiles (`Example_Profile.xlsx`, `Example_Profile.json`).
- `scripts/`: ArcPy scripts and python toolboxes (`Cad2Gis_ArcPy.pyt`).
- `state/`: Persistent step state and audit logs (`last_export.json`).
- `settings.json`: Runtime settings for tolerances, thresholds, and helper paths.

## Key Operational Rules
1. **Export Directory**: If `Export_Output` is missing, the tool automatically creates it at Step 1 to guarantee uninterrupted workflow.
2. **PLOT_ID Extraction**: Extracted spatially (point-in-polygon & proximity fallback) from the designated `PlotText` layer.
3. **USAGE & REMARKS Precedence**:
   - Step 2 assigns `USAGE` from CAD layer classification (Polygon mode or "Line with data layer" dominant boundary) and `REMARKS` from spatial join of layer(s) designated as `UsageText`.
   - Step 3 gives absolute priority to Excel (`PARCEs_landuse.xlsx` / Custom Data) when matched on `PLOT_ID`.

## Runtime Resolution Priority
1. User-specified path configured in UI / settings.
2. `<ProjectFolder>\HelperFiles` (adjacent to active ArcGIS Pro project).
3. `<AssemblyLocation>\HelperFiles` (bundled with Add-in).
4. `%LOCALAPPDATA%\CAD2GIS\HelperFiles` (fallback location).

