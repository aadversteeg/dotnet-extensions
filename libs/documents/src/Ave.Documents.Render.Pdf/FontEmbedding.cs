using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.Pdf;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Render.Pdf;

/// <summary>
/// Embeds a TrueType font as a composite font (ISO 32000-1 §9.7): a Type0 font with the Identity-H encoding,
/// whose one descendant is a CIDFontType2 that maps codes to glyph ids one to one. Text is then written as
/// two-byte glyph ids, which reaches every glyph of the font, not just 256 of them as a simple font would.
/// </summary>
/// <remarks>
/// Only the used glyphs are embedded (<see cref="FontSubsetter"/>, glyph ids kept), and the font is named with its
/// six-letter subset tag, <c>EOODIA+Lato-Regular</c> (§9.6.4: "the PostScript name of the font … shall begin with a
/// tag followed by a plus sign").
/// </remarks>
internal static class FontEmbedding
{
    /// <summary>PDF font metrics are in glyph space: 1000 units per em (§9.2.4).</summary>
    private const double GlyphSpaceUnitsPerEm = 1000.0;

    // Font descriptor flags (§9.8.2, Table 123): bit 1 FixedPitch, bit 3 Symbolic, bit 7 Italic.
    private const int FixedPitchFlag = 1 << 0;
    private const int SymbolicFlag = 1 << 2;
    private const int ItalicFlag = 1 << 6;

    /// <summary>Builds the five objects of one font, or fails when the font's licence forbids embedding.</summary>
    /// <param name="used">The font and the glyphs the document uses.</param>
    /// <param name="numbers">The object numbers reserved for it.</param>
    /// <returns>The Type0 font, CIDFont, font descriptor, font file and ToUnicode CMap.</returns>
    public static Result<ImmutableArray<IndirectObject>, Error> Build(UsedFont used, FontObjectNumbers numbers)
    {
        var font = used.Font;
        if (!font.Embedding.AllowsOutlineEmbedding)
        {
            return Result<ImmutableArray<IndirectObject>, Error>.Failure(
                new Error(
                        PdfErrorCodes.Write.FontEmbeddingNotPermitted,
                        $"The licence bits of font '{font.Names.PostScriptName}' (OS/2 fsType 0x{font.Embedding.FsType:X4}) do not allow embedding.")
                    .With(ErrorMetadataKeys.FontName, font.Names.PostScriptName));
        }

        return FontSubsetter.Subset(font, used.GlyphTexts.Keys.Select(glyph => new GlyphId(glyph))).OnSuccessMap(subset =>
        {
            var name = new PdfName(subset.PdfName(font));
            return ImmutableArray.Create<IndirectObject>(
                new IndirectValue(numbers.Type0, Type0Font(name, numbers)),
                new IndirectValue(numbers.CidFont, CidFont(name, used, numbers)),
                new IndirectValue(numbers.Descriptor, Descriptor(name, font, numbers)),
                new IndirectStream(numbers.FontFile, FontFile(subset)),
                new IndirectStream(numbers.ToUnicode, new PdfStream(PdfDictionary.Of(), ToUnicodeCMap.Build(used.GlyphTexts), StreamEncoding.Flate)));
        });
    }

    /// <summary>
    /// §9.7.6.1, Table 121. For a CIDFontType2 descendant the BaseFont "should be the same as the CIDFont's
    /// BaseFont name". Identity-H: two-byte codes, code = CID (§9.7.5.2, Table 118).
    /// </summary>
    private static PdfDictionary Type0Font(PdfName name, FontObjectNumbers numbers) => PdfDictionary.Of(
        ("Type", new PdfName("Font")),
        ("Subtype", new PdfName("Type0")),
        ("BaseFont", name),
        ("Encoding", new PdfName("Identity-H")),
        ("DescendantFonts", new PdfArray([new PdfReference(numbers.CidFont)])),
        ("ToUnicode", new PdfReference(numbers.ToUnicode)));

