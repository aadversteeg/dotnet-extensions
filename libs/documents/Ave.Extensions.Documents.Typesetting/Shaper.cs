using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Fonts.OpenType;
using Ave.Extensions.Documents.Typesetting.Shaping;
using Ave.Extensions.Documents.Typesetting.Unicode;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>
/// Turns text into positioned glyphs with the font's OpenType layout tables: characters become glyphs through
/// 'cmap'; GSUB substitutes glyphs (ligatures such as "fi"); 'hmtx' gives each glyph its advance; GPOS (or the old
/// 'kern' table) adjusts pairs and puts marks on their bases.
/// </summary>
/// <remarks>
/// <para>What is implemented, for the scripts that need no reordering — Latin, Greek and Cyrillic:</para>
/// <list type="bullet">
/// <item>GSUB features <c>ccmp</c>, <c>liga</c>, <c>clig</c>; lookup types 1 (single), 2 (multiple) and 4
/// (ligature), reached directly or through type 7 (extension).</item>
/// <item>GPOS features <c>kern</c> and <c>mark</c>; lookup types 2 (pair adjustment, formats 1 and 2) and 4
/// (mark-to-base), directly or through type 9 (extension). Without a GPOS <c>kern</c> feature, the 'kern' table
/// is used.</item>
/// <item>The script's default language system (OpenType spec, "ScriptList table"); language-specific systems
/// are not used.</item>
/// </list>
/// <para>DOCUMENTED SKIPS (never errors, never crashes): a subtable of any other lookup type or format inside these
/// features is skipped — for example contextual substitution (types 5 and 6) inside <c>ccmp</c>; a lookup that
/// uses a mark filtering set is skipped; device tables and variation data are not applied. Features outside the
/// list, such as <c>calt</c> (contextual alternates, type 6 in Lato), are not applied at all. Text of other scripts
/// is shaped with the same features and the font's default script, which is right for scripts without
/// reordering or joining, and is a documented limit for the complex scripts (Arabic, Indic, …), which are not
/// supported.</para>
/// <para>Each script run of the text is shaped on its own (UAX #24 Script property; characters of the Common and
/// Inherited scripts belong to the run around them), because a font's lookups are chosen per script. Each is first
/// normalized for the font (<see cref="Normalization"/>): canonically equivalent text gives the same glyphs where the font
/// allows it.</para>
/// </remarks>
public static class Shaper
{
    /// <summary>The GSUB features applied: glyph composition/decomposition, standard and contextual ligatures.</summary>
    public static readonly FrozenSet<string> SubstitutionFeatures = new[] { "ccmp", "liga", "clig" }.ToFrozenSet();

    /// <summary>The GPOS features applied: kerning and mark positioning.</summary>
    public static readonly FrozenSet<string> PositioningFeatures = new[] { "kern", "mark" }.ToFrozenSet();

    /// <summary>
    /// Shapes one run of text in one font and size. A character the font has no glyph for is an error, never a
    /// silent drop or a .notdef box; ALL such characters in the run are reported together.
    /// </summary>
    /// <param name="text">The text, already white-space collapsed.</param>
    /// <param name="font">The font.</param>
    /// <param name="fontSize">The font size in points.</param>
    /// <returns>The glyphs in text order, or a <see cref="FontErrorCodes.GlyphMissing"/> error (several
    /// combined into <see cref="ErrorCodes.Aggregate"/>), or <see cref="DocumentErrorCodes.Input.Invalid.Text"/>
    /// for text that is not valid UTF-16.</returns>
    public static Result<ImmutableArray<ShapedGlyph>, Error> Shape(string text, TrueTypeFont font, double fontSize) =>
        Shape(text, font, fontSize, shared: null);

