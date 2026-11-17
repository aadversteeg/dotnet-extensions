using System;
using System.Diagnostics;
using System.Linq;
using Ave.Documents.Fluent;
using Ave.Documents.Fonts;

namespace Ave.Documents.Samples;

/// <summary>
/// Measures how long one document takes from the front-end value to the PDF bytes (translate, lay out, paint, write),
/// with the fonts already parsed, as the reporting PoC measured Typst. Measurement only: nothing in the engine is tuned
/// by it. The first render of a process is the cold time (it includes the JIT compiling the engine's code); the warm
/// time is the median of the renders after it.
/// </summary>
public static class Timing
{
    /// <summary>Renders the document once cold and <paramref name="warmRuns"/> times warm and prints the times.</summary>
    /// <param name="label">What is measured, for the output line.</param>
    /// <param name="build">Builds the document (included in the time: building the value is part of the work).</param>
    /// <param name="fonts">The parsed fonts.</param>
    /// <param name="warmRuns">How many warm renders.</param>
    /// <returns>True when every render succeeded.</returns>
    public static bool Measure(string label, Func<Document> build, FontSet fonts, int warmRuns)
    {
        double Once(out int size)
        {
            var watch = Stopwatch.StartNew();
            var pdf = build().ToPdf(fonts, SampleOutput.Options);
            watch.Stop();
            size = pdf.IsSuccess ? pdf.Value.Length : -1;
            return watch.Elapsed.TotalMilliseconds;
        }

        var cold = Once(out var bytes);
        var warm = Enumerable.Range(0, warmRuns).Select(_ => Once(out _)).Order().ToArray();
        var median = warm.Length == 0 ? double.NaN : warm[warm.Length / 2];
        Console.WriteLine(FormattableString.Invariant(
            $"{label}: {bytes} bytes, cold {cold:F1} ms, warm median {median:F1} ms (min {warm.FirstOrDefault():F1}, max {warm.LastOrDefault():F1}, n={warmRuns})"));
        return bytes > 0;
    }
}
