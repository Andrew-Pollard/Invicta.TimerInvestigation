// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Invicta;

/// <summary>A named period of a trace, as recorded in the capture script's timeline.</summary>
/// <param name="Name">The scenario's name.</param>
/// <param name="StartUtc">When the scenario started.</param>
/// <param name="EndUtc">When the scenario ended.</param>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by System.Text.Json when reading the timeline.")]
internal sealed record Scenario(string Name, DateTime StartUtc, DateTime EndUtc);
