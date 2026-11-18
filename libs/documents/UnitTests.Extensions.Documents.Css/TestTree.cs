using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.ErrorPaths;

namespace UnitTests.Extensions.Documents.Css;

/// <summary>An HTML element for the cascade tests: a name, attributes and children; parents and siblings are linked
/// when the tree is made.</summary>
public sealed class TestElement : IStyleElement
{
    private readonly Dictionary<string, string> _attributes;

    public TestElement(string localName, params object[] content)
    {
        LocalName = localName;
        _attributes = content.OfType<(string Name, string Value)>().ToDictionary(attribute => attribute.Name, attribute => attribute.Value);
        Children = [.. content.OfType<TestElement>()];
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Parent = this;
            Children[i].Index = i + 1;
            Children[i].PreviousSibling = i > 0 ? Children[i - 1] : null;
            Children[i].NextSibling = i + 1 < Children.Count ? Children[i + 1] : null;
            Children[i].SiblingCount = Children.Count;
            Children[i].IndexOfType = Children.Take(i + 1).Count(child => child.LocalName == Children[i].LocalName);
            Children[i].CountOfType = Children.Count(child => child.LocalName == Children[i].LocalName);
        }
    }

    public List<TestElement> Children { get; }

    public string LocalName { get; }

    public bool IsHtml => true;

    public bool IsQuirksMode => false;

    public string? Id => GetAttribute("id");

    public ValueList<string> Classes => GetAttribute("class") is { } value ? [.. value.Split(' ', StringSplitOptions.RemoveEmptyEntries)] : [];

    public bool HasClass(string name) => Classes.Contains(name);

    public bool AttributeHasWord(string localName, string word, bool ignoreCase) =>
        GetAttribute(localName) is { } value && value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(item => ignoreCase ? string.Equals(item, word, StringComparison.OrdinalIgnoreCase) : item == word);

    public string? GetAttribute(string localName) => _attributes.TryGetValue(localName, out var value) ? value : null;

    public bool IsCaseInsensitiveAttributeValue(string localName) => localName is "type" or "lang" or "dir";

    public TestElement? Parent { get; private set; }

    IStyleElement? IStyleElement.Parent => Parent;

    public IStyleElement? PreviousSibling { get; private set; }

    public IStyleElement? NextSibling { get; private set; }

    public int Index { get; private set; } = 1;

    public int SiblingCount { get; private set; } = 1;

    public int IndexOfType { get; private set; } = 1;

    public int CountOfType { get; private set; } = 1;

    public bool IsRoot => Parent is null;

    public bool IsEmpty => Children.Count == 0;

    public bool IsLink => LocalName == "a" && GetAttribute("href") is not null;

    /// <summary>The element and its descendants in tree order.</summary>
    public IEnumerable<TestElement> TreeOrder()
    {
        yield return this;
        foreach (var descendant in Children.SelectMany(child => child.TreeOrder()))
        {
            yield return descendant;
        }
    }

    /// <summary>The first element in tree order with this ID.</summary>
    public TestElement this[string id] => TreeOrder().First(element => element.Id == id);

    public override string ToString() => Id is { } id ? $"{LocalName}#{id}" : LocalName;
}

/// <summary>Builds trees and computes their styles.</summary>
public static class TestTree
{
    public static TestElement E(string name, params object[] content) => new(name, content);

    public static (string, string) A(string name, string value) => (name, value);

    public static StyleSheet Sheet(string css, string name = "author.css")
    {
        var result = CssParser.ParseStyleSheet(css, new CssSourceOptions(name));
        return result.Value;
    }

    /// <summary>The computed styles of every element, with every diagnostic (of reading the sheets, of the cascade).</summary>
    public static Computed Compute(TestElement root, string author, string? userAgent = null, MediaContext? media = null, StyleSheetLoader? imports = null,
        Func<TestElement, ElementDeclarations?>? own = null, ValueContext? values = null)
    {
        var diagnostics = new List<Error>();
        var sheets = new List<CascadeSheet>();
        if (userAgent is not null)
        {
            var ua = CssParser.ParseStyleSheet(userAgent, new CssSourceOptions("ua.css"));
            diagnostics.AddRange(ua.Diagnostics);
            sheets.Add(new CascadeSheet(ua.Value, CascadeOrigin.UserAgent));
        }

        var parsed = CssParser.ParseStyleSheet(author, new CssSourceOptions("author.css"));
        diagnostics.AddRange(parsed.Diagnostics);
        sheets.Add(new CascadeSheet(parsed.Value, CascadeOrigin.Author));
        var index = CascadeIndex.Build(sheets, media ?? MediaContext.PrintA4, imports);
        diagnostics.AddRange(index.Diagnostics);
        var computer = new StyleComputer(index.Value, values ?? ValueContext.A4);
        var styles = new Dictionary<TestElement, ComputedStyle>();
        foreach (var element in root.TreeOrder())
        {
            styles[element] = computer.Compute(element, own?.Invoke(element));
        }

        diagnostics.AddRange(computer.Diagnostics);
        return new Computed(styles, diagnostics);
    }

    /// <summary>A style attribute's declarations.</summary>
    public static ElementDeclarations Style(string text) =>
        new(CssParser.ParseDeclarations(text, new CssSourceOptions("doc.html")).Value, [], "doc.html");

    /// <summary>Presentational hints written as declarations.</summary>
    public static ElementDeclarations Hints(string text) =>
        new([], CssParser.ParseDeclarations(text, new CssSourceOptions("doc.html")).Value, "doc.html");
}

/// <summary>Computed styles and diagnostics.</summary>
public sealed record Computed(Dictionary<TestElement, ComputedStyle> Styles, List<Error> Diagnostics)
{
    public ComputedStyle this[TestElement element] => Styles[element];

    /// <summary>A computed value as text.</summary>
    public string Value(TestElement element, string property) => Styles[element][property].ToString()!;

    public IEnumerable<Error> Coded(ErrorCode code) => Diagnostics.Where(error => error.Code == code);
}
