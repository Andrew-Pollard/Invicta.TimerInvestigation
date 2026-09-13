// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Threading;

/// <summary>
/// A request for a finer global timer resolution, made with <c>timeBeginPeriod</c> and cleared with
/// <c>timeEndPeriod</c> when disposed.
/// </summary>
public sealed class TimerResolutionRequest : IDisposable
{
    private readonly uint _milliseconds;
    private bool _disposed;

    /// <summary>Requests the given timer resolution.</summary>
    /// <param name="milliseconds">The requested resolution, in whole milliseconds.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="milliseconds"/> is less than 1.</exception>
    /// <exception cref="InvalidOperationException">Windows rejected the request.</exception>
    public TimerResolutionRequest(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(milliseconds, 1);

        _milliseconds = (uint)milliseconds;
        if (Winmm.timeBeginPeriod(_milliseconds) != Winmm.TIMERR_NOERROR)
        {
            throw new InvalidOperationException("timeBeginPeriod rejected the requested resolution.");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _ = Winmm.timeEndPeriod(_milliseconds);
        }
    }
}
