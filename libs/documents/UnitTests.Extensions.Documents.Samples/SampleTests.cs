using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.Samples;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// The letter and the long-text samples end to end: golden files for the three text serialisations, byte-identical output on
/// repeated runs, and the text read back with an independent PDF reader (PdfPig).
/// </summary>
public class SampleTests
{
    private static readonly PdfRenderOptions Options = SampleOutput.Options;

    private sealed record Stages(StyledDocument Model, ValueList<PageFragment> Fragments, PageDocument Pages, ImmutableArray<byte> Pdf);

    private static Stages Run(Document document)
    {
        var model = DocumentTranslator.ToModel(document).Value;
        var fragments = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.SetWithItalic), model).Value;
        var pages = PagePainter.Paint(model.Metadata, fragments);
        return new Stages(model, fragments, pages, PdfRenderer.Render(pages, Options).Value);
    }

    [Fact]
    public void Letter_serialisations_match_their_golden_files()
    {
        var letter = Run(SampleDocuments.Letter());

        Golden.Compare("letter.model.txt", DocumentModelText.Write(letter.Model)).Should().BeNull();
        Golden.Compare("letter.fragments.txt", FragmentText.Write(letter.Fragments)).Should().BeNull();
        Golden.Compare("letter.page.txt", PageModelText.Write(letter.Pages)).Should().BeNull();
    }

    [Fact]
    public void Long_text_fragment_tree_matches_its_golden_file() =>
        Golden.Compare("long.fragments.txt", FragmentText.Write(Run(SampleDocuments.LongText()).Fragments)).Should().BeNull();

    [Fact]
    public void The_letter_has_two_pages_and_the_long_text_more_than_three()
    {
        Run(SampleDocuments.Letter()).Pages.Pages.Should().HaveCount(2);
        Run(SampleDocuments.LongText()).Pages.Pages.Count.Should().BeGreaterThan(3);
    }

    [Fact]
    public void Two_builds_of_the_same_document_are_equal_values_at_every_stage()
    {
        // Documents, document models, fragment trees and page models are values: equal content, equal value.
        var first = Run(SampleDocuments.Letter());
        var second = Run(SampleDocuments.Letter());

        (SampleDocuments.Letter() == SampleDocuments.Letter()).Should().BeTrue();
        (first.Model == second.Model).Should().BeTrue();
        (first.Fragments == second.Fragments).Should().BeTrue();
        (first.Pages == second.Pages).Should().BeTrue();
    }

    [Fact]
    public void A_changed_leaf_makes_the_trees_unequal_at_every_stage()
    {
        var original = Run(SampleDocuments.Letter());
        var changed = Run(SampleDocuments.Letter(closing: "Kind regards!"));

        (SampleDocuments.Letter() == SampleDocuments.Letter(closing: "Kind regards!")).Should().BeFalse();
        (original.Model == changed.Model).Should().BeFalse();
        (original.Fragments == changed.Fragments).Should().BeFalse();
        (original.Pages == changed.Pages).Should().BeFalse();
    }

    [Fact]
    public void Subsetting_keeps_both_samples_far_below_100_kilobytes()
    {
        // With the whole fonts embedded the files were 715,081 and 720,324 bytes.
        Run(SampleDocuments.Letter()).Pdf.Length.Should().BeLessThan(100_000);
        Run(SampleDocuments.LongText()).Pdf.Length.Should().BeLessThan(100_000);
    }

    [Fact]
    public void Two_runs_give_byte_identical_pdfs()
    {
        Run(SampleDocuments.Letter()).Pdf.SequenceEqual(Run(SampleDocuments.Letter()).Pdf).Should().BeTrue();
        Run(SampleDocuments.LongText()).Pdf.SequenceEqual(Run(SampleDocuments.LongText()).Pdf).Should().BeTrue();
    }

    [Theory]
    [InlineData("letter")]
    [InlineData("long")]
    public void Every_glyph_reads_back_from_the_pdf_where_the_page_model_put_it(string sample)
    {
        var stages = Run(sample == "letter" ? SampleDocuments.Letter() : SampleDocuments.LongText());

        PdfReadBack.Differences(stages.Pages, stages.Pdf).Should().BeEmpty();
    }

    [Theory]
    [InlineData("sample-a-letter")]
    [InlineData("timesheet-normal.nl")]
    [InlineData("timesheet-long.en")]
    public void Every_rectangle_and_image_reads_back_in_its_place_size_and_colour(string name)
    {
        // Backgrounds, grid lines and the logo, read back by PdfPig: a moved rectangle, swapped colour channels or an
        // image drawn at the wrong size fail here, which no text check and no validator notices.
        var stages = Run(SampleFiles.Documents.Single(sample => sample.Name == name).Document);

        stages.Pages.Pages.SelectMany(page => page.Items).OfType<global::Ave.Extensions.Documents.PageModel.FillRectangle>().Should().NotBeEmpty();
        PdfReadBack.AreaDifferences(stages.Pages, stages.Pdf).Should().BeEmpty();
    }

    [Theory]
    [InlineData("letter")]
    [InlineData("long")]
    public void The_text_read_back_is_the_input_text(string sample)
    {
        var document = sample == "letter" ? SampleDocuments.Letter() : SampleDocuments.LongText();
        var stages = Run(document);

        // Line breaking legitimately removes the spaces at line ends and collapses runs of white space, so the
        // comparison is on everything except white space, in reading order.
        var input = string.Concat(Spans(document.Body).Select(span => span.Text)).Where(character => !char.IsWhiteSpace(character));
        var readBack = PdfReadBack.Letters(stages.Pdf).SelectMany(page => page).SelectMany(letter => letter.Value).Where(character => !char.IsWhiteSpace(character));
        new string([.. readBack]).Should().Be(new string([.. input]));
    }

    private static IEnumerable<Span> Spans(IEnumerable<Element> elements) =>
        elements.SelectMany(element => element switch
        {
            BlockElement block => Spans(block.Children),
            ParagraphElement paragraph => paragraph.Spans,
            _ => [],
        });
}
