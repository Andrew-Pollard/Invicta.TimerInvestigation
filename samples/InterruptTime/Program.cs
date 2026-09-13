// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using Invicta.Diagnostics;
using static System.FormattableString;

namespace Invicta;

/// <summary>
/// Compares Windows' interrupt time, which advances when an interrupt is serviced, against the performance
/// counter: first at each wake-up of an idle waiting thread, then at every change seen by a thread that keeps
/// its core busy polling.
/// </summary>
internal static class Program
{
    /// <summary>Runs the idle and busy measurements and prints a report for each.</summary>
    private static void Main()
    {
        Report("Idle: sampled after each Thread.Sleep(1) wake-up", RecordIdle(TimeSpan.FromSeconds(6)));

        List<Sample> busy = RecordBusy(TimeSpan.FromSeconds(3), out TimeSpan finest);
        Report("Busy: polled continuously, one sample per change", busy);

        // Another process lowering the global timer resolution raises the interrupt rate the busy poll sees.
        Console.WriteLine(Invariant(
            $"    finest global timer resolution in effect during the poll {finest.TotalMilliseconds:F3} ms"));
    }

    /// <summary>Samples both clocks immediately after each wake-up of a sleeping thread.</summary>
    /// <returns>The paired samples.</returns>
    private static List<Sample> RecordIdle(TimeSpan duration)
    {
        List<Sample> samples = [];
        long end = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            Thread.Sleep(1);
            samples.Add(new Sample(Stopwatch.GetTimestamp(), WindowsClock.QueryUnbiasedInterruptTime()));
        }

        return samples;
    }

    /// <summary>
    /// Polls the interrupt time without pausing, recording each change, and checks the global timer resolution
    /// every 100 ms. This keeps one core awake.
    /// </summary>
    /// <param name="duration">How long to poll for.</param>
    /// <param name="finestResolution">The finest global timer resolution seen during the poll.</param>
    /// <returns>One sample per change of interrupt time.</returns>
    private static List<Sample> RecordBusy(TimeSpan duration, out TimeSpan finestResolution)
    {
        List<Sample> samples = [];
        finestResolution = WindowsClock.QueryTimerResolution().Current;
        TimeSpan last = WindowsClock.QueryUnbiasedInterruptTime();
        long nextResolutionCheck = Stopwatch.GetTimestamp();
        long end = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            TimeSpan now = WindowsClock.QueryUnbiasedInterruptTime();
            if (now != last)
            {
                samples.Add(new Sample(Stopwatch.GetTimestamp(), now));
                last = now;
            }

            if (Stopwatch.GetTimestamp() >= nextResolutionCheck)
            {
                TimeSpan current = WindowsClock.QueryTimerResolution().Current;
                finestResolution = current < finestResolution ? current : finestResolution;
                nextResolutionCheck += Stopwatch.Frequency / 10;
            }
        }

        return samples;
    }

    /// <summary>
    /// Prints the distribution of performance-counter intervals and of interrupt-time steps, and how far each
    /// clock advanced overall.
    /// </summary>
    private static void Report(string title, List<Sample> samples)
    {
        double[] counterIntervals = StopwatchTimestamps.ToIntervals([.. samples.Select(sample => sample.Timestamp)]);
        double[] interruptSteps = new double[samples.Count - 1];
        for (int i = 1; i < samples.Count; i++)
        {
            interruptSteps[i - 1] = (samples[i].InterruptTime - samples[i - 1].InterruptTime).TotalMilliseconds;
        }

        Distribution counter = new(counterIntervals);
        Distribution interrupt = new(interruptSteps);
        double counterTotal = StopwatchTimestamps.ToMilliseconds(samples[^1].Timestamp - samples[0].Timestamp);
        double interruptTotal = (samples[^1].InterruptTime - samples[0].InterruptTime).TotalMilliseconds;

        Console.WriteLine(title);
        const string Indent = "                         ";
        Console.WriteLine(Invariant($"    counter interval     {counter}"));
        Console.WriteLine(Invariant($"{Indent}{counter.FormatHistogram(binWidth: 0.25, minimumShare: 0.01)}"));
        Console.WriteLine(Invariant($"    interrupt-time step  {interrupt}"));
        Console.WriteLine(Invariant($"{Indent}{interrupt.FormatHistogram(binWidth: 0.25, minimumShare: 0.01)}"));
        Console.WriteLine(
            Invariant($"    over {counterTotal:F1} ms of counter time, ") +
            Invariant($"interrupt time advanced {interruptTotal:F1} ms (ratio {interruptTotal / counterTotal:F5})"));
    }

    /// <summary>A performance-counter timestamp paired with the interrupt time read straight after it.</summary>
    /// <param name="Timestamp">The <see cref="Stopwatch"/> timestamp.</param>
    /// <param name="InterruptTime">The unbiased interrupt time.</param>
    private readonly record struct Sample(long Timestamp, TimeSpan InterruptTime);
}
