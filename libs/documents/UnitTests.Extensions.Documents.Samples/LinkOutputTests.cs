using System.Collections.Immutable;
using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Raster;
using Ave.Extensions.Documents.Samples;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// A link to a URI through every back-end: in PDF one link annotation per link area, with its rectangle, flags,
/// description and URI action; one Link element that holds its content and an object reference to each annotation; the
/// annotations' parent-tree entries naming that element; veraPDF passes PDF/A-3a and PDF/UA-1; the raster back-end
/// draws a link as its text.
/// </summary>
public class LinkOutputTests
{
    private const string Terms = "https://example.com/terms?order=2026-001";

    private const string TermsText = "the terms and conditions that apply to this order";

    /// <summary>A link that runs over the end of its line onto the next.</summary>
    private static Document OverTwoLines(bool asLink = true) =>
        Document(
            [Paragraph([Text("Please read "), asLink ? Link(Terms, TermsText) : Text(TermsText), Text(" before you pay.")])],
            Page(300.Pt(), 200.Pt()).Margin(20.Pt()),
            title: "Links",
            language: "en").FontFamily("Lato");

    /// <summary>A link whose lines are on two pages: four lines fill most of the first page, the link's three lines
    /// follow, one of them on the first page.</summary>
    private static Document OverAPageBreak() =>
        Document(
            [
                Paragraph("One\u2028two\u2028three\u2028four"),
                Paragraph([Text("See "), Link("mailto:info@example.com", "our\u2028mail\u2028address")]).Orphans(1).Widows(1),
            ],
            Page(300.Pt(), 125.Pt()).Margin(20.Pt()),
            title: "Links over a page break",
            language: "en").FontFamily("Lato");

    private static ImmutableArray<(string Name, Document Document)> Cases { get; } = [("link-two-lines", OverTwoLines()), ("link-page-break", OverAPageBreak())];

