using System.Text.RegularExpressions;

namespace Ave.Extensions.Documents.Core;

/// <summary>
/// The form of a language tag, in one place for every layer: a primary subtag of 1–8 letters, then subtags of 1–8
/// letters or digits, each after a hyphen (RFC 3066 §2.1, the form ISO 32000-1 §14.9.2.2 refers to). It is also the
/// pattern veraPDF tests for a PDF's <c>/Lang</c> (rules 6.7.4-1 and 7.2-29). Whether the subtags are registered is not
/// checked. A front-end checks the author's tag with it; the PDF back-end checks it again for the profiles that require
/// it, because other producers than the front-end may hand the back-end a page model.
/// </summary>
public static class LanguageTags
{
    private static readonly Regex Form = new(@"^[A-Za-z]{1,8}(-[A-Za-z0-9]{1,8})*\z", RegexOptions.CultureInvariant);

    /// <summary>Whether the text has the form of a language tag, for example <c>nl-NL</c> or <c>en</c>.</summary>
    /// <param name="tag">The text.</param>
    /// <returns>True when it has the form.</returns>
    public static bool IsWellFormed(string tag) => Form.IsMatch(tag);
}
