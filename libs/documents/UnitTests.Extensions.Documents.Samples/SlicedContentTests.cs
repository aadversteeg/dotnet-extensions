using System.Collections.Immutable;
using Ave.Extensions.Documents.TestSupport;
using UglyToad.PdfPig;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// Sample C, content taller than the page sliced over pages (<c>MonolithicOverflow.Slice</c>), read back with PdfPig as
/// PDF/A-3a + PDF/UA-1: every slice is clipped to its part of the page; every slice of a logo is content of its figure,
/// and the slices of the line after the first are pagination artifacts, so a reader that follows the tags reads the
/// line's text and each logo's alternative text once. One logo is in a table, below its repeated header row. veraPDF's
/// verdict on the sample is in <see cref="ReferenceValidationTests"/>.
/// </summary>
public class SlicedContentTests
{
    private static ImmutableArray<byte> Sliced() => SampleFiles.Pdf(SampleFiles.Documents.Single(sample => sample.Name == "sample-c-sliced").Document);

    [Fact]
    public void The_sliced_line_is_tagged_content_once_and_an_artifact_on_the_next_page()
    {
        using var pdf = PdfDocument.Open([.. Sliced()]);
        var pages = pdf.GetPages().ToList();

        // Every glyph is inside a marked-content sequence (ISO 14289-1 §7.1); the 200 pt W is drawn on two pages, as
        // content on the first and as an artifact on the second.
        foreach (var page in pages)
        {
            page.GetMarkedContents().Sum(content => content.Letters.Count).Should().Be(page.Letters.Count);
        }

        var drawn = pages.SelectMany(page => page.GetMarkedContents().SelectMany(content => Flatten(content))
            .SelectMany(content => content.Letters.Where(letter => letter.Value == "W" && letter.PointSize > 100).Select(_ => (page.Number, content.IsArtifact))))
            .ToList();
        drawn.Should().HaveCount(2);
        drawn.Select(entry => entry.IsArtifact).Should().Equal(false, true);
    }

    [Fact]
    public void The_text_outside_artifacts_holds_every_word_once()
    {
        using var pdf = PdfDocument.Open([.. Sliced()]);
        var tagged = string.Concat(pdf.GetPages().SelectMany(page => page.GetMarkedContents().SelectMany(content => Flatten(content))
            .Where(content => !content.IsArtifact)
            .SelectMany(content => content.Letters.Select(letter => letter.Value))));

        tagged.Replace(" ", string.Empty).Should().Be(
            "Tallerthanapage"
            + "Thelogobelowistallerthanthepagearea.Itstartsonthenextpageandisslicedovertwopages."
            + "Thelinebelowissetin200pointtype,alsotallerthanthepagearea."
            + "W"
            + "Thetablebelowrepeatsitsheaderrowaboveeachsliceofthelogoinit."
            + "Logo"
            + "Theend.");
    }

    [Fact]
    public void Each_logo_is_one_figure_with_its_alternative_text_once()
    {
        var elements = PdfStructureReadBack.Read(Sliced()).Single().DepthFirst().ToList();

        elements.Where(element => element.Type == "Figure").Select(element => element.Details)
            .Should().Equal("Alt=\"A white letter V on an orange square\"", "Alt=\"The same logo in a table\"");
        elements.Count(element => element.Details.Contains("Alt=", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public void Every_slice_of_a_logo_is_tagged_content_on_its_page()
    {
        // Each page shows another part of the logo: marked content of the figure on every page, never an artifact.
        using var pdf = PdfDocument.Open([.. Sliced()]);
        var images = pdf.GetPages().SelectMany(page => page.GetMarkedContents().SelectMany(content => Flatten(content))
            .Where(content => content.Images.Any())
            .Select(content => (page.Number, content.IsArtifact, content.Tag))).ToList();

        images.Should().HaveCountGreaterThanOrEqualTo(4);
        images.Should().AllSatisfy(image => image.IsArtifact.Should().BeFalse($"page {image.Number}"));
        images.Select(image => image.Tag).Distinct().Should().Equal("Figure");
    }

    [Fact]
    public void Each_page_clips_its_slice()
    {
        // The content streams hold one clip per slice: "re W n" between "q" and "Q".
        using var pdf = PdfDocument.Open([.. Sliced()]);
        var clips = pdf.GetPages().Select(page => System.Text.Encoding.ASCII.GetString(page.Operations
            .Select(operation => System.Text.Encoding.ASCII.GetBytes(operation.Operator + "\n")).SelectMany(bytes => bytes).ToArray()))
            .Select(operators => System.Text.RegularExpressions.Regex.Matches(operators, "re\nW\nn\n").Count)
            .ToList();

        clips.Sum().Should().Be(6, "each logo and the line have two slices");
    }

    private static IEnumerable<UglyToad.PdfPig.Content.MarkedContentElement> Flatten(UglyToad.PdfPig.Content.MarkedContentElement element) =>
        element.Children.SelectMany(Flatten).Prepend(element);
}
