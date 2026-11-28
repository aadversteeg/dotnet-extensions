using System.IO;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// The listed limits of page breaks and line heights that the code front-end's tests pin with documents of their own
/// (<c>ListedDifferenceTests</c>, <c>PageBreakProgressTests</c>, <c>LineBoxReferenceTests</c>, <c>FontMetricRoundingTests</c>
/// and <c>TableBreakReferenceTests</c> of <c>UnitTests.Extensions.Documents.Fluent</c>, tests of the same names), here
/// through the HTML path: each test lays out the reference case and pins, in numbers, where the engine and Chromium break
/// the pages or put the line. The status file of each case holds every difference the comparison finds; these tests say
/// what causes them.
/// </summary>
public class HtmlListedBreakTests
{
    private static PrintedPages Chromium(string name) =>
        PrintedPages.Parse(File.ReadAllText(Path.Combine(HtmlCases.Folder(), "..", "measured", name + ".print.txt")));

    private static PrintedPages Engine(string name)
    {
        var boxes = HtmlCases.Boxes(name).Value;
        return PrintedPages.Engine(DocumentLayout.Run(boxes.Document, boxes.Fonts).Value);
    }

    /// <summary>The words of each page of the engine and of Chromium, pages written <c>|</c> apart.</summary>
    private static void PagesAre(string name, string engine, string chromium)
    {
        var (ours, theirs) = (Engine(name), Chromium(name));

        ours.PageTexts.Should().Equal(engine.Split('|'));
        theirs.PageTexts.Should().Equal(chromium.Split('|'));
    }

    /// <summary>The baseline of a word on its page, of one side.</summary>
    private static double Baseline(PrintedPages pages, int page, string text) => pages.Pages[page - 1].Words.Single(word => word.Text == text).Baseline;

    /// <summary>A box with keep-together after "n0" in a block of break-inside avoid: the engine breaks before the kept box,
    /// after "n0", where Chromium keeps "n1" and the kept box on page 1 and breaks before "p0" (case
    /// <c>fr-old-keep-together-hides</c>).</summary>
    [Theory]
    [InlineData("fr-old-keep-together-hides", "n0|n1 o0 o1 p0", "n0 n1 o0 o1|p0", 79.5)]
    public void A_box_kept_together_does_not_hide_a_better_breakpoint_before_it(string name, string engine, string chromium, double lastOnPageOne)
    {
        PagesAre(name, engine, chromium);
        Baseline(Chromium(name), 1, "o1").Should().Be(lastOnPageOne);
        Baseline(Engine(name), 2, "o1").Should().Be(lastOnPageOne - 18, "the engine's page 2 starts one line of 18 pt higher with n1");
    }

    /// <summary>A continued cell whose rest fits the page area but not with its 30 pt bottom padding, below an outer
    /// repeated header "O": the engine goes on on page 2 without the header, its first line "r10" at 24.375 pt, where Chromium
    /// repeats the header (15.75 pt) and puts "r10" at 40.5 pt, and prints a third, empty page for the padding that passes
    /// the page end (case <c>a4-continued-cell-pad-bottom-30-under-header</c>).</summary>
    [Theory]
    [InlineData("a4-continued-cell-pad-bottom-30-under-header", "O r0 r1 r2 r3 r4 r5 r6 r7 r8 r9|r10 r11 r12 r13 r14 r15 r16 r17 r18", "O r0 r1 r2 r3 r4 r5 r6 r7 r8 r9|O r10 r11 r12 r13 r14 r15 r16 r17 r18|", 24.375, 40.5)]
    public void A_continued_cell_whose_rest_fits_the_page_area_but_not_with_its_bottom_padding_goes_on_without_the_outer_repeated_header(string name, string engine, string chromium, double ours, double theirs)
    {
        PagesAre(name, engine, chromium);
        Baseline(Engine(name), 2, "r10").Should().Be(ours);
        Baseline(Chromium(name), 2, "r10").Should().Be(theirs);
    }

    /// <summary>A forced break after boxes that are all empty: the engine does not take it and prints "s0" on page 1, where
    /// Chromium leaves page 1 blank (case <c>fr-old-forced-after-empty</c>).</summary>
    [Theory]
    [InlineData("fr-old-forced-after-empty", "s0", "|s0")]
    public void A_forced_break_after_only_empty_boxes_is_not_taken(string name, string engine, string chromium)
    {
        PagesAre(name, engine, chromium);
        Baseline(Engine(name), 1, "s0").Should().Be(Baseline(Chromium(name), 2, "s0"), "s0 starts its page in both");
    }

    /// <summary>A forced break inside a box kept together: the engine moves the whole box to page 2 first and then takes
    /// the break, three pages; Chromium takes the break where it is, two (case <c>fr-old-forced-in-keep-together</c>).</summary>
    [Theory]
    [InlineData("fr-old-forced-in-keep-together", "a0|f0|g0", "a0 f0|g0")]
    public void A_forced_break_inside_a_box_kept_together_moves_the_whole_box_first(string name, string engine, string chromium) =>
        PagesAre(name, engine, chromium);

    /// <summary>A paragraph of three lines that moved to page 2 whole under widows 4: the engine counts only its orphans
    /// before a break there and puts two lines on page 2, Chromium three (case <c>fr-moved-para-widows-4</c>).</summary>
    [Theory]
    [InlineData("fr-moved-para-widows-4", "i0 i1|a0 a1|a2 a3 a4", "i0 i1|a0 a1 a2|a3 a4")]
    public void A_paragraph_that_moved_to_a_new_page_whole_counts_only_its_orphans_before_a_break_there(string name, string engine, string chromium) =>
        PagesAre(name, engine, chromium);

