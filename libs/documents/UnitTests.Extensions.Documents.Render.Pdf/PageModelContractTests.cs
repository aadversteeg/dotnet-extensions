using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Render.Pdf;

/// <summary>
/// The back-end's contract is total: <see cref="PdfRenderer.Render"/> validates the whole page model and the options
/// first and answers every value it cannot write with an error, never an exception and never an allocation that one
/// number can make unbounded. Each case is a hand-made page model a producer other than this engine's layout could
/// hand it: structure that does not nest as tagged PDF's table structure requires, one node with two roles, values
/// outside an enum, missing values, a file identifier that is empty or unset, and a cell span of millions.
/// </summary>
public class PageModelContractTests
{
    private static readonly DateTimeOffset Date = new(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1));

    private static readonly SourceLocation Here = new("PageModelContractTests.cs", 1);

    private static readonly DocumentMetadata Accessible = new("Title", null) { Language = "en" };

    private static PdfRenderOptions With(PdfProfile profile) => new(Date) { Profile = profile };

    private static GlyphRun Run(string text)
    {
        var glyphs = Shaper.Shape(text, TestFonts.Regular, 10).Value;
        return new GlyphRun(TestFonts.Regular, 10, Color.Black, new Point(10, 50), [.. glyphs.Select((glyph, index) => new PositionedGlyph(glyph.Glyph, index * 5.0, glyph.Text))]);
    }

    private static StructureStart Start(int node, SemanticRole role) => new(new NodeId(node), role, Here);

    private static StructureEnd End(int node) => new(new NodeId(node));

    private static PageDocument Tagged(params DrawItem[] content) =>
        new(Accessible, [new Page(new Size(200, 100), [Start(0, SemanticRole.Document), .. content, End(0)])]);

    public static TheoryData<string> Profiles => ["plain", "3b", "3a", "ua1"];

    private static PdfProfile ProfileNamed(string name) => name switch
    {
        "plain" => PdfProfile.Plain,
        "3b" => PdfProfile.PdfA3b,
        "3a" => PdfProfile.PdfA3a,
        _ => PdfProfile.PdfUa1,
    };

    private static Error Refused(PageDocument document, PdfRenderOptions options)
    {
        Result<ImmutableArray<byte>, Error>? result = null;
        var render = () => result = PdfRenderer.Render(document, options);

        render.Should().NotThrow();
        result!.Value.IsFailure.Should().BeTrue("the page model or the options break the contract");
        return result.Value.Error;
    }

    private static Error Refused(PageDocument document, string profile = "plain") => Refused(document, With(ProfileNamed(profile)));

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_table_cell_outside_a_table_row_is_refused(string profile) =>
        Refused(Tagged(Start(1, SemanticRole.Table), Start(2, SemanticRole.TableCell), Run("x"), End(2), End(1)), profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_table_row_inside_a_paragraph_is_refused(string profile) =>
        Refused(Tagged(Start(1, SemanticRole.Paragraph), Start(2, SemanticRole.TableRow), Start(3, SemanticRole.TableCell), Run("x"), End(3), End(2), End(1)), profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_table_head_inside_a_paragraph_is_refused(string profile) =>
        // A THead's parent is a Table (ISO 32000-1 Table 337).
        Refused(Tagged(Start(1, SemanticRole.Paragraph), Start(2, SemanticRole.TableHead), Start(3, SemanticRole.TableRow), Start(4, SemanticRole.TableHeaderCell), Run("x"), End(4), End(3), End(2), End(1)), profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_paragraph_directly_in_a_table_is_refused(string profile) =>
        // A Table holds rows or row groups only.
        Refused(Tagged(Start(1, SemanticRole.Table), Start(2, SemanticRole.Paragraph), Run("x"), End(2), End(1)), profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_paragraph_directly_in_a_table_head_is_refused(string profile) =>
        // A THead holds rows only.
        Refused(Tagged(Start(1, SemanticRole.Table), Start(2, SemanticRole.TableHead), Start(3, SemanticRole.Paragraph), Run("x"), End(3), End(2), End(1)), profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Fact]
    public void A_paragraph_directly_in_a_table_row_is_refused() =>
        Refused(Tagged(Start(1, SemanticRole.Table), Start(2, SemanticRole.TableRow), Start(3, SemanticRole.Paragraph), Run("x"), End(3), End(2), End(1))).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Fact]
    public void The_table_structure_the_layout_makes_is_accepted()
    {
        var document = Tagged(
            Start(1, SemanticRole.Table),
            Start(2, SemanticRole.TableHead), Start(3, SemanticRole.TableRow), Start(4, SemanticRole.TableHeaderCell), Run("h"), End(4), End(3), End(2),
            Start(5, SemanticRole.TableBody), Start(6, SemanticRole.TableRow), Start(7, SemanticRole.TableCell), Start(8, SemanticRole.Paragraph), Run("d"), End(8), End(7), End(6), End(5),
            End(1));

        PdfRenderer.Render(document, With(PdfProfile.PdfA3a.AndPdfUa1())).IsSuccess.Should().BeTrue();
    }

    /// <summary>A table whose kids are the given row groups and rows, each holding one row of one cell.</summary>
    private static PageDocument TableOf(params SemanticRole[] kids)
    {
        IEnumerable<DrawItem> Kid(SemanticRole role, int node) => role == SemanticRole.TableRow
            ? [Start(node, role), Start(node + 1, SemanticRole.TableCell), Run("x"), End(node + 1), End(node)]
            : [Start(node, role), Start(node + 1, SemanticRole.TableRow), Start(node + 2, SemanticRole.TableCell), Run("x"), End(node + 2), End(node + 1), End(node)];
        return Tagged([Start(1, SemanticRole.Table), .. kids.SelectMany((role, index) => Kid(role, 10 * (index + 1))), End(1)]);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_table_with_two_heads_is_refused(string profile) =>
        // ISO 32000-1 Table 337 gives a table "an optional table head (structure type THead)"; veraPDF rule 7.2-11
        // allows zero or one.
        Refused(TableOf(SemanticRole.TableHead, SemanticRole.TableHead, SemanticRole.TableBody), profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_table_head_inside_a_decorative_node_of_the_table_counts_as_a_kid_of_the_table(string profile)
    {
        // A decorative node has no structure type, so it is no parent in the structure tree: the THead inside it is the
        // table's kid, and with the THead beside it the table has two (the nearest node WITH a type is the table).
        var document = Tagged(
        [
            Start(1, SemanticRole.Table),
            Start(10, SemanticRole.TableHead), Start(11, SemanticRole.TableRow), Start(12, SemanticRole.TableCell), Run("x"), End(12), End(11), End(10),
            Start(20, SemanticRole.Decorative),
            Start(21, SemanticRole.TableHead), Start(22, SemanticRole.TableRow), Start(23, SemanticRole.TableCell), Run("x"), End(23), End(22), End(21),
            End(20),
            Start(30, SemanticRole.TableBody), Start(31, SemanticRole.TableRow), Start(32, SemanticRole.TableCell), Run("x"), End(32), End(31), End(30),
            End(1),
        ]);

        var error = Refused(document, profile);

        error.Parts().Should().Contain(part => part.Code == PdfRenderErrorCodes.Write.InvalidPageModel && part.Message.Contains("more than one THead", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_table_head_after_a_body_is_refused(string profile) =>
        Refused(TableOf(SemanticRole.TableBody, SemanticRole.TableHead), profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_table_of_rows_and_row_groups_together_is_refused(string profile) =>
        Refused(TableOf(SemanticRole.TableRow, SemanticRole.TableBody), profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Fact]
    public void A_head_bodies_and_a_table_of_rows_are_accepted()
    {
        PdfRenderer.Render(TableOf(SemanticRole.TableHead, SemanticRole.TableBody, SemanticRole.TableBody), With(PdfProfile.PdfA3a.AndPdfUa1())).IsSuccess.Should().BeTrue();
        PdfRenderer.Render(TableOf(SemanticRole.TableRow, SemanticRole.TableRow), With(PdfProfile.PdfA3a.AndPdfUa1())).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_table_head_continued_on_the_next_page_is_one_head()
    {
        // A node split over pages is one structure element, so the head that opens again on page 2 is still one THead.
        IEnumerable<DrawItem> Part(string body) =>
        [
            Start(0, SemanticRole.Document), Start(1, SemanticRole.Table),
            Start(2, SemanticRole.TableHead), Start(3, SemanticRole.TableRow), Start(4, SemanticRole.TableHeaderCell), Run("h"), End(4), End(3), End(2),
            Start(5, SemanticRole.TableBody), Start(6, SemanticRole.TableRow), Start(7, SemanticRole.TableCell), Run(body), End(7), End(6), End(5),
            End(1), End(0),
        ];
        var document = new PageDocument(Accessible, [new Page(new Size(200, 100), [.. Part("a")]), new Page(new Size(200, 100), [.. Part("b")])]);

        PdfRenderer.Render(document, With(PdfProfile.PdfA3a.AndPdfUa1())).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void One_node_with_two_roles_is_refused(string profile)
    {
        // Node 1 is a paragraph on page 1 and a heading on page 2.
        var document = new PageDocument(Accessible,
        [
            new Page(new Size(200, 100), [Start(0, SemanticRole.Document), Start(1, SemanticRole.Paragraph), Run("a"), End(1), End(0)]),
            new Page(new Size(200, 100), [Start(0, SemanticRole.Document), Start(1, SemanticRole.Heading(1)), Run("b"), End(1), End(0)]),
        ]);

        Refused(document, profile).Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void An_artifact_kind_outside_the_enum_is_refused(string profile) =>
        Refused(Tagged(new ArtifactStart((ArtifactKind)99), Run("x"), new ArtifactEnd()), profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_missing_glyph_font_or_role_is_refused(string profile)
    {
        var run = Run("x");

        Refused(Tagged(Start(1, SemanticRole.Paragraph), run with { Glyphs = [null!] }, End(1)), profile).Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
        Refused(Tagged(Start(1, SemanticRole.Paragraph), run with { Font = null! }, End(1)), profile).Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
        Refused(Tagged(Start(1, null!), run, End(1)), profile).Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Missing_metadata_is_refused(string profile) =>
        Refused(Tagged(Start(1, SemanticRole.Paragraph), Run("x"), End(1)) with { Metadata = null! }, profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_glyph_without_text_is_refused(string profile)
    {
        // A ToUnicode map gives every shown glyph a text; a glyph without one cannot be mapped. (The empty text is a
        // text: the later glyphs of a one-to-many substitution carry it.)
        var run = Run("x");

        Refused(Tagged(Start(1, SemanticRole.Paragraph), run with { Glyphs = [run.Glyphs[0] with { Text = null! }] }, End(1)), profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_font_size_that_is_not_a_positive_finite_number_is_refused(string profile)
    {
        foreach (var size in new[] { double.NaN, 0, -1, double.PositiveInfinity })
        {
            Refused(Tagged(Start(1, SemanticRole.Paragraph), Run("x") with { FontSize = size }, End(1)), profile).Code
                .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel, $"font size {size}");
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_page_without_a_positive_finite_size_is_refused(string profile)
    {
        // A page's /MediaBox is a rectangle (ISO 32000-1 §14.11.2): a page of no area, or of negative or infinite size,
        // shows nothing any reader can lay out, whatever the profile; nor does a side below the smallest the writer
        // writes as more than 0 (it would be written as 0).
        foreach (var size in new[]
                 {
                     new Size(0, 0), new Size(-100, 100), new Size(100, -1), new Size(double.PositiveInfinity, 100), new Size(100, double.PositiveInfinity),
                     new Size(100, double.NaN), new Size(1e-7, 1e-7), new Size(100, 0.00009), new Size(0.00009, 100),
                 })
        {
            var document = new PageDocument(Accessible, [new Page(size, [Start(0, SemanticRole.Document), Start(1, SemanticRole.Paragraph), Run("x"), End(1), End(0)])]);

            Refused(document, profile).Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel, $"{size}");
        }
    }

    [Fact]
    public void A_page_of_the_smallest_side_is_written_with_that_side()
    {
        var side = PageModelRules.MinPageSide;
        var document = new PageDocument(Accessible, [new Page(new Size(side, side), [Start(0, SemanticRole.Document), End(0)])]);

        var written = PdfRenderer.Render(document, With(PdfProfile.Plain));

        written.IsSuccess.Should().BeTrue(written.IsFailure ? written.Error.ToString() : string.Empty);
        System.Text.Encoding.ASCII.GetString([.. written.Value]).Should().Contain("/MediaBox [0 0 0.0001 0.0001]");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void An_attachment_without_a_file_name_or_with_an_undefined_relationship_is_refused(string profile)
    {
        var attachment = new Attachment("data.xml", [.. "<a/>"u8], "application/xml", Here) { Relationship = AttachmentRelationship.Data };
        var page = Tagged(Start(1, SemanticRole.Paragraph), Run("x"), End(1));

        Refused(page with { Metadata = Accessible with { Attachments = [attachment with { FileName = " " }] } }, profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
        Refused(page with { Metadata = Accessible with { Attachments = [attachment with { Relationship = (AttachmentRelationship)42 }] } }, profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
        Refused(page with { Metadata = Accessible with { Attachments = [null!] } }, profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
        Refused(page with { Metadata = Accessible with { Attachments = [attachment with { MediaType = " " }] } }, profile).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel, "a blank media type is a missing one");
    }

    [Theory]
    [InlineData(5_000_000, 1)]
    [InlineData(1, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void A_cell_span_beyond_the_table_model_s_limits_is_refused_quickly(int columns, int rows)
    {
        // Under PDF/UA the regularity check fills a grid with the spans; one huge number made it allocate without bound.
        var document = Tagged(
            Start(1, SemanticRole.Table), Start(2, SemanticRole.TableRow),
            Start(3, SemanticRole.TableCell) with { Span = new CellSpan(columns, rows) }, Run("x"), End(3),
            End(2), End(1));
        var before = GC.GetAllocatedBytesForCurrentThread();

        Refused(document, With(PdfProfile.PdfUa1)).Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(64L * 1024 * 1024, "refusing a span takes no grid of its size");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(2000)]
    public void Rows_of_spans_at_the_limits_take_a_grid_no_wider_than_the_first_row(int count)
    {
        // Each row holds one cell spanning 1000 columns and 65 534 rows: valid spans. Each cell starts right of the cells
        // from the rows above, so a grid as wide as the widest row grows with the square of the rows. A regular grid
        // has the first row's width, so the check fills no wider grid than that.
        var rows = Enumerable.Range(0, count).SelectMany(row => new DrawItem[]
        {
            Start(10 + 2 * row, SemanticRole.TableRow),
            Start(11 + 2 * row, SemanticRole.TableCell) with { Span = new CellSpan(TableSlots.MaxColumnSpan, TableSlots.MaxRowSpan) },
            Run("x"), End(11 + 2 * row), End(10 + 2 * row),
        });
        var document = Tagged([Start(1, SemanticRole.Table), Start(2, SemanticRole.TableBody), .. rows, End(2), End(1)]);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var error = Refused(document, With(PdfProfile.PdfUa1));

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(64L * 1024 * 1024, "the grid is as wide as the first row");
        error.Parts().Select(part => part.Code).Should().OnlyContain(code => code == PdfRenderErrorCodes.Conformance.PdfUa1.IrregularTable);
    }

    /// <summary>A table whose first row covers 1 024 columns (two cells of 1 000 and 24) and <paramref name="rows"/> - 1
    /// rows of one cell of 1 000 columns follow: irregular, as the later rows leave 24 columns free.</summary>
    private static PageDocument WideTable(int rows)
    {
        var first = new DrawItem[]
        {
            Start(3, SemanticRole.TableRow),
            Start(4, SemanticRole.TableCell) with { Span = new CellSpan(1_000, 1) }, Run("x"), End(4),
            Start(5, SemanticRole.TableCell) with { Span = new CellSpan(24, 1) }, Run("x"), End(5),
            End(3),
        };
        var later = Enumerable.Range(0, rows - 1).SelectMany(row => new DrawItem[]
        {
            Start(100 + 2 * row, SemanticRole.TableRow), Start(101 + 2 * row, SemanticRole.TableCell) with { Span = new CellSpan(1_000, 1) },
            Run("x"), End(101 + 2 * row), End(100 + 2 * row),
        });
        return Tagged([Start(1, SemanticRole.Table), Start(2, SemanticRole.TableBody), .. first, .. later, End(2), End(1)]);
    }

    [Fact]
    public void A_table_of_exactly_the_slots_the_check_fills_is_checked_and_one_row_more_is_refused()
    {
        // 1 024 columns x 4 096 rows = 4 194 304 slots, the limit itself: the grid is filled and its rows are checked.
        Refused(WideTable(4_096), With(PdfProfile.PdfUa1)).Parts().Select(part => part.Code).Distinct()
            .Should().Equal([PdfRenderErrorCodes.Conformance.PdfUa1.IrregularTable], "at the limit the table is checked, not refused as too large");

        Refused(WideTable(4_097), With(PdfProfile.PdfUa1)).Code.Should().Be(PdfRenderErrorCodes.Conformance.PdfUa1.TableTooLarge);
    }

    [Fact]
    public void A_later_row_wider_than_the_first_is_irregular_at_its_extra_cell()
    {
        // The first row covers two columns; the second has three cells. The third reaches past the last column.
        var document = Tagged(
        [
            Start(1, SemanticRole.Table), Start(2, SemanticRole.TableBody),
            Start(10, SemanticRole.TableRow), Start(11, SemanticRole.TableCell), Run("a"), End(11), Start(12, SemanticRole.TableCell), Run("b"), End(12), End(10),
            Start(20, SemanticRole.TableRow), Start(21, SemanticRole.TableCell), Run("c"), End(21), Start(22, SemanticRole.TableCell), Run("d"), End(22),
            Start(23, SemanticRole.TableCell), Run("e"), End(23), End(20),
            End(2), End(1),
        ]);

        var error = Refused(document, With(PdfProfile.PdfUa1));

        error.Parts().Should().ContainSingle().Which.Should().Match<Error>(part =>
            part.Code == PdfRenderErrorCodes.Conformance.PdfUa1.IrregularTable && (string)part.Metadata![ErrorMetadataKeys.Node] == new NodeId(23).ToString());
    }

    [Fact]
    public void A_table_whose_grid_has_more_slots_than_the_check_fills_is_refused_without_filling_it()
    {
        // The first row covers 5 x 1000 columns, and 1000 rows follow: 5 million slots, past the 4 194 304 the check fills.
        var first = Enumerable.Range(0, 5).SelectMany(cell => new DrawItem[]
        {
            Start(20 + cell, SemanticRole.TableCell) with { Span = new CellSpan(TableSlots.MaxColumnSpan, 1) }, Run("x"), End(20 + cell),
        });
        var later = Enumerable.Range(0, 999).SelectMany(row => new DrawItem[]
        {
            Start(100 + 2 * row, SemanticRole.TableRow), Start(101 + 2 * row, SemanticRole.TableCell), Run("x"), End(101 + 2 * row), End(100 + 2 * row),
        });
        var document = Tagged([Start(1, SemanticRole.Table), Start(2, SemanticRole.TableBody), Start(3, SemanticRole.TableRow), .. first, End(3), .. later, End(2), End(1)]);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var error = Refused(document, With(PdfProfile.PdfUa1));

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(16L * 1024 * 1024);
        error.Code.Should().Be(PdfRenderErrorCodes.Conformance.PdfUa1.TableTooLarge);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("5000x1000");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void An_empty_or_unset_file_identifier_is_refused(string profile)
    {
        var document = Tagged(Start(1, SemanticRole.Paragraph), Run("x"), End(1));

        Refused(document, With(ProfileNamed(profile)) with { FileIdentifier = ImmutableArray<byte>.Empty }).Code
            .Should().Be(PdfErrorCodes.Write.InvalidFileIdentifier);
        Refused(document, With(ProfileNamed(profile)) with { FileIdentifier = default(ImmutableArray<byte>) }).Code
            .Should().Be(PdfErrorCodes.Write.InvalidFileIdentifier);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_file_identifier_longer_than_a_PDF_string_is_refused(string profile)
    {
        // veraPDF rule 6.1.13-3: under PDF/A no string is longer than 32 767 bytes; the writer holds the identifier to
        // that in every profile.
        var document = Tagged(Start(1, SemanticRole.Paragraph), Run("x"), End(1));
        var longest = With(ProfileNamed(profile)) with { FileIdentifier = [.. Enumerable.Repeat((byte)7, 32_767)] };

        Refused(document, longest with { FileIdentifier = [.. Enumerable.Repeat((byte)7, 32_768)] }).Code
            .Should().Be(PdfErrorCodes.Write.InvalidFileIdentifier);
        PdfRenderer.Render(document, longest).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_profile_with_an_undefined_PDF_A_level_is_refused() =>
        Refused(Tagged(Start(1, SemanticRole.Paragraph), Run("x"), End(1)), With(PdfProfile.Plain with { PdfA3 = (PdfA3Level)9 })).Code
            .Should().Be(PdfRenderErrorCodes.Write.InvalidOptions);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_glyph_id_must_be_below_the_font_s_glyph_count(string profile)
    {
        // Glyph ids are "sequential integers beginning at zero" and numGlyphs in 'maxp' is their number (OpenType
        // 'glyf'), so the glyph count itself is one past the last id.
        var run = Run("x");
        var last = run with { Glyphs = [run.Glyphs[0] with { Glyph = new GlyphId((ushort)(TestFonts.Regular.GlyphCount - 1)) }] };
        var past = run with { Glyphs = [run.Glyphs[0] with { Glyph = new GlyphId((ushort)TestFonts.Regular.GlyphCount) }] };

        Refused(Tagged(Start(1, SemanticRole.Paragraph), past, End(1)), profile).Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
        PdfRenderer.Render(Tagged(Start(1, SemanticRole.Paragraph), last, End(1)), With(ProfileNamed(profile))).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void A_cell_span_below_one_is_refused(int columns, int rows)
    {
        var document = Tagged(
            Start(1, SemanticRole.Table), Start(2, SemanticRole.TableBody), Start(3, SemanticRole.TableRow),
            Start(4, SemanticRole.TableCell) with { Span = new CellSpan(columns, rows) }, Run("x"), End(4),
            End(3), End(2), End(1));

        Refused(document).Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
    }

    [Fact]
    public void PDF_UA_refuses_cells_that_overlap()
    {
        // Row 1: a, then b spanning two rows. Row 2: c spanning two columns, which runs into the slot b covers.
        var document = Tagged(
            Start(1, SemanticRole.Table), Start(2, SemanticRole.TableBody),
            Start(3, SemanticRole.TableRow),
            Start(4, SemanticRole.TableCell), Run("a"), End(4),
            Start(5, SemanticRole.TableCell) with { Span = new CellSpan(1, 2) }, Run("b"), End(5),
            End(3),
            Start(6, SemanticRole.TableRow),
            Start(7, SemanticRole.TableCell) with { Span = new CellSpan(2, 1) }, Run("c"), End(7),
            End(6),
            End(2), End(1));

        Refused(document, With(PdfProfile.PdfUa1)).Code.Should().Be(PdfRenderErrorCodes.Conformance.PdfUa1.IrregularTable);
    }

    [Fact]
    public void PDF_UA_refuses_a_table_head_without_a_body()
    {
        var document = Tagged(
            Start(1, SemanticRole.Table),
            Start(2, SemanticRole.TableHead), Start(3, SemanticRole.TableRow), Start(4, SemanticRole.TableHeaderCell), Run("h"), End(4), End(3), End(2),
            End(1));

        Refused(document, With(PdfProfile.PdfUa1)).Code.Should().Be(PdfRenderErrorCodes.Conformance.PdfUa1.TableHeadWithoutBody);
        PdfRenderer.Render(document, With(PdfProfile.PdfA3a)).IsSuccess.Should().BeTrue("the rule is PDF/UA's");
    }

    [Fact]
    public void The_page_model_s_number_range_is_the_PDF_writer_s()
    {
        PageModelRules.MaxNumber.Should().Be(PdfSerializer.MaxReal);
    }

    [Fact]
    public void A_number_beyond_the_range_of_a_PDF_real_is_a_breach_of_the_contract()
    {
        // Before the shared range rule the writer refused it later, as Documents.Pdf.Write.InvalidNumber.
        var page = new Page(new Size(100, 100), [new FillRectangle(new Rect(new Point(10, 10), new Size(10, 1.5e308)), Color.Black)]);

        var result = PdfRenderer.Render(new PageDocument(new DocumentMetadata("t", null), [page]), With(PdfProfile.Plain));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PdfRenderErrorCodes.Write.InvalidPageModel);
    }
}
