// © 2026 Andrew Pollard. All rights reserved.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Invicta.Threading;

/// <summary>
/// A Win32 waitable timer used directly, so that a measurement involves no .NET timer machinery at all.
/// </summary>
public sealed class WaitableTimer : IDisposable
{
    private readonly SafeWaitHandle _handle;

    /// <summary>Creates an unnamed waitable timer.</summary>
    /// <param name="highResolution">
    /// <see langword="true"/> to create the timer with <c>CREATE_WAITABLE_TIMER_HIGH_RESOLUTION</c>, which
    /// requires Windows 10 version 1803 or later; <see langword="false"/> for the default resolution.
    /// </param>
    /// <exception cref="Win32Exception">The timer could not be created.</exception>
    public WaitableTimer(bool highResolution)
    {
        _handle = Kernel32.CreateWaitableTimerExW(
            lpTimerAttributes: nint.Zero,
            lpTimerName: null,
            highResolution ? Kernel32.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION : 0,
            Kernel32.TIMER_MODIFY_STATE | Kernel32.SYNCHRONIZE);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWaitableTimerExW failed.");
        }
    }

    /// <summary>Arms the timer for a relative due time and blocks the calling thread until it is signaled.</summary>
    /// <param name="dueTime">How long from now the timer is due. Values below 100 ns are raised to 100 ns.</param>
    /// <exception cref="Win32Exception">The timer could not be armed or waited on.</exception>
    public void Wait(TimeSpan dueTime)
    {
        // TimeSpan ticks are 100 ns, the unit the API uses; a negative due time is relative.
        long relative = -Math.Max(dueTime.Ticks, 1);
        if (Kernel32.SetWaitableTimer(_handle, in relative, 0, nint.Zero, nint.Zero, fResume: 0) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWaitableTimer failed.");
        }

        if (Kernel32.WaitForSingleObject(_handle, Kernel32.INFINITE) == Kernel32.WAIT_FAILED)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "WaitForSingleObject failed.");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _handle.Dispose();
    }
}
