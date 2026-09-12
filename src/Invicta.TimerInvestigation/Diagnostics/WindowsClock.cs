// © 2026 Andrew Pollard. All rights reserved.

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Invicta.Diagnostics;

/// <summary>Reads the state of the Windows system clock and timer resolution.</summary>
public static class WindowsClock
{
    /// <summary>Queries the interval between clock interrupts that Windows reports, 15.625 ms by default.</summary>
    /// <returns>The clock increment.</returns>
    /// <exception cref="Win32Exception">The query failed.</exception>
    public static TimeSpan QueryClockIncrement()
    {
        if (Kernel32.GetSystemTimeAdjustment(out _, out uint increment, out _) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetSystemTimeAdjustment failed.");
        }

        return TimeSpan.FromTicks(increment);
    }

    /// <summary>
    /// Queries the global timer resolution: the range Windows supports and the value currently in effect, which
    /// any process can lower with <c>timeBeginPeriod</c>.
    /// </summary>
    /// <returns>The coarsest, finest and current resolutions.</returns>
    /// <exception cref="InvalidOperationException">The query failed.</exception>
    public static TimerResolution QueryTimerResolution()
    {
        int status = Ntdll.NtQueryTimerResolution(out uint coarsest, out uint finest, out uint current);
        if (status < 0)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"NtQueryTimerResolution failed with 0x{status:X8}."));
        }

        return new TimerResolution(
            Coarsest: TimeSpan.FromTicks(coarsest),
            Finest: TimeSpan.FromTicks(finest),
            Current: TimeSpan.FromTicks(current));
    }

    /// <summary>
    /// Queries the unbiased interrupt time. Windows updates it when an interrupt is serviced, so polling it shows
    /// when interrupts occur.
    /// </summary>
    /// <returns>The interrupt time, excluding time spent suspended or hibernating.</returns>
    /// <exception cref="Win32Exception">The query failed.</exception>
    public static TimeSpan QueryUnbiasedInterruptTime()
    {
        if (Kernel32.QueryUnbiasedInterruptTime(out ulong time) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "QueryUnbiasedInterruptTime failed.");
        }

        return TimeSpan.FromTicks((long)time);
    }
}
