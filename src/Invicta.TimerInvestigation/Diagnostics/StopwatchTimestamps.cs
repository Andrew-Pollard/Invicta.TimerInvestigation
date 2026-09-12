// © 2026 Andrew Pollard. All rights reserved.

using System.Diagnostics;

namespace Invicta.Diagnostics;

/// <summary>Converts sequences of <see cref="Stopwatch.GetTimestamp"/> values into milliseconds.</summary>
public static class StopwatchTimestamps
{
    /// <summary>Converts a difference between two timestamps into milliseconds.</summary>
    /// <param name="elapsed">The difference, in <see cref="Stopwatch"/> ticks.</param>
    /// <returns>The difference in milliseconds.</returns>
    public static double ToMilliseconds(long elapsed)
    {
        return elapsed * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>Gets the interval between each pair of consecutive timestamps.</summary>
    /// <param name="timestamps">The timestamps, in the order they were taken.</param>
    /// <returns>One interval in milliseconds for each timestamp after the first.</returns>
    public static double[] ToIntervals(IReadOnlyList<long> timestamps)
    {
        ArgumentNullException.ThrowIfNull(timestamps);

        double[] intervals = new double[Math.Max(timestamps.Count - 1, 0)];
        for (int i = 0; i < intervals.Length; i++)
        {
            intervals[i] = ToMilliseconds(timestamps[i + 1] - timestamps[i]);
        }

        return intervals;
    }

    /// <summary>Gets each timestamp's offset from the first.</summary>
    /// <param name="timestamps">The timestamps, in the order they were taken.</param>
    /// <returns>One offset in milliseconds for each timestamp, starting at zero.</returns>
    public static double[] ToOffsets(IReadOnlyList<long> timestamps)
    {
        ArgumentNullException.ThrowIfNull(timestamps);

        double[] offsets = new double[timestamps.Count];
        for (int i = 0; i < offsets.Length; i++)
        {
            offsets[i] = ToMilliseconds(timestamps[i] - timestamps[0]);
        }

        return offsets;
    }
}
