// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using Invicta.Diagnostics;
using Invicta.Threading;
using static System.FormattableString;

namespace Invicta;

/// <summary>
/// Records every wake-up of four ways to wait 1 ms, from Win32 up to <see cref="Task.Delay(int)"/>, and reports
/// which clock the wake-ups follow: the 15.625 ms tick, a display's refresh interval, or something else.
/// </summary>
internal static class Program
{
    /// <summary>The clock increment Windows reports by default.</summary>
    private const double TickPeriod = 15.625;

    /// <summary>Records each mechanism in turn and prints a report for each.</summary>
    /// <param name="args">Optional seconds to record each mechanism for; the default is 8.</param>
    private static async Task Main(string[] args)
    {
        double seconds = args.Length > 0 ? double.Parse(args[0], CultureInfo.InvariantCulture) : 8;
        TimeSpan duration = TimeSpan.FromSeconds(seconds);

        PrintClock("before");
        Report("Thread.Sleep(1)", RecordSleep(duration));
        Report("Win32 waitable timer, default resolution", RecordWaitableTimer(duration));
        Report("System.Threading.Timer, 1 ms period", RecordThreadingTimer(duration));
        Report("await Task.Delay(1)", await RecordTaskDelay(duration));
        PrintClock("after");
    }

    /// <summary>Records the wake-ups of a thread calling <see cref="Thread.Sleep(int)"/> in a loop.</summary>
    /// <returns>A <see cref="Stopwatch"/> timestamp for each wake-up.</returns>
    private static List<long> RecordSleep(TimeSpan duration)
    {
        List<long> wakeUps = [];
        long end = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            Thread.Sleep(1);
            wakeUps.Add(Stopwatch.GetTimestamp());
        }

        return wakeUps;
    }

    /// <summary>Records the wake-ups of a thread waiting on a default-resolution Win32 timer in a loop.</summary>
    /// <returns>A <see cref="Stopwatch"/> timestamp for each wake-up.</returns>
    private static List<long> RecordWaitableTimer(TimeSpan duration)
    {
        using WaitableTimer timer = new(highResolution: false);
        List<long> wakeUps = [];
        long end = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            timer.Wait(TimeSpan.FromMilliseconds(1));
            wakeUps.Add(Stopwatch.GetTimestamp());
        }

        return wakeUps;
    }

    /// <summary>Records the callbacks of a periodic <see cref="Timer"/>.</summary>
    /// <returns>A <see cref="Stopwatch"/> timestamp for each callback.</returns>
    private static List<long> RecordThreadingTimer(TimeSpan duration)
    {
        List<long> callbacks = [];
        Lock gate = new();

        using (Timer timer = new(
            _ =>
            {
                long now = Stopwatch.GetTimestamp();
                lock (gate)
                {
                    callbacks.Add(now);
                }
            },
            state: null,
            dueTime: 1,
            period: 1))
        {
            Thread.Sleep(duration);
        }

        lock (gate)
        {
            return [.. callbacks];
        }
    }

    /// <summary>Records the continuations of <see cref="Task.Delay(int)"/> awaited in a loop.</summary>
    /// <returns>A <see cref="Stopwatch"/> timestamp for each continuation.</returns>
    private static async Task<List<long>> RecordTaskDelay(TimeSpan duration)
    {
        List<long> wakeUps = [];
        long end = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            await Task.Delay(1);
            wakeUps.Add(Stopwatch.GetTimestamp());
        }

        return wakeUps;
    }

    /// <summary>
    /// Prints the interval distribution, the concentration on the tick grid, and the fundamental period of the
    /// grid the wake-ups actually lie on.
    /// </summary>
    private static void Report(string mechanism, List<long> wakeUps)
    {
        Distribution intervals = new(StopwatchTimestamps.ToIntervals(wakeUps));
        double[] offsets = StopwatchTimestamps.ToOffsets(wakeUps);
        GridPeriod grid = PeriodScan.FindFundamental(offsets, minimum: 2, maximum: 20, step: 0.0025);

        Console.WriteLine(mechanism);
        Console.WriteLine(Invariant($"    interval   {intervals}"));
        Console.WriteLine(Invariant($"    histogram  {intervals.FormatHistogram(binWidth: 0.5, minimumShare: 0.01)}"));
        double tick = PeriodScan.Concentration(offsets, TickPeriod);
        Console.WriteLine(Invariant(
            $"    grid       tick R={tick:F2}, fundamental {grid.Period:F3} ms R={grid.Concentration:F2}"));
    }

    /// <summary>Prints the clock increment and the global timer resolution.</summary>
    private static void PrintClock(string when)
    {
        TimerResolution resolution = WindowsClock.QueryTimerResolution();
        TimeSpan increment = WindowsClock.QueryClockIncrement();
        Console.WriteLine(
            Invariant($"Clock increment {increment.TotalMilliseconds:F3} ms; ") +
            Invariant($"timer resolution {resolution.Current.TotalMilliseconds:F3} ms {when}, ") +
            Invariant($"finest {resolution.Finest.TotalMilliseconds:F3} ms"));
    }
}
