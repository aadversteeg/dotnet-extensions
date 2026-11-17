using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Ave.Extensions.Documents.TestSupport;

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
    /// <param name="BoundingBox">The <c>/BBox</c> of its layout attributes (left, bottom, right, top in default user
    /// space), or null when it has none. Kept out of <paramref name="Details"/>, so outlines do not change with the
    /// layout.</param>
    public sealed record Element(string Type, string Details, string Text, ImmutableArray<Element> Children, IReadOnlyList<double>? BoundingBox = null)
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

    /// <summary>
    /// Checks the parent tree (ISO 32000-1 §14.7.4.4) against the structure: for every page, the array the page's
    /// <c>/StructParents</c> key selects must name, at each marked-content identifier, the element whose kids hold that
    /// identifier on that page. A reversed or shifted parent tree passes veraPDF and every text check, but sends a
    /// screen reader from the content to the wrong element.
    /// </summary>
    /// <param name="pdf">The PDF bytes.</param>
    /// <returns>One line per difference; empty when the parent tree matches the structure.</returns>
    public static IReadOnlyList<string> ParentTreeDifferences(ImmutableArray<byte> pdf)
    {
        using var document = PdfDocument.Open(pdf.ToArray());
        return new Reader(document).ParentTreeDifferences();
    }

    private sealed class Reader(PdfDocument document)
    {
        public IReadOnlyList<string> ParentTreeDifferences()
        {
            var catalog = _structure.Catalog.CatalogDictionary;
            if (!catalog.TryGet(NameToken.Create("StructTreeRoot"), out var root))
            {
                return ["The file has no structure tree."];
            }

            // What the structure says: (page, MCID) → the element that owns it.
            var owners = new Dictionary<(int Page, int Mcid), IndirectReference>();
            void Walk(IndirectReference reference)
            {
                var element = (DictionaryToken)_structure.GetObject(reference).Data;
                var page = element.TryGet(NameToken.Create("Pg"), out var pg) ? PageIndex(pg) : -1;
                if (!element.TryGet(NameToken.Create("K"), out var k))
                {
                    return;
                }

                foreach (var kid in Resolve(k) is ArrayToken array ? array.Data : [k])
                {
                    switch (kid)
                    {
                        case NumericToken mcid:
                            owners[(page, mcid.Int)] = reference;
                            break;
                        case IndirectReferenceToken child when Resolve(child) is DictionaryToken childElement && childElement.ContainsKey(NameToken.Create("S")):
                            Walk(child.Data);
                            break;
                        case DictionaryToken marked when marked.TryGet(NameToken.Create("MCID"), out var mcid):
                            owners[(PageIndex(marked.Data["Pg"]), ((NumericToken)Resolve(mcid)).Int)] = reference;
                            break;
                    }
                }
            }

            var rootKids = Dictionary(root).Data["K"];
            foreach (var kid in Resolve(rootKids) is ArrayToken kids ? kids.Data : [rootKids])
            {
                Walk(((IndirectReferenceToken)kid).Data);
            }

            // What the parent tree says.
            var nums = ((ArrayToken)Resolve(Dictionary(Dictionary(root).Data["ParentTree"]).Data["Nums"])).Data;
            var arrays = Enumerable.Range(0, nums.Count / 2).ToDictionary(index => ((NumericToken)nums[2 * index]).Int, index => (ArrayToken)Resolve(nums[2 * index + 1]));
            var differences = new List<string>();
            for (var page = 0; page < _pages.Length; page++)
            {
                var pageObject = (DictionaryToken)_structure.GetObject(_pages[page]).Data;
                var key = ((NumericToken)pageObject.Data["StructParents"]).Int;
                var entries = arrays[key].Data;
                foreach (var ((ownerPage, mcid), owner) in owners.Where(pair => pair.Key.Page == page))
                {
                    var listed = mcid < entries.Count ? ((IndirectReferenceToken)entries[mcid]).Data : default;
                    if (!listed.Equals(owner))
                    {
                        differences.Add($"Page {page + 1} MCID {mcid}: the structure says object {owner.ObjectNumber}, the parent tree {listed.ObjectNumber}.");
                    }
                }
            }

            return differences;
        }

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

            // /A is one attribute object or an array of them (ISO 32000-1 §14.7.5).
            var attributes = element.TryGet(NameToken.Create("A"), out var a)
                ? (Resolve(a) is ArrayToken many ? many.Data.Select(Dictionary).ToList() : [Dictionary(a)])
                : [];
            details.AddRange(attributes.SelectMany(owner => owner.Data)
                .Where(entry => entry.Key is not "O" and not "BBox")
                .Select(entry => $"{entry.Key}={Plain(Resolve(entry.Value))}"));
            var box = attributes
                .Where(owner => owner.TryGet(NameToken.Create("O"), out var o) && Name(Resolve(o)) == "Layout")
                .Select(owner => owner.TryGet(NameToken.Create("BBox"), out var bbox) ? (ArrayToken)Resolve(bbox) : null)
                .FirstOrDefault(bbox => bbox is not null);

            return new Element(
                type,
                string.Join(" ", details),
                string.Concat(kids.OfType<string>()),
                [.. kids.OfType<Element>()],
                box?.Data.Select(number => ((NumericToken)Resolve(number)).Double).ToList());
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
