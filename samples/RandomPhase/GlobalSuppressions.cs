// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Security", "CA5394:Do not use insecure randomness",
    Justification = "The randomness only staggers when each wait starts and has no security role.")]
