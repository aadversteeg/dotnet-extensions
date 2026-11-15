using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Ave.Documents.TestSupport;

/// <summary>
/// Reads the logical structure of a tagged PDF back with PdfPig, an independent reader: the structure tree from the
/// catalog's <c>/StructTreeRoot</c>, each element's type, alternative text and table attributes, and the text of the
/// marked content each element owns (found through the page's marked-content sequences by MCID). Tests compare it with
/// what the document says, so the tree is checked from the outside, not by the code that wrote it.
/// </summary>
public static class PdfStructureReadBack
{
    /// <summary>A structure element as read back.</summary>
    /// <param name="Type">The structure type, for example <c>TH</c>.</param>
    /// <param name="Details">Alternative text and attributes, for example <c>Scope=Column</c>, or empty.</param>
    /// <param name="Text">The text of the content the element owns directly (not its children's).</param>
    /// <param name="Children">The child elements, in order.</param>
    public sealed record Element(string Type, string Details, string Text, ImmutableArray<Element> Children)
    {
        /// <summary>All elements of the subtree, depth-first, this one first.</summary>
        public IEnumerable<Element> DepthFirst() => Children.SelectMany(child => child.DepthFirst()).Prepend(this);
    }

    /// <summary>The top-level structure elements, or empty when the file has no structure tree.</summary>
    /// <param name="pdf">The PDF bytes.</param>
    /// <returns>The elements.</returns>
    public static ImmutableArray<Element> Read(ImmutableArray<byte> pdf)
    {
        using var document = PdfDocument.Open(pdf.ToArray());
        var reader = new Reader(document);
        return reader.Root();
    }

    /// <summary>
    /// The tree as indented text, one element per line: type, details, and the quoted text it owns. Good for golden
    /// files and for reading a failure.
    /// </summary>
    /// <param name="elements">The top-level elements.</param>
    /// <returns>The text.</returns>
    public static string Outline(IEnumerable<Element> elements)
    {
        var text = new StringBuilder();
        void Write(Element element, int depth)
        {
            text.Append(' ', depth * 2).Append(element.Type);
            if (element.Details.Length > 0)
            {
                text.Append(' ').Append(element.Details);
            }

            if (element.Text.Length > 0)
            {
                text.Append(" \"").Append(element.Text).Append('"');
            }

            text.Append('\n');
            foreach (var child in element.Children)
            {
                Write(child, depth + 1);
            }
        }

        foreach (var element in elements)
        {
            Write(element, 0);
        }

        return text.ToString();
    }

    private sealed class Reader(PdfDocument document)
    {
        private readonly Structure _structure = document.Structure;
        private readonly Dictionary<(int Page, int Mcid), string> _content = Content(document);
        private readonly ImmutableArray<IndirectReference> _pages = PageReferences(document);

        public ImmutableArray<Element> Root()
        {
            var catalog = _structure.Catalog.CatalogDictionary;
            if (!catalog.TryGet(NameToken.Create("StructTreeRoot"), out var root))
            {
                return [];
            }

            return [.. Kids(Dictionary(root), page: -1).OfType<Element>()];
        }

        private Element Element(DictionaryToken element)
        {
            var type = Name(Resolve(element.Data["S"]));
            var page = element.TryGet(NameToken.Create("Pg"), out var pg) ? PageIndex(pg) : -1;
            var kids = Kids(element, page).ToList();
            var details = new List<string>();
            if (element.TryGet(NameToken.Create("Alt"), out var alt))
            {
                details.Add($"Alt=\"{((StringToken)Resolve(alt)).Data}\"");
            }

            if (element.TryGet(NameToken.Create("A"), out var attributes))
            {
                details.AddRange(Dictionary(attributes).Data
                    .Where(entry => entry.Key != "O")
                    .Select(entry => $"{entry.Key}={Plain(Resolve(entry.Value))}"));
            }

            return new Element(
                type,
                string.Join(" ", details),
                string.Concat(kids.OfType<string>()),
                [.. kids.OfType<Element>()]);
        }

        /// <summary>The kids: child elements as <see cref="Element"/>, owned content as its text.</summary>
        private IEnumerable<object> Kids(DictionaryToken element, int page)
        {
            if (!element.TryGet(NameToken.Create("K"), out var k))
            {
                yield break;
            }

            var kids = Resolve(k) is ArrayToken array ? array.Data : [k];
            foreach (var kid in kids)
            {
                switch (kid)
                {
                    case NumericToken mcid:
                        yield return Text(page, mcid.Int);
                        break;
                    case IndirectReferenceToken reference:
                        yield return Element(Dictionary(reference));
                        break;
                    case DictionaryToken reference when reference.TryGet(NameToken.Create("MCID"), out var mcid):
                        yield return Text(PageIndex(reference.Data["Pg"]), ((NumericToken)Resolve(mcid)).Int);
                        break;
                }
            }
        }

        private string Text(int page, int mcid) => _content.TryGetValue((page, mcid), out var text) ? text : $"<missing MCID {mcid} on page {page + 1}>";

        private int PageIndex(IToken page) => _pages.IndexOf(((IndirectReferenceToken)page).Data);

        private IToken Resolve(IToken token) => token is IndirectReferenceToken reference ? _structure.GetObject(reference.Data).Data : token;

        private DictionaryToken Dictionary(IToken token) => (DictionaryToken)Resolve(token);

        private static string Name(IToken token) => ((NameToken)token).Data;

        private static string Plain(IToken token) => token switch
        {
            NameToken name => name.Data,
            NumericToken number => number.Int.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => token.ToString() ?? string.Empty,
        };

        private static ImmutableArray<IndirectReference> PageReferences(PdfDocument document)
        {
            var catalog = document.Structure.Catalog.CatalogDictionary;
            var pages = (DictionaryToken)document.Structure.GetObject(((IndirectReferenceToken)catalog.Data["Pages"]).Data).Data;
            return [.. ((ArrayToken)pages.Data["Kids"]).Data.Cast<IndirectReferenceToken>().Select(kid => kid.Data)];
        }

        private static Dictionary<(int Page, int Mcid), string> Content(PdfDocument document)
        {
            var content = new Dictionary<(int Page, int Mcid), string>();
            foreach (var page in document.GetPages())
            {
                foreach (var marked in Flatten(page.GetMarkedContents()).Where(marked => !marked.IsArtifact && marked.MarkedContentIdentifier >= 0))
                {
                    var text = string.Concat(marked.Letters.Select(letter => letter.Value));
                    content[(page.Number - 1, marked.MarkedContentIdentifier)] = marked.Images.Count > 0 ? text + "[image]" : text;
                }
            }

            return content;
        }

        private static IEnumerable<MarkedContentElement> Flatten(IEnumerable<MarkedContentElement> elements) =>
            elements.SelectMany(element => Flatten(element.Children).Prepend(element));
    }
}
