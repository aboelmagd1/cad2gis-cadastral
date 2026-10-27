using System;
using System.Collections.Generic;
using System.Linq;
using Cad2Gis.Core.Models;

namespace Cad2Gis.Core.Services;

/// <summary>
/// Service to assign parcel PLOT_ID (and PLOT_NO1) from PLOT_TXT in "Line with data layer" mode.
/// </summary>
public static class PlotIdFromPlotTextService
{
    public sealed record Result(
        int Assigned, 
        int EmptyText, 
        int Changed, 
        IReadOnlyList<string> DuplicateIds,
        IReadOnlyList<string> Examples);

    /// <summary>
    /// Applies PLOT_TXT to PLOT_ID and PLOT_NO1 for parcels where PLOT_TXT is not empty.
    /// </summary>
    public static Result Apply(IList<ParcelRecord> parcels)
    {
        if (parcels == null) throw new ArgumentNullException(nameof(parcels));

        int assigned = 0;
        int empty = 0;
        int changed = 0;
        var examples = new List<string>();

        foreach (var p in parcels)
        {
            var text = p.PlotText?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                empty++;
                continue; // leave PLOT_ID as it is
            }

            string oldPlotId = p.PlotId;
            if (!string.Equals(oldPlotId, text, StringComparison.Ordinal))
            {
                changed++;
                if (examples.Count < 5)
                {
                    examples.Add($"{oldPlotId} → {text}");
                }
            }

            p.PlotId = text;

            // keep PlotNo1 consistent with PlotId if empty or previously matching PlotId
            if (string.IsNullOrEmpty(p.PlotNo1) || string.Equals(p.PlotNo1, oldPlotId, StringComparison.Ordinal))
            {
                p.PlotNo1 = text;
            }

            assigned++;
        }

        var duplicates = parcels
            .Where(x => !string.IsNullOrEmpty(x.PlotId))
            .GroupBy(x => x.PlotId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        return new Result(assigned, empty, changed, duplicates, examples);
    }
}
