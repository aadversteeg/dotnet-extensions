using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.Fonts.OpenType;
using Ave.Documents.Typesetting.Shaping;
using Ave.Documents.Typesetting.Unicode;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Documents.Typesetting;

/// <summary>
/// Turns text into positioned glyphs with the font's OpenType layout tables: characters become glyphs through
/// 'cmap'; GSUB substitutes glyphs (ligatures such as "fi"); 'hmtx' gives each glyph its advance; GPOS (or the old
/// 'kern' table) adjusts pairs and puts marks on their bases.
/// </summary>
/// <remarks>
/// <para>Stage-1 scope, for the scripts that need no reordering — Latin, Greek and Cyrillic:</para>
/// <list type="bullet">
/// <item>GSUB features <c>ccmp</c>, <c>liga</c>, <c>clig</c>; lookup types 1 (single), 2 (multiple) and 4
/// (ligature), reached directly or through type 7 (extension).</item>
/// <item>GPOS features <c>kern</c> and <c>mark</c>; lookup types 2 (pair adjustment, formats 1 and 2) and 4
/// (mark-to-base), directly or through type 9 (extension). Without a GPOS <c>kern</c> feature, the 'kern' table
/// is used.</item>
/// <item>The script's default language system (OpenType spec, "Script list table"); language-specific systems
/// are not used yet.</item>
/// </list>
/// <para>DOCUMENTED SKIPS (never errors, never crashes): a subtable of any other lookup type or format inside these
/// features is skipped — for example contextual substitution (types 5 and 6) inside <c>ccmp</c>; a lookup that
/// uses a mark filtering set is skipped; device tables and variation data are not applied. Features outside the
/// list, such as <c>calt</c> (contextual alternates, type 6 in Lato), are not applied at all. Text of other scripts
/// is shaped with the same features and the font's default script, which is right for scripts without
/// reordering or joining, and is a documented limit for the complex scripts (Arabic, Indic, …) that a later stage
/// adds.</para>
/// <para>Each script run of the text is shaped on its own (UAX #24 Script property; characters of the Common and
/// Inherited scripts belong to the run around them), because a font's lookups are chosen per script.</para>
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
    /// <returns>The glyphs in text order, or a <see cref="DocumentErrorCodes.Font.GlyphMissing"/> error (several
    /// combined into <see cref="ErrorCodes.Aggregate"/>), or <see cref="DocumentErrorCodes.Input.Invalid.Text"/>
    /// for text that is not valid UTF-16.</returns>
    public static Result<ImmutableArray<ShapedGlyph>, Error> Shape(string text, TrueTypeFont font, double fontSize) =>
        Characters(text).OnSuccessBind(characters =>
            characters.Select((character, index) => (character, index))
                .TraverseAll(item => InitialGlyph(item.character, item.index, font))
                .OnSuccessMap(glyphs => ShapeScriptRuns(characters, [.. glyphs], font, fontSize)));

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

    /// <summary>The glyph 'cmap' gives a character, before any substitution; its cluster is the character's index.</summary>
    private static Result<GlyphItem, Error> InitialGlyph(Rune character, int index, TrueTypeFont font)
    {
        var filter = new GlyphFilter(font.Layout.Definitions);
        var isNonspacingMark = (UnicodeProperties.Of(character.Value).Traits & CharacterTraits.NonspacingMark) != 0;
        return font.GlyphFor(character).Match(
            glyph => Result<GlyphItem, Error>.Success(new GlyphItem(glyph, index, character.ToString(), filter.ClassOf(glyph, isNonspacingMark))),
            () => Result<GlyphItem, Error>.Failure(GlyphMissing(character, font)));
    }

    private static ImmutableArray<ShapedGlyph> ShapeScriptRuns(ImmutableArray<Rune> characters, ImmutableArray<GlyphItem> glyphs, TrueTypeFont font, double fontSize) =>
        [.. ScriptRuns(characters).SelectMany(run => ShapeRun([.. glyphs.Skip(run.Start).Take(run.Length)], run.Tag, font)
            .Select(glyph => new ShapedGlyph(
                glyph.Glyph,
                font.Metrics.ToPoints(glyph.XAdvance, fontSize),
                glyph.Text,
                font.Metrics.ToPoints(glyph.XOffset, fontSize),
                font.Metrics.ToPoints(glyph.YOffset, fontSize))))];

    /// <summary>
    /// The shaping steps for one script run, all in font design units: GSUB lookups; the 'hmtx' advance of every
    /// resulting glyph; then the old 'kern' table if GPOS has no kerning, and the GPOS lookups.
    /// </summary>
    private static ImmutableArray<GlyphItem> ShapeRun(ImmutableArray<GlyphItem> glyphs, string script, TrueTypeFont font)
    {
        var layout = font.Layout;
        var filter = new GlyphFilter(layout.Definitions);
        var substituted = GlyphSubstitution.Apply(glyphs, layout.Substitutions.LookupsFor(script, SubstitutionFeatures), filter);
        var withAdvances = substituted.Select(glyph => glyph with { XAdvance = font.AdvanceWidth(glyph.Glyph) }).ToImmutableArray();

        // "kern" in GPOS wins over the 'kern' table: the table is used only when no GPOS feature is called kern.
        var hasGposKerning = layout.Positions.Features.Any(feature => feature.Tag == "kern");
        var kerned = hasGposKerning ? withAdvances : GlyphPositioning.ApplyLegacyKerning(withAdvances, layout.Kerning);
        return GlyphPositioning.Apply(kerned, layout.Positions.LookupsFor(script, PositioningFeatures), filter);
    }

    /// <summary>A run of characters of one script, with the OpenType script tag its lookups are chosen by.</summary>
    private sealed record ScriptRun(int Start, int Length, string Tag);

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

    private static Error GlyphMissing(Rune character, TrueTypeFont font)
    {
        var codePoint = $"U+{character.Value:X4}";
        return new Error(
                DocumentErrorCodes.Font.GlyphMissing,
                $"Font '{font.Names.PostScriptName}' has no glyph for '{character}' ({codePoint}).")
            .With(ErrorMetadataKeys.FontName, font.Names.PostScriptName)
            .With(ErrorMetadataKeys.Character, character.ToString())
            .With(ErrorMetadataKeys.CodePoint, codePoint);
    }
}
