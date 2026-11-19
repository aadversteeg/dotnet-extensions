using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The bottom margin of a box's last child stays inside the box when padding or a border separates them (CSS 2.1
/// §8.3.1). There is no breakpoint between that child and its margin (CSS Fragmentation 3 §4.1: a class C break needs a
/// gap between a child's margin edge and its container's content edge), so when the margin does not fit the box breaks
/// earlier. No fragment is ever taller than the space it was given.
/// </summary>
public class TrailingMarginTests
{
    private const double Epsilon = 1e-9;

    private static BoxStyle Padded(double padding) => BoxStyle.Initial with { Padding = Edges.All(padding.Pt()), Background = new Color(0xDD, 0xEB, 0xF7) };

    private static BoxStyle MarginBottom(double margin) => BoxStyle.Initial with { Margin = new Edges(0.Pt(), 0.Pt(), margin.Pt(), 0.Pt()) };

    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>Every box fragment with its bottom edge on the page, relative to the page area.</summary>
    private static IEnumerable<(BoxFragment Box, double Bottom)> Bottoms(Fragment fragment, double top) =>
        fragment is BoxFragment box
            ? box.Children.SelectMany(child => Bottoms(child.Fragment, top + child.Offset.Y)).Prepend((box, top + box.Size.Height))
            : [];

    private static void EveryBoxEndsInsideThePageArea(ValueList<PageFragment> pages, double areaHeight, string because)
    {
        for (var page = 0; page < pages.Count; page++)
        {
            foreach (var (box, bottom) in Bottoms(pages[page].Root.Fragment, 0))
            {
                bottom.Should().BeLessThanOrEqualTo(areaHeight + Epsilon, $"{because}: box #{box.Box.Id.Value} on page {page + 1}");
            }
        }
    }

    private static StyledDocument OnPage(double areaHeight, params Node[] children) =>
        new(new DocumentMetadata("Test", null), new PageStyle(300.Pt(), (areaHeight + 20).Pt(), Edges.All(10.Pt())), Flow(0, [.. children]));