    /// <summary>
    /// §9.7.4.1, Table 117. CIDToGIDMap /Identity makes CID = glyph id. The character collection
    /// Adobe-Identity-0 says the CIDs have no meaning outside this font. /W gives the widths of the used glyphs;
    /// viewers use them to advance the pen, so they must equal the advances layout used.
    /// </summary>
    private static PdfDictionary CidFont(PdfName name, UsedFont used, FontObjectNumbers numbers) => PdfDictionary.Of(
        ("Type", new PdfName("Font")),
        ("Subtype", new PdfName("CIDFontType2")),
        ("BaseFont", name),
        ("CIDSystemInfo", PdfDictionary.Of(
            ("Registry", PdfString.FromText("Adobe")),
            ("Ordering", PdfString.FromText("Identity")),
            ("Supplement", new PdfInteger(0)))),
        ("FontDescriptor", new PdfReference(numbers.Descriptor)),
        ("W", Widths(used)),
        ("CIDToGIDMap", new PdfName("Identity")));

    /// <summary>
    /// §9.7.4.3: the /W array as groups "c [w1 w2 … wn]": glyphs c, c+1, … have widths w1, w2, …. Consecutive
    /// glyph ids share one group.
    /// </summary>
    private static PdfArray Widths(UsedFont used)
    {
        var groups = new List<List<ushort>>();
        foreach (var glyph in used.GlyphTexts.Keys)
        {
            if (groups.Count > 0 && groups[^1][^1] + 1 == glyph)
            {
                groups[^1].Add(glyph);
            }
            else
            {
                groups.Add([glyph]);
            }
        }

        return new PdfArray([.. groups.SelectMany(group => new PdfObject[]
        {
            new PdfInteger(group[0]),
            PdfArray.OfNumbers(group.Select(glyph => GlyphSpace(used.Font, used.Font.AdvanceWidth(new GlyphId(glyph))))),
        })]);
    }

    /// <summary>
    /// §9.8.1, Table 122. All metrics in glyph space. CapHeight is required; fonts without it in 'OS/2' use
    /// the ascender instead. StemV is required too but TrueType fonts do not record it; viewers use it only to
    /// imitate a font that is NOT embedded, so for an embedded font an estimate from the weight class is enough
    /// (80 at weight 400, rising to about 160 at weight 700).
    /// </summary>
    private static PdfDictionary Descriptor(PdfName name, TrueTypeFont font, FontObjectNumbers numbers)
    {
        var metrics = font.Metrics;
        var box = metrics.BoundingBox;
        var flags = SymbolicFlag
            | (metrics.IsFixedPitch ? FixedPitchFlag : 0)
            | (metrics.ItalicAngle != 0 ? ItalicFlag : 0);
        var stemV = Math.Round(80 + (metrics.WeightClass - 400) * 0.27);
        return PdfDictionary.Of(
            ("Type", new PdfName("FontDescriptor")),
            ("FontName", name),
            ("Flags", new PdfInteger(flags)),
            ("FontBBox", PdfArray.OfNumbers(GlyphSpace(font, box.XMin), GlyphSpace(font, box.YMin), GlyphSpace(font, box.XMax), GlyphSpace(font, box.YMax))),
            ("ItalicAngle", PdfNumber.From(metrics.ItalicAngle)),
            ("Ascent", PdfNumber.From(GlyphSpace(font, metrics.Ascender))),
            ("Descent", PdfNumber.From(GlyphSpace(font, metrics.Descender))),
            ("CapHeight", PdfNumber.From(GlyphSpace(font, metrics.CapHeight ?? metrics.Ascender))),
            ("StemV", PdfNumber.From(Math.Max(stemV, 1))),
            ("FontFile2", new PdfReference(numbers.FontFile)));
    }

    /// <summary>§9.9, Tables 126 and 127: a FontFile2 stream holds a TrueType font program; /Length1 is its
    /// length before compression.</summary>
    private static PdfStream FontFile(FontSubset subset) =>
        new(PdfDictionary.Of(("Length1", new PdfInteger(subset.FontFile.Length))), subset.FontFile, StreamEncoding.Flate);

    /// <summary>Converts font design units to glyph space units (1000 per em).</summary>
    public static double GlyphSpace(TrueTypeFont font, double designUnits) =>
        designUnits * GlyphSpaceUnitsPerEm / font.Metrics.UnitsPerEm;
}
