using System;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fonts;

/// <summary>
/// The fonts a document may use, given explicitly by the caller. The engine never looks at fonts installed on
/// the machine: output then depends only on the input, and a missing font is an error instead of a silent
/// substitution.
/// </summary>
/// <param name="Fonts">The fonts, in the order the caller gave them. A default array is an empty set; a set with a missing
/// (null) font is refused by <see cref="Find(string, FontWeight, FontStyle)"/>.</param>
public sealed record FontSet(ImmutableArray<TrueTypeFont> Fonts)
{
    /// <summary>
    /// Finds the upright font for a family and weight; see <see cref="Find(string, FontWeight, FontStyle)"/>.
    /// </summary>
    /// <param name="family">The family name, for example <c>Lato</c>.</param>
    /// <param name="weight">The weight, for example <see cref="FontWeight.Bold"/>.</param>
    /// <returns>The font, or a <see cref="FontErrorCodes.NotFound"/> error.</returns>
    public Result<TrueTypeFont, Error> Find(string family, FontWeight weight) => Find(family, weight, FontStyle.Normal);

    /// <summary>
    /// Finds the font for a family, weight and style. The family is compared ASCII case-insensitively (CSS Fonts 4
    /// §5.1: family names are matched case-insensitively). Weight and style must match exactly: the CSS fallbacks to
    /// the nearest weight and to a synthesised or oblique face (CSS Fonts 4 §5.2 step 4) are not implemented, so a
    /// missing italic is an error rather than a silent upright substitute. When two fonts match, the first one given
    /// wins.
    /// </summary>
    /// <param name="family">The family name, for example <c>Lato</c>.</param>
    /// <param name="weight">The weight, for example <see cref="FontWeight.Bold"/>.</param>
    /// <param name="style">Upright or italic.</param>
    /// <returns>The font, or a <see cref="FontErrorCodes.NotFound"/> error, also when an entry of the set is missing
    /// (null), whatever is asked for.</returns>
    public Result<TrueTypeFont, Error> Find(string family, FontWeight weight, FontStyle style)
    {
        var fonts = Items;
        var missing = fonts.IndexOf(null!);
        if (missing >= 0)
        {
            return Result<TrueTypeFont, Error>.Failure(
                new Error(FontErrorCodes.NotFound, $"Font {missing + 1} of the font set is missing, so no font for family '{family}' is looked up.")
                    .With(ErrorMetadataKeys.FontName, family)
                    .With(ErrorMetadataKeys.Value, weight + StyleSuffix(style == FontStyle.Italic)));
        }

        var match = fonts.FirstOrDefault(font =>
            string.Equals(font.Names.Family, family, StringComparison.OrdinalIgnoreCase)
            && font.Metrics.WeightClass == weight.Value
            && font.Metrics.IsItalic == (style == FontStyle.Italic));

        return match is not null
            ? Result<TrueTypeFont, Error>.Success(match)
            : Result<TrueTypeFont, Error>.Failure(
                new Error(
                        FontErrorCodes.NotFound,
                        $"No font for family '{family}' with weight {weight}{StyleSuffix(style == FontStyle.Italic)}. Available: {Describe()}.")
                    .With(ErrorMetadataKeys.FontName, family)
                    .With(ErrorMetadataKeys.Value, weight + StyleSuffix(style == FontStyle.Italic)));
    }

    /// <summary>The fonts, with a default array read as no fonts.</summary>
    private ImmutableArray<TrueTypeFont> Items => Fonts.IsDefault ? [] : Fonts;

    private static string StyleSuffix(bool italic) => italic ? " italic" : string.Empty;

    private string Describe() =>
        Items.IsEmpty
            ? "none"
            : string.Join(", ", Items.Select(font => $"{font.Names.Family} {font.Metrics.WeightClass}{StyleSuffix(font.Metrics.IsItalic)}"));
}
