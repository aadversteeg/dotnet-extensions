using System.Collections.Immutable;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf;
using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Ave.Documents.Render.Pdf;

/// <summary>
/// The PDF back-end is a public library: other producers than this engine's layout may hand it a page model. It
/// validates what it is given — the page-model contract for every profile, and everything a profile requires — and
/// answers bad input with an error value. It never throws for it, and never writes a file that claims a conformance
/// level it does not meet. The page models are made by hand.
/// </summary>
public class PageModelValidationTests
{
    private static readonly DateTimeOffset Date = new(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1));

    private static readonly SourceLocation Here = new("PageModelValidationTests.cs", 1);

    private static PdfRenderOptions With(PdfProfile profile) => new(Date) { Profile = profile };

    private static GlyphRun Run(string text)
    {
        var glyphs = Shaper.Shape(text, TestFonts.Regular, 10).Value;
        return new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 50), [.. glyphs.Select((glyph, index) => new PositionedGlyph(glyph.Glyph, index * 5.0, glyph.Text))]);
    }

    private static StructureStart Start(int node, SemanticRole role) => new(new NodeId(node), role, Here);

    private static StructureEnd End(int node) => new(new NodeId(node));

    private static PageDocument Tagged(DocumentMetadata metadata, params DrawItem[] content) =>
        new(metadata, [new Page(new Size(200, 100), [Start(0, SemanticRole.Document), .. content, End(0)])]);

    private static DocumentMetadata Accessible => new("Title", null) { Language = "en" };

    private static Error Refused(PageDocument document, PdfProfile profile)
    {
        Result<ImmutableArray<byte>, Error>? result = null;
        var render = () => result = PdfRenderer.Render(document, With(profile));

        render.Should().NotThrow();
        result!.Value.IsFailure.Should().BeTrue($"{profile} must refuse this page model");
        return result.Value.Error;
    }

    public static TheoryData<string> Profiles => ["plain", "3b", "3a", "ua1", "3a+ua1"];

    private static PdfProfile ProfileNamed(string name) => name switch
    {
        "plain" => PdfProfile.Plain,
        "3b" => PdfProfile.PdfA3b,
        "3a" => PdfProfile.PdfA3a,
        "ua1" => PdfProfile.PdfUa1,
        _ => PdfProfile.PdfA3a.AndPdfUa1(),
    };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_document_without_pages_is_an_error(string profile) =>
        Refused(new PageDocument(Accessible, []), ProfileNamed(profile)).Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_missing_item_is_an_error(string profile) =>
        Refused(new PageDocument(Accessible, [new Page(new Size(200, 100), [Run("x"), null!])]), ProfileNamed(profile)).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_glyph_the_font_does_not_have_is_an_error(string profile)
    {
        var run = new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 50), [new PositionedGlyph(new GlyphId(60000), 0, "x")]);

        var error = Refused(new PageDocument(Accessible, [new Page(new Size(200, 100), [Start(0, SemanticRole.Document), run, End(0)])]), ProfileNamed(profile));

        error.Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
        error.Message.Should().Contain("60000");
    }

    [Fact]
    public void A_cell_span_below_one_is_an_error() =>
        Refused(Tagged(Accessible, Start(1, SemanticRole.Table), Start(2, SemanticRole.TableRow), Start(3, SemanticRole.TableCell) with { Span = new CellSpan(-1, 1) }, Run("x"), End(3), End(2), End(1)), PdfProfile.PdfA3a)
            .Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Two_attachments_with_one_name_are_an_error(string profile)
    {
        var attachment = new Attachment("data.xml", [.. "<a/>"u8], "application/xml", Here) { Relationship = AttachmentRelationship.Data };
        var metadata = Accessible with { Attachments = [attachment, attachment with { Content = [.. "<b/>"u8] }] };

        Refused(Tagged(metadata, Start(1, SemanticRole.Paragraph), Run("x"), End(1)), ProfileNamed(profile)).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(0)]
    public void A_heading_level_outside_1_to_6_has_no_structure_type_and_is_refused_when_tagged(int level) =>
        Refused(Tagged(Accessible, Start(1, SemanticRole.Heading(level)), Run("x"), End(1)), PdfProfile.PdfA3a).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidStructure);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void PDF_UA_refuses_an_empty_language(string language) =>
        Refused(Tagged(Accessible with { Language = language }, Start(1, SemanticRole.Paragraph), Run("x"), End(1)), PdfProfile.PdfUa1).Code
            .Should().Be(PdfRenderErrorCodes.Conformance.PdfUa1.MissingLanguage);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void PDF_UA_refuses_a_figure_with_blank_alternative_text(string text)
    {
        var image = new DrawImage(new Rect(new Point(10, 10), new Size(20, 20)), TestImages.Load("rgb8.png"));

        Refused(Tagged(Accessible, Start(1, SemanticRole.Figure(text)), image, End(1)), PdfProfile.PdfUa1).Code
            .Should().Be(PdfRenderErrorCodes.Conformance.PdfUa1.FigureWithoutAlternativeText);
    }

    [Fact]
    public void PDF_UA_refuses_a_row_span_past_its_row_group()
    {
        // A table of one group with two rows; the first cell spans three rows.
        var error = Refused(
            Tagged(
                Accessible,
                Start(1, SemanticRole.Table), Start(2, SemanticRole.TableBody),
                Start(3, SemanticRole.TableRow), Start(4, SemanticRole.TableCell) with { Span = new CellSpan(1, 3) }, Run("a"), End(4), Start(5, SemanticRole.TableCell), Run("b"), End(5), End(3),
                Start(6, SemanticRole.TableRow), Start(7, SemanticRole.TableCell), Run("c"), End(7), End(6),
                End(2), End(1)),
            PdfProfile.PdfUa1);

        error.Code.Should().Be(PdfRenderErrorCodes.Conformance.PdfUa1.IrregularTable);
    }

    /// <summary>The profile rules that only a hand-made page model can break (the front-end never draws glyph 0 and
    /// refuses the characters these rules forbid): each is refused with its own code.</summary>
    [Theory]
    [InlineData("3b")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void The_notdef_glyph_is_refused_under_PDF_A_and_PDF_UA(string profile)
    {
        var notdef = new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 50), [new PositionedGlyph(new GlyphId(0), 0, "x")]);

        Refused(Tagged(Accessible, Start(1, SemanticRole.Paragraph), notdef, End(1)), ProfileNamed(profile)).Code
            .Should().Be(PdfRenderErrorCodes.Conformance.Glyphs.NotdefGlyph);
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("\uFEFF")]
    [InlineData("\uFFFE")]
    public void Glyph_text_a_ToUnicode_map_cannot_carry_is_refused_when_tagged(string text)
    {
        var glyph = Shaper.Shape("x", TestFonts.Regular, 10).Value[0];
        var run = new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 50), [new PositionedGlyph(glyph.Glyph, 0, text)]);

        Refused(Tagged(Accessible, Start(1, SemanticRole.Paragraph), run, End(1)), PdfProfile.PdfA3a).Code
            .Should().Be(PdfRenderErrorCodes.Conformance.Tagged.UnmappableText);
    }

    [Fact]
    public void Private_use_text_is_refused_at_PDF_A_level_A_only()
    {
        var glyph = Shaper.Shape("x", TestFonts.Regular, 10).Value[0];
        var run = new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 50), [new PositionedGlyph(glyph.Glyph, 0, "\uE000")]);
        var document = Tagged(Accessible, Start(1, SemanticRole.Paragraph), run, End(1));

        Refused(document, PdfProfile.PdfA3a).Code.Should().Be(PdfRenderErrorCodes.Conformance.PdfA3.PrivateUseText);
        PdfRenderer.Render(document, With(PdfProfile.PdfA3b)).IsSuccess.Should().BeTrue("level B has no rule on the text of glyphs");
    }

    [Theory]
    [InlineData("3a", "not a language")]
    [InlineData("ua1", "not a language")]
    [InlineData("3a", "en\n")]
    public void A_document_language_that_is_not_a_language_tag_is_refused_when_tagged(string profile, string language) =>
        Refused(Tagged(Accessible with { Language = language }, Start(1, SemanticRole.Paragraph), Run("x"), End(1)), ProfileNamed(profile)).Code
            .Should().Be(PdfRenderErrorCodes.Conformance.Tagged.InvalidLanguage);

    [Fact]
    public void More_indirect_objects_than_PDF_A_allows_are_refused()
    {
        ProfileLimits.ObjectCount(ProfileLimits.MaxIndirectObjects, PdfProfile.PdfA3b).Should().BeEmpty();
        ProfileLimits.ObjectCount(ProfileLimits.MaxIndirectObjects + 1, PdfProfile.PdfA3b).Should().ContainSingle()
            .Which.Code.Should().Be(PdfRenderErrorCodes.Conformance.PdfA3.TooManyObjects);
        ProfileLimits.ObjectCount(ProfileLimits.MaxIndirectObjects + 1, PdfProfile.PdfUa1).Should().BeEmpty("the limit is PDF/A's");
    }

    [Fact]
    public void A_valid_hand_made_page_model_is_written() =>
        PdfRenderer.Render(Tagged(Accessible, Start(1, SemanticRole.Paragraph), Run("x"), End(1)), With(PdfProfile.PdfA3a.AndPdfUa1())).IsSuccess.Should().BeTrue();
}
