// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using Invicta.Diagnostics;
using Invicta.Threading;
using static System.FormattableString;

namespace Invicta;

/// <summary>
/// Asks Win32 waitable timers for a range of due times and reports what is delivered, to show how Windows
/// rounds them: onto roughly 0.5 ms steps at high resolution, and 15.625 ms periods at the default resolution.
/// </summary>
internal static class Program
{
    /// <summary>Sweeps both kinds of timer and prints one line per requested due time.</summary>
    private static void Main()
    {
        Console.WriteLine("High-resolution timer");
        foreach (double requested in (double[])[0.5, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.3, 5.0])
        {
            Report(requested, Record(highResolution: true, requested, TimeSpan.FromSeconds(1)));
        }

        Console.WriteLine("Default-resolution timer");
        foreach (double requested in (double[])[1.0, 10.0, 15.0, 15.6, 16.0, 20.0, 31.0, 32.0])
        {
            Report(requested, Record(highResolution: false, requested, TimeSpan.FromSeconds(1.5)));
        }
    }

    /// <summary>Waits repeatedly for the requested due time, measuring each wait.</summary>
    /// <returns>The distribution of actual wait times in milliseconds.</returns>
    private static Distribution Record(bool highResolution, double requested, TimeSpan duration)
    {
        using WaitableTimer timer = new(highResolution);
        List<double> actual = [];
        long end = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            long start = Stopwatch.GetTimestamp();
            timer.Wait(TimeSpan.FromMilliseconds(requested));
            actual.Add(StopwatchTimestamps.ToMilliseconds(Stopwatch.GetTimestamp() - start));
        }

        return new Distribution(actual);
    }

    /// <summary>Prints the requested due time alongside the delivered wait times.</summary>
    private static void Report(double requested, Distribution actual)
    {
        double median = actual.Quantile(0.5);
        Console.WriteLine(
            Invariant($"    {requested,5:F2} ms -> p5={actual.Quantile(0.05),7:F3} p50={median,7:F3} ") +
            Invariant($"p95={actual.Quantile(0.95),7:F3} (median {median - requested:F3} late) n={actual.Count}"));
    }
}
