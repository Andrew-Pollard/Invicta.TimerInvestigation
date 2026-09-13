// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;

namespace Invicta;

/// <summary>
/// Identifies the kernel events the analysis uses. The interrupt, DPC and high-resolution timer events are decoded
/// by the Windows event schema; the meanings of the timer set and expiration opcodes, and of the idle-state events,
/// were inferred from their payloads and from which threads log them.
/// </summary>
internal static class KernelEvents
{
    /// <summary>The PerfInfo task of the NT kernel logger.</summary>
    public static readonly Guid PerfInfo = new("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc");

    /// <summary>The provider of processor idle-state events enabled by WPR's IdleStates keyword.</summary>
    public static readonly Guid IdleStates = new("e43445e0-0903-48c3-b878-ff0fccebdd04");

    /// <summary>A line-based interrupt service routine: initial time, routine, return value, vector.</summary>
    public const int Interrupt = 67;

    /// <summary>A message-signaled interrupt service routine, with the same layout plus a message number.</summary>
    public const int MessageSignaledInterrupt = 50;

    /// <summary>A deferred procedure call: initial time, routine.</summary>
    public const int DeferredProcedureCall = 68;

    /// <summary>A timer DPC, with the same layout as <see cref="DeferredProcedureCall"/>.</summary>
    public const int TimerDeferredProcedureCall = 69;

    /// <summary>A threaded DPC, with the same layout as <see cref="DeferredProcedureCall"/>.</summary>
    public const int ThreadedDeferredProcedureCall = 66;

    /// <summary>Inferred: a timer expiring, logged in interrupt context with its due time and object address.</summary>
    public const int TimerExpiration = 81;

    /// <summary>Inferred: a thread setting a timer, with the due time and object address.</summary>
    public const int TimerSet = 83;

    /// <summary>Gets a readable label for a PerfInfo or idle-state event that has no driver to name.</summary>
    /// <param name="provider">The event's provider or task GUID.</param>
    /// <param name="opcode">The event's opcode.</param>
    /// <returns>The label.</returns>
    public static string Label(Guid provider, int opcode)
    {
        if (provider == IdleStates)
        {
            return opcode switch
            {
                57 => "idle enter",
                58 => "idle exit",
                _ => Invariant(opcode, "idle-state event {0}"),
            };
        }

        return opcode switch
        {
            TimerExpiration => "timer expiration",
            TimerSet => "timer set",
            104 => "high-resolution timer set",
            105 => "high-resolution timer expiration",
            106 => "high-resolution timer cancel",
            _ => Invariant(opcode, "PerfInfo opcode {0}"),
        };
    }

    private static string Invariant(int value, string format)
    {
        return string.Format(CultureInfo.InvariantCulture, format, value);
    }
}
