using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Render.Pdf;

/// <summary>
/// The rules of the conformance profiles that depend on the input and that the writer cannot meet by construction
/// (docs/conformance-matrix.md lists every rule of the three veraPDF profiles and how each is met). Each rule names the
/// veraPDF rule it implements; all are pure checks of the page model, run before anything is written.
/// </summary>
internal static class ProfileLimits
{
    /// <summary>The longest string a PDF/A file may contain, in bytes (veraPDF rule 6.1.13-3).</summary>
    public const int MaxStringBytes = 32767;

    /// <summary>The longest name a PDF/A file may contain, in bytes (veraPDF rule 6.1.13-4).</summary>
    public const int MaxNameBytes = 127;

    /// <summary>The smallest and largest page side of a PDF/A file, in default user space units (rule 6.1.13-11).</summary>
    public const double MinPageSide = 3;

    /// <summary>See <see cref="MinPageSide"/>.</summary>
    public const double MaxPageSide = 14400;

    /// <summary>The most indirect objects a PDF/A file may contain (rule 6.1.13-7).</summary>
    public const int MaxIndirectObjects = 8388607;

    /// <summary>
    /// A subset font is named with a six-letter tag and a plus sign before its PostScript name (ISO 32000-1 §9.6.4), so
    /// the name is seven bytes longer than the font's own.
    /// </summary>
    private const int SubsetTagBytes = 7;

    /// <summary>veraPDF rule 6.8-1's pattern for an embedded file's media type, <c>/^[-\w+\.]+\/[-\w+\.]+$/</c> in
    /// JavaScript, written out for .NET: <c>\w</c> there means the ASCII letters, digits and the underscore, so the classes
    /// name them; and <c>$</c> there is the end of the text, which is <c>\z</c> in .NET, whose <c>$</c> also matches
    /// before a final line feed.</summary>
    private static readonly Regex PdfA3MediaType = new(@"^[-A-Za-z0-9_+.]+/[-A-Za-z0-9_+.]+\z", RegexOptions.CultureInvariant);

    /// <summary>The PDF/A-3 limits: page size, string and name lengths, the media type form and, at level A, text in
    /// the Private Use Area.</summary>
    /// <param name="document">The page document.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>The violations.</returns>
    public static IEnumerable<Error> PdfA3(PageDocument document, PdfProfile profile)
    {
        if (profile.PdfA3 is null)
        {
            return [];
        }

        var pageSizes = document.Pages.Select((page, index) => (page.Size, Number: index + 1))
            .Where(page => !IsAllowedSide(page.Size.Width) || !IsAllowedSide(page.Size.Height))
            .Select(page => new Error(PdfRenderErrorCodes.Conformance.PdfA3.PageSize, $"{profile} allows page sides of {MinPageSide} to {MaxPageSide} units; page {page.Number} is {Number(page.Size.Width)} × {Number(page.Size.Height)} (veraPDF rule 6.1.13-11).")
                .With(ErrorMetadataKeys.Value, $"page {page.Number}: {Number(page.Size.Width)}x{Number(page.Size.Height)}"));
        var texts = WrittenTexts(document)
            .Select(text => (text.Property, text.Source, Bytes: PdfString.FromText(text.Text).Bytes.Count))
            .Where(text => text.Bytes > MaxStringBytes)
            .Select(text => Located(new Error(PdfRenderErrorCodes.Conformance.PdfA3.TextTooLong, $"{profile} allows strings of at most {MaxStringBytes} bytes; the {text.Property} takes {text.Bytes} (veraPDF rule 6.1.13-3).")
                .With(ErrorMetadataKeys.Property, text.Property)
                .With(ErrorMetadataKeys.Value, text.Bytes.ToString(CultureInfo.InvariantCulture)), text.Source));
        var mediaTypes = document.Metadata.Attachments
            .Where(attachment => !PdfA3MediaType.IsMatch(attachment.MediaType))
            .Select(attachment => new Error(PdfRenderErrorCodes.Conformance.PdfA3.MediaType, $"{profile} needs a media type of letters, digits and _ - + . on both sides of the slash; '{attachment.MediaType}' of attachment '{attachment.FileName}' is not (veraPDF rule 6.8-1) ({attachment.Source}).")
                .With(ErrorMetadataKeys.Value, attachment.MediaType)
                .WithSource(attachment.Source));
        var names = WrittenNames(document)
            .Where(name => Encoding.UTF8.GetByteCount(name.Name) + name.ExtraBytes > MaxNameBytes)
            .Select(name => Located(new Error(PdfRenderErrorCodes.Conformance.PdfA3.NameTooLong, $"{profile} allows names of at most {MaxNameBytes} bytes; the {name.Property} '{name.Name}' would be longer (veraPDF rule 6.1.13-4).")
                .With(ErrorMetadataKeys.Property, name.Property)
                .With(ErrorMetadataKeys.Value, name.Name), name.Source));
        var privateUse = profile.PdfA3 == PdfA3Level.A ? PrivateUseGlyphs(document, profile) : [];
        return pageSizes.Concat(texts).Concat(mediaTypes).Concat(names).Concat(privateUse);
    }

