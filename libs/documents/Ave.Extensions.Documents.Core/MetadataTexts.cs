using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ave.Extensions.Documents.Core;

/// <summary>
/// THE RULE FOR A DOCUMENT'S METADATA TEXT, in one place for every layer: which texts a document's title, author and
/// description (<see cref="DocumentMetadata"/>) may be. They are written into the document information and the XMP
/// metadata, which is XML, and a reader shows them as they are, so a text holds only characters XML 1.0 allows
/// (<see cref="XmlCharacters"/>: of the control characters U+0000 to U+001F only tab, line feed and carriage return),
/// and none of the control characters U+007F to U+009F either. An author or a description is at most
/// <see cref="MaxLength"/> UTF-16 code units long. A title has no bound of its own: under PDF/A it is bounded, as every
/// string the file holds, by the profile's 32 767 bytes (veraPDF rule 6.1.13-3), checked when the PDF is written.
/// </summary>
/// <remarks>
/// A text that breaks the rule is refused, never cleaned: the metadata is then the text the author wrote or none, never a
/// text the engine made from it, and a hidden control character in a title is more likely a fault of whatever made the
/// input than meant. A front-end checks the author's text with it; the model check and the page model check apply it
/// again, because other producers than the front-ends may hand them a model or a page model. One pass over the text.
/// </remarks>
public static class MetadataTexts
{
    /// <summary>The longest author or description: 4 096 UTF-16 code units, far inside the longest string PDF/A allows
    /// (32 767 bytes; as UTF-16 such a text takes at most 8 194), longer than any name or summary of a document needs.</summary>
    public const int MaxLength = 4_096;

    /// <summary>The position of the first UTF-16 code unit that is not part of a character the rule allows, or null when
    /// every character is allowed.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The offset in UTF-16 code units, or null.</returns>
    public static int? IndexOfForbidden(string text)
    {
        var xml = XmlCharacters.IndexOfForbidden(text);
        var control = text.AsSpan().IndexOfAnyInRange('\u007F', '\u009F');
        return (xml, control) switch
        {
            (null, < 0) => null,
            (null, _) => control,
            ({ } at, < 0) => at,
            ({ } at, _) => Math.Min(at, control),
        };
    }

    /// <summary>Why a title is not accepted, or null when it is: a character the rule does not allow.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The reason, in words that name the character and its position, or null.</returns>
    public static string? TitleProblem(string title) => Characters(title);

    /// <summary>Why an author or a description is not accepted, or null when it is: longer than <see cref="MaxLength"/>,
    /// or a character the rule does not allow.</summary>
    /// <param name="text">The author or the description.</param>
    /// <returns>The reason, in words, or null.</returns>
    public static string? Problem(string text) =>
        text.Length > MaxLength
            ? $"it is {text.Length.ToString(CultureInfo.InvariantCulture)} characters long; at most {MaxLength.ToString(CultureInfo.InvariantCulture)} are allowed"
            : Characters(text);

    /// <summary>Each of a document's title, author and description that the rule does not accept, in that order: what it is
    /// (<c>title</c>, <c>author</c> or <c>description</c>), the text, why (<see cref="TitleProblem"/>, <see cref="Problem"/>)
    /// and the offset of the character when one is the reason. A null text is no text, and accepted.</summary>
    /// <param name="title">The title, or null.</param>
    /// <param name="author">The author, or null.</param>
    /// <param name="description">The description, or null.</param>
    /// <returns>The texts not accepted.</returns>
    public static IEnumerable<(string Property, string Text, string Problem, int? Offset)> Problems(string? title, string? author, string? description)
    {
        if (title is not null && TitleProblem(title) is { } titleProblem)
        {
            yield return ("title", title, titleProblem, IndexOfForbidden(title));
        }

        foreach (var (property, text) in new[] { ("author", author), ("description", description) })
        {
            if (text is not null && Problem(text) is { } problem)
            {
                yield return (property, text, problem, text.Length > MaxLength ? null : IndexOfForbidden(text));
            }
        }
    }

    private static string? Characters(string text) =>
        IndexOfForbidden(text) is { } at
            ? $"it holds U+{(int)text[at]:X4} at position {at.ToString(CultureInfo.InvariantCulture)}, a control character or a character XML does not allow"
            : null;
}
