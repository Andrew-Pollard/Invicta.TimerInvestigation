// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace Invicta;

/// <summary>Reads a kernel trace once and collects per-scenario aggregates.</summary>
internal sealed class TraceReader
{
    private const string SampleName = "WakeGrid";

    private readonly List<(ulong Base, ulong End, string Name)> _drivers = [];
    private readonly HashSet<int> _sampleProcesses = [];
    private readonly Dictionary<int, int> _threadProcesses = [];
    private readonly Dictionary<int, RecentEvents> _recent = [];
    private readonly List<ScenarioData> _scenarios;
    private readonly List<TimerEvent> _expirations = [];
    private bool _driversSorted;

    private TraceReader(List<ScenarioData> scenarios)
    {
        _scenarios = scenarios;
    }

    /// <summary>Gets the aggregates for each scenario.</summary>
    public IReadOnlyList<ScenarioData> Scenarios => _scenarios;

    /// <summary>Gets every timer expiration in the trace.</summary>
    public IReadOnlyList<TimerEvent> Expirations => _expirations;

    /// <summary>Reads a trace.</summary>
    /// <param name="tracePath">The path of the .etl file.</param>
    /// <param name="scenarios">The scenarios recorded in the trace.</param>
    /// <returns>The reader, holding the collected aggregates.</returns>
    public static TraceReader Read(string tracePath, IReadOnlyList<Scenario> scenarios)
    {
        using ETWTraceEventSource source = new(tracePath);
        DateTime startUtc = source.SessionStartTime.ToUniversalTime();
        TraceReader reader = new(
        [
            .. scenarios.Select(s => new ScenarioData(
                s,
                (s.StartUtc - startUtc).TotalMilliseconds,
                (s.EndUtc - startUtc).TotalMilliseconds)),
        ]);

        source.Kernel.ImageDCStart += reader.OnImage;
        source.Kernel.ImageLoad += reader.OnImage;
        source.Kernel.ProcessDCStart += reader.OnProcess;
        source.Kernel.ProcessStart += reader.OnProcess;
        source.Kernel.ThreadDCStart += reader.OnThread;
        source.Kernel.ThreadStart += reader.OnThread;
        source.Kernel.DispatcherReadyThread += reader.OnReadyThread;
        source.AllEvents += reader.OnEvent;
        source.Process();

        return reader;
    }

    private void OnImage(ImageLoadTraceData data)
    {
        // Only kernel-mode images can service interrupts and DPCs.
        if (data.ImageBase >= 0xFFFF800000000000UL)
        {
            _drivers.Add((data.ImageBase, data.ImageBase + (ulong)data.ImageSize, Path.GetFileName(data.FileName)));
            _driversSorted = false;
        }
    }

    private void OnProcess(ProcessTraceData data)
    {
        if (data.ImageFileName.Contains(SampleName, StringComparison.OrdinalIgnoreCase))
        {
            _ = _sampleProcesses.Add(data.ProcessID);
        }
    }

    private void OnThread(ThreadTraceData data)
    {
        _threadProcesses[data.ThreadID] = data.ProcessID;
    }

    private void OnReadyThread(DispatcherReadyThreadTraceData data)
    {
        double time = data.TimeStampRelativeMSec;
        if (!_sampleProcesses.Contains(data.AwakenedProcessID) || ScenarioAt(time) is not { } scenario)
        {
            return;
        }

        // A process ID of -1 means the thread was readied from interrupt or DPC context.
        string context = data.ProcessID == -1 ? "interrupt context"
            : _sampleProcesses.Contains(data.ProcessID) ? "a WakeGrid thread" : "another process's thread";
        IEnumerable<string> before = _recent.TryGetValue(data.ProcessorNumber, out RecentEvents? recent)
            ? recent.Before(time, window: 2, limit: 3)
            : [];
        string pattern = $"{context}: {string.Join(" <- ", before)}";

        scenario.Wakes++;
        scenario.WakePatterns[pattern] = scenario.WakePatterns.GetValueOrDefault(pattern) + 1;
    }

    private void OnEvent(TraceEvent data)
    {
        Guid task = data.TaskGuid;
        int opcode = (int)data.Opcode;
        double time = data.TimeStampRelativeMSec;
        ScenarioData? scenario = ScenarioAt(time);

        if (task == KernelEvents.IdleStates || data.ProviderGuid == KernelEvents.IdleStates)
        {
            Recent(data.ProcessorNumber).Add(time, KernelEvents.Label(KernelEvents.IdleStates, opcode));
            return;
        }

        if (task != KernelEvents.PerfInfo)
        {
            return;
        }

        byte[] payload = data.EventData();
        string label;
        switch (opcode)
        {
            case KernelEvents.Interrupt or KernelEvents.MessageSignaledInterrupt when payload.Length >= 19:
            {
                string key = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{DriverAt(BitConverter.ToUInt64(payload, 8))} vector {BitConverter.ToUInt16(payload, 17)}");
                label = $"interrupt ({key})";
                Count(scenario?.Interrupts, key);
                break;
            }

            case KernelEvents.DeferredProcedureCall or KernelEvents.TimerDeferredProcedureCall
                or KernelEvents.ThreadedDeferredProcedureCall when payload.Length >= 16:
            {
                string kind = opcode == KernelEvents.TimerDeferredProcedureCall ? "timer DPC" : "DPC";
                string key = $"{kind} {DriverAt(BitConverter.ToUInt64(payload, 8))}";
                label = key;
                Count(scenario?.DeferredProcedureCalls, key);
                break;
            }

            case KernelEvents.TimerSet when payload.Length >= 16:
            {
                label = KernelEvents.Label(task, opcode);
                if (scenario is not null
                    && _threadProcesses.TryGetValue(data.ThreadID, out int process)
                    && _sampleProcesses.Contains(process))
                {
                    scenario.TimerSets.Add(ReadTimerEvent(time, payload));
                }

                break;
            }

            case KernelEvents.TimerExpiration when payload.Length >= 16:
                label = KernelEvents.Label(task, opcode);
                _expirations.Add(ReadTimerEvent(time, payload));
                break;

            default:
                label = KernelEvents.Label(task, opcode);
                break;
        }

        Recent(data.ProcessorNumber).Add(time, label);
    }

    private static void Count(Dictionary<string, int>? counts, string key)
    {
        counts?[key] = counts.GetValueOrDefault(key) + 1;
    }

    /// <summary>Reads the due time and timer address that lead a timer set or expiration payload.</summary>
    private static TimerEvent ReadTimerEvent(double time, byte[] payload)
    {
        return new TimerEvent(time, BitConverter.ToUInt64(payload, 0), BitConverter.ToUInt64(payload, 8));
    }

    private RecentEvents Recent(int processor)
    {
        if (!_recent.TryGetValue(processor, out RecentEvents? recent))
        {
            recent = new RecentEvents();
            _recent[processor] = recent;
        }

        return recent;
    }

    private ScenarioData? ScenarioAt(double time)
    {
        return _scenarios.Find(scenario => scenario.Contains(time));
    }

    private string DriverAt(ulong address)
    {
        if (!_driversSorted)
        {
            _drivers.Sort((a, b) => a.Base.CompareTo(b.Base));
            _driversSorted = true;
        }

        int low = 0;
        int high = _drivers.Count - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (address < _drivers[middle].Base)
            {
                high = middle - 1;
            }
            else if (address >= _drivers[middle].End)
            {
                low = middle + 1;
            }
            else
            {
                return _drivers[middle].Name;
            }
        }

        return "unknown driver";
    }
}
