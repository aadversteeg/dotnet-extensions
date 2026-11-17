using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// LIMIT (NF-1, review 35; docs/architecture.md, Known limits): leading space (margins, empty boxes) plus a first
/// unbreakable piece that together exceed the page area, at the document start or after a forced page break, is
/// refused. The layout counts a box after only margins and empty boxes as being at the start of the page, so it does
/// not move it to the next page, where its leading space would be gone. These tests pin today's behaviour, each case a
/// face of that limit; the rule itself is work for the next milestone. The refusal says what does not fit and how much
/// room it had. Page area 280 × 150 pt; one line of text is 14.4 pt.
/// </summary>
public class LeadingSpaceLimitTests
{
    private static PageSetup Area => Page(300.Pt(), 170.Pt()).Margin(10.Pt());

    private static Result<ValueList<PageFragment>, global::Ave.Extensions.ErrorPaths.Error> Paginate(params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => Paginator.Paginate(LayoutEnvironment.Create(TestFonts.SetWithItalic), model));

    private static TableElement OneRow() => Table(Columns(Fill(1)), [TableRow([Cell("row")])]);

    /// <summary>Every word on each page with its top, from the page's root.</summary>
    private static IEnumerable<(string Word, double Y)> Words(Fragment fragment, double y) => fragment switch
    {
        BoxFragment box => box.Children.SelectMany(child => Words(child.Fragment, y + child.Offset.Y)),
        LineFragment line => line.Children.SelectMany(child => Words(child.Fragment, y + child.Offset.Y)),
        TextFragment text => string.Concat(text.Glyphs.Select(glyph => glyph.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => (word, y)),
        _ => [],
    };

    /// <summary>The words of each page, joined by spaces.</summary>
    private static List<string> Pages(ValueList<PageFragment> pages) =>
        [.. pages.Select(page => string.Join(" ", Words(page.Root.Fragment, 0).Select(found => found.Word)))];

    /// <summary>The refusal: Documents.Layout.DoesNotFit, naming the element and the 11.5 pt (150 − 138.5) left for it.</summary>
    private static void RefusedWithItsRoom(Result<ValueList<PageFragment>, global::Ave.Extensions.ErrorPaths.Error> result, string role)
    {
        result.IsFailure.Should().BeTrue("LIMIT (NF-1): leading space and the first piece exceed the page area");
        result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        result.Error.Message.Should().StartWith($"The {role} made at LeadingSpaceLimitTests.cs:")
            .And.EndWith(" does not fit in the 11.5pt left for it on the page and cannot be broken; nothing before it on the page can move to a later page.");
        result.Error.Metadata![ErrorMetadataKeys.Value].Should().Be("11.5");
    }

    [Fact]
    public void A_table_after_an_empty_block_whose_margin_leaves_too_little_room_is_refused() =>
        RefusedWithItsRoom(Paginate(Block([]).MarginBottom(138.5.Pt()), OneRow()), "table");

    [Fact]
    public void A_table_whose_own_top_margin_leaves_too_little_room_is_refused() =>
        RefusedWithItsRoom(Paginate(OneRow().MarginTop(138.5.Pt())), "table");

    [Fact]
    public void A_paragraph_after_an_empty_block_whose_margin_leaves_too_little_room_is_refused() =>
        RefusedWithItsRoom(Paginate(Block([]).MarginBottom(138.5.Pt()), Paragraph("para")), "paragraph");

    [Fact]
    public void A_paragraph_whose_own_top_margin_leaves_too_little_room_is_refused()
    {
        RefusedWithItsRoom(Paginate(Paragraph("para").MarginTop(138.5.Pt())), "paragraph");

        // With room for its line it is laid out under its margin.
        Words(Paginate(Paragraph("para").MarginTop(135.Pt())).Value[0].Root.Fragment, 0).Should().Equal([("para", 135.0)]);
    }

    [Fact]
    public void A_top_margin_taller_than_the_page_area_leaves_no_room_and_the_refusal_says_so()
    {
        var result = Paginate(Paragraph("para").MarginTop(200.Pt()));

        result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        result.Error.Message.Should().EndWith(" does not fit in the page, which has no room left for it, and cannot be broken; nothing before it on the page can move to a later page.");
        result.Error.Metadata![ErrorMetadataKeys.Value].Should().Be("-50");
    }

    [Fact]
    public void After_a_forced_page_break_the_same_leading_space_is_refused() =>
        RefusedWithItsRoom(Paginate(Paragraph("first"), PageBreak(), Block([]).MarginBottom(138.5.Pt()), OneRow()), "table");

    [Fact]
    public void With_text_before_it_the_table_moves_to_the_next_page()
    {
        // The control: a paragraph is content a break can leave behind, so the table moves on.
        Pages(Paginate(Paragraph("intro").MarginBottom(124.1.Pt()), OneRow()).Value).Should().Equal("intro", "row");
    }

    [Fact]
    public void A_first_row_kept_together_after_an_empty_block_is_cut_inside_instead_of_moved()
    {
        // A softer face of the limit: the table counts as at the start of the page, where a row kept together that does
        // not fit is cut anyway (nothing would be gained by moving it), so its nine lines are split 7 + 2.
        TableElement Table9() => Table(Columns(Fill(1)), [TableRow([Cell([.. Enumerable.Range(0, 9).Select(i => (Element)Paragraph($"l{i}"))])]).KeepTogether(), TableRow([Cell("next")])]);

        Pages(Paginate(Block([]).MarginBottom(40.Pt()), Table9()).Value).Should().Equal(
            "l0 l1 l2 l3 l4 l5 l6", "l7 l8 next");

        // With a paragraph before it the row moves to the next page whole.
        Pages(Paginate(Paragraph("intro").MarginBottom(25.6.Pt()), Table9()).Value).Should().Equal(
            "intro", "l0 l1 l2 l3 l4 l5 l6 l7 l8 next");
    }

    [Fact]
    public void A_header_row_after_an_empty_block_may_stand_alone_at_the_foot_of_the_page()
    {
        // The other softer face: a header alone at a page end is allowed only at the start of an empty page (TablePage),
        // and the table after the empty block counts as there.
        TableElement Headed() => Table(Columns(Fill(1)), [HeaderRow([Cell("head")]), TableRow([Cell("r0")]), TableRow([Cell("r1")])]);

        var pages = Paginate(Block([]).MarginBottom(125.Pt()), Headed()).Value;
        Pages(pages).Should().Equal("head", "head r0 r1");
        Words(pages[0].Root.Fragment, 0).Single().Y.Should().BeApproximately(125, 1e-6);

        // With a paragraph before it the whole table moves on.
        Pages(Paginate(Paragraph("intro").MarginBottom(110.6.Pt()), Headed()).Value).Should().Equal("intro", "head r0 r1");
    }
}
