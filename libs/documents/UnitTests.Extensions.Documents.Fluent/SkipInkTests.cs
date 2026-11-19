using System.Globalization;
using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using Xunit.Abstractions;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The gaps an underline or overline leaves around a glyph's ink, <c>text-decoration-skip-ink: auto</c> (CSS Text Decoration 4
/// §2.10.4; THE RULE FOR THE GAPS AROUND A GLYPH'S INK): cases <c>sk-*</c> and <c>dl-skip</c>, Lato 12 pt on 24 pt lines unless
/// a row says otherwise, written with the code front-end (rows with a decorating box around text of another size in the
/// model), under <see cref="FontMetricRounding.WholePixels"/>.
/// </summary>
/// <remarks>
/// THE COMPARISON: the words as <see cref="InlineImageTests"/> compares them; and the visible pieces of the lines, Chromium's
/// from its clips (<c>ink.txt</c>: the line minus the union of the gaps) and every other filled rectangle of its print, against
/// the engine's rectangles, both joined where two touch on the same rows and ordered by top and start. Tops and heights are
/// equal (whole pixels). Each end lies within <see cref="Tolerance"/> of Chromium's: one layout unit for each glyph before it
/// on the line (each advance is ceiled to a layout unit, README.md "The comparison rule", as <see cref="TextDecorationTests"/>
/// counts rectangles) plus <see cref="OutlineTolerance"/> for the outline.
/// </remarks>
public class SkipInkTests(ITestOutputHelper output)
{
    private static readonly FontSet Fonts = new([TestFonts.Regular, TestFonts.Bold, TestFonts.Italic, TestFonts.TheanoDidot]);

    /// <summary>
    /// What the outline adds to an end's tolerance, in points. Skia holds a glyph's path at 64 px in 1/64 px steps (the font
    /// backend's 26.6 fixed point), so each of its points lies within 1/128 px of the exact one at 64 px, which at a font size
    /// of s px is (1/128)·(s/64) px; the engine computes in design units. Where an end comes from a point or a crossing on a
    /// steep side, it moves by that much; the largest font of the cases is 30 pt (40 px): 40/8192 px ≈ 0.004 pt. A crossing
    /// on a side that is flat at the band's edge moves more (a curve's extreme): it moves by the point's error divided by the
    /// side's slope, so such rows are named where they differ, not covered by a wider tolerance. The float arithmetic of Skia
    /// (24-bit mantissa, at most 64 px) adds less than 1e-5 px.
    /// </summary>
    private const double OutlineTolerance = 40.0 / 8192 * 0.75 + 1e-4;

