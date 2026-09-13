// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Diagnostics;

/// <summary>
/// Finds the periodic grid, if any, that a series of event times lies on: for example, whether wake-ups follow
/// the 15.625 ms clock tick or a display's refresh interval.
/// </summary>
/// <remarks>
/// For a candidate period, each event time becomes an angle around a circle of that period. The mean resultant
/// length of those angles, the concentration, is 1 when every event lands at the same phase and near 0 when the
/// events bear no relation to the period. A grid also concentrates at its whole-number fractions (a 16 ms grid
/// scores 1 at 8 ms), so the fundamental is the longest period that scores close to the best.
/// </remarks>
public static class PeriodScan
{
    /// <summary>Measures how closely event times lie on a grid of the given period.</summary>
    /// <param name="offsets">The event times, in milliseconds from any fixed origin.</param>
    /// <param name="period">The candidate period, in milliseconds.</param>
    /// <returns>The concentration, from 0 (no relationship) to 1 (every event at the same phase).</returns>
    public static double Concentration(IReadOnlyList<double> offsets, double period)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);

        double cosine = 0;
        double sine = 0;
        foreach (double offset in offsets)
        {
            double angle = 2 * Math.PI * offset / period;
            cosine += Math.Cos(angle);
            sine += Math.Sin(angle);
        }

        return offsets.Count == 0 ? 0 : Math.Sqrt((cosine * cosine) + (sine * sine)) / offsets.Count;
    }

    /// <summary>Scans a range of periods for the fundamental period of the grid the event times lie on.</summary>
    /// <param name="offsets">The event times, in milliseconds from any fixed origin.</param>
    /// <param name="minimum">The shortest period to consider, in milliseconds.</param>
    /// <param name="maximum">The longest period to consider, in milliseconds.</param>
    /// <param name="step">The spacing between candidate periods, in milliseconds.</param>
    /// <returns>The longest period whose concentration is within 10% of the best in the range.</returns>
    public static GridPeriod FindFundamental(IReadOnlyList<double> offsets, double minimum, double maximum, double step)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        List<GridPeriod> scan = [];
        for (double period = minimum; period <= maximum; period += step)
        {
            scan.Add(new GridPeriod(period, Concentration(offsets, period)));
        }

        List<GridPeriod> peaks = [];
        for (int i = 1; i < scan.Count - 1; i++)
        {
            double concentration = scan[i].Concentration;
            if (concentration >= scan[i - 1].Concentration && concentration >= scan[i + 1].Concentration)
            {
                peaks.Add(scan[i]);
            }
        }

        if (peaks.Count == 0)
        {
            return scan.MaxBy(candidate => candidate.Concentration);
        }

        double best = peaks.Max(peak => peak.Concentration);
        return peaks.Where(peak => peak.Concentration >= best * 0.9).MaxBy(peak => peak.Period);
    }
}
