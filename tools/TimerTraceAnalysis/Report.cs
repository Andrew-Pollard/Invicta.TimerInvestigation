// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Diagnostics;
using static System.FormattableString;

namespace Invicta;

/// <summary>Prints the aggregates collected by a <see cref="TraceReader"/>.</summary>
internal static class Report
{
    private const double TickMilliseconds = 15.625;

    /// <summary>Prints every scenario's aggregates.</summary>
    /// <param name="reader">The reader holding the aggregates.</param>
    /// <param name="output">Where to print.</param>
    public static void Print(TraceReader reader, TextWriter output)
    {
        double offset = InterruptTimeOffset(reader);
        foreach (ScenarioData scenario in reader.Scenarios)
        {
            output.WriteLine(Invariant($"===== {scenario.Scenario.Name} ({scenario.Seconds:F1} s)"));

            output.WriteLine(
                Invariant($"WakeGrid threads readied: {scenario.Wakes}; ") +
                "what preceded them on the same processor, most recent first:");
            foreach ((string pattern, int count) in scenario.WakePatterns.OrderByDescending(pair => pair.Value).Take(6))
            {
                output.WriteLine(Invariant($"    {count * 100.0 / Math.Max(scenario.Wakes, 1),5:F1}%  {pattern}"));
            }

            PrintRates(output, "Interrupts per second:", scenario.Interrupts, scenario.Seconds, 8);
            PrintRates(output, "DPCs per second:", scenario.DeferredProcedureCalls, scenario.Seconds, 6);
            PrintTimers(output, scenario, reader.Expirations, offset);
            output.WriteLine();
        }
    }

    private static void PrintRates(
        TextWriter output,
        string title,
        Dictionary<string, int> counts,
        double seconds,
        int limit)
    {
        output.WriteLine(title);
        foreach ((string key, int count) in counts.OrderByDescending(pair => pair.Value).Take(limit))
        {
            output.WriteLine(Invariant($"    {count / seconds,8:F1}  {key}"));
        }
    }

    private static void PrintTimers(
        TextWriter output,
        ScenarioData scenario,
        IReadOnlyList<TimerEvent> expirations,
        double offset)
    {
        List<TimerEvent> sets = scenario.TimerSets;
        output.WriteLine(Invariant($"Timers set by WakeGrid threads: {sets.Count}"));
        if (sets.Count == 0 || double.IsNaN(offset))
        {
            return;
        }

        // Interrupt time at the moment each timer was set, from the trace timestamp and the estimated offset.
        Distribution requested = new(sets.Select(set => (set.Due - ((set.Time * 10_000) - offset)) / 10_000));
        Distribution phase = new(sets.Select(set => (set.Due % 156_250) / 10_000.0));

        Dictionary<(ulong Timer, ulong Due), double> expiryTimes = [];
        foreach (TimerEvent expiry in expirations)
        {
            expiryTimes.TryAdd((expiry.Timer, expiry.Due), expiry.Time);
        }

        List<double> lateness = [];
        foreach (TimerEvent set in sets)
        {
            if (expiryTimes.TryGetValue((set.Timer, set.Due), out double expiredAt))
            {
                lateness.Add(((expiredAt * 10_000) - offset - set.Due) / 10_000);
            }
        }

        output.WriteLine(Invariant($"    due time minus time set, ms       {requested}"));
        output.WriteLine(Invariant($"    due time modulo {TickMilliseconds} ms, ms    {phase}"));
        if (lateness.Count > 0)
        {
            output.WriteLine(Invariant($"    expiry minus due time, ms         {new Distribution(lateness)}"));
        }
    }

    /// <summary>
    /// Estimates the offset between trace time and interrupt time. A timer expires at or after its due time, so the
    /// smallest difference between expiry and due time over WakeGrid's own timers is the offset.
    /// </summary>
    private static double InterruptTimeOffset(TraceReader reader)
    {
        HashSet<ulong> timers = [.. reader.Scenarios.SelectMany(s => s.TimerSets).Select(set => set.Timer)];
        double[] differences =
        [
            .. reader.Expirations.Where(e => timers.Contains(e.Timer)).Select(e => (e.Time * 10_000) - e.Due),
        ];
        return differences.Length == 0 ? double.NaN : differences.Min();
    }
}