    /// <summary>The rules on shown glyphs: no .notdef glyph under PDF/A-3 and PDF/UA-1 (rules 6.2.11.8-1, 7.21.8-1);
    /// under a tagged profile, no glyph text with U+0000, U+FEFF or U+FFFE (rules 6.2.11.7.2-2, 7.21.7-2).</summary>
    /// <param name="document">The page document.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>The violations, at most one of each kind per page.</returns>
    public static IEnumerable<Error> Glyphs(PageDocument document, PdfProfile profile)
    {
        var pages = Pages(document).ToList();
        var notdef = profile.PdfA3 is not null || profile.IsPdfUa1
            ? pages.Where(page => page.Glyphs.Any(glyph => glyph.Glyph.Value == 0))
                .Select(page => new Error(PdfRenderErrorCodes.Conformance.Glyphs.NotdefGlyph, $"{profile} does not allow the .notdef glyph (glyph 0), which page {page.Number} shows (veraPDF rules 6.2.11.8-1, 7.21.8-1).")
                    .With(ErrorMetadataKeys.Value, Number(page.Number)))
            : [];
        var unmappable = profile.IsTagged
            ? pages.Where(page => page.Glyphs.Any(glyph => glyph.Text.Contains('\0') || glyph.Text.Contains('\uFEFF') || glyph.Text.Contains('\uFFFE')))
                .Select(page => new Error(PdfRenderErrorCodes.Conformance.Tagged.UnmappableText, $"{profile} does not allow U+0000, U+FEFF or U+FFFE as the text of a glyph, which page {page.Number} has (veraPDF rules 6.2.11.7.2-2, 7.21.7-2).")
                    .With(ErrorMetadataKeys.Value, Number(page.Number)))
            : [];
        return notdef.Concat(unmappable);
    }

    /// <summary>Under a tagged profile, the document language is a language tag (rules 6.7.4-1, 7.2-29). The empty
    /// language means "unknown": rule 6.7.4-1 of PDF/A-3a accepts it. PDF/UA-1 refuses a blank language as a missing one
    /// (<c>MissingLanguage</c>), so under PDF/UA-1 a blank language is not reported again here.</summary>
    /// <param name="document">The page document.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>The violation, if any.</returns>
    public static IEnumerable<Error> Language(PageDocument document, PdfProfile profile) =>
        profile.IsTagged
        && document.Metadata.Language is { Length: > 0 } language
        && !LanguageTags.IsWellFormed(language)
        && !(profile.IsPdfUa1 && string.IsNullOrWhiteSpace(language))
            ? [new Error(PdfRenderErrorCodes.Conformance.Tagged.InvalidLanguage, $"{profile} needs a language tag such as nl-NL as the document language, not '{language}' (veraPDF rules 6.7.4-1, 7.2-29).")
                .With(ErrorMetadataKeys.Value, language)]
            : [];

    /// <summary>Rule 6.1.13-7, checked on the finished list of objects.</summary>
    /// <param name="count">The number of indirect objects.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>The violation, if any.</returns>
    public static IEnumerable<Error> ObjectCount(int count, PdfProfile profile) =>
        profile.PdfA3 is not null && count > MaxIndirectObjects
            ? [new Error(PdfRenderErrorCodes.Conformance.PdfA3.TooManyObjects, $"{profile} allows at most {MaxIndirectObjects} indirect objects; this file would have {count} (veraPDF rule 6.1.13-7).")
                .With(ErrorMetadataKeys.Value, count.ToString(CultureInfo.InvariantCulture))]
            : [];

