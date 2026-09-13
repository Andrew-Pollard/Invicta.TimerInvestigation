// © 2026 Andrew Pollard. All rights reserved.

namespace Invicta;

/// <summary>The last few labeled events logged on one processor.</summary>
internal sealed class RecentEvents
{
    private const int Capacity = 6;

    private readonly (double Time, string Label)[] _events = new (double Time, string Label)[Capacity];
    private int _next;
    private int _count;

    /// <summary>Records an event, discarding the oldest once full.</summary>
    /// <param name="time">When the event was logged, in milliseconds from the start of the trace.</param>
    /// <param name="label">A readable label for the event.</param>
    public void Add(double time, string label)
    {
        _events[_next] = (time, label);
        _next = (_next + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
    }

    /// <summary>Gets the labels of events logged shortly before a point, most recent first.</summary>
    /// <param name="time">The point, in milliseconds from the start of the trace.</param>
    /// <param name="window">How far back to look, in milliseconds.</param>
    /// <param name="limit">The most labels to return.</param>
    /// <returns>The labels.</returns>
    public IEnumerable<string> Before(double time, double window, int limit)
    {
        for (int i = 1; i <= _count && limit > 0; i++)
        {
            (double eventTime, string label) = _events[(_next - i + Capacity) % Capacity];
            if (time - eventTime > window)
            {
                yield break;
            }

            limit--;
            yield return label;
        }
    }
}
