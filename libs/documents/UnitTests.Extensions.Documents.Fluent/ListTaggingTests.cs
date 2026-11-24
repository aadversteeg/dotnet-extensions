using System.Collections.Immutable;
using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Lists in tagged PDF (ISO 32000-1 §14.8.4.3.3, Table 336): <c>L</c> with its <c>ListNumbering</c> attribute (§14.8.5.5,
/// Table 347), <c>LI</c>, <c>Lbl</c>, <c>LBody</c>, a nested list inside the outer item's <c>LBody</c>; a list without labels
/// is <c>L</c> with numbering <c>None</c> and items of an <c>LBody</c> only. A drawn bullet's <c>Lbl</c> carries the bullet's
/// character as <c>/ActualText</c> on the structure element (§14.9.4), not in the content, so the text read from the page
/// is the same as Chromium's print, which has no bullet text. Checked with veraPDF for PDF/A-3a and PDF/UA-1 (the Matterhorn
/// checks of list structure, 16-001 to 16-003, as far as a machine checks them).
/// </summary>
public class ListTaggingTests
{
    private static FontSet Fonts => new([TestFonts.Regular, TestFonts.Bold]);

    private static Document Lists() => Document(
        [
            Heading(1, "Lists"),
            BulletedList([ListItem("Apples"), ListItem("Pears")]),
            NumberedList([ListItem("First"), ListItem([Paragraph("Second"), BulletedList([ListItem("Inner")])])]),
            BulletedList([ListItem("Plain")]).Bullet(ListBullet.None),
        ],
        Page(300.Pt(), 400.Pt()).Margin(12.Pt()), title: "Lists", language: "en").FontFamily("Lato").FontSize(10.5.Pt());

    private static ImmutableArray<byte> Pdf(PdfProfile profile)
    {
        var result = Lists().ToPdf(Fonts, new PdfRenderOptions(CreationDate: new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero)) { Profile = profile });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Fact]
    public void A_list_is_tagged_L_LI_Lbl_LBody_with_its_numbering_and_a_bullet_has_its_character_as_actual_text()
    {
        var outline = PdfStructureReadBack.Outline(PdfStructureReadBack.Read(Pdf(PdfProfile.PdfA3a.AndPdfUa1())));

        outline.Should().Be(
            """
            Document
              H1 "Lists"
              L ListNumbering=Disc
                LI
                  Lbl ActualText="•"
                  LBody
                    P "Apples"
                LI
                  Lbl ActualText="•"
                  LBody
                    P "Pears"
              L ListNumbering=Decimal
                LI
                  Lbl "1. "
                  LBody
                    P "First"
                LI
                  Lbl "2. "
                  LBody
                    P "Second"
                    L ListNumbering=Circle
                      LI
                        Lbl ActualText="◦"
                        LBody
                          P "Inner"
              L ListNumbering=None
                LI
                  LBody
                    P "Plain"

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void The_words_read_from_a_bulleted_list_hold_no_bullet_character()
    {
        var text = string.Join("\n", PdfReadBack.Text(Pdf(PdfProfile.PdfA3a.AndPdfUa1())));

        text.Should().Contain("Apples").And.Contain("1.");
        text.Should().NotContainAny("•", "◦", "▪");
    }

    public static TheoryData<string> BrokenNesting() => ["LI outside L", "Lbl outside LI", "P in L", "P in LI"];

    [Theory]
    [MemberData(nameof(BrokenNesting))]
    public void A_page_model_whose_list_structure_does_not_nest_is_refused_by_the_back_end(string which)
    {
        // ISO 32000-1 Table 336; veraPDF rules 7.2-17 to 7.2-20. A page model from any producer is held to it.
        var here = new SourceLocation("ListTaggingTests.cs", 1);
        StructureStart Start(int node, SemanticRole role) => new(new NodeId(node), role, here);
        StructureEnd End(int node) => new(new NodeId(node));
        var (outer, inner) = which switch
        {
            "LI outside L" => (SemanticRole.Document, SemanticRole.ListItem),
            "Lbl outside LI" => (SemanticRole.List(ListNumbering.Disc), SemanticRole.ListLabel("\u2022")),
            "P in L" => (SemanticRole.List(ListNumbering.Disc), SemanticRole.Paragraph),
            _ => (SemanticRole.ListItem, SemanticRole.Paragraph),
        };
        ValueList<DrawItem> items = [Start(0, SemanticRole.Document), .. outer == SemanticRole.Document ? Array.Empty<DrawItem>() : [Start(1, outer)],
            Start(2, inner), End(2), .. outer == SemanticRole.Document ? Array.Empty<DrawItem>() : [End(1)], End(0)];
        var pages = new PageDocument(new DocumentMetadata("Lists", null) { Language = "en" }, [new Page(new Size(100, 100), items)]);

        var result = PdfRenderer.Render(pages, new PdfRenderOptions(CreationDate: DateTimeOffset.UnixEpoch) { Profile = PdfProfile.PdfUa1 });

        result.IsFailure.Should().BeTrue(which);
        result.Error.Parts().Should().Contain(part => part.Code == PdfRenderErrorCodes.Write.InvalidPageModel, result.Error.ToString());
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_file_with_bulleted_numbered_nested_and_unlabelled_lists_passes_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-lists-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "lists.pdf"), [.. Pdf(flavour == "3a" ? PdfProfile.PdfA3a : PdfProfile.PdfUa1)]);

            var result = VeraPdf.Validate(folder, flavour).Single();

            result.IsCompliant.Should().BeTrue(result.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
