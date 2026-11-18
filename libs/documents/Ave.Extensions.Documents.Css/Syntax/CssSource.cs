using System;
using System.Collections.Generic;
using System.Text;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Css.Syntax;

/// <summary>
/// The text of a style sheet with every carriage return, and every carriage return followed by a line feed, made one
/// line feed, and an index of where each line starts, so a token's index converts to a line and column
/// (<see cref="TextPosition"/>). The rest of "filter code points" (CSS Syntax 3 §3.3: form feed to line feed, NULL and
/// surrogates to U+FFFD) is done by the tokenizer as it reads, so that a form feed, which an editor shows inside a line,
/// does not start a new line in the positions. Removing a carriage return changes no column.
/// </summary>
internal sealed class CssSource
{
    private readonly int[] _lineStarts;

    private readonly Func<int, TextPosition>? _positions;

    private CssSource(string text, int[] lineStarts, TextPosition origin, Func<int, TextPosition>? positions)
    {
        Text = text;
        _lineStarts = lineStarts;
        Origin = origin;
        _positions = positions;
    }

    /// <summary>The text, carriage returns normalised.</summary>
    public string Text { get; }

    /// <summary>Where the text starts in its document: 1:1 for a style sheet file; the place of the <c>style</c> element's
    /// first character or of the attribute value for CSS inside HTML.</summary>
    public TextPosition Origin { get; }

    /// <summary>Reads a text.</summary>
    /// <param name="text">The style sheet.</param>
    /// <param name="origin">Where it starts in its document (null: 1:1).</param>
    /// <param name="positions">Where each index was written, when it is not one run from the origin (an HTML
    /// attribute value); null to count from the origin.</param>
    /// <returns>The source.</returns>
    public static CssSource Of(string text, TextPosition? origin = null, Func<int, TextPosition>? positions = null)
    {
        var normalised = text.Contains('\r', StringComparison.Ordinal) ? Normalise(text) : text;
        var starts = new List<int> { 0 };
        for (var i = 0; i < normalised.Length; i++)
        {
            if (normalised[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        // The map is of the text as given; it holds no carriage return when it comes from HTML, so the indices agree.
        return new CssSource(normalised, [.. starts], origin ?? TextPosition.Start, normalised.Length == text.Length ? positions : null);
    }

    /// <summary>The line and column of an index (the length of the text, the end, is a position too), counted from
    /// <see cref="Origin"/>: the first line continues the origin's line at its column.</summary>
    public TextPosition PositionOf(int index)
    {
        if (_positions is not null)
        {
            return _positions(index);
        }

        var line = Array.BinarySearch(_lineStarts, index);
        if (line < 0)
        {
            line = ~line - 1;
        }

        var column = index - _lineStarts[line] + 1;
        return line == 0
            ? new TextPosition(Origin.Line, Origin.Column + column - 1)
            : new TextPosition(Origin.Line + line, column);
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
