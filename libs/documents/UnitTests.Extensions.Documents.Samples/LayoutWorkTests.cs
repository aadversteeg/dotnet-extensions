using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.Functional;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// The work the layout of three documents takes, counted, not timed, so the test holds on a loaded machine: the long
/// timesheet through the code front-end and through HTML (five pages, "page x of y" in the page areas only), and a
/// text-heavy HTML document (paragraphs with spans, links and inline-blocks). For each:
/// <list type="bullet">
/// <item>the bytes the layout allocates per page, on the engine's own environment: deterministic on one runtime, so the
/// bound is the measured value times 1.5. A second body pass (about twice the bytes) or a copy per glyph or per line in the
/// inner loops passes it. What moves it legitimately: a runtime with other collection sizes, another font, or a layout
/// rule that adds work; measure again and write the new value with the reason.</item>
/// <item>the calls of the inline algorithm per paragraph or cell, through counting wrappers around the engine's
/// algorithms. Each layout call shapes the box's runs once and breaks its lines once; each measurement shapes them once
/// and breaks them twice (min-content and max-content), so these counts are the shaping and line-breaking calls. An
/// environment with wrappers is not the engine's own, so the paginator lays the body out in every pass of the page-count
/// fixpoint there (<see cref="Paginator.Paginate"/>): the counts are per pass, and the passes are checked.</item>
/// </list>
/// </summary>
public class LayoutWorkTests(ITestOutputHelper output)
{
    private static readonly PageStyle Letter = new(Length.FromPoints(612), Length.FromPoints(792), default);

    // Measured (Debug build, .NET 10): bytes a page 41.9 M, 40.7 M and 16.5 M; inline layouts a box a pass 1.15, 1.13
    // and 1.17; measurements 1.05, 1.03 and 0.93. Each bound is 1.5 times its value.
    [Theory]
    [InlineData("timesheet-long-code", 5, 2, 62_900_000, 1.73, 1.58)]
    [InlineData("timesheet-long-html", 5, 2, 61_100_000, 1.70, 1.55)]
    [InlineData("text", 3, 1, 24_700_000, 1.76, 1.40)]
    public void The_layout_of_a_document_takes_bounded_work(
        string name, int pages, int passes, long bytesPerPage, double layoutsPerBox, double measuresPerBox)
    {
        var (document, fonts) = Document(name);

        // Allocation, on the engine's own environment; a first layout warms the fonts' tables, the second is measured.
        var environment = LayoutEnvironment.Create(fonts);
        Paginator.Paginate(environment, document).IsSuccess.Should().BeTrue();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var laidOut = Paginator.Paginate(environment, document).Value;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Work, through counting wrappers.
        var (bodyLayouts, inlineLayouts, inlineMeasures, inlineBoxes) = Work(environment, document);

        var perPass = (Layouts: (double)inlineLayouts / passes / inlineBoxes, Measures: (double)inlineMeasures / passes / inlineBoxes);
        output.WriteLine($"{name}: {laidOut.Count} pages, {allocated / laidOut.Count:N0} bytes a page; counting: {bodyLayouts} body layouts, "
            + $"{inlineBoxes} inline boxes, {inlineLayouts} layouts and {inlineMeasures} measurements of them ({perPass.Layouts:F2} and {perPass.Measures:F2} a box a pass)");
        laidOut.Should().HaveCount(pages);
        (allocated / laidOut.Count).Should().BeLessThan(bytesPerPage, "the bytes allocated per page");
        bodyLayouts.Should().Be(passes * pages, "the body is laid out once per page in each pass");
        perPass.Layouts.Should().BeLessThan(layoutsPerBox, "the inline layouts per paragraph or cell in one pass");
        perPass.Measures.Should().BeLessThan(measuresPerBox, "the inline measurements per paragraph or cell in one pass");
    }

