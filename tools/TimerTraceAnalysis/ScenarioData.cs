// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta;

/// <summary>The aggregates collected for one scenario of a trace.</summary>
/// <param name="scenario">The scenario.</param>
/// <param name="start">The scenario's start, in milliseconds from the start of the trace.</param>
/// <param name="end">The scenario's end, in milliseconds from the start of the trace.</param>
internal sealed class ScenarioData(Scenario scenario, double start, double end)
{
    /// <summary>Gets the scenario.</summary>
    public Scenario Scenario { get; } = scenario;

    /// <summary>Gets the scenario's length, in seconds.</summary>
    public double Seconds => (end - start) / 1000;

    /// <summary>Gets the number of interrupts per driver and vector.</summary>
    public Dictionary<string, int> Interrupts { get; } = [];

    /// <summary>Gets the number of DPCs per kind and driver.</summary>
    public Dictionary<string, int> DeferredProcedureCalls { get; } = [];

    /// <summary>Gets how often each sequence of events preceded a WakeGrid thread being readied.</summary>
    public Dictionary<string, int> WakePatterns { get; } = [];

    /// <summary>Gets the timers set by WakeGrid threads.</summary>
    public List<TimerEvent> TimerSets { get; } = [];

    /// <summary>Gets or sets the number of times a WakeGrid thread was readied.</summary>
    public int Wakes { get; set; }

    /// <summary>Reports whether a point in the trace falls within the scenario.</summary>
    /// <param name="time">The point, in milliseconds from the start of the trace.</param>
    /// <returns><see langword="true"/> if the point is within the scenario.</returns>
    public bool Contains(double time)
    {
        return time >= start && time <= end;
    }
}
