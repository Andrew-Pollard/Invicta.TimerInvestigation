// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using Invicta.Diagnostics;
using static System.FormattableString;

namespace Invicta;

/// <summary>
/// Polls the global timer resolution in a tight loop and reports how often it changes, how long it spends at each
/// value, and how long each spell at a finer resolution lasts. A spell of a few microseconds is invisible to an
/// occasional check.
/// </summary>
internal static class Program
{
    /// <summary>Polls for the given number of seconds and prints the report.</summary>
    /// <param name="args">Optional seconds to poll for; the default is 4.</param>
    private static void Main(string[] args)
    {
        double seconds = args.Length > 0 ? double.Parse(args[0], CultureInfo.InvariantCulture) : 4;
        long start = Stopwatch.GetTimestamp();
        long end = start + (long)(seconds * Stopwatch.Frequency);

        TimeSpan coarsest = WindowsClock.QueryTimerResolution().Coarsest;
        TimeSpan last = WindowsClock.QueryTimerResolution().Current;
        long lastChange = start;
        Dictionary<TimeSpan, double> timeAt = [];
        List<double> finerSpells = [];
        List<long> finerStarts = [];

        while (Stopwatch.GetTimestamp() < end)
        {
            TimeSpan current = WindowsClock.QueryTimerResolution().Current;
            if (current == last)
            {
                continue;
            }

            long now = Stopwatch.GetTimestamp();
            double spell = StopwatchTimestamps.ToMilliseconds(now - lastChange);
            timeAt[last] = timeAt.GetValueOrDefault(last) + spell;
            if (last < coarsest)
            {
                finerSpells.Add(spell);
            }

            if (current < coarsest && last >= coarsest)
            {
                finerStarts.Add(now);
            }

            last = current;
            lastChange = now;
        }

        double finalSpell = StopwatchTimestamps.ToMilliseconds(Stopwatch.GetTimestamp() - lastChange);
        timeAt[last] = timeAt.GetValueOrDefault(last) + finalSpell;

        int changes = finerSpells.Count + finerStarts.Count;
        Console.WriteLine(Invariant($"{changes} changes of the global timer resolution in {seconds:F1} s"));
        foreach ((TimeSpan resolution, double ms) in timeAt.OrderBy(pair => pair.Key))
        {
            Console.WriteLine(Invariant(
                $"    {resolution.TotalMilliseconds,7:F3} ms resolution for {ms / (seconds * 10):F2}% of the time"));
        }

        if (finerSpells.Count > 0)
        {
            Console.WriteLine(Invariant($"    finer spells, ms  {new Distribution(finerSpells)}"));
        }

        if (finerStarts.Count > 1)
        {
            Console.WriteLine(Invariant(
                $"    gaps between spells, ms  {new Distribution(StopwatchTimestamps.ToIntervals(finerStarts))}"));
        }
    }
}
