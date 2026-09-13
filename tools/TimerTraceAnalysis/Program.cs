// © 2026 Andrew Pollard. All rights reserved.

using System.Text.Json;

namespace Invicta;

/// <summary>
/// Analyzes a trace recorded by <c>Trace-TimerWakeUps.ps1</c>: for each scenario, what preceded each wake-up of a
/// WakeGrid thread, interrupt and DPC rates per driver, and when WakeGrid's timers were due and expired. Only
/// aggregates are printed; process IDs, addresses and system details in the trace are not.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Reads the trace and timeline and prints the analysis.</summary>
    /// <param name="args">The path of the .etl trace, then the path of its timeline.json.</param>
    /// <returns>Zero on success, or 1 when the arguments are missing.</returns>
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: TimerTraceAnalysis <trace.etl> <timeline.json>");
            return 1;
        }

        string timeline = File.ReadAllText(args[1]);
        List<Scenario> scenarios = JsonSerializer.Deserialize<List<Scenario>>(timeline, s_jsonOptions) ?? [];
        TraceReader reader = TraceReader.Read(args[0], scenarios);
        Report.Print(reader, Console.Out);
        return 0;
    }
}