    /// <summary>
    /// An empty box with bottom padding or a bottom border, where the page ends inside it (found in fix round 5 while
    /// testing the row-cut rule): its bottom padding and border are no place for a break, so when they do not fit, the
    /// box moves to the next page whole. Before, an empty box was placed anyway — empty content "fits" even the
    /// negative height left once that room is kept free — and its bottom ran past the page end by up to its padding
    /// and border.
    /// </summary>
    [Theory]
    [InlineData(2.0, 0.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(2.0, 1.0)]
    public void An_empty_box_whose_bottom_padding_or_border_does_not_fit_moves_to_the_next_page(double padding, double border)
    {
        var style = BoxStyle.Initial with
        {
            Padding = new Edges(padding.Pt(), 0.Pt(), padding.Pt(), 0.Pt()),
            Border = Border.All(BorderSide.None) with { Bottom = new BorderSide(border.Pt(), Color.Black) },
        };
        var height = 2 * padding + border;
        for (var area = 14.4 + 0.25; area < 14.4 + height + 14.4; area += 0.25)
        {
            var pages = Paginate(OnPage(area, Paragraph(1, "first", size: 12), Flow(2, [], style), Paragraph(3, "after", size: 12)));

            var because = $"padding {padding}, border {border}, page area {area}";
            EveryBoxEndsInsideThePageArea(pages, area, because);
            pages.SelectMany(page => Bottoms(page.Root.Fragment, 0)).Where(found => found.Box.Box.Id == new NodeId(2)).Should()
                .ContainSingle(because).Which.Box.Size.Height.Should().BeApproximately(height, Epsilon, because + ": the empty box is placed once, whole");
        }
    }

    [Fact]
    public void A_padded_box_whose_last_child_has_a_large_bottom_margin_moves_to_the_next_page()
    {
        // Five 12pt lines, then a block with 6pt padding around a paragraph with a 40pt bottom
        // margin. 165.6pt page area: the block would end at 72 + 6 + 14.4 + 40 + 6 = 138.4 — it fits; with seven lines
        // above it, at 100.8 + 66.4 = 167.2, it does not and must move whole.
        ValueList<Node> lines = [.. Enumerable.Range(1, 7).Select(i => (Node)Paragraph(i, $"filler {i}", size: 12))];
        var block = Flow(50, [Paragraph(51, "inside", MarginBottom(40), size: 12)], Padded(6));

        var pages = Paginate(OnPage(165.6, [.. lines, block]));

        EveryBoxEndsInsideThePageArea(pages, 165.6, "a padded block after seven lines");
        pages.Should().HaveCount(2);
        pages[1].Root.Fragment.Should().BeOfType<BoxFragment>().Which.Children.Should().ContainSingle()
            .Which.Fragment.Should().BeOfType<BoxFragment>().Which.Size.Height.Should().BeApproximately(6 + 14.4 + 40 + 6, Epsilon);
    }

    [Fact]
    public void No_start_offset_lets_a_note_box_run_past_the_page_area()
    {
        // Note boxes (4mm padding, two paragraphs with a 6pt bottom margin, 8pt between the
        // boxes) down a page, moved by an introduction whose bottom margin is 0 to 72pt in 0.5pt steps.
        const double Area = 760;
        var note = Padded(Length.FromMillimetres(4).Points);
        for (var offset = 0.0; offset < 72; offset += 0.5)
        {
            var id = 1000;
            ValueList<Node> notes = [.. Enumerable.Range(0, 30).Select(i => (Node)Flow(id += 10,
            [
                Paragraph(id + 1, $"Note {i} first paragraph.", MarginBottom(6)),
                Paragraph(id + 2, "Second paragraph.", MarginBottom(6)),
            ], note with { Margin = new Edges(0.Pt(), 0.Pt(), 8.Pt(), 0.Pt()) }))];

            var pages = Paginate(OnPage(Area, [Paragraph(1, "Intro", MarginBottom(offset)), .. notes]));

            EveryBoxEndsInsideThePageArea(pages, Area, $"offset {offset}");
        }
    }

    [Fact]
    public void The_last_block_of_the_document_stays_on_its_page_when_only_its_bottom_margin_passes_the_page_end()
    {
        // Five one-line paragraphs (12pt text, 14.4pt lines) with a 12pt bottom margin: the text ends at
        // 4 × 26.4 + 14.4 = 120. Nothing of the root follows the last margin (no bottom padding or border), so the
        // margin adjoins the page end and is truncated there: from a 120pt page area on, one page. Below 120 the fifth
        // line does not fit and the page breaks before it.
        ValueList<Node> paragraphs = [.. Enumerable.Range(1, 5).Select(i => (Node)Paragraph(i, $"line {i}", MarginBottom(12), size: 12))];
        for (var area = 118.0; area <= 136; area += 0.5)
        {
            var pages = Paginate(OnPage(area, [.. paragraphs]));

            pages.Should().HaveCount(area >= 120 ? 1 : 2, $"page area {area}");
            EveryBoxEndsInsideThePageArea(pages, area, $"page area {area}");
        }
    }

    [Fact]
    public void A_page_area_truncates_the_last_margin_of_its_content_at_its_end()
    {
        // A page area is the root of its own formatting context too, and nothing follows its content: a fixed 14.4pt
        // footer holds one 12pt line whose 12pt bottom margin is cut at the end of the area, on every page.
        var footer = new PageArea(Flow(60, [Paragraph(61, "footer", MarginBottom(12), size: 12)]), 14.4.Pt());
        ValueList<Node> paragraphs = [.. Enumerable.Range(1, 12).Select(i => (Node)Paragraph(i, $"line {i}", MarginBottom(12), size: 12))];
        var document = OnPage(150, [.. paragraphs]) with
        {
            Page = new PageStyle(300.Pt(), 170.Pt(), Edges.All(10.Pt())) { Template = new PageTemplate(null, footer) },
        };

        var pages = Paginate(document);

        pages.Should().HaveCountGreaterThan(1);
        pages.Should().OnlyContain(page => page.Footer != null);
    }

    /// <summary>
    /// The case the page-root rule must not reach: a padded root still breaks earlier, because its bottom padding must
    /// end on the page with it.
    /// </summary>
    [Fact]
    public void A_padded_root_still_breaks_before_its_last_child_when_the_margin_does_not_fit()
    {
        ValueList<Node> paragraphs = [.. Enumerable.Range(1, 5).Select(i => (Node)Paragraph(i, $"line {i}", MarginBottom(12), size: 12))];
        var document = OnPage(130, [.. paragraphs]) with { Root = Flow(0, [.. paragraphs], BoxStyle.Initial with { Padding = new Edges(0.Pt(), 0.Pt(), 2.Pt(), 0.Pt()) }) };

        var pages = Paginate(document);

        pages.Should().HaveCount(2, "120 of text + 12 of margin + 2 of padding pass the 130pt area, and the margin cannot be cut there");
        EveryBoxEndsInsideThePageArea(pages, 130, "padded root");
    }

    /// <summary>
    /// The rule is general (CSS Fragmentation 3 §5.2: margins adjoining an unforced break are truncated): it holds in
    /// every box without a bottom inset, a table cell included, not only in the page root. Spot check of fix round 3,
    /// mutant R3N1c: a cell with two paragraphs, each with a 10 pt bottom margin, beside a long cell; no grid lines and no
    /// cell padding, so nothing of the cell follows the second paragraph's margin. When the page ends inside the row,
    /// that margin adjoins the break and is cut off there: the paragraph stays on its page. With bottom padding in the
    /// cell the margin cannot be cut (the padding must end on the page with the cell), and the paragraph moves on.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(2.0)]
    public void A_table_cell_without_a_bottom_inset_truncates_its_last_margin_at_the_page_end(double padding)
    {
        for (var area = 36.0; area <= 46; area += 0.5)
        {
            var t = new TableModels();
            Box P(string text, double margin) => new(t.Next(), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, MarginBottom(margin), [new TextRun(t.Next(), text, Text(), At(0))], At(0));
            var style = BoxStyle.Initial with { Padding = new Edges(0.Pt(), 0.Pt(), padding.Pt(), 0.Pt()) };
            Box Cell(params Box[] paragraphs) => new(t.Next(), SemanticRole.TableCell, LayoutAlgorithmId.Flow, style, [.. paragraphs], At(0));
            var table = t.Table([new TrackSize.Fill(1), new TrackSize.Fill(1)],
                t.Body(t.Row(Cell(P("p1", 10), P("p2", 10)), Cell([.. Enumerable.Range(0, 40).Select(i => P($"l{i:00}", 0))]))));

            var pages = Paginate(TableModels.Page(280, area, table));

            // p1 (12 pt line) and its 10 pt margin end at 22, p2's line at 34: from a 36 pt page area p2's text fits.
            var first = Words(pages[0].Root.Fragment).ToList();
            if (padding == 0)
            {
                first.Should().Contain("p2", $"page area {area}: the margin after p2 adjoins the break and is truncated");
            }
            else if (area < 34 + 10 + padding)
            {
                first.Should().NotContain("p2", $"page area {area}: the cell's bottom padding follows p2's margin, which then does not fit");
            }

            EveryBoxEndsInsideThePageArea(pages, area, $"page area {area}, padding {padding}");
        }
    }

    private static IEnumerable<string> Words(Fragment fragment) => fragment switch
    {
        BoxFragment box => box.Children.SelectMany(child => Words(child.Fragment)),
        LineFragment line => line.Children.SelectMany(child => Words(child.Fragment)),
        TextFragment text => [string.Concat(text.Glyphs.Select(glyph => glyph.Text))],
        _ => [],
    };

    [Fact]
    public void At_the_start_of_a_page_a_margin_that_cannot_fit_is_truncated_at_the_page_end_and_the_bottom_padding_moves_on()
    {
        // The box has no breakpoint inside: the margin adjoins the break before the box's bottom padding and is cut off
        // at the page end, and the bottom padding starts the next page (SlicedBox, Chromium's FinishFragmentation). Every
        // box still ends inside the page area.
        var block = Flow(50, [Paragraph(51, "inside", MarginBottom(500), size: 12)], Padded(6));

        var pages = Paginate(OnPage(100, block));

        pages.Should().HaveCount(2);
        EveryBoxEndsInsideThePageArea(pages, 100, "a 500pt margin on a 100pt page");
        ((BoxFragment)pages[1].Root.Fragment).Children.Single().Fragment.Size.Height.Should().Be(6, "page 2 holds the bottom padding only");
    }
}
