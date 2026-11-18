using System;
using System.Globalization;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Typesetting.Shaping;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// The limits of shaping the pieces of ONE paragraph again at its line ends (<see cref="GreedyLineBreaker"/>, THE RULE FOR
/// SHAPING AT A BREAK), shared by all of them, so that cutting a paragraph into many pieces does not multiply the work a
/// font may cost. It belongs to one call of
/// <see cref="GreedyLineBreaker.BreakLines(System.Collections.Generic.IReadOnlyList{Core.ValueList{ShapedGlyph}}, double, LineShaper)"/>,
/// which makes it for the paragraph and hands it to every <see cref="LineShaper"/> call; it cannot be made elsewhere.
/// </summary>
/// <remarks>
/// <para>It is state, the one place on this path that is: it is spent as the pieces are shaped. Two limits, both sized
/// from the paragraph's length n (its characters after white-space collapsing):</para>
/// <list type="bullet">
/// <item>characters: the breaker hands the reshaper at most <c>max(<see cref="MinCharacters"/>,
/// <see cref="CharactersPerCharacter"/> × n)</c> characters in all;</item>
/// <item>shaping: all pieces together spend at most what shaping the paragraph once may spend, counted as in
/// <see cref="Shaper.Shape(string, Fonts.TrueTypeFont, double)"/>: <c>max(65 536, 1 024 × n)</c> operations, and
/// substitutions that make at most <c>max(16 384, 64 × n)</c> glyphs (the shaper's limits for a text of n glyphs).</item>
/// </list>
/// <para>The breaker itself never comes near the first limit (it shapes at most a few times each line's text, see
/// <see cref="GreedyLineBreaker.MaxEarlierBreaksTried"/>); a real font never comes near the second. When one is spent,
/// the piece that would pass it, and every later one, comes back as an <see cref="Error"/> with the code
/// <see cref="TypesettingErrorCodes.TooMuchWork"/> (or <see cref="TypesettingErrorCodes.TooManyGlyphs"/> for the glyph
/// limit), and the paragraph is refused with it. Nothing throws.</para>
/// </remarks>
public sealed class LineShapingBudget
{
    /// <summary>How many characters the breaker may hand to the reshaper per character of the paragraph, in all.</summary>
    public const int CharactersPerCharacter = 16;

    /// <summary>The fewest characters any paragraph may have shaped again, so a short paragraph is not held to 16 per
    /// character.</summary>
    public const int MinCharacters = 4096;

    private long _charactersLeft;

    /// <summary>A budget for a paragraph of <paramref name="length"/> characters.</summary>
    internal LineShapingBudget(int length)
    {
        Length = length;
        CharacterLimit = Math.Max(MinCharacters, (long)CharactersPerCharacter * length);
        _charactersLeft = CharacterLimit;
        Shaping = new ShapingBudget(length);
    }

    /// <summary>The paragraph's length in characters.</summary>
    public int Length { get; }

    /// <summary>The most characters the breaker may hand to the reshaper.</summary>
    public long CharacterLimit { get; }

    /// <summary>The operations and glyph limits all pieces share.</summary>
    internal ShapingBudget Shaping { get; }

    /// <summary>Spends <paramref name="characters"/> handed to the reshaper; false once the limit is passed (and from then on).</summary>
    internal bool SpendCharacters(int characters)
    {
        _charactersLeft -= characters;
        return _charactersLeft >= 0;
    }

    /// <summary>The error for a spent character limit.</summary>
    internal Error CharactersSpent() =>
        new Error(TypesettingErrorCodes.TooMuchWork, string.Create(CultureInfo.InvariantCulture,
                $"Breaking a paragraph of {Length} characters into lines would shape more than {CharacterLimit} characters again ({CharactersPerCharacter} per character, at least {MinCharacters})."))
            .With(ErrorMetadataKeys.Value, CharacterLimit.ToString(CultureInfo.InvariantCulture));
}
