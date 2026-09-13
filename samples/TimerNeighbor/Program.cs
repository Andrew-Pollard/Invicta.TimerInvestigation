// © 2026 Andrew Pollard. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using Invicta.Threading;
using static System.FormattableString;

namespace Invicta;

/// <summary>
/// A companion process to run alongside <c>WakeGrid</c>, to test what another process can do to its timers: run a
/// precise timer, hold a 1 ms timer resolution, or raise and lower the resolution on a schedule.
/// </summary>
internal static class Program
{
    /// <summary>Runs the chosen behavior for the given number of seconds, then prints what it did.</summary>
    /// <param name="args">
    /// A mode (<c>precise</c>, <c>hold</c> or <c>toggle</c>), the number of seconds to run for, and for
    /// <c>precise</c> and <c>toggle</c> a period in milliseconds.
    /// </param>
    /// <returns>Zero on success, or 1 when the arguments are not understood.</returns>
    private static int Main(string[] args)
    {
        if (args.Length < 2
            || !double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            return Usage();
        }

        TimeSpan duration = TimeSpan.FromSeconds(seconds);
        double periodMs = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 3;
        TimeSpan period = TimeSpan.FromMilliseconds(periodMs);

        switch (args[0])
        {
            case "precise":
                Report("precise", RunOnSchedule(period, duration, static () => { }), periodMs);
                return 0;

            case "toggle":
                int toggles = RunOnSchedule(period, duration, static () => new TimerResolutionRequest(1).Dispose());
                Report("toggle", toggles, periodMs);
                return 0;

            case "hold":
                using (new TimerResolutionRequest(1))
                {
                    Thread.Sleep(duration);
                }

                Console.WriteLine(Invariant($"TimerNeighbor: held a 1 ms timer resolution for {seconds:F1} s"));
                return 0;

            default:
                return Usage();
        }
    }

    /// <summary>
    /// Wakes on a high-resolution timer at start + n periods, skipping any already passed, and runs an action at
    /// each wake-up.
    /// </summary>
    /// <returns>The number of wake-ups.</returns>
    private static int RunOnSchedule(TimeSpan period, TimeSpan duration, Action onWake)
    {
        using WaitableTimer timer = new(highResolution: true);
        long periodTicks = (long)(period.TotalSeconds * Stopwatch.Frequency);
        long start = Stopwatch.GetTimestamp();
        long end = start + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        long next = start + periodTicks;
        int wakes = 0;
        while (Stopwatch.GetTimestamp() < end)
        {
            long now = Stopwatch.GetTimestamp();
            while (next <= now)
            {
                next += periodTicks;
            }

            timer.Wait(Stopwatch.GetElapsedTime(now, next));
            onWake();
            wakes++;
            next += periodTicks;
        }

        return wakes;
    }

    /// <summary>Prints what the scheduled behavior did.</summary>
    private static void Report(string mode, int wakes, double periodMs)
    {
        string action = mode == "toggle"
            ? "raised and lowered the timer resolution"
            : "woke on a high-resolution timer";
        Console.WriteLine(Invariant($"TimerNeighbor: {action} {wakes} times, every {periodMs:0.###} ms"));
    }

    /// <summary>Prints how to run the program.</summary>
    /// <returns>1, for the process exit code.</returns>
    private static int Usage()
    {
        Console.Error.WriteLine("Usage: TimerNeighbor precise|toggle <seconds> [period ms]");
        Console.Error.WriteLine("       TimerNeighbor hold <seconds>");
        return 1;
    }
}
