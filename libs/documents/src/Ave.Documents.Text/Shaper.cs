using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Text;

/// <summary>
/// Turns text into glyphs. Stage 0 shaping: one glyph per Unicode character, chosen through the font's 'cmap';
/// each advances the pen by its 'hmtx' advance width. No kerning (GPOS) and no ligatures (GSUB) yet: those make
/// stage 1, and arrive behind this same function.
/// </summary>
public static class Shaper
{
    /// <summary>
    /// Shapes one run of text in one font and size. A character the font has no glyph for is an error, never a
    /// silent drop or a .notdef box; ALL such characters in the run are reported together.
    /// </summary>
    /// <param name="text">The text, already white-space collapsed.</param>
    /// <param name="font">The font.</param>
    /// <param name="fontSize">The font size in points.</param>
    /// <returns>One glyph per character, or a <see cref="DocumentErrorCodes.Font.GlyphMissing"/> error (several
    /// combined into <see cref="DocumentErrorCodes.Multiple"/>), or <see cref="DocumentErrorCodes.Input.Invalid.Text"/>
    /// for text that is not valid UTF-16.</returns>
    public static Result<ImmutableArray<ShapedGlyph>, Error> Shape(string text, TrueTypeFont font, double fontSize) =>
        Characters(text).OnSuccessBind(characters => characters.TraverseAll(character => ShapeCharacter(character, font, fontSize)));

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

    private static Result<ShapedGlyph, Error> ShapeCharacter(Rune character, TrueTypeFont font, double fontSize) =>
        font.GlyphFor(character).Match(
            glyph => Result<ShapedGlyph, Error>.Success(new ShapedGlyph(
                glyph,
                font.Metrics.ToPoints(font.AdvanceWidth(glyph), fontSize),
                character.ToString())),
            () => Result<ShapedGlyph, Error>.Failure(GlyphMissing(character, font)));

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