    /// <summary>An empty box past the end of the last page: the engine adds no page for it, Chromium prints a second, empty
    /// page (case <c>fr-old-blank-last-page</c>).</summary>
    [Theory]
    [InlineData("fr-old-blank-last-page", "a0 a1 a2", "a0 a1 a2|")]
    public void An_empty_box_past_the_end_of_the_last_page_adds_no_page(string name, string engine, string chromium)
    {
        PagesAre(name, engine, chromium);
        Chromium(name).Pages[1].Rectangles.Should().BeEmpty("Chromium's second page shows nothing");
    }

    /// <summary>An empty box whose margin carries it past the page end: the engine keeps it on page 1 and "head0" after it
    /// there, where Chromium moves both to page 2 (case <c>fr-old-empty-box-past-end</c>).</summary>
    [Theory]
    [InlineData("fr-old-empty-box-past-end", "a0 b0 head0|z0", "a0 b0|head0 z0", 61.5)]
    public void An_empty_box_whose_margin_carries_it_past_the_page_end_stays_on_the_page(string name, string engine, string chromium, double head)
    {
        PagesAre(name, engine, chromium);
        Baseline(Engine(name), 1, "head0").Should().Be(head);
        Baseline(Chromium(name), 2, "z0").Should().Be(head);
    }

    /// <summary>Fifty lines of Lato at 0.37 and 0.38 pt, then "after" at 12 pt: Chromium draws none of the fifty lines and
    /// gives them no height, "after" at 24 pt; the engine draws them at their size, and at 0.38 pt gives each line a pixel,
    /// so "after" is 37.5 pt lower (cases <c>f-tiny</c>, <c>f-tiny-038</c>; the code front-end's test of the same name says why).</summary>
    [Theory]
    [InlineData("f-tiny", 0.37, 24)]
    [InlineData("f-tiny-038", 0.38, 61.5)]
    public void At_font_sizes_below_half_a_pixel_Chromium_draws_no_text_where_the_engine_does(string name, double size, double after)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));

        chromium.PageTexts.Should().Equal("after");
        chromium.Pages[0].Words.Single().Baseline.Should().Be(24);
        engine.Pages.Should().ContainSingle();
        engine.Pages[0].Words.Should().HaveCount(51);
        engine.Pages[0].Words.Where(word => word.Text != "after").Should().OnlyContain(word => Math.Abs(word.FontSize - size) < 1e-9);
        Baseline(engine, 1, "after").Should().Be(after);
    }

    /// <summary>Keep-with-next inside a box whose bottom padding does not fit page 3: the engine keeps "j0" with what follows
    /// across that padding and prints it alone on page 3, where Chromium leaves page 3 blank and starts "j0" on page 4
    /// (cases <c>fr-kwn-pad-*</c>; with a bordered box and widows 4 Chromium breaks after "j1").</summary>
    [Theory]
    [InlineData("fr-kwn-pad-on-inner-box", "a0|a1|j0|j1 j2 z", "a0|a1||j0 j1 j2|z", 52.5)]
    [InlineData("fr-kwn-pad-then-kept-para", "a0|a1|j0|j1 j2 z", "a0|a1||j0 j1 j2|z", 52.5)]
    [InlineData("fr-kwn-pad-then-para", "a0|a1|j0|j1 j2 z", "a0|a1||j0 j1 j2|z", 52.5)]
    [InlineData("fr-kwn-pad-then-bordered-box-widows4", "a0|a1|j0|j1 j2 z", "a0|a1||j0 j1|j2 z", 53.25)]
    public void Keep_with_next_inside_a_box_holds_across_its_bottom_padding_on_a_page_of_its_own(string name, string engine, string chromium, double j0)
    {
        PagesAre(name, engine, chromium);
        Baseline(Engine(name), 3, "j0").Should().Be(j0);
        Chromium(name).Pages[2].Words.Should().BeEmpty();
    }

    /// <summary>A box with 72 pt of padding below a repeated header "H": on the page that holds only padding the engine
    /// prints nothing, where Chromium prints the header there too (case <c>fr-header-pad-box-72</c>).</summary>
    [Theory]
    [InlineData("fr-header-pad-box-72", "H a||H AFTER", "H a|H|H AFTER")]
    public void The_same_on_a_page_where_Chromium_repeats_the_header_is_printed(string name, string engine, string chromium)
    {
        PagesAre(name, engine, chromium);
        Baseline(Chromium(name), 2, "H").Should().Be(Baseline(Chromium(name), 1, "H"));
    }

    /// <summary>Lines of 10 pt under an explicit line height of 15 pt: every line is 15 pt below the one before in both, and
    /// the engine's first baseline is 0.75 pt, one pixel, below Chromium's, the measured difference (case
    /// <c>fr-lh-first-baseline</c>).</summary>
    [Theory]
    [InlineData("fr-lh-first-baseline", 23.25, 22.5)]
    public void Under_an_explicit_line_height_the_first_baseline_is_within_the_measured_difference_of_Chromium_s(string name, double ours, double theirs)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        string[] lines = ["a0", "a1", "a2", "a3"];

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        lines.Select((text, index) => Baseline(engine, 1, text) - index * 15).Should().AllSatisfy(baseline => baseline.Should().Be(ours));
        lines.Select((text, index) => Baseline(chromium, 1, text) - index * 15).Should().AllSatisfy(baseline => baseline.Should().Be(theirs));
    }
}