    private static PageDocument Pages(Document document) =>
        DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).Value;

    private static ImmutableArray<byte> Pdf(Document document, PdfProfile? profile = null) =>
        SampleFiles.Pdf(document, profile is null ? null : SampleOutput.Options with { Profile = profile });

    /// <summary>The objects of a PDF, read back with PdfPig.</summary>
    private sealed class ReadBack : IDisposable
    {
        private readonly UglyToad.PdfPig.PdfDocument _document;

        public ReadBack(ImmutableArray<byte> pdf) => _document = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray());

        public int PageCount => _document.NumberOfPages;

        public DictionaryToken Page(int number) => _document.GetPage(number).Dictionary;

        /// <summary>The reference of a page (1-based), from the flat page tree the writer makes.</summary>
        public IndirectReference PageReference(int number)
        {
            var pages = Dictionary(_document.Structure.Catalog.CatalogDictionary.Data["Pages"]);
            return ((IndirectReferenceToken)((ArrayToken)pages.Data["Kids"]).Data[number - 1]).Data;
        }

        public IToken Resolve(IToken token) => token is IndirectReferenceToken reference ? _document.Structure.GetObject(reference.Data).Data : token;

        public DictionaryToken Dictionary(IToken token) => (DictionaryToken)Resolve(token);

        /// <summary>The annotation references of a page, in order; empty without /Annots.</summary>
        public IReadOnlyList<IndirectReference> Annotations(int number) =>
            Page(number).TryGet(NameToken.Create("Annots"), out var annots)
                ? [.. ((ArrayToken)Resolve(annots)).Data.Select(item => ((IndirectReferenceToken)item).Data)]
                : [];

        public DictionaryToken? StructureTreeRoot =>
            _document.Structure.Catalog.CatalogDictionary.TryGet(NameToken.Create("StructTreeRoot"), out var root) ? Dictionary(root) : null;

        /// <summary>Every structure element with its reference, depth-first.</summary>
        public IEnumerable<(IndirectReference Reference, DictionaryToken Element)> Elements()
        {
            var pending = new Stack<IToken>(Kids(StructureTreeRoot!).Reverse());
            while (pending.Count > 0)
            {
                if (pending.Pop() is IndirectReferenceToken reference && Resolve(reference) is DictionaryToken element && element.ContainsKey(NameToken.Create("S")))
                {
                    yield return (reference.Data, element);
                    foreach (var kid in Kids(element).Reverse())
                    {
                        pending.Push(kid);
                    }
                }
            }
        }

        public IEnumerable<IToken> Kids(DictionaryToken element) =>
            !element.TryGet(NameToken.Create("K"), out var k) ? []
            : Resolve(k) is ArrayToken array ? array.Data
            : [k];

        public void Dispose() => _document.Dispose();
    }

    private static string StringOf(ReadBack read, DictionaryToken dictionary, string key)
    {
        var token = read.Resolve(dictionary.Data[key]);
        return token is StringToken text ? text.Data : ((HexToken)token).Data;
    }

    private static double Number(ReadBack read, IToken token) => ((NumericToken)read.Resolve(token)).Double;

    [Fact]
    public void Each_link_area_is_a_link_annotation_with_its_rectangle_flags_description_and_uri_action()
    {
        var pages = Pages(OverTwoLines());
        var areas = pages.Pages[0].Items.OfType<LinkArea>().ToList();
        areas.Should().HaveCount(2, "the link runs over two lines");
        using var read = new ReadBack(Pdf(OverTwoLines()));

        var annotations = read.Annotations(1);
        annotations.Should().HaveCount(2);
        for (var index = 0; index < annotations.Count; index++)
        {
            var annotation = read.Dictionary(new IndirectReferenceToken(annotations[index]));
            annotation.Data["Subtype"].Should().Be(NameToken.Create("Link"));
            Number(read, annotation.Data["F"]).Should().Be(4, "Print set, Hidden, Invisible, NoView and ToggleNoView clear");
            ((ArrayToken)annotation.Data["Border"]).Data.Select(item => Number(read, item)).Should().Equal(0, 0, 0);
            StringOf(read, annotation, "Contents").Should().Be(TermsText, "the description is the link's text");
            var action = read.Dictionary(annotation.Data["A"]);
            action.Data["S"].Should().Be(NameToken.Create("URI"));
            StringOf(read, action, "URI").Should().Be(Terms);
            annotation.ContainsKey(NameToken.Create("AP")).Should().BeFalse("a link needs no appearance stream (veraPDF rule 6.3.3-1)");
            Number(read, annotation.Data["StructParent"]).Should().Be(1 + index, "the annotations' keys follow the one page's key");

            var area = areas[index].Area;
            var height = pages.Pages[0].Size.Height;
            ((ArrayToken)annotation.Data["Rect"]).Data.Select(item => Number(read, item)).Should().Equal(
                [area.Origin.X, height - area.Origin.Y - area.Size.Height, area.Origin.X + area.Size.Width, height - area.Origin.Y],
                (written, expected) => Math.Abs(written - expected) < 1e-4);
        }
    }

    [Fact]
    public void The_link_element_holds_its_content_and_an_object_reference_to_each_annotation_and_the_parent_tree_names_it()
    {
        var pdf = Pdf(OverAPageBreak());
        using var read = new ReadBack(pdf);
        read.PageCount.Should().Be(2);
        var onFirst = read.Annotations(1);
        var onSecond = read.Annotations(2);
        onFirst.Should().HaveCount(1);
        onSecond.Should().HaveCount(2);

        var links = read.Elements().Where(element => element.Element.Data["S"].Equals(NameToken.Create("Link"))).ToList();
        var link = links.Should().ContainSingle("a link over two pages is one element").Which;
        var references = read.Kids(link.Element).Select(read.Resolve).OfType<DictionaryToken>()
            .Where(kid => kid.TryGet(NameToken.Create("Type"), out var type) && type.Equals(NameToken.Create("OBJR"))).ToList();
        references.Select(reference => ((IndirectReferenceToken)reference.Data["Obj"]).Data).Should().Equal([.. onFirst, .. onSecond]);
        references.Select(reference => ((IndirectReferenceToken)reference.Data["Pg"]).Data).Should().Equal(read.PageReference(1), read.PageReference(2), read.PageReference(2));
        read.Kids(link.Element).Should().Contain(kid => kid is NumericToken, "the link's text is its content too");

        var root = read.StructureTreeRoot!;
        Number(read, root.Data["ParentTreeNextKey"]).Should().Be(2 + 3, "two pages' keys, then three annotations' keys");
        var nums = ((ArrayToken)read.Resolve(read.Dictionary(root.Data["ParentTree"]).Data["Nums"])).Data;
        var entries = Enumerable.Range(0, nums.Count / 2).ToDictionary(index => (int)Number(read, nums[2 * index]), index => nums[2 * index + 1]);
        foreach (var (annotation, index) in onFirst.Concat(onSecond).Select((annotation, index) => (annotation, index)))
        {
            var key = (int)Number(read, read.Dictionary(new IndirectReferenceToken(annotation)).Data["StructParent"]);
            key.Should().Be(2 + index);
            ((IndirectReferenceToken)entries[key]).Data.Should().Be(link.Reference, "an annotation's parent-tree entry is its link's element itself");
        }

        PdfStructureReadBack.ParentTreeDifferences(pdf).Should().BeEmpty();
        PdfStructureReadBack.Outline(PdfStructureReadBack.Read(pdf)).Should().Contain("  P \"See \"\n    Link \"ourmailaddress\"\n", "the forced breaks between the lines are no text");
    }

    [Fact]
    public void An_untagged_file_has_the_annotations_with_their_description_and_no_structure()
    {
        using var read = new ReadBack(Pdf(OverTwoLines(), PdfProfile.PdfA3b));

        read.StructureTreeRoot.Should().BeNull();
        var annotation = read.Dictionary(new IndirectReferenceToken(read.Annotations(1)[0]));
        annotation.ContainsKey(NameToken.Create("StructParent")).Should().BeFalse();
        StringOf(read, annotation, "Contents").Should().Be(TermsText);
    }

    [Fact]
    public void A_link_whose_text_is_blank_is_described_by_its_uri()
    {
        var document = Document([Paragraph([Text("x"), Link("tel:+31201234567", " ")])], Page(300.Pt(), 200.Pt()), title: "Blank", language: "en").FontFamily("Lato");

        using var read = new ReadBack(Pdf(document));

        var annotations = read.Annotations(1);
        annotations.Should().BeEmpty("a link whose text collapses away has no part on any line");
        var model = DocumentTranslator.ToModel(document).Value;
        Ave.Extensions.Documents.DocumentModel.DocumentModelText.Write(model).Should().Contain("description=\"tel:+31201234567\"");
    }

    [Fact]
    public void The_raster_back_end_draws_a_link_as_its_text()
    {
        var linked = OverTwoLines().ToPng(TestFonts.SetWithItalic, 1, new RasterOptions(96)).Value;
        var plain = OverTwoLines(asLink: false).ToPng(TestFonts.SetWithItalic, 1, new RasterOptions(96)).Value;

        linked.Should().Equal(plain);
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void Links_over_two_lines_and_over_a_page_break_pass_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-links-").FullName;
        try
        {
            foreach (var (name, document) in Cases)
            {
                File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), [.. SampleFiles.Pdf(document)]);
            }

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().HaveCount(Cases.Length);
            results.Should().OnlyContain(result => result.IsCompliant && result.PassedChecks > 0, VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
