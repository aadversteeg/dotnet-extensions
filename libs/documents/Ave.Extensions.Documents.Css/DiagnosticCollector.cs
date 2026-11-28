using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css;

/// <summary>
/// The diagnostics of one reading or one cascade, at most <see cref="CssInputLimits.MaxDiagnostics"/>: past the limit
/// they are only counted, and the list ends with one <see cref="DocumentErrorCodes.TooManyDiagnostics"/>, a refusal. Hostile
/// input can make a diagnostic every few characters; this bounds the memory they take.
/// </summary>
internal sealed class DiagnosticCollector
{
    private readonly List<Error> _kept = [];
    private int _suppressed;
    private int _refusals;
    private string? _firstRefusal;

    /// <summary>The number kept so far.</summary>
    public int Count => _kept.Count;

    /// <summary>The diagnostic kept at a place.</summary>
    /// <param name="index">The place, below <see cref="Count"/>.</param>
    public Error this[int index] => _kept[index];

    /// <summary>Adds a diagnostic, or counts it past the limit (and whether it refuses the document). The summary of
    /// another collector adds its counts.</summary>
    public void Add(Error error)
    {
        if (error.Code == DocumentErrorCodes.TooManyDiagnostics)
        {
            _suppressed += MetadataCount(error, ErrorMetadataKeys.Value, 1);
            _refusals += MetadataCount(error, ErrorMetadataKeys.Refusals, 0);
            _firstRefusal ??= FirstRefusal(error);
            return;
        }

        if (_kept.Count < CssInputLimits.MaxDiagnostics)
        {
            _kept.Add(error);
            return;
        }

        _suppressed++;
        if (CssDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal)
        {
            _refusals++;
            _firstRefusal ??= error.Message;
        }
    }

    /// <summary>Adds diagnostics.</summary>
    /// <summary>Takes back the diagnostics kept after the first <paramref name="count"/>; those past
    /// <see cref="CssInputLimits.MaxDiagnostics"/> stay counted.</summary>
    /// <param name="count">How many to keep, at most <see cref="Count"/>.</param>
    public void TruncateTo(int count) => _kept.RemoveRange(count, _kept.Count - count);

    public void AddRange(IEnumerable<Error> errors)
    {
        foreach (var error in errors)
        {
            Add(error);
        }
    }

    /// <summary>The diagnostics kept, and the count of the others at the end.</summary>
    public ValueList<Error> ToValueList()
    {
        if (_suppressed == 0)
        {
            return [.. _kept];
        }

        return [.. _kept, LeftOut(_suppressed, _refusals, _firstRefusal, CssInputLimits.MaxDiagnostics)];
    }

    /// <summary>The summary of the diagnostics left out: how many, how many of them refuse the document, and the first
    /// of those (the reason a refusal would otherwise not show).</summary>
    internal static Error LeftOut(int suppressed, int refusals, string? firstRefusal, int kept)
    {
        var summary = new Error(DocumentErrorCodes.TooManyDiagnostics,
                $"{suppressed} more diagnostics are left out, {refusals} of them refusals{(firstRefusal is { } first ? $" (the first: {first})" : string.Empty)}; {kept} are kept.")
            .With(ErrorMetadataKeys.Value, suppressed)
            .With(ErrorMetadataKeys.Refusals, refusals);
        return firstRefusal is null ? summary : summary.With(ErrorMetadataKeys.FirstRefusal, firstRefusal);
    }

    /// <summary>The first refusal a summary of left-out diagnostics quotes, or null.</summary>
    internal static string? FirstRefusal(Error summary) =>
        summary.Metadata is { } metadata && metadata.TryGetValue(ErrorMetadataKeys.FirstRefusal, out var value) ? value as string : null;

    private static int MetadataCount(Error error, string key, int otherwise) =>
        error.Metadata is { } metadata && metadata.TryGetValue(key, out var value) && value is int count ? count : otherwise;
}
