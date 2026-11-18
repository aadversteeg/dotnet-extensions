using System;
using System.Collections.Generic;
using System.Text;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Html.Parsing.Tokenizing;

/// <summary>
/// The input stream after "preprocessing the input stream" (HTML Standard §13.2.3.5): every carriage return followed by
/// a line feed, and every carriage return alone, becomes one line feed (the Infra Standard's "normalize newlines"). It
/// knows where each line starts, so any index converts to a line and column (<see cref="TextPosition"/>). Removing a
/// carriage return changes no column: it only ever stood at the end of a line.
/// </summary>
internal sealed class SourceText
{
    private readonly int[] _lineStarts;

    private SourceText(string text, int[] lineStarts)
    {
        Text = text;
        _lineStarts = lineStarts;
    }

    /// <summary>The normalised text.</summary>
    public string Text { get; }

    /// <summary>The length of the normalised text.</summary>
    public int Length => Text.Length;

    /// <summary>Normalises the newlines of a text and indexes its lines.</summary>
    /// <param name="text">The decoded text.</param>
    /// <returns>The source text.</returns>
    public static SourceText Of(string text)
    {
        var normalised = text.Contains('\r', StringComparison.Ordinal) ? Normalise(text) : text;
        return Indexed(normalised);
    }

    /// <summary>The index in the normalised text of each index of <paramref name="text"/> (sorted ascending), for
    /// positions found before normalising (the decoder's replacements).</summary>
    /// <param name="text">The text before normalising.</param>
    /// <param name="indices">Indices in it, ascending.</param>
    /// <returns>The same places in the normalised text.</returns>
    public static IReadOnlyList<int> NormalisedIndices(string text, IReadOnlyList<int> indices)
    {
        var mapped = new List<int>(indices.Count);
        var removed = 0;
        var next = 0;
        for (var i = 0; i < text.Length && next < indices.Count; i++)
        {
            while (next < indices.Count && indices[next] == i)
            {
                mapped.Add(i - removed);
                next++;
            }

            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                removed++;
            }
        }

        return mapped;
    }

    private static SourceText Indexed(string normalised)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < normalised.Length; i++)
        {
            if (normalised[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return new SourceText(normalised, [.. starts]);
    }

    /// <summary>The line and column of an index; the length of the text (the end of the input) is a position too.</summary>
    /// <param name="index">An index from 0 to <see cref="Length"/>.</param>
    /// <returns>The 1-based line and column.</returns>
    public TextPosition PositionOf(int index)
    {
        var line = Array.BinarySearch(_lineStarts, index);
        if (line < 0)
        {
            line = ~line - 1;
        }

        return new TextPosition(line + 1, index - _lineStarts[line] + 1);
    }

    private static string Normalise(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                builder.Append('\n');
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
            }
            else
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }
}
