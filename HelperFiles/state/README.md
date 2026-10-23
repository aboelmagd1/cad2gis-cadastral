# CAD2GIS Export State Management (Part B)

This directory maintains runtime continuity state records for the CAD2GIS Add-in.

## Key Files:
- `last_export.json`: Pointer to the most recent successful export run, containing:
  - Run ID, timestamp, and profile used
  - Working UTM zone and export CRS
  - Exported shapefile / feature class paths
  - Dataset hashes, modification timestamps, and record counts
- `export_history.json`: Rolling history of the last N exports (configurable, default 10) allowing users to switch back to older exports.
