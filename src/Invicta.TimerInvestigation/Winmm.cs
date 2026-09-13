// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace Invicta;

// Names and types follow the Win32 headers rather than .editorconfig's .NET naming rules, per
// https://learn.microsoft.com/dotnet/standard/native-interop/best-practices.
#pragma warning disable IDE1006 // Naming Styles

/// <summary>P/Invoke declarations for winmm.dll.</summary>
internal static partial class Winmm
{
    internal const uint TIMERR_NOERROR = 0;

    /// <summary>Requests a minimum resolution for periodic timers.</summary>
    /// <param name="uPeriod">The requested resolution, in milliseconds.</param>
    /// <returns><see cref="TIMERR_NOERROR"/> on success.</returns>
    [LibraryImport(nameof(Winmm))]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint timeBeginPeriod(uint uPeriod);

    /// <summary>Clears a request previously made with <see cref="timeBeginPeriod"/>.</summary>
    /// <param name="uPeriod">The resolution passed to the matching <see cref="timeBeginPeriod"/> call.</param>
    /// <returns><see cref="TIMERR_NOERROR"/> on success.</returns>
    [LibraryImport(nameof(Winmm))]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint timeEndPeriod(uint uPeriod);
}
