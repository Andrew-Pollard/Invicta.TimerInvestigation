// © 2026 Andrew Pollard. All rights reserved.

namespace Invicta;

/// <summary>A kernel timer being set or expiring.</summary>
/// <param name="Time">When the event was logged, in milliseconds from the start of the trace.</param>
/// <param name="Due">The timer's due time, in 100 ns units of interrupt time.</param>
/// <param name="Timer">The address of the timer object, used only to match sets with expirations.</param>
internal readonly record struct TimerEvent(double Time, ulong Due, ulong Timer);
