using System.Collections.Immutable;
using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using UglyToad.PdfPig.Tokens;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Box generation of <c>th</c> (THE RULE FOR A HEADER CELL'S SCOPE IN HTML): the <c>scope</c> attribute in any ASCII
/// case, the auto state by the cell's row and row group, and a table with a header row, a column of row headers and two
/// explicit scopes against the auto state, read back from a tagged PDF (the <c>/Scope</c> of each <c>TH</c>) that veraPDF
/// passes.</summary>
public class TableHeaderScopeTests
{
    private const string Head = "<!doctype html><html lang=\"en\"><head><title>Hours</title><style>body { font-family: Lato; font-size: 10.5pt; margin: 0 } table { border-collapse: collapse }</style></head><body>";

    /// <summary>A header row with one explicit row scope, two body rows with a row header each (the second with an
    /// explicit column scope), and a body row of header cells only.</summary>
    private const string Hours =
        "<table><thead><tr><th>Name</th><th>Hours</th><th scope=\"row\">Rate</th></tr></thead>"
        + "<tbody><tr><th>Alice</th><td>8</td><td>10</td></tr>"
        + "<tr><th scope=\"col\">Bob</th><td>6</td><td>12</td></tr>"
        + "<tr><th>Total</th><th>14</th><th>22</th></tr></tbody></table>";

    private static readonly TableHeaderScope[] HoursScopes =
    [
        TableHeaderScope.Column, TableHeaderScope.Column, TableHeaderScope.Row,
        TableHeaderScope.Row,
        TableHeaderScope.Column,
        TableHeaderScope.Column, TableHeaderScope.Column, TableHeaderScope.Column,
    ];

    private static Result<HtmlBoxes, Error> Boxes(string body) =>
        HtmlParser.Parse(Head + body + "</body></html>", new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Page = HtmlCases.Letter }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic }));

    /// <summary>The scope of every header cell, in document order.</summary>
    private static List<TableHeaderScope> Scopes(string body)
    {
        var result = Boxes(body);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var scopes = new List<TableHeaderScope>();
        var pending = new Stack<Node>([result.Value.Document.Root]);
        while (pending.Count > 0)
        {
            if (pending.Pop() is Box box)
            {
                if (box.Role is SemanticRole.TableHeaderCellRole header)
                {
                    scopes.Add(header.Scope);
                }

                foreach (var child in box.Children.Reverse())
                {
                    pending.Push(child);
                }
            }
        }

        return scopes;
    }

    private static ImmutableArray<byte> TaggedPdf(string body) =>
        Boxes(body)
            .OnSuccessBind(boxes => DocumentLayout.Run(boxes.Document, boxes.Fonts))
            .OnSuccessBind(pages => pages.ToPdf(new PdfRenderOptions(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) { Profile = PdfProfile.PdfA3a.AndPdfUa1() }))
            .Value;

    [Theory]
    [InlineData("row", TableHeaderScope.Row)]
    [InlineData("ROW", TableHeaderScope.Row)]
    [InlineData("rowgroup", TableHeaderScope.Row)]
    [InlineData("RowGroup", TableHeaderScope.Row)]
    [InlineData("col", TableHeaderScope.Column)]
    [InlineData("Col", TableHeaderScope.Column)]
    [InlineData("colgroup", TableHeaderScope.Column)]
    public void An_explicit_scope_in_any_ASCII_case_decides_also_in_a_row_of_header_cells(string scope, TableHeaderScope expected)
    {
        Scopes($"<table><tbody><tr><th scope=\"{scope}\">A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></tbody></table>")
            .Should().Equal(expected, TableHeaderScope.Column);
        Scopes($"<table><tbody><tr><th scope=\"{scope}\">A</th><td>1</td></tr></tbody></table>").Should().Equal(expected);
    }

    [Theory]
    [InlineData(" row")]
    [InlineData("rows")]
    [InlineData("column")]
    [InlineData("auto")]
    [InlineData("")]
    public void An_invalid_scope_is_the_auto_state(string scope)
    {
        Scopes($"<table><tbody><tr><th scope=\"{scope}\">A</th><td>1</td></tr></tbody></table>").Should().Equal(TableHeaderScope.Row);
        Scopes($"<table><thead><tr><th scope=\"{scope}\">A</th></tr></thead><tbody><tr><td>1</td></tr></tbody></table>").Should().Equal(TableHeaderScope.Column);
    }

    [Fact]
    public void In_the_auto_state_a_header_group_and_a_row_of_header_cells_head_their_columns_and_any_other_th_its_row()
    {
        Scopes(Hours).Should().Equal(HoursScopes);
        Scopes("<table><tfoot><tr><th>Sum</th><td>3</td></tr></tfoot><tbody><tr><td>1</td><td>2</td></tr></tbody></table>")
            .Should().Equal([TableHeaderScope.Row], "a footer group is no header group");
        Scopes("<table><thead><tr><th>A</th><td>1</td></tr></thead></table>")
            .Should().Equal([TableHeaderScope.Column], "a head without a body is drawn as the body, and its cells keep heading their columns");
    }

    [Fact]
    public void The_headers_attribute_is_not_read_and_draws_the_table_as_without_it()
    {
        var with = Boxes("<table><tbody><tr><th id=\"a\">A</th><td headers=\"a\">1</td></tr></tbody></table>");
        var without = Boxes("<table><tbody><tr><th id=\"a\">A</th><td>1</td></tr></tbody></table>");

        with.IsSuccess.Should().BeTrue(with.IsFailure ? with.Error.ToString() : string.Empty);
        DocumentModelText.Write(with.Value.Document).Should().Be(DocumentModelText.Write(without.Value.Document));
    }

    [Fact]
    public void A_tagged_PDF_writes_each_header_cell_s_scope_on_its_TH_element()
    {
        using var read = new PdfRead(TaggedPdf(Hours));

        var headers = read.Elements().Where(element => element.Data["S"].Equals(NameToken.Create("TH"))).ToList();

        headers.Select(read.Scope).Should().Equal(HoursScopes.Select(scope => scope.ToString()));
        read.Elements().Where(element => element.Data["S"].Equals(NameToken.Create("TD"))).Select(read.Scope).Should().OnlyContain(scope => scope == null);
    }

    /// <summary>The table with a header row, a column of row headers and two explicit scopes against the auto state,
    /// through the HTML path into a tagged PDF: veraPDF passes PDF/A-3a and PDF/UA-1 (among its rules 7.5-1, a scope on
    /// every <c>TH</c>).</summary>
    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void A_table_with_row_and_column_headers_passes_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-html-header-scopes-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "header-scopes.pdf"), [.. TaggedPdf(Hours)]);

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().ContainSingle().Which.IsCompliant.Should().BeTrue(VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A PDF read back with PdfPig: its structure elements and their table attributes.</summary>
    private sealed class PdfRead(ImmutableArray<byte> pdf) : IDisposable
    {
        private readonly UglyToad.PdfPig.PdfDocument _document = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray());

        private IToken Resolve(IToken token) => token is IndirectReferenceToken reference ? _document.Structure.GetObject(reference.Data).Data : token;

        private IEnumerable<IToken> Kids(DictionaryToken element) =>
            !element.TryGet(NameToken.Create("K"), out var k) ? []
            : Resolve(k) is ArrayToken array ? array.Data
            : [k];

        /// <summary>Every structure element, depth-first in document order.</summary>
        public IEnumerable<DictionaryToken> Elements()
        {
            var root = (DictionaryToken)Resolve(_document.Structure.Catalog.CatalogDictionary.Data["StructTreeRoot"]);
            var pending = new Stack<IToken>(Kids(root).Reverse());
            while (pending.Count > 0)
            {
                if (Resolve(pending.Pop()) is DictionaryToken element && element.ContainsKey(NameToken.Create("S")))
                {
                    yield return element;
                    foreach (var kid in Kids(element).Reverse())
                    {
                        pending.Push(kid);
                    }
                }
            }
        }

        /// <summary>The <c>/Scope</c> of an element's table attributes (owner <c>/Table</c>), or null.</summary>
        public string? Scope(DictionaryToken element)
        {
            if (!element.TryGet(NameToken.Create("A"), out var attributes))
            {
                return null;
            }

            var objects = Resolve(attributes) is ArrayToken array ? array.Data.Select(Resolve) : [Resolve(attributes)];
            return objects.OfType<DictionaryToken>()
                .Where(owner => owner.Data["O"].Equals(NameToken.Create("Table")) && owner.ContainsKey(NameToken.Create("Scope")))
                .Select(owner => ((NameToken)Resolve(owner.Data["Scope"])).Data)
                .SingleOrDefault();
        }

        public void Dispose() => _document.Dispose();
    }
}