    /// <summary>As <see cref="Shape(string, TrueTypeFont, double)"/>, under <paramref name="shared"/> when it is given (the
    /// pieces of one paragraph, <see cref="LineShapingBudget"/>), else under a budget of the text's own.</summary>
    internal static Result<ImmutableArray<ShapedGlyph>, Error> Shape(string text, TrueTypeFont font, double fontSize, ShapingBudget? shared) =>
        Characters(text).OnSuccessBind(characters =>
        {
            // Each piece is normalized for the font before its characters become glyphs (Unicode.Normalization), as
            // HarfBuzz normalizes the text of each shaping call.
            var pieces = ScriptRuns(characters).SelectMany(run => SplitAfterLineBreaks(characters, run)).ToImmutableArray();
            return pieces
                .TraverseAll(piece => Normalization.ForFont([.. characters.Skip(piece.Start).Take(piece.Length)], codePoint => font.GlyphFor(new Rune(codePoint)).HasValue)
                    .TraverseAll(normalized => InitialGlyph(normalized.Character, normalized.Text, font))
                    .OnSuccessMap(glyphs => (piece.Tag, Glyphs: glyphs.ToImmutableArray())))
                .OnSuccessBind(shaped => ShapePieces([.. shaped], font, fontSize, shared));
        });

    /// <summary>
    /// Shapes ONE piece of a run's text as a line shows it, under a budget of its own (<see cref="ShapingBudget"/>, sized
    /// from the piece). For the pieces of one paragraph that the line breaker shapes again, use
    /// <see cref="ShapeLinePiece(string, TrueTypeFont, double, bool, LineShapingBudget)"/>, which shares the paragraph's
    /// budget; otherwise as that overload.
    /// </summary>
    /// <param name="text">The piece of text, already white-space collapsed.</param>
    /// <param name="font">The run's font.</param>
    /// <param name="fontSize">The run's font size in points.</param>
    /// <param name="showsHyphen">True when the piece ends with a soft hyphen at which its line breaks.</param>
    /// <returns>The glyphs, or an error as from <see cref="Shape(string, TrueTypeFont, double)"/>.</returns>
    public static Result<ValueList<ShapedGlyph>, Error> ShapeLinePiece(string text, TrueTypeFont font, double fontSize, bool showsHyphen) =>
        LinePiece(text, font, fontSize, showsHyphen, shared: null);

    /// <summary>
    /// Shapes a piece of a run's text as a line shows it (<see cref="GreedyLineBreaker"/>, THE RULE FOR SHAPING AT A
    /// BREAK): the piece alone, and, when <paramref name="showsHyphen"/>, with the soft hyphen it ends with drawn as the
    /// hyphen that shows where a line breaks there (<see cref="Hyphen"/>). The hyphen is shaped ALONE and put after the
    /// piece, so it is never kerned with the letter before it, as Chromium shapes it (Blink <c>HyphenResult::Shape</c>
    /// shapes the hyphen string as a text of its own, and <c>LineBreaker::AddHyphen</c> adds its width to the line). That
    /// glyph's text stays the soft hyphen it stands for. A font without a hyphen leaves the soft hyphen invisible. The work
    /// is charged to <paramref name="budget"/>, the budget of the paragraph the piece belongs to; once it is spent, the
    /// piece is refused (<see cref="LineShapingBudget"/>).
    /// </summary>
    /// <param name="text">The piece of text, already white-space collapsed.</param>
    /// <param name="font">The run's font.</param>
    /// <param name="fontSize">The run's font size in points.</param>
    /// <param name="showsHyphen">True when the piece ends with a soft hyphen at which its line breaks.</param>
    /// <param name="budget">The budget of the piece's paragraph, as the line breaker hands it to its <see cref="LineShaper"/>.</param>
    /// <returns>The glyphs, or an error as from <see cref="Shape(string, TrueTypeFont, double)"/>.</returns>
    public static Result<ValueList<ShapedGlyph>, Error> ShapeLinePiece(string text, TrueTypeFont font, double fontSize, bool showsHyphen, LineShapingBudget budget) =>
        LinePiece(text, font, fontSize, showsHyphen, budget.Shaping);

    private static Result<ValueList<ShapedGlyph>, Error> LinePiece(string text, TrueTypeFont font, double fontSize, bool showsHyphen, ShapingBudget? shared) =>
        Shape(text, font, fontSize, shared).OnSuccessMap(glyphs =>
            showsHyphen && glyphs.Length > 0 && glyphs[^1].Text == SoftHyphen && glyphs[^1].BreakHyphen is { } hyphen
                ? (ValueList<ShapedGlyph>)[.. glyphs.Take(glyphs.Length - 1), hyphen]
                : [.. glyphs]);

