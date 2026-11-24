using System.Text.RegularExpressions;

namespace Ave.Extensions.Documents.Core;

/// <summary>
/// THE RULE FOR A LANGUAGE TAG, in one place for every layer: a tag of at most <see cref="MaxLength"/> characters that is
/// well-formed by the syntax of BCP 47 (RFC 5646 §2.1): a language subtag (2–3 letters with up to three extended subtags
/// of 3 letters, or 4, or 5–8 letters), then optionally a script (4 letters), a region (2 letters or 3 digits),
/// variants (5–8 letters or digits, or a digit and 3 letters or digits), extensions (a singleton other than <c>x</c> and
/// subtags of 2–8 letters or digits) and a private-use part (<c>x</c> and subtags of 1–8); or a private-use tag alone;
/// or one of the grandfathered tags RFC 5646 lists. Letters in any case. Whether the subtags are registered, and the
/// rules a "valid" tag adds on top (no variant or singleton twice), are not checked. Every such tag also has the form
/// ISO 32000-1 §14.9.2 refers to (RFC 3066: subtags of 1–8 letters or digits) and that veraPDF tests for a <c>/Lang</c>
/// (rules 6.7.4-1 and 7.2-29). The tag is written as it is given. A front-end checks the author's tag with it; the
/// back-ends check it again, because other producers than the front-ends may hand them a model or a page model.
/// </summary>
public static class LanguageTags
{
    /// <summary>The longest tag: 64 characters, far more than any tag of a language, script, region and a variant
    /// needs; the bound keeps the check of a tag from untrusted input short.</summary>
    public const int MaxLength = 64;

    private const string Grandfathered =
        "en-gb-oed|i-ami|i-bnn|i-default|i-enochian|i-hak|i-klingon|i-lux|i-mingo|i-navajo|i-pwn|i-tao|i-tay|i-tsu|sgn-be-fr|sgn-be-nl|sgn-ch-de"
        + "|art-lojban|cel-gaulish|no-bok|no-nyn|zh-guoyu|zh-hakka|zh-min|zh-min-nan|zh-xiang";

    private static readonly Regex Form = new(
        @"^(?:(?:[a-z]{2,3}(?:-[a-z]{3}){0,3}|[a-z]{4}|[a-z]{5,8})(?:-[a-z]{4})?(?:-(?:[a-z]{2}|[0-9]{3}))?(?:-(?:[a-z0-9]{5,8}|[0-9][a-z0-9]{3}))*"
        + @"(?:-[0-9a-wy-z](?:-[a-z0-9]{2,8})+)*(?:-x(?:-[a-z0-9]{1,8})+)?|x(?:-[a-z0-9]{1,8})+|" + Grandfathered + @")\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    /// <summary>Whether the text is a well-formed language tag, for example <c>nl-NL</c>, <c>en</c> or <c>zh-Hant-TW</c>.</summary>
    /// <param name="tag">The text.</param>
    /// <returns>True when it is one.</returns>
    public static bool IsWellFormed(string tag) => tag.Length is > 0 and <= MaxLength && Form.IsMatch(tag);
}
