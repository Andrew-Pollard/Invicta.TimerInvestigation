// © 2026 Andrew Pollard. All rights reserved.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Invicta.Threading;

/// <summary>Restricts threads to particular logical processors.</summary>
public static class ThreadAffinity
{
    /// <summary>Restricts the calling thread to a single logical processor.</summary>
    /// <param name="processor">The zero-based index of the logical processor, below 64.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="processor"/> is negative, or not below both 64 and <see cref="Environment.ProcessorCount"/>.
    /// </exception>
    /// <exception cref="Win32Exception">The affinity could not be set.</exception>
    public static void PinCurrentThread(int processor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(processor);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(processor, Math.Min(Environment.ProcessorCount, 64));

        if (Kernel32.SetThreadAffinityMask(Kernel32.GetCurrentThread(), (nuint)1 << processor) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetThreadAffinityMask failed.");
        }
    }
}
