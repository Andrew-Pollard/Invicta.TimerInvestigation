// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Diagnostics;

/// <summary>A candidate period for a grid of events, and how closely the events lie on it.</summary>
/// <param name="Period">The period, in milliseconds.</param>
/// <param name="Concentration">From 0 (no relationship) to 1 (every event at the same phase).</param>
public readonly record struct GridPeriod(double Period, double Concentration);
