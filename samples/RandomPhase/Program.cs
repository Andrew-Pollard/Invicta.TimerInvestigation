// © 2026 Andrew Pollard. All rights reserved.

using System.Diagnostics;
using Invicta.Diagnostics;
using Invicta.Threading;
using static System.FormattableString;

namespace Invicta;

/// <summary>
/// Measures how late a high-resolution timer fires when each wait starts immediately after the previous
/// wake-up, as a benchmark loop does, and when each wait starts at a random moment.
/// </summary>
internal static class Program
{
    /// <summary>How long each measurement runs for.</summary>
    private static readonly TimeSpan s_duration = TimeSpan.FromSeconds(4);

    /// <summary>Measures each due time with back-to-back and random starts.</summary>
    private static void Main()
    {
        Random random = new(Seed: 12345);
        foreach (double requested in (double[])[1.0, 1.25, 5.0])
        {
            Report(requested, "back-to-back", Record(requested, random: null));
            Report(requested, "random start", Record(requested, random));
        }
    }

    /// <summary>
    /// Waits repeatedly for the requested due time, measuring how late each wait ends. With a random source, each
    /// wait is preceded by a busy wait of up to 1 ms so that it starts at an arbitrary phase.
    /// </summary>
    /// <returns>The distribution of lateness in milliseconds.</returns>
    private static Distribution Record(double requested, Random? random)
    {
        using WaitableTimer timer = new(highResolution: true);
        List<double> lateness = [];
        long end = Stopwatch.GetTimestamp() + (long)(s_duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            if (random is not null)
            {
                long resume = Stopwatch.GetTimestamp() + (long)(random.NextDouble() * Stopwatch.Frequency / 1000);
                while (Stopwatch.GetTimestamp() < resume)
                {
                    Thread.SpinWait(1);
                }
            }

            long start = Stopwatch.GetTimestamp();
            timer.Wait(TimeSpan.FromMilliseconds(requested));
            lateness.Add(StopwatchTimestamps.ToMilliseconds(Stopwatch.GetTimestamp() - start) - requested);
        }

        return new Distribution(lateness);
    }

    /// <summary>Prints the lateness distribution and its histogram.</summary>
    private static void Report(double requested, string start, Distribution lateness)
    {
        Console.WriteLine(Invariant($"{requested:F2} ms, {start}"));
        Console.WriteLine(Invariant($"    late       {lateness}"));
        Console.WriteLine(Invariant($"    histogram  {lateness.FormatHistogram(binWidth: 0.1, minimumShare: 0.02)}"));
    }
}
