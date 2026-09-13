// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Diagnostics;

/// <summary>One bin of a <see cref="Distribution"/> histogram.</summary>
/// <param name="Start">The inclusive lower bound of the bin, in milliseconds.</param>
/// <param name="Share">The share of all measurements that fall in the bin, from 0 to 1.</param>
public readonly record struct HistogramBin(double Start, double Share);
