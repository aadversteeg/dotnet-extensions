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
    /// Finds the font for a family and weight. The family is compared ASCII case-insensitively (CSS Fonts 4
    /// §5.1: family names are matched case-insensitively). The weight must match exactly: the CSS fallback to
    /// the nearest weight (CSS Fonts 4 §5.2 step 4) is a later stage. When two fonts match, the first one given
    /// wins.
    /// </summary>
    /// <param name="family">The family name, for example <c>Lato</c>.</param>
    /// <param name="weight">The weight, for example <see cref="FontWeight.Bold"/>.</param>
    /// <returns>The font, or a <see cref="DocumentErrorCodes.Font.NotFound"/> error.</returns>
    public Result<TrueTypeFont, Error> Find(string family, FontWeight weight)
    {
        var match = Fonts.FirstOrDefault(font =>
            string.Equals(font.Names.Family, family, StringComparison.OrdinalIgnoreCase)
            && font.Metrics.WeightClass == weight.Value);

        return match is not null
            ? Result<TrueTypeFont, Error>.Success(match)
            : Result<TrueTypeFont, Error>.Failure(
                new Error(
                        DocumentErrorCodes.Font.NotFound,
                        $"No font for family '{family}' with weight {weight}. Available: {Describe()}.")
                    .With(ErrorMetadataKeys.FontName, family)
                    .With(ErrorMetadataKeys.Value, weight.ToString()));
    }

    private string Describe() =>
        Fonts.IsEmpty
            ? "none"
            : string.Join(", ", Fonts.Select(font => $"{font.Names.Family} {font.Metrics.WeightClass}"));
}
