// © 2026 Andrew Pollard. All rights reserved.

using System.Runtime.InteropServices;

namespace Invicta;

// Names and types follow the native headers rather than .editorconfig's .NET naming rules, per
// https://learn.microsoft.com/dotnet/standard/native-interop/best-practices.
#pragma warning disable IDE1006 // Naming Styles

/// <summary>P/Invoke declarations for ntdll.dll.</summary>
internal static partial class Ntdll
{
    /// <summary>Gets the range of supported timer resolutions and the resolution currently in effect.</summary>
    /// <param name="MaximumTime">The coarsest resolution, in 100 ns units.</param>
    /// <param name="MinimumTime">The finest resolution, in 100 ns units.</param>
    /// <param name="CurrentTime">The resolution currently in effect, in 100 ns units.</param>
    /// <returns>An <c>NTSTATUS</c>, which is negative on failure.</returns>
    [LibraryImport(nameof(Ntdll))]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int NtQueryTimerResolution(
        out uint MaximumTime,
        out uint MinimumTime,
        out uint CurrentTime);
}
