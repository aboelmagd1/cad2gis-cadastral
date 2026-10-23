# CAD2GIS HelperFiles

This directory contains runtime helper assets, templates, and lookups required by the CAD2GIS ArcGIS Pro Add-in.

## Structure
- `templates/`: Official ESRI Shapefile templates (`PARCEL.shp`, `border.shp`) with full municipal schemas.
- `adminareas/`: Administrative boundaries (`cities.shp`, `regons.shp`) used for point-in-polygon spatial detection.
- `Required Fields in the Shapefile.xlsx`: Master specification for required fields and BORDERTYPE domain lookups.
- `expected_layers/`: Layer classification and mapping definitions (`Expected_Layers.xlsx`).
- `PARCEs_landuse.xlsx`: Template for external land-use data enrichment.
- `lookups/`: CSV lookups for municipal codes.
- `profiles/`: Consultant configuration profiles.
- `settings.json`: Runtime settings for tolerances, thresholds, and helper paths.

## Runtime Resolution
The Add-in searches for this folder in the following priority order:
1. User-specified path configured in settings.
2. `<ProjectFolder>\HelperFiles` (adjacent to active ArcGIS Pro project).
3. `%LOCALAPPDATA%\CAD2GIS\HelperFiles` (fallback location if project is unsaved).
