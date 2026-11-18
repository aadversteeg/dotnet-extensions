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

    /// <summary>The number kept so far.</summary>
    public int Count => _kept.Count;

    /// <summary>Adds a diagnostic, or counts it past the limit.</summary>
    public void Add(Error error)
    {
        if (_kept.Count < CssInputLimits.MaxDiagnostics)
        {
            _kept.Add(error);
        }
        else
        {
            _suppressed++;
        }
    }

    /// <summary>Adds diagnostics.</summary>
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

        return [.. _kept, new Error(DocumentErrorCodes.TooManyDiagnostics, $"{_suppressed} more diagnostics are left out: {CssInputLimits.MaxDiagnostics} are kept.")
            .With(ErrorMetadataKeys.Value, _suppressed)];
    }
}
