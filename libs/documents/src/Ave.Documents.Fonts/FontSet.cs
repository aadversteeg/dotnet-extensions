using System;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts;

/// <summary>
/// The fonts a document may use, given explicitly by the caller. The engine never looks at fonts installed on
/// the machine: output then depends only on the input, and a missing font is an error instead of a silent
/// substitution.
/// </summary>
/// <param name="Fonts">The fonts, in the order the caller gave them.</param>
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
    /// <returns>The font, or a <see cref="FontErrorCodes.NotFound"/> error.</returns>
    public Result<TrueTypeFont, Error> Find(string family, FontWeight weight, FontStyle style)
    {
        var match = Fonts.FirstOrDefault(font =>
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

    private static string StyleSuffix(bool italic) => italic ? " italic" : string.Empty;

    private string Describe() =>
        Fonts.IsEmpty
            ? "none"
            : string.Join(", ", Fonts.Select(font => $"{font.Names.Family} {font.Metrics.WeightClass}{StyleSuffix(font.Metrics.IsItalic)}"));
}
