// © 2026 Andrew Pollard. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Invicta;

// Names and types follow the Win32 headers rather than .editorconfig's .NET naming rules, per
// https://learn.microsoft.com/dotnet/standard/native-interop/best-practices.
#pragma warning disable IDE1006 // Naming Styles

/// <summary>P/Invoke declarations for kernel32.dll.</summary>
internal static partial class Kernel32
{
    internal const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    internal const uint TIMER_MODIFY_STATE = 0x0002;
    internal const uint SYNCHRONIZE = 0x00100000;
    internal const uint INFINITE = 0xFFFFFFFF;
    internal const uint WAIT_FAILED = 0xFFFFFFFF;

    /// <summary>Creates or opens a waitable timer object.</summary>
    /// <param name="lpTimerAttributes">An optional <c>SECURITY_ATTRIBUTES</c> pointer, always zero here.</param>
    /// <param name="lpTimerName">A name for the timer, or null for an unnamed one.</param>
    /// <param name="dwFlags">
    /// <see cref="CREATE_WAITABLE_TIMER_HIGH_RESOLUTION"/>, or zero for a timer at the default resolution.
    /// </param>
    /// <param name="dwDesiredAccess">The access rights requested for the handle.</param>
    /// <returns>A handle to the timer, or an invalid handle on failure.</returns>
    [LibraryImport(nameof(Kernel32), SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial SafeWaitHandle CreateWaitableTimerExW(
        nint lpTimerAttributes,
        string? lpTimerName,
        uint dwFlags,
        uint dwDesiredAccess);

    /// <summary>Activates the timer, either as a one-shot or as a periodic timer.</summary>
    /// <param name="hTimer">The timer to activate.</param>
    /// <param name="lpDueTime">
    /// When the timer is first signaled. Negative values are a relative time in 100 ns units; positive values
    /// are an absolute file time.
    /// </param>
    /// <param name="lPeriod">The period in milliseconds, or zero for a timer that signals once.</param>
    /// <param name="pfnCompletionRoutine">An optional <c>PTIMERAPCROUTINE</c>, always zero here.</param>
    /// <param name="lpArgToCompletionRoutine">The argument passed to that APC, always zero here.</param>
    /// <param name="fResume">Nonzero to wake a suspended system when the timer signals, zero here.</param>
    /// <returns>A Win32 <c>BOOL</c>, which is a 4-byte int that is zero on failure.</returns>
    [LibraryImport(nameof(Kernel32), SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SetWaitableTimer(
        SafeWaitHandle hTimer,
        in long lpDueTime,
        int lPeriod,
        nint pfnCompletionRoutine,
        nint lpArgToCompletionRoutine,
        int fResume);

    /// <summary>Waits until the object is signaled or the timeout elapses.</summary>
    /// <param name="hHandle">The object to wait on.</param>
    /// <param name="dwMilliseconds">The timeout, or <see cref="INFINITE"/> to wait without one.</param>
    /// <returns><c>WAIT_OBJECT_0</c> once signaled, or <see cref="WAIT_FAILED"/> on failure.</returns>
    [LibraryImport(nameof(Kernel32), SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint WaitForSingleObject(SafeWaitHandle hHandle, uint dwMilliseconds);

    /// <summary>Gets a pseudo handle for the calling thread.</summary>
    /// <returns>A pseudo handle that is only meaningful within the calling thread and needs no closing.</returns>
    [LibraryImport(nameof(Kernel32))]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetCurrentThread();

    /// <summary>Sets the logical processors a thread may run on.</summary>
    /// <param name="hThread">The thread to restrict.</param>
    /// <param name="dwThreadAffinityMask">One bit per permitted logical processor.</param>
    /// <returns>The previous affinity mask, or zero on failure.</returns>
    [LibraryImport(nameof(Kernel32), SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nuint SetThreadAffinityMask(nint hThread, nuint dwThreadAffinityMask);

    /// <summary>Gets the interrupt time, excluding time spent suspended or hibernating.</summary>
    /// <param name="UnbiasedTime">The interrupt time in 100 ns units.</param>
    /// <returns>A Win32 <c>BOOL</c>, which is zero on failure.</returns>
    [LibraryImport(nameof(Kernel32), SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int QueryUnbiasedInterruptTime(out ulong UnbiasedTime);

    /// <summary>Gets the periodic time adjustment and the interval between clock interrupts.</summary>
    /// <param name="lpTimeAdjustment">The adjustment added to the clock at each interrupt, in 100 ns units.</param>
    /// <param name="lpTimeIncrement">The interval between clock interrupts, in 100 ns units.</param>
    /// <param name="lpTimeAdjustmentDisabled">A Win32 <c>BOOL</c> that is nonzero when no adjustment applies.</param>
    /// <returns>A Win32 <c>BOOL</c>, which is zero on failure.</returns>
    [LibraryImport(nameof(Kernel32), SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetSystemTimeAdjustment(
        out uint lpTimeAdjustment,
        out uint lpTimeIncrement,
        out int lpTimeAdjustmentDisabled);
}
