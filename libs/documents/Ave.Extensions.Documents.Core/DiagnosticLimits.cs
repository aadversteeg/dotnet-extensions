using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Core;

/// <summary>
/// The bound on what one render reports, through every stage: at most <see cref="MaxDiagnostics"/> diagnostics or
/// errors are kept, and one <see cref="DocumentErrorCodes.TooManyDiagnostics"/> at the end counts the rest; an error's
/// message is at most <see cref="MaxMessageLength"/> characters, and an aggregate keeps its parts only while their
/// messages together stay within <see cref="MaxAggregateLength"/>, so its own message (the parts' messages joined)
/// stays within that too.
/// </summary>
public static class DiagnosticLimits
{
    /// <summary>The most diagnostics one render keeps: 10 000, as each reader of input keeps.</summary>
    public const int MaxDiagnostics = 10_000;

    /// <summary>The longest message of one error kept: 4 096 characters; a longer one is cut, with <c>…</c> at the end.
    /// The engine's messages quote at most 100 characters of the input each.</summary>
    public const int MaxMessageLength = 4_096;

    /// <summary>The most characters of the messages of an aggregate's parts together: 2^20.</summary>
    public const int MaxAggregateLength = 1 << 20;

    /// <summary>
    /// The diagnostics kept: in order, at most <see cref="MaxDiagnostics"/>, each message cut to
    /// <see cref="MaxMessageLength"/>; the rest counted in one
    /// <see cref="DocumentErrorCodes.TooManyDiagnostics"/> at the end, which also takes in the counts of such a summary
    /// among the diagnostics given.
    /// </summary>
    /// <param name="diagnostics">The diagnostics, in order.</param>
    /// <param name="refuses">Whether a diagnostic refuses the document; null when every one does (errors).</param>
    /// <returns>The diagnostics kept.</returns>
    public static List<Error> Kept(IEnumerable<Error> diagnostics, Func<Error, bool>? refuses = null) => Kept(diagnostics, refuses, long.MaxValue);

    private static List<Error> Kept(IEnumerable<Error> diagnostics, Func<Error, bool>? refuses, long maxLength)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var kept = new List<Error>();
        var (length, suppressed, refusals, own) = (0L, 0, 0, 0);
        Error? firstRefusal = null;
        Error? summary = null;
        var summaries = 0;
        // The parts of an aggregate are diagnostics of their own.
        foreach (var diagnostic in diagnostics.SelectMany(diagnostic => diagnostic.Parts()))
        {
            if (diagnostic.Code == DocumentErrorCodes.TooManyDiagnostics)
            {
                suppressed += Count(diagnostic, ErrorMetadataKeys.Value, 1);
                refusals += Count(diagnostic, ErrorMetadataKeys.Refusals, 0);
                summary = diagnostic;
                summaries++;
                continue;
            }

            var error = Cut(diagnostic);
            // An aggregate joins its parts' messages with "; ", so each part costs its message and two characters.
            if (kept.Count < MaxDiagnostics && length + error.Message.Length + 2 <= maxLength)
            {
                kept.Add(error);
                length += error.Message.Length + 2;
                continue;
            }

            suppressed++;
            own++;
            if (refuses?.Invoke(diagnostic) ?? true)
            {
                refusals++;
                firstRefusal ??= error;
            }
        }

        if (own == 0 && summaries == 1 && summary is { } only)
        {
            // Nothing more left out here: the one summary given says it as it was written.
            kept.Add(only);
        }
        else if (suppressed > 0)
        {
            kept.Add(new Error(DocumentErrorCodes.TooManyDiagnostics,
                    $"{suppressed} more diagnostics are left out, {refusals} of them refusals{(firstRefusal is { } first ? $" (the first: {Cut(first.Message, 200)})" : string.Empty)}; {kept.Count} are kept.")
                .With(ErrorMetadataKeys.Value, suppressed)
                .With(ErrorMetadataKeys.Refusals, refusals));
        }

        return kept;
    }

    /// <summary>The errors as one: the only one, or an aggregate of those <see cref="Kept(IEnumerable{Error}, Func{Error, bool})"/>
    /// keeps while their messages together stay within <see cref="MaxAggregateLength"/>.</summary>
    /// <param name="errors">The errors, at least one.</param>
    /// <returns>The error.</returns>
    public static Error Aggregate(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var kept = Kept(errors, null, MaxAggregateLength);
        return kept.Count == 1 ? kept[0] : Errors.Aggregate(kept);
    }

    private static Error Cut(Error error) =>
        error.Message.Length <= MaxMessageLength
            ? error
            : new Error(error.Code, Cut(error.Message, MaxMessageLength), error.Metadata, error.Inner);

    private static string Cut(string text, int length) => text.Length <= length ? text : string.Concat(text.AsSpan(0, length - 1), "…");

    private static int Count(Error error, string key, int otherwise) =>
        error.Metadata is { } metadata && metadata.TryGetValue(key, out var value) && value is int count ? count : otherwise;
}
