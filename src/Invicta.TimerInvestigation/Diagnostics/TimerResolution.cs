// © 2026 Andrew Pollard. All rights reserved.

namespace Invicta.Diagnostics;

/// <summary>The timer resolutions Windows supports, and the one currently in effect.</summary>
/// <param name="Coarsest">The coarsest resolution, 15.625 ms by default.</param>
/// <param name="Finest">The finest resolution, typically 0.5 ms.</param>
/// <param name="Current">The resolution currently in effect across the machine.</param>
public readonly record struct TimerResolution(TimeSpan Coarsest, TimeSpan Finest, TimeSpan Current);