    private static Result<PageDocument, Error> Laid(double pageHeight, Func<StyledDocument, StyledDocument> change, params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Page(300.Pt(), pageHeight.Pt()).Margin(12.Pt()), title: "Title", language: "en")
                .FontFamily("Lato").FontSize(12.Pt()).LineHeight(24.Pt()).FontMetricRounding(FontMetricRounding.WholePixels))
            .OnSuccessBind(model => DocumentLayout.Run(change(model), Fonts));

    private static PageDocument Painted(double pageHeight, Func<StyledDocument, StyledDocument> change, params Element[] body)
    {
        var laid = Laid(pageHeight, change, body);
        laid.IsSuccess.Should().BeTrue(laid.IsFailure ? laid.Error.ToString() : string.Empty);
        return laid.Value;
    }

    private static PageDocument Painted(double pageHeight, params Element[] body) => Painted(pageHeight, model => model, body);

    /// <summary>Chromium's rectangles with each clipped line replaced by its visible pieces (ink.txt), in drawing order.</summary>
    private static List<PrintedBox> ChromiumPieces(string caseName)
    {
        var printed = PrintedPages.Chromium(caseName).Pages[0].Rectangles.ToList();
        var path = Path.Combine(AppContext.BaseDirectory, "chromium", caseName + ".ink.txt");
        var pieces = new List<PrintedBox>();
        PrintedBox? fill = null;
        void Flush()
        {
            if (fill is { } line)
            {
                var index = printed.FindIndex(box => Math.Abs(box.X0 - line.X0) < 0.002 && Math.Abs(box.Y0 - line.Y0) < 0.002 && Math.Abs(box.X1 - line.X1) < 0.002);
                index.Should().BeGreaterThanOrEqualTo(0, $"the clipped line {line} is a rectangle of the print");
                printed.RemoveAt(index);
                printed.InsertRange(index, pieces);
            }

            pieces.Clear();
        }

        foreach (var row in File.Exists(path) ? File.ReadAllLines(path) : [])
        {
            var parts = row.Split(' ');
            double At(int index) => double.Parse(parts[index], CultureInfo.InvariantCulture);
            switch (parts[0])
            {
                case "fill":
                    Flush();
                    fill = new PrintedBox(1, At(2), At(3), At(4), At(5));
                    break;
                case "piece":
                    pieces.Add(fill! with { X0 = At(2), X1 = At(3) });
                    break;
                case "part":
                    pieces.Add(new PrintedBox(1, At(2), At(3), At(4), At(5)));
                    break;
                case "gap":
                    break;
                default:
                    throw new FormatException($"{caseName}.ink.txt: {row}");
            }
        }

        Flush();
        return printed;
    }

    private static List<PrintedBox> Joined(IEnumerable<PrintedBox> rectangles)
    {
        var joined = new List<PrintedBox>();
        foreach (var box in rectangles.OrderBy(box => Math.Round(box.Y0, 2)).ThenBy(box => Math.Round(box.Y1, 2)).ThenBy(box => box.X0))
        {
            if (joined.Count > 0 && joined[^1] is var last && Math.Abs(last.Y0 - box.Y0) < 0.01 && Math.Abs(last.Y1 - box.Y1) < 0.01 && box.X0 <= last.X1 + 0.001)
            {
                joined[^1] = last with { X1 = Math.Max(last.X1, box.X1) };
                continue;
            }

            joined.Add(box);
        }

        return joined;
    }

    /// <summary>An end's tolerance in points: a layout unit per glyph of the line before it, plus the outline's.</summary>
    private static double Tolerance(PrintedPage chromium, PrintedBox line, double x) =>
        (chromium.Words.Where(word => word.Baseline > line.Y0 - 40 && word.Baseline < line.Y1 + 40 && word.X0 < x).Sum(word => word.Text.Length) + 1)
        * ChromiumLayout.LayoutUnit + OutlineTolerance;

    /// <summary>The words without soft hyphens and U+2010: in the PDF text layer the engine drops a soft hyphen and Chromium keeps it
    /// and adds U+2010 at a shown hyphen (a listed difference of the text layer, as <see cref="WhiteSpaceTests"/> reads it).</summary>
    private static PrintedPages WithoutInvisibles(PrintedPages pages) =>
        new([.. pages.Pages.Select(page => page with
        {
            Words = [.. page.Words.Select(word => word with { Text = string.Concat(word.Text.Where(character => character is not ('\u00AD' or '\u2010'))) })],
        })]);

    private List<string> Breaches(PageDocument engine, string caseName, bool aligned = false)
    {
        var (mine, theirs) = (WithoutInvisibles(PrintedPages.Engine(engine)), WithoutInvisibles(PrintedPages.Chromium(caseName)));
        var breaches = InlineImageTests.Breaches(mine, theirs, halfPixelBaselines: false, extraUnits: aligned ? 2 : 0);
        var (engineLines, chromiumLines) = (Joined(mine.Pages[0].Rectangles), Joined(ChromiumPieces(caseName)));
        if (engineLines.Count != chromiumLines.Count)
        {
            breaches.Add($"{engineLines.Count} rectangles, Chromium {chromiumLines.Count}: engine [{string.Join("; ", engineLines)}], Chromium [{string.Join("; ", chromiumLines)}]");
            return breaches;
        }

        var worst = 0.0;
        for (var index = 0; index < engineLines.Count; index++)
        {
            var (box, measured) = (engineLines[index], chromiumLines[index]);
            var (d0, d1) = (Math.Abs(box.X0 - measured.X0), Math.Abs(box.X1 - measured.X1));
            worst = Math.Max(worst, Math.Max(d0, d1));
            var extra = aligned ? 2 * ChromiumLayout.LayoutUnit : 0;
            if (Math.Abs(box.Y0 - measured.Y0) > 0.001 || Math.Abs(box.Y1 - measured.Y1) > 0.001
                || d0 > Tolerance(theirs.Pages[0], measured, measured.X0) + extra || d1 > Tolerance(theirs.Pages[0], measured, measured.X1) + extra)
            {
                breaches.Add($"rectangle {index}: engine {box.X0:0.000},{box.Y0:0.000}–{box.X1:0.000},{box.Y1:0.000}, Chromium {measured.X0:0.000},{measured.Y0:0.000}–{measured.X1:0.000},{measured.Y1:0.000} ({d0:0.0000}/{d1:0.0000} pt)");
            }
        }

        output.WriteLine($"{caseName}: {engineLines.Count} rectangles, largest difference of an end {worst:0.0000} pt");
        return breaches;
    }

    private void ShouldMatch(PageDocument engine, string caseName, bool aligned = false)
    {
        var breaches = Breaches(engine, caseName, aligned);
        breaches.Should().BeEmpty(string.Join("\n", breaches));
    }

    /// <summary>A model change: the runs of a paragraph (by index in the body) with the given font sizes and decorations, as
    /// Chromium's rows with a decorating box around text of another size (the code front-end has no box around runs).</summary>
    private static Func<StyledDocument, StyledDocument> Runs(params (int Paragraph, (double Size, TextDecoration[] Lines)[] Runs)[] rows) => document =>
    {
        var body = document.Root.Children.ToArray();
        foreach (var (paragraph, runs) in rows)
        {
            var box = (Box)body[paragraph];
            body[paragraph] = box with
            {
                Children = [.. box.Children.Select((child, index) => (Node)((TextRun)child with
                {
                    Style = ((TextRun)child).Style with { FontSize = runs[index].Size.Pt(), Decorations = [.. runs[index].Lines] },
                }))],
            };
        }

        return document with { Root = document.Root with { Children = [.. body] } };
    };

    private static TextDecoration Line(TextDecorationLine line, double size, string family = "Lato") => new(line, Color.Black, family, size.Pt(), FontWeight.Regular);

    private static ParagraphElement Big(ValueList<Span> spans) => Paragraph(spans).FontSize(24.Pt()).LineHeight(48.Pt());

    public static TheoryData<string, string> FontCases => new()
    {
        { "sk-lato", "regular" }, { "sk-lato-bold", "bold" }, { "sk-lato-italic", "italic" }, { "sk-theano", "theano" },
    };

    [Theory]
    [MemberData(nameof(FontCases))]
    public void Descenders_and_accents_cut_the_lines_where_Chromium_cuts_them_in_each_font_at_two_sizes(string caseName, string face)
    {
        ParagraphElement Face(ParagraphElement paragraph) => face switch
        {
            "bold" => paragraph.Bold(),
            "italic" => paragraph.Italic(),
            "theano" => paragraph.FontFamily("Theano Didot"),
            _ => paragraph,
        };

        var body = new[] { (12, 24), (24, 48) }.SelectMany(size => new Element[]
        {
            Face(Paragraph("gjpqy Qx ab fy")).FontSize(size.Item1.Pt()).LineHeight(size.Item2.Pt()).Underline(),
            Face(Paragraph("ÅÉÎÔ Ñx Hb")).FontSize(size.Item1.Pt()).LineHeight(size.Item2.Pt()).Overline(),
            Face(Paragraph("gjpqy Qx")).FontSize(size.Item1.Pt()).LineHeight(size.Item2.Pt()).LineThrough(),
        });

        ShouldMatch(Painted(500, [.. body]), caseName);
    }

    [Fact]
    public void The_first_skip_ink_case_is_cut_like_Chromium() =>
        ShouldMatch(Painted(300, model => model,
            Big([Text("gjpqy Hxg")]).Underline(),
            Big([Text("ÅÉÎ Hxg")]).Overline(),
            Big([Text("gjpqy Hxg")]).LineThrough()), "dl-skip");

    [Fact]
    public void The_band_comes_from_the_decorating_box_and_the_outline_from_the_text_like_Chromium()
    {
        var (u24, u12, u8, o24) = (Line(TextDecorationLine.Underline, 24), Line(TextDecorationLine.Underline, 12), Line(TextDecorationLine.Underline, 8), Line(TextDecorationLine.Overline, 24));
        var page = Painted(400,
            Runs(
                (0, [(24, [u24]), (8, [u24]), (24, [u24])]),
                (1, [(12, [u12]), (24, [u12]), (12, [u12])]),
                (2, [(24, [o24]), (8, [o24]), (24, [o24])]),
                (3, [(8, [u8]), (24, [u8])])),
            Paragraph([Text("gy "), Text("gjpqy"), Text(" gy")]).LineHeight(48.Pt()),
            Paragraph([Text("gy "), Text("gjpqy"), Text(" gy")]).LineHeight(48.Pt()),
            Paragraph([Text("Éx "), Text("ÅÉÎ"), Text(" Hx")]).LineHeight(48.Pt()),
            Paragraph([Text("gy "), Text("gjpqy")]).LineHeight(48.Pt()));

        ShouldMatch(page, "sk-size");
    }

    [Fact]
    public void Each_piece_is_cut_by_its_own_glyphs_only_like_Chromium()
    {
        var u24 = Line(TextDecorationLine.Underline, 24);
        var page = Painted(400,
            Runs((3, [(24, [u24]), (8, [u24]), (24, [u24])])),
            Big([Text("gj").Underline(), Text("yg").Bold().Underline(), Text("pq").Underline()]),
            Big([Text("a").Underline(), Text("f").Italic().Underline(), Text("a").Underline(), Text(" "), Text("fjf").Italic().Underline(), Text("x").Underline()]),
            Big([Text("g").Underline(), Text("j").Color(new Color(0xCC, 0, 0)).Underline(), Text("y").Underline()]),
            Big([Text("ab"), Text("gj"), Text("yy")]),
            Big([Text("Q").Underline(), Text("j").Bold().Underline()]));

        ShouldMatch(page, "sk-touch");
    }

    [Fact]
    public void The_value_of_the_text_decides_not_that_of_the_decorating_box_like_Chromium()
    {
        var none = TextDecorationSkipInk.None;
        var page = Painted(400,
            Big([Text("gy ").Underline(), Text("gjpqy").Underline().TextDecorationSkipInk(none), Text(" gy").Underline()]),
            Big([Text("gy ").Underline().TextDecorationSkipInk(none), Text("gjpqy").Underline(), Text(" gy").Underline().TextDecorationSkipInk(none)]),
            Big([Text("gy ").TextDecorationSkipInk(none), Text("gjpqy").Underline(), Text(" gy").TextDecorationSkipInk(none)]),
            Big([Text("gy "), Text("gjpqy").Underline().TextDecorationSkipInk(none), Text(" gy")]),
            Big([Text("gy ").Underline(), Text("gjpqy").Underline().Overline(), Text(" gy").Underline()]).TextDecorationSkipInk(none));

        ShouldMatch(page, "sk-whose");
    }

    [Fact]
    public void Justified_spaces_and_hanging_spaces_have_no_ink_and_leave_no_gap_like_Chromium()
    {
        ParagraphElement Narrow(ValueList<Span> spans, TextAlign align = TextAlign.Left) =>
            Big(spans).PaddingRight((276 - 120).Pt()).TextAlign(align);
        var preserve = WhiteSpaceCollapse.Preserve;
        var page = Painted(400,
            Narrow([Text("gy jp qy gg jj pp qq yy").Underline()], TextAlign.Justify),
            Narrow([Text("gy jp      ").Underline()]).WhiteSpaceCollapse(preserve),
            Narrow([Text("gy jp           qq").Underline()]).WhiteSpaceCollapse(preserve));

        ShouldMatch(page, "sk-space", aligned: true);
    }

    [Fact]
    public void An_image_a_shown_soft_hyphen_and_ligatures_are_cut_like_Chromium()
    {
        var figure = InlineImage(Image(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "figure.png"))).Alt("figure").Width(16.8.Pt()));
        var page = Painted(400,
            Big([Text("gy ").Underline(), figure, Text(" py").Underline()]),
            Big([Text("jump­ing gyp").Underline()]).PaddingRight((276 - 60).Pt()),
            Big([Text("fi fj ffi fly gy").Underline()]),
            Big([Text("figure fjord").Underline()]).FontFamily("Theano Didot"));

        ShouldMatch(page, "sk-misc");
    }

    [Fact]
    public void Curve_extremes_near_the_band_are_cut_like_Chromium()
    {
        var body = new[] { 9, 10, 11, 13, 14, 16, 18, 20, 30 }
            .Select(size => (Element)Paragraph([Text("gạọụ gyj").Underline()]).FontSize(size.Pt()).LineHeight(30.Pt()))
            .Append(Paragraph([Text("gạọụ gyj").Underline()]).FontFamily("Theano Didot").FontSize(20.Pt()).LineHeight(30.Pt()))
            .Append(Paragraph([Text("gạọụ gyj").Underline()]).Bold().FontSize(20.Pt()).LineHeight(30.Pt()));

        ShouldMatch(Painted(500, [.. body]), "sk-extreme");
    }

    [Fact]
    public void A_line_is_also_cut_by_the_gaps_of_the_lines_painted_before_it_on_the_same_text_like_Chromium()
    {
        var (u24, u8, o24) = (Line(TextDecorationLine.Underline, 24), Line(TextDecorationLine.Underline, 8), Line(TextDecorationLine.Overline, 24));
        var page = Painted(300,
            Runs(
                (0, [(24, [u24]), (24, [u24, u24]), (24, [u24])]),
                (1, [(24, [u24]), (8, [u24, u8]), (24, [u24])]),
                (2, [(24, [u24]), (24, [u24, o24]), (24, [u24])])),
            Big([Text("gy "), Text("gjpqy"), Text(" gy")]),
            Paragraph([Text("gy "), Text("gjpqy"), Text(" gy")]).LineHeight(48.Pt()),
            Big([Text("gy "), Text("ÅÉgjpqy"), Text(" gy")]));

        ShouldMatch(page, "sk-nest");
    }

    [Fact]
    public void A_later_line_is_cut_by_an_earlier_line_s_gaps_only_over_the_rows_its_clip_reaches_like_Chromium()
    {
        // Case sk-part: (a) an 8 pt underline around a 24 pt one, (b) the other order: the 8 pt line keeps its upper rows where
        // the 24 pt clips reach its lower half, (c) 24 pt around 20 pt: the later line wholly inside the earlier clip, (d) the
        // pair of (a) as overlines in Theano Didot (Lato's overlines are never cut), (e) underline and line-through of one box:
        // the line-through whole, (f) "g" with a combining dot below, placed by the font's marks.
        var (u8, u20, u24) = (Line(TextDecorationLine.Underline, 8), Line(TextDecorationLine.Underline, 20), Line(TextDecorationLine.Underline, 24));
        var (o8, o24) = (Line(TextDecorationLine.Overline, 8, "Theano Didot"), Line(TextDecorationLine.Overline, 24, "Theano Didot"));
        var page = Painted(400,
            Runs((0, [(8, []), (24, [u8, u24])]), (1, [(8, []), (24, [u24, u8])]), (2, [(20, []), (24, [u24, u20])]), (3, [(8, []), (24, [o8, o24])])),
            Big([Text(""), Text("gjpqy Qx")]),
            Big([Text(""), Text("gjpqy Qx")]),
            Big([Text(""), Text("gjpqy Qx")]),
            Big([Text(""), Text("ÅÉÎ Qx")]).FontFamily("Theano Didot"),
            Big([Text("gjpqy Qx")]).Underline().LineThrough(),
            Big([Text("g\u0323g g\u0323y").Underline()]));

        ShouldMatch(page, "sk-part");
        page.Pages[0].Items.OfType<FillRectangle>().Select(fill => fill.Area).Where(area => Math.Abs(area.Origin.Y - 99.75) < 0.001 && area.Size.Height < 0.5)
            .Should().NotBeEmpty("row b: the 8 pt line keeps only its upper rows (99.75–100.125) under the 24 pt gaps");
    }

    private static Result<StyledDocument, Error> Model(params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Page(300.Pt(), 300.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()));

    private static TextDecorationSkipInk SkipInkOf(StyledDocument model, int run) =>
        ((TextRun)((Box)model.Root.Children[0]).Children[run]).Style.DecorationSkipInk;

    [Fact]
    public void The_value_is_auto_unless_set_and_inherited_by_the_text_inside_the_element_that_sets_it()
    {
        var none = TextDecorationSkipInk.None;

        SkipInkOf(Model(Paragraph("ab").Underline()).Value, 0).Should().Be(TextDecorationSkipInk.Auto, "the initial value, as the model's");
        var model = Model(Paragraph([Text("ab"), Text("cd").TextDecorationSkipInk(TextDecorationSkipInk.Auto)]).TextDecorationSkipInk(none)).Value;
        SkipInkOf(model, 0).Should().Be(none, "inherited from the paragraph");
        SkipInkOf(model, 1).Should().Be(TextDecorationSkipInk.Auto, "the span's own value wins");
        ((TextRun)((Box)((Box)Model(Block([Paragraph("ab")]).TextDecorationSkipInk(none)).Value.Root.Children[0]).Children[0]).Children[0]).Style.DecorationSkipInk
            .Should().Be(none, "inherited through a block");
    }

    [Fact]
    public void A_value_set_in_a_style_applies_as_one_set_on_the_element_and_the_element_s_own_value_wins()
    {
        var whole = Style.Empty.TextDecorationSkipInk(TextDecorationSkipInk.None);

        SkipInkOf(Model(Paragraph("ab").With(whole)).Value, 0).Should().Be(TextDecorationSkipInk.None);
        SkipInkOf(Model(Paragraph("ab").With(whole).TextDecorationSkipInk(TextDecorationSkipInk.Auto)).Value, 0).Should().Be(TextDecorationSkipInk.Auto);
    }

    [Fact]
    public void A_value_outside_its_enum_is_refused_by_the_front_end()
    {
        var error = Model(Paragraph("ab").TextDecorationSkipInk((TextDecorationSkipInk)9)).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.TextDecoration);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("text-decoration-skip-ink");
    }

    [Fact]
    public void Under_none_every_line_is_whole_and_under_auto_an_underline_is_cut_and_a_line_through_never()
    {
        PageDocument Lines(TextDecorationSkipInk skipInk) => Painted(300, Big([Text("gjpqy Hxg")]).Underline().LineThrough().TextDecorationSkipInk(skipInk));

        Lines(TextDecorationSkipInk.None).Pages[0].Items.OfType<FillRectangle>().Should().HaveCount(2);
        var cut = Lines(TextDecorationSkipInk.Auto).Pages[0].Items.OfType<FillRectangle>().ToList();
        cut.Should().HaveCount(3 + 1, "three pieces of the underline (dl-skip: 36.478–50.901, 57.840–58.050, 65.671–103.704) and one whole line-through");
        cut[^1].Area.Size.Width.Should().BeApproximately(105.738, 0.05, "the line-through is painted last, whole");
        Lines(TextDecorationSkipInk.Auto).Pages[0].Items.OfType<ArtifactStart>().Should().HaveCount(4, "each piece is a layout artifact like the whole line");
    }
}
