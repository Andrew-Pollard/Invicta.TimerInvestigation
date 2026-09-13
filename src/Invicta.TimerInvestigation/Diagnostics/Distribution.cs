// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;

namespace Invicta.Diagnostics;

/// <summary>Summary statistics over a set of measurements in milliseconds.</summary>
public sealed class Distribution
{
    private readonly double[] _sorted;

    /// <summary>Creates a distribution from a set of measurements.</summary>
    /// <param name="values">The measurements, in milliseconds.</param>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    public Distribution(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        _sorted = [.. values.Order()];
        if (_sorted.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        Mean = _sorted.Average();
    }

    /// <summary>Gets the number of measurements.</summary>
    public int Count => _sorted.Length;

    /// <summary>Gets the arithmetic mean.</summary>
    public double Mean { get; }

    /// <summary>Gets the largest measurement.</summary>
    public double Maximum => _sorted[^1];

    /// <summary>Gets the measurement at a given quantile, using the nearest rank below it.</summary>
    /// <param name="fraction">The quantile, from 0 to 1; 0.5 is the median.</param>
    /// <returns>The measurement at that quantile.</returns>
    public double Quantile(double fraction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fraction, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fraction, 1);

        return _sorted[(int)Math.Min(_sorted.Length - 1, fraction * _sorted.Length)];
    }

    /// <summary>Groups the measurements into fixed-width bins, keeping only bins above a minimum share.</summary>
    /// <param name="binWidth">The width of each bin, in milliseconds.</param>
    /// <param name="minimumShare">The smallest share of all measurements, from 0 to 1, that a bin must hold.</param>
    /// <returns>The retained bins, in ascending order.</returns>
    public IReadOnlyList<HistogramBin> Histogram(double binWidth, double minimumShare)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binWidth);

        return
        [
            .. _sorted
                .GroupBy(value => Math.Floor(value / binWidth) * binWidth)
                .Select(group => new HistogramBin(group.Key, (double)group.Count() / _sorted.Length))
                .Where(bin => bin.Share >= minimumShare),
        ];
    }

    /// <summary>Formats the histogram as <c>start:share%</c> pairs, such as <c>16.0:91%</c>.</summary>
    /// <param name="binWidth">The width of each bin, in milliseconds.</param>
    /// <param name="minimumShare">The smallest share of all measurements, from 0 to 1, that a bin must hold.</param>
    /// <returns>The formatted histogram.</returns>
    public string FormatHistogram(double binWidth, double minimumShare)
    {
        IEnumerable<string> bins = Histogram(binWidth, minimumShare)
            .Select(bin => string.Create(CultureInfo.InvariantCulture, $"{bin.Start:0.0##}:{bin.Share * 100:F0}%"));

        return string.Join("  ", bins);
    }

    /// <summary>Formats the count, mean, 5th, 50th and 95th percentiles, and maximum.</summary>
    /// <returns>The formatted summary.</returns>
    public override string ToString()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"n={Count} mean={Mean:F3} p5={Quantile(0.05):F3} p50={Quantile(0.5):F3} p95={Quantile(0.95):F3} " +
            $"max={Maximum:F3}");
    }
}