    /// <summary>
    /// Splits text into Unicode scalar values, joining surrogate pairs. An unpaired surrogate is not a
    /// character at all, so it is an error rather than being replaced by U+FFFD.
    /// </summary>
    private static Result<ImmutableArray<Rune>, Error> Characters(string text)
    {
        var characters = new List<Rune>(text.Length);
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var character, out var consumed) != System.Buffers.OperationStatus.Done)
            {
                var offset = text.Length - remaining.Length;
                return Result<ImmutableArray<Rune>, Error>.Failure(
                    new Error(DocumentErrorCodes.Input.Invalid.Text, $"The text has an unpaired surrogate at offset {offset}.")
                        .With(ErrorMetadataKeys.Offset, offset));
            }

            characters.Add(character);
            remaining = remaining[consumed..];
        }

        return Result<ImmutableArray<Rune>, Error>.Success([.. characters]);
    }

    /// <summary>
    /// The glyph 'cmap' gives a character, before any substitution. A
    /// default-ignorable character needs no glyph: it is invisible whatever the font has for it (HarfBuzz hides
    /// them the same way, hb-ot-shape <c>hb_ot_hide_default_ignorables</c>), so it is never missing. Nor does a
    /// mandatory line break (UAX #14 classes BK, CR, LF and NL, such as the line separator U+2028 that
    /// <c>LineBreak()</c> writes): it ends a line and draws nothing.
    /// </summary>
    private static Result<GlyphItem, Error> InitialGlyph(Rune character, string text, TrueTypeFont font)
    {
        var filter = new GlyphFilter(font.Layout.Definitions);
        var properties = UnicodeProperties.Of(character.Value);
        var traits = properties.Traits;
        var isLineBreak = IsMandatoryBreak(character);
        if ((traits & CharacterTraits.DefaultIgnorable) != 0 || isLineBreak)
        {
            return Result<GlyphItem, Error>.Success(new GlyphItem(new GlyphId(0), text, GlyphClass.Base) { IsDefaultIgnorable = true, IsLineBreak = isLineBreak });
        }

        var isNonspacingMark = (traits & CharacterTraits.NonspacingMark) != 0;
        return font.RequireGlyph(character)
            .OnSuccessMap(glyph => new GlyphItem(glyph, text, filter.ClassOf(glyph, isNonspacingMark)));
    }

    /// <summary>Shapes each piece of the text (a script run, cut after every forced line break) on its own, under ONE
    /// budget for the whole text (<see cref="ShapingBudget"/>), so the pieces share its limits: <paramref name="shared"/>
    /// when it is given, else one made for the text. Once a limit is reached the later pieces stop at their first step,
    /// and the text gets the one error of that limit.</summary>
    private static Result<ImmutableArray<ShapedGlyph>, Error> ShapePieces(
        ImmutableArray<(string Tag, ImmutableArray<GlyphItem> Glyphs)> shaping, TrueTypeFont font, double fontSize, ShapingBudget? shared)
    {
        var hyphen = Hyphen(font, fontSize);
        var budget = shared ?? new ShapingBudget(shaping.Sum(piece => piece.Glyphs.Length));
        var pieces = shaping.Select(piece => ShapeRun(piece.Glyphs, piece.Tag, font, budget)).ToList();
        return budget.Exceeded is { } limit
            ? Result<ImmutableArray<ShapedGlyph>, Error>.Failure(LimitReached(font, budget, limit))
            : Result<ImmutableArray<ShapedGlyph>, Error>.Success([.. pieces.SelectMany(piece => piece!.Value).Select(glyph => new ShapedGlyph(
                    glyph.Glyph,
                    font.Metrics.ToPoints(glyph.XAdvance, fontSize),
                    glyph.Text,
                    font.Metrics.ToPoints(glyph.XOffset, fontSize),
                    font.Metrics.ToPoints(glyph.YOffset, fontSize))
                {
                    IsInvisible = glyph.IsDefaultIgnorable,
                    IsLineBreak = glyph.IsLineBreak,
                    BreakHyphen = glyph.Text == SoftHyphen ? hyphen : null,
                    UnsafeToBreak = glyph.UnsafeToBreak,
                })]);
    }

    /// <summary>The soft hyphen U+00AD.</summary>
    private const string SoftHyphen = "\u00AD";

    /// <summary>
    /// The hyphen shown where a line breaks at a soft hyphen. UAX #14 §5.4: the soft hyphen is "an invisible format
    /// character that merely indicates a preferred intraword line break position. If the line is broken at that
    /// point, then whatever mechanism is appropriate for intraword line breaks should be invoked" (CSS Text 3 §5.3,
    /// <c>hyphens: manual</c>, hyphenates only there). Drawn with the glyph of U+2010 HYPHEN when the font has it,
    /// otherwise of U+002D HYPHEN-MINUS; null when it has neither, and the break then shows nothing (a documented
    /// limit). Whatever glyph is drawn, its text is the soft hyphen it stands for, not the character whose glyph it
    /// borrows: a reader gets the word back whole, and a hyphen-minus elsewhere in the document keeps its own text.
    /// Tagged PDF asks for exactly this: "such an incidental word division shall be represented by a soft hyphen
    /// character" (ISO 32000-1 §14.8.2.2.3, Hyphenation).
    /// </summary>
    private static ShapedGlyph? Hyphen(TrueTypeFont font, double fontSize) =>
        HyphenCharacter(font) is { } character && font.GlyphFor(new Rune(character[0])) is { HasValue: true } glyph
            ? new ShapedGlyph(glyph.Value, font.Metrics.ToPoints(font.AdvanceWidth(glyph.Value), fontSize), SoftHyphen)
            : null;

    /// <summary>The character whose glyph shows a hyphen at a soft hyphen (<see cref="Hyphen"/>): U+2010 HYPHEN when the
    /// font has it, otherwise U+002D HYPHEN-MINUS; null when it has neither.</summary>
    private static string? HyphenCharacter(TrueTypeFont font) =>
        new[] { 0x2010, 0x002D }.Where(codePoint => font.GlyphFor(new Rune(codePoint)).HasValue).Select(char.ConvertFromUtf32).FirstOrDefault();

    /// <summary>
    /// The shaping steps for one script run, all in font design units: GSUB lookups; the 'hmtx' advance of every
    /// resulting glyph; then the old 'kern' table if GPOS has no kerning, and the GPOS lookups. Each step spends from the
    /// text's <paramref name="budget"/>; null when one reaches a limit.
    /// </summary>
    private static ImmutableArray<GlyphItem>? ShapeRun(ImmutableArray<GlyphItem> glyphs, string script, TrueTypeFont font, ShapingBudget budget)
    {
        var layout = font.Layout;
        var filter = new GlyphFilter(layout.Definitions);
        if (GlyphSubstitution.Apply(glyphs, layout.Substitutions.LookupsFor(script, SubstitutionFeatures), filter, budget) is not { } substituted)
        {
            return null;
        }

        var withAdvances = substituted.Select(glyph => glyph with { XAdvance = glyph.IsDefaultIgnorable ? 0 : font.AdvanceWidth(glyph.Glyph) }).ToImmutableArray();

        // "kern" in GPOS wins over the 'kern' table: the table is used only when no GPOS feature is called kern.
        var hasGposKerning = layout.Positions.Features.Any(feature => feature.Tag == "kern");
        var kerned = hasGposKerning ? withAdvances : GlyphPositioning.ApplyLegacyKerning(withAdvances, layout.Kerning, budget);
        return kerned is { } ready ? GlyphPositioning.Apply(ready, layout.Positions.LookupsFor(script, PositioningFeatures), filter, budget) : null;
    }

    /// <summary>The error for the limit of <paramref name="budget"/> that the text reached: which limit, how it follows
    /// from the text's length, and what spent it. Only the font's substitutions make glyphs, and only its lookups spend
    /// more than a fixed few operations per glyph (see <see cref="ShapingBudget"/>), so the message names the font.</summary>
    private static Error LimitReached(TrueTypeFont font, ShapingBudget budget, ShapingLimit limit)
    {
        var name = font.Names.PostScriptName;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var error = limit == ShapingLimit.Glyphs
            ? new Error(TypesettingErrorCodes.TooManyGlyphs, string.Create(culture,
                    $"Font '{name}' turns a text of {budget.InputLength} glyphs into more than {budget.GlyphLimit} glyphs ({GlyphSubstitution.MaxGrowthFactor} per glyph, at least {GlyphSubstitution.MinGlyphLimit}): its substitutions make more glyphs than any real font needs."))
                .With(ErrorMetadataKeys.Value, budget.GlyphLimit.ToString(culture))
            : new Error(TypesettingErrorCodes.TooMuchWork, string.Create(culture,
                    $"Font '{name}' needs more than {budget.OperationLimit} operations to shape a text of {budget.InputLength} glyphs ({ShapingBudget.OperationsPerGlyph} per glyph, at least {ShapingBudget.MinOperations}): its lookups look at each glyph more often than any real font needs."))
                .With(ErrorMetadataKeys.Value, budget.OperationLimit.ToString(culture));
        return error.With(ErrorMetadataKeys.FontName, name);
    }

    /// <summary>A run of characters of one script, with the OpenType script tag its lookups are chosen by.</summary>
    private sealed record ScriptRun(int Start, int Length, string Tag);

    /// <summary>A mandatory line break: UAX #14 classes BK, CR, LF and NL.</summary>
    private static bool IsMandatoryBreak(Rune character) =>
        UnicodeProperties.Of(character.Value).Class is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL;

    /// <summary>
    /// A script run cut after every mandatory line break in it. A forced break ends its line as a wrap does, so the
    /// glyphs on either side are on different lines and must not be shaped together: no kerning pair, ligature or
    /// mark attachment may join them. Each piece is shaped on its own.
    /// </summary>
    private static IEnumerable<ScriptRun> SplitAfterLineBreaks(ImmutableArray<Rune> characters, ScriptRun run)
    {
        var start = run.Start;
        var end = run.Start + run.Length;
        for (var index = run.Start; index < end; index++)
        {
            if (IsMandatoryBreak(characters[index]))
            {
                yield return run with { Start = start, Length = index + 1 - start };
                start = index + 1;
            }
        }

        if (start < end)
        {
            yield return run with { Start = start, Length = end - start };
        }
    }

    /// <summary>
    /// Splits the text where its script changes (UAX #24 Script property). Characters of the Common and Inherited
    /// scripts (spaces, punctuation, digits, combining marks) take the script of the character before them, or —
    /// at the start of the text — of the first character after them that has one. The OpenType script tags are
    /// <c>latn</c>, <c>grek</c> and <c>cyrl</c> (OpenType spec, "Script tags"); any other script, or text with no
    /// script at all, uses the default script <c>DFLT</c>.
    /// </summary>
    private static ImmutableArray<ScriptRun> ScriptRuns(ImmutableArray<Rune> characters)
    {
        var scripts = characters.Select(character => UnicodeProperties.Of(character.Value).Script).ToArray();
        var current = scripts.FirstOrDefault(script => script is not (TextScript.Common or TextScript.Inherited), TextScript.Common);
        var runs = ImmutableArray.CreateBuilder<ScriptRun>();
        var start = 0;
        for (var index = 0; index < scripts.Length; index++)
        {
            var script = scripts[index] is TextScript.Common or TextScript.Inherited ? current : scripts[index];
            if (script != current)
            {
                runs.Add(new ScriptRun(start, index - start, Tag(current)));
                start = index;
                current = script;
            }
        }

        if (scripts.Length > 0)
        {
            runs.Add(new ScriptRun(start, scripts.Length - start, Tag(current)));
        }

        return runs.ToImmutable();
    }

    private static string Tag(TextScript script) => script switch
    {
        TextScript.Latin => "latn",
        TextScript.Greek => "grek",
        TextScript.Cyrillic => "cyrl",
        _ => LayoutTable.DefaultScript,
    };
}
