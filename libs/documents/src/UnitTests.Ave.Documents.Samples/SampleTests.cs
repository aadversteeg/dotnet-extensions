using System.Collections.Immutable;
using Ave.Documents.Fluent;
using Ave.Documents.Layout;
using Ave.Documents.Model;
using Ave.Documents.Page;
using Ave.Documents.Render.Pdf;
using Ave.Documents.TestSupport;
using Samples;

namespace UnitTests.Ave.Documents.Samples;

/// <summary>
/// The two stage-0 samples end to end: golden files for the three text serialisations, byte-identical output on
/// repeated runs, and the text read back with an independent PDF reader (PdfPig).
/// </summary>
public class SampleTests
{
    private static readonly PdfRenderOptions Options = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)));

    private sealed record Stages(DocumentModel Model, ImmutableArray<PageFragment> Fragments, PageDocument Pages, ImmutableArray<byte> Pdf);

    private static Stages Run(Document document)
    {
        var model = DocumentTranslator.ToModel(document).Value;
        var fragments = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), model).Value;
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
        Run(SampleDocuments.LongText()).Pages.Pages.Length.Should().BeGreaterThan(3);
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
