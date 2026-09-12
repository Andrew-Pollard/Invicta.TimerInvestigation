// © 2026 Andrew Pollard. All rights reserved.

using System.Diagnostics;
using Invicta.Diagnostics;
using Invicta.Threading;
using static System.FormattableString;

namespace Invicta;

/// <summary>
/// Runs a 1 ms periodic schedule on a high-resolution timer two ways: re-arming a full period after each
/// wake-up, so lateness carries into every later tick, and aiming each tick at a fixed absolute time, so it
/// does not.
/// </summary>
internal static class Program
{
    /// <summary>The period both schedules aim for.</summary>
    private static readonly TimeSpan s_period = TimeSpan.FromMilliseconds(1);

    /// <summary>How long each schedule runs for.</summary>
    private static readonly TimeSpan s_duration = TimeSpan.FromSeconds(5);

    /// <summary>Runs both schedules and prints a report for each.</summary>
    private static void Main()
    {
        Report("Relative: re-armed one period after each wake-up", RecordRelative());
        Report("Absolute: each tick aimed at start + n periods", RecordAbsolute());
    }

    /// <summary>Waits one full period after every wake-up.</summary>
    /// <returns>A <see cref="Stopwatch"/> timestamp for each wake-up.</returns>
    private static List<long> RecordRelative()
    {
        using WaitableTimer timer = new(highResolution: true);
        List<long> wakeUps = [Stopwatch.GetTimestamp()];
        long end = wakeUps[0] + (long)(s_duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            timer.Wait(s_period);
            wakeUps.Add(Stopwatch.GetTimestamp());
        }

        return wakeUps;
    }

    /// <summary>
    /// Waits until the next multiple of the period after the start, skipping any multiples already passed, as
    /// a drift-free scheduler does.
    /// </summary>
    /// <returns>A <see cref="Stopwatch"/> timestamp for each wake-up.</returns>
    private static List<long> RecordAbsolute()
    {
        using WaitableTimer timer = new(highResolution: true);
        long periodTicks = (long)(s_period.TotalSeconds * Stopwatch.Frequency);
        List<long> wakeUps = [Stopwatch.GetTimestamp()];
        long end = wakeUps[0] + (long)(s_duration.TotalSeconds * Stopwatch.Frequency);
        long next = wakeUps[0] + periodTicks;
        while (Stopwatch.GetTimestamp() < end)
        {
            long now = Stopwatch.GetTimestamp();
            while (next <= now)
            {
                next += periodTicks;
            }

            timer.Wait(Stopwatch.GetElapsedTime(now, next));
            wakeUps.Add(Stopwatch.GetTimestamp());
            next += periodTicks;
        }

        return wakeUps;
    }

    /// <summary>Prints the interval distribution and the achieved rate.</summary>
    private static void Report(string schedule, List<long> wakeUps)
    {
        Distribution intervals = new(StopwatchTimestamps.ToIntervals(wakeUps));
        double elapsed = StopwatchTimestamps.ToMilliseconds(wakeUps[^1] - wakeUps[0]);

        Console.WriteLine(schedule);
        Console.WriteLine(Invariant($"    interval   {intervals}"));
        Console.WriteLine(Invariant($"    histogram  {intervals.FormatHistogram(binWidth: 0.25, minimumShare: 0.02)}"));
        double achieved = intervals.Count / (elapsed / s_period.TotalMilliseconds);
        Console.WriteLine(
            Invariant($"    {intervals.Count} wake-ups in {elapsed:F0} ms, ") +
            Invariant($"{achieved * 100:F1}% of the ticks the period asks for"));
    }
}