    /// <summary>The body layouts (calls for the document's root box), and the inline algorithm's layout and measure calls
    /// and the boxes they were made for (by node: a front-end's box may be rebuilt for a call), in one pagination.</summary>
    private static (int BodyLayouts, int InlineLayouts, int InlineMeasures, int InlineBoxes) Work(LayoutEnvironment environment, StyledDocument document)
    {
        var (bodyLayouts, layouts, measures) = (0, 0, 0);
        var boxes = new HashSet<NodeId>();
        var flow = environment.Algorithms[LayoutAlgorithmId.Flow];
        var inline = environment.Algorithms[LayoutAlgorithmId.Inline];
        var counting = environment with
        {
            Algorithms = environment.Algorithms
                .SetItem(LayoutAlgorithmId.Flow, new RegisteredAlgorithm(
                    (env, box, space, incoming) =>
                    {
                        bodyLayouts += ReferenceEquals(box, document.Root) ? 1 : 0;
                        return flow.Layout(env, box, space, incoming);
                    },
                    flow.Measure))
                .SetItem(LayoutAlgorithmId.Inline, new RegisteredAlgorithm(
                    (env, box, space, incoming) =>
                    {
                        layouts++;
                        boxes.Add(box.Id);
                        return inline.Layout(env, box, space, incoming);
                    },
                    (env, box) =>
                    {
                        measures++;
                        boxes.Add(box.Id);
                        return inline.Measure(env, box);
                    })),
        };

        Paginator.Paginate(counting, document).IsSuccess.Should().BeTrue();
        return (bodyLayouts, layouts, measures, boxes.Count);
    }

    private static (StyledDocument Document, FontSet Fonts) Document(string name) => name switch
    {
        "timesheet-long-code" => (DocumentTranslator.ToModel(SampleFiles.Timesheet("long", "nl-NL")).Value, TestFonts.SetWithItalic),
        "timesheet-long-html" => Html(File.ReadAllText(Path.Combine(Family(), "tools", "reference", "chromium", "documents", "timesheet-pages.html")), "timesheet-pages"),
        _ => Html(TextHeavy(), "text"),
    };

    /// <summary>The boxes of an HTML document, on the reference tool's paper, with its fonts.</summary>
    private static (StyledDocument Document, FontSet Fonts) Html(string html, string name)
    {
        var baseUrl = new Uri(Path.Combine(Family(), "tools", "reference", "chromium", "documents", name + ".html")).AbsoluteUri;
        var boxes = HtmlParser.Parse(html, new HtmlParseOptions(name + ".html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Page = Letter, Resources = Resource, BaseUrl = baseUrl }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Resources = Resource, BaseUrl = baseUrl, MissingCharacters = Shaper.MissingCharacters }))
            .Value;
        return (boxes.Document, boxes.Fonts);
    }

    /// <summary>Twelve paragraphs of 110 words, every few words a bordered span, a link, a bold word or an inline-block,
    /// in Lato from the family's fonts: three pages.</summary>
    private static string TextHeavy()
    {
        var fonts = Path.Combine(Family(), "assets", "fonts", "lato");
        var text = new StringBuilder("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Text</title><style>\n");
        foreach (var (file, weight, style) in new[] { ("Lato-Regular.ttf", 400, "normal"), ("Lato-Bold.ttf", 700, "normal"), ("Lato-Italic.ttf", 400, "italic") })
        {
            text.Append($"@font-face {{ font-family: Lato; src: url({new Uri(Path.Combine(fonts, file)).AbsoluteUri}); font-weight: {weight}; font-style: {style}; }}\n");
        }

        text.Append("@page { size: A4; margin: 2cm; }\nbody { font-family: Lato; font-size: 11pt; line-height: 1.4; margin: 0; }\n")
            .Append("span.tag { background: #ddebf7; border: 1px solid #8ab; padding: 0 2px; }\n.ib { display: inline-block; border: 1px solid #888; padding: 1px 3px; }\na { color: #036; }\n")
            .Append("</style></head><body>\n");
        var words = "the engine lays out text in lines and pages with shaping kerning and justification of every word in a paragraph".Split(' ');
        var random = new Random(7);
        for (var paragraph = 0; paragraph < 12; paragraph++)
        {
            text.Append("<p>");
            for (var index = 0; index < 110; index++)
            {
                var word = words[random.Next(words.Length)];
                text.Append((index % 23) switch
                {
                    5 => $"<span class=\"tag\">{word}</span> ",
                    11 => $"<a href=\"https://example.org/{paragraph}/{index}\">{word} link</a> ",
                    17 => $"<strong>{word}</strong> ",
                    20 => $"<span class=\"ib\">{word}</span> ",
                    _ => word + " ",
                });
            }

            text.Append("</p>\n");
        }

        return text.Append("</body></html>").ToString();
    }

    /// <summary>The files under the family folder, by file URL: the documents' fonts and images.</summary>
    private static HtmlResource? Resource(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile && Path.GetFullPath(uri.LocalPath) is var path
            && path.StartsWith(Family(), StringComparison.Ordinal) && File.Exists(path)
            ? new HtmlResource(File.ReadAllBytes(path))
            : null;

    /// <summary>The family folder in the source tree.</summary>
    private static string Family([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, ".."));
}
