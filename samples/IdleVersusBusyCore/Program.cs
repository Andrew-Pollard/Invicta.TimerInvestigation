// © 2026 Andrew Pollard. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using Invicta.Diagnostics;
using Invicta.Threading;
using static System.FormattableString;

namespace Invicta;

/// <summary>
/// Waits on one logical processor while it is idle, then while a low-priority thread keeps it busy, to separate
/// how an idle processor is woken from how the periodic clock tick services timers.
/// </summary>
internal static class Program
{
    /// <summary>How long each measurement runs for.</summary>
    private static readonly TimeSpan s_duration = TimeSpan.FromSeconds(5);

    /// <summary>Runs every measurement on an idle processor, then on a busy one.</summary>
    /// <param name="args">Optional zero-based index of the logical processor to use; the default is 3.</param>
    private static void Main(string[] args)
    {
        int processor = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 3;

        Measure(processor, busy: false);
        Measure(processor, busy: true);
    }

    /// <summary>Runs the measurements on a thread pinned to <paramref name="processor"/>.</summary>
    private static void Measure(int processor, bool busy)
    {
        using CancellationTokenSource stop = new();
        Thread? spinner = null;
        if (busy)
        {
            // Lowest priority, so the measuring thread preempts it the moment its wait ends.
            spinner = new Thread(() => Spin(processor, stop.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.Lowest,
            };
            spinner.Start();
            Thread.Sleep(200);
        }

        Distribution? sleepIntervals = null;
        Distribution? highResolutionLateness = null;
        Distribution? defaultResolutionLateness = null;

        Thread measurer = new(() =>
        {
            ThreadAffinity.PinCurrentThread(processor);
            sleepIntervals = new Distribution(RecordSleepIntervals());
            highResolutionLateness = new Distribution(RecordLateness(highResolution: true));
            defaultResolutionLateness = new Distribution(RecordLateness(highResolution: false));
        })
        { Priority = ThreadPriority.Highest };
        measurer.Start();
        measurer.Join();

        stop.Cancel();
        spinner?.Join();

        Console.WriteLine(Invariant($"{(busy ? "Busy" : "Idle")} logical processor {processor}"));
        Console.WriteLine(Invariant($"    Thread.Sleep(1) interval          {sleepIntervals}"));
        Console.WriteLine(Invariant($"    1 ms high-resolution timer, late  {highResolutionLateness}"));
        Console.WriteLine(Invariant($"    1 ms default timer, late          {defaultResolutionLateness}"));
    }

    /// <summary>Keeps a logical processor busy until cancelled.</summary>
    private static void Spin(int processor, CancellationToken stop)
    {
        ThreadAffinity.PinCurrentThread(processor);
        while (!stop.IsCancellationRequested)
        {
            Thread.SpinWait(1000);
        }
    }

    /// <summary>Records the interval between consecutive wake-ups of <see cref="Thread.Sleep(int)"/>.</summary>
    /// <returns>One interval in milliseconds per wake-up.</returns>
    private static List<double> RecordSleepIntervals()
    {
        List<double> intervals = [];
        long end = Stopwatch.GetTimestamp() + (long)(s_duration.TotalSeconds * Stopwatch.Frequency);
        long previous = Stopwatch.GetTimestamp();
        while (Stopwatch.GetTimestamp() < end)
        {
            Thread.Sleep(1);
            long now = Stopwatch.GetTimestamp();
            intervals.Add(StopwatchTimestamps.ToMilliseconds(now - previous));
            previous = now;
        }

        return intervals;
    }

    /// <summary>Records how long after its 1 ms due time a waitable timer is signaled.</summary>
    /// <returns>One lateness in milliseconds per wait.</returns>
    private static List<double> RecordLateness(bool highResolution)
    {
        using WaitableTimer timer = new(highResolution);
        List<double> lateness = [];
        long end = Stopwatch.GetTimestamp() + (long)(s_duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            long start = Stopwatch.GetTimestamp();
            timer.Wait(TimeSpan.FromMilliseconds(1));
            lateness.Add(StopwatchTimestamps.ToMilliseconds(Stopwatch.GetTimestamp() - start) - 1);
        }

        return lateness;
    }
}