    private static bool IsAllowedSide(double side) => side >= MinPageSide && side <= MaxPageSide;

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static Error Located(Error error, SourceLocation? source) => source is { } location ? error.WithSource(location) : error;

    /// <summary>Every text the writer turns into a PDF string, with what it is and where it was written.</summary>
    private static IEnumerable<(string Property, string Text, SourceLocation? Source)> WrittenTexts(PageDocument document)
    {
        var metadata = document.Metadata;
        var fields = new (string Property, string? Text)[] { ("title", metadata.Title), ("author", metadata.Author), ("language", metadata.Language) }
            .Where(field => field.Text is not null)
            .Select(field => (field.Property, field.Text!, (SourceLocation?)null));
        var attachments = metadata.Attachments.SelectMany(attachment => new (string, string?)[] { ("attachment file name", attachment.FileName), ("attachment description", attachment.Description) }
            .Where(field => field.Item2 is not null)
            .Select(field => (field.Item1, field.Item2!, (SourceLocation?)attachment.Source)));
        var starts = document.Pages.SelectMany(page => page.Items.OfType<StructureStart>()).ToList();
        var alternatives = starts
            .Where(start => start.Role is SemanticRole.FigureRole { AlternativeText: not null })
            .DistinctBy(start => start.Node)
            .Select(start => ("alternative text", ((SemanticRole.FigureRole)start.Role).AlternativeText!, (SourceLocation?)start.Source));
        var links = starts
            .Where(start => start.Role is SemanticRole.LinkRole)
            .DistinctBy(start => start.Node)
            .Select(start => ("link description", ((SemanticRole.LinkRole)start.Role).Description, (SourceLocation?)start.Source));
        // A glyph's text becomes a string only as replacement text (/ActualText). Text of n UTF-16 units takes at most
        // 2n + 2 bytes, so only a text longer than that bound can pass the limit; the rest need not be encoded.
        var glyphTexts = Pages(document).SelectMany(page => page.Glyphs)
            .Where(glyph => 2 * glyph.Text.Length + 2 > MaxStringBytes)
            .Select(glyph => ("text of a glyph", glyph.Text, (SourceLocation?)null));
        return fields.Concat(attachments).Concat(alternatives).Concat(links).Concat(glyphTexts);
    }

    /// <summary>Every value from the input that the writer turns into a PDF name, with the bytes it adds.</summary>
    private static IEnumerable<(string Property, string Name, int ExtraBytes, SourceLocation? Source)> WrittenNames(PageDocument document) =>
        document.Metadata.Attachments.Select(attachment => ("media type", attachment.MediaType, 0, (SourceLocation?)attachment.Source))
            .Concat(document.Pages.SelectMany(page => page.Items.OfType<GlyphRun>())
                .Select(run => run.Font.Names.PostScriptName)
                .Distinct()
                .Select(name => ("font name", name, SubsetTagBytes, (SourceLocation?)null)));

    private static IEnumerable<Error> PrivateUseGlyphs(PageDocument document, PdfProfile profile) =>
        Pages(document)
            .Select(page => (page.Number, Glyph: page.Glyphs.FirstOrDefault(glyph => glyph.Text.EnumerateRunes().Any(IsPrivateUse))))
            .Where(page => page.Glyph is not null)
            .Select(page => new Error(PdfRenderErrorCodes.Conformance.PdfA3.PrivateUseText, $"{profile} needs replacement text for a Private Use Area character such as U+{page.Glyph!.Text.EnumerateRunes().First(IsPrivateUse).Value:X4} on page {page.Number}, which the document cannot carry (veraPDF rule 6.2.11.7.3-1).")
                .With(ErrorMetadataKeys.Value, $"page {page.Number}"));

    private static bool IsPrivateUse(Rune rune) => Rune.GetUnicodeCategory(rune) == UnicodeCategory.PrivateUse;

    private static IEnumerable<(int Number, IEnumerable<PositionedGlyph> Glyphs)> Pages(PageDocument document) =>
        document.Pages.Select((page, index) => (index + 1, page.Items.OfType<GlyphRun>().SelectMany(run => run.Glyphs)));
}
