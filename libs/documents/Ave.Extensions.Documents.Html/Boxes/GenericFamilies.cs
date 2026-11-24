using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>
/// The families the generic families of CSS Fonts 4 §2.1 and the initial value of <c>font-family</c> stand for. A browser
/// takes a font of the system for each; the engine has no system fonts, so the caller names a family for each generic
/// family a document may use, and a generic family the options do not map is refused.
/// </summary>
/// <remarks>
/// A mapped name is looked up as a family name written in the document is: the document's <c>@font-face</c> rules first,
/// then the caller's fonts, with the same weight and style rules. Which generic family takes which name follows what
/// Chromium 159 prints in the reference container (measured): <see cref="Standard"/> is its standard font, the one it
/// prints for the initial value and for every generic family it has no setting of its own for.
/// <code>
/// var options = HtmlRenderOptions.Default with { Fonts = new FontSet([lato, latoBold, theano]),
///     GenericFamilies = new GenericFamilies { Standard = "Theano Didot", SansSerif = "Lato", Monospace = "Lato" } };
/// var pages = HtmlRendering.ToPages(html, options);
/// </code>
/// </remarks>
public sealed record GenericFamilies
{
    /// <summary>The longest name a mapping may hold: the longest family name a face of the caller's fonts can have (a
    /// string of the TrueType 'name' table has a 16-bit length in bytes, two bytes per UTF-16 unit).</summary>
    public const int MaxNameLength = ushort.MaxValue / 2;

    /// <summary>No generic family mapped: each one a document uses is refused.</summary>
    public static GenericFamilies None { get; } = new();

    /// <summary>The family of the initial value of <c>font-family</c> (Chromium computes it as "Times New Roman", and a
    /// document's <c>@font-face</c> rule of that name, or a font of that family in the caller's fonts, still comes first)
    /// and of the generic families <c>cursive</c>, <c>fantasy</c>, <c>math</c>, <c>emoji</c>, <c>fangsong</c> and
    /// <c>ui-*</c>, which Chromium prints in its standard font; and of <c>serif</c> when <see cref="Serif"/> is not set.
    /// Null for none.</summary>
    public string? Standard { get; init; }

    /// <summary>The family of <c>serif</c>; null takes <see cref="Standard"/>.</summary>
    public string? Serif { get; init; }

    /// <summary>The family of <c>sans-serif</c> and <c>system-ui</c>; null for none.</summary>
    public string? SansSerif { get; init; }

    /// <summary>The family of <c>monospace</c>; null for none. The keyword still sets the size of text whose family is
    /// <c>monospace</c> alone (13px at <c>medium</c>, as Chromium computes it), whatever family it maps to.</summary>
    public string? Monospace { get; init; }

    /// <summary>The name a generic family keyword maps to and the property that maps it (for a refusal), or a null name
    /// when that property is not set.</summary>
    /// <param name="keyword">The keyword, lower case, as the style gives it.</param>
    internal (string? Name, string Property) For(string keyword) => keyword switch
    {
        "serif" when Serif is null => (Standard, $"{nameof(GenericFamilies)}.{nameof(Serif)} (or {nameof(GenericFamilies)}.{nameof(Standard)})"),
        "serif" => (Serif, $"{nameof(GenericFamilies)}.{nameof(Serif)}"),
        "sans-serif" or "system-ui" => (SansSerif, $"{nameof(GenericFamilies)}.{nameof(SansSerif)}"),
        "monospace" => (Monospace, $"{nameof(GenericFamilies)}.{nameof(Monospace)}"),
        _ => (Standard, $"{nameof(GenericFamilies)}.{nameof(Standard)}"),
    };

    /// <summary>The problems of the mapped names, each a <see cref="HtmlStyleErrorCodes.InvalidOptions"/>: a name that is
    /// empty, holds a control character, is longer than <see cref="MaxNameLength"/> or is itself a generic family
    /// keyword.</summary>
    internal IEnumerable<Error> Problems()
    {
        foreach (var (property, name) in new[] { (nameof(Standard), Standard), (nameof(Serif), Serif), (nameof(SansSerif), SansSerif), (nameof(Monospace), Monospace) })
        {
            if (name is null)
            {
                continue;
            }

            var problem = name.Trim().Length == 0 ? "is empty"
                : name.Length > MaxNameLength ? $"is longer than {MaxNameLength} characters"
                : ControlCharacter(name) ? "holds a control character"
                : PropertyRegistry.IsGenericFamily(name) ? "is a generic family itself: name a family a face has"
                : null;
            if (problem is not null)
            {
                yield return new Error(HtmlStyleErrorCodes.InvalidOptions, $"The family name of {nameof(GenericFamilies)}.{property} {problem}.")
                    .With(ErrorMetadataKeys.Property, $"{nameof(GenericFamilies)}.{property}")
                    .With(ErrorMetadataKeys.Value, name.Length > 64 ? name[..64] : name);
            }
        }
    }

    private static bool ControlCharacter(string name)
    {
        foreach (var character in name)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }
}
