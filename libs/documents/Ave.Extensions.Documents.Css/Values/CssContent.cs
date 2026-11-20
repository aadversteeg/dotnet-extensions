using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Values;

/// <summary>
/// The computed value of <c>content</c> on a page-margin box (CSS Generated Content 3 §1; CSS Paged Media 3 §5): the
/// keyword <c>none</c> or <c>normal</c>, which generate no box, or a list of items, which does, also when it is the one
/// empty string <c>""</c> (Chromium 159, measured: a <c>@top-right</c> with <c>content: ""</c> and a background is
/// drawn and takes the width of its padding; one with <c>none</c>, <c>normal</c> or no <c>content</c> is not drawn and
/// takes no width).
/// </summary>
/// <param name="Keyword">none or normal; null for a list.</param>
/// <param name="Items">The items in order; empty for a keyword.</param>
public sealed record CssContentList(string? Keyword, ValueList<CssContentItem> Items) : CssValue
{
    /// <summary>The initial value, <c>normal</c>: on a page-margin box it generates no box.</summary>
    public static CssContentList Normal { get; } = new("normal", []);

    /// <summary><c>none</c>: no box.</summary>
    public static CssContentList None { get; } = new("none", []);

    /// <summary>Whether the value generates a box: a list, not <c>none</c> or <c>normal</c>.</summary>
    public bool Generates => Keyword is null;

    /// <summary>Formats as CSS.</summary>
    public override string ToString() => Keyword ?? string.Join(" ", System.Linq.Enumerable.Select(Items, item => item.ToString()));
}

/// <summary>One item of a <c>content</c> list (CSS Generated Content 3 §1.1).</summary>
public abstract record CssContentItem;

/// <summary>A string, its escapes resolved by the tokenizer (CSS Syntax 3 §4.3.7: <c>\A</c> is a line feed,
/// <c>\2003</c> an em space).</summary>
/// <param name="Value">The characters.</param>
public sealed record CssContentString(string Value) : CssContentItem
{
    /// <summary>Formats as a quoted string.</summary>
    public override string ToString() => new CssString(Value).ToString();
}

/// <summary>The page-based counters (CSS Paged Media 3 §6.1).</summary>
public enum PageCounterName
{
    /// <summary><c>page</c>: the number of the page, from 1.</summary>
    Page,

    /// <summary><c>pages</c>: the number of pages of the document.</summary>
    Pages,
}

/// <summary><c>counter(page)</c> or <c>counter(pages)</c>, in decimal (CSS Lists 3 §4.7, CSS Paged Media 3 §6.1).</summary>
/// <param name="Counter">Which counter.</param>
public sealed record CssContentCounter(PageCounterName Counter) : CssContentItem
{
    /// <summary>Formats as CSS.</summary>
    public override string ToString() => Counter == PageCounterName.Page ? "counter(page)" : "counter(pages)";
}

/// <summary>An image: <c>url()</c>, or <c>image-set()</c> with one URL and its resolution (CSS Images 4 §2.2): drawn at
/// its natural size divided by the resolution.</summary>
/// <param name="Url">The URL as written; the caller resolves it against the base URL of the style sheet.</param>
/// <param name="Resolution">The resolution in dots per CSS pixel (<c>dppx</c>, CSS Values 4 §7.4); 1 for a bare
/// <c>url()</c>.</param>
public sealed record CssContentImage(string Url, double Resolution) : CssContentItem
{
    /// <summary>Formats as CSS.</summary>
    public override string ToString() => $"image-set({new CssUrl(Url)} {Resolution}x)";
}

/// <summary>
/// Reads the value of <c>content</c> on a page-margin box (CSS Generated Content 3 §1): <c>none</c>, <c>normal</c>,
/// or a list of strings, <c>counter(page)</c>, <c>counter(pages)</c> (with no style or <c>decimal</c>), <c>url()</c>,
/// and <c>image-set()</c> of one URL and one resolution. Every other value, valid or not, is refused with the reason: the
/// engine cannot tell what of the rest of the grammar it would draw as Chromium does.
/// </summary>
internal static class ContentValue
{
    /// <summary>The value, or the reason it is refused.</summary>
    /// <param name="values">The declaration's value.</param>
    /// <returns>The value, or null and the reason.</returns>
    public static (CssContentList? Value, string? Refused) Parse(IReadOnlyList<ComponentValue> values)
    {
        var items = new List<CssContentItem>();
        var significant = Significant(values);

        if (significant is [CssToken { Kind: CssTokenKind.Ident } keyword] && (keyword.IsIdent("none") || keyword.IsIdent("normal")))
        {
            return (keyword.IsIdent("none") ? CssContentList.None : CssContentList.Normal, null);
        }

        if (significant.Count == 0)
        {
            return (null, "the value is empty");
        }

        foreach (var value in significant)
        {
            switch (value)
            {
                case CssToken { Kind: CssTokenKind.String } text:
                    items.Add(new CssContentString(text.Text));
                    break;
                case CssToken { Kind: CssTokenKind.Url } url:
                    items.Add(new CssContentImage(url.Text, 1));
                    break;
                case FunctionValue function when function.IsNamed("url") && UrlOf(function) is { } quoted:
                    items.Add(new CssContentImage(quoted, 1));
                    break;
                case FunctionValue function when function.IsNamed("counter"):
                    if (Counter(function) is not { } counter)
                    {
                        return (null, $"'{CssSerializer.Shortened([function])}' is not drawn: only counter(page) and counter(pages), in decimal, are");
                    }

                    items.Add(counter);
                    break;
                case FunctionValue function when function.IsNamed("image-set"):
                    if (ImageSet(function) is not { } image)
                    {
                        return (null, $"'{CssSerializer.Shortened([function])}' is not drawn: image-set() takes one url() and its resolution here");
                    }

                    items.Add(image);
                    break;
                default:
                    return (null, $"'{CssSerializer.Shortened([value])}' is not drawn: only strings, counter(page), counter(pages), url() and image-set() are");
            }
        }

        return (new CssContentList(null, [.. items]), null);
    }

    /// <summary>The URL of <c>url("…")</c> (a function with one string), or null.</summary>
    private static string? UrlOf(FunctionValue function) =>
        Significant(function.Values) is [CssToken { Kind: CssTokenKind.String } text] ? text.Text : null;

    /// <summary><c>counter(page)</c> or <c>counter(pages)</c>, optionally <c>, decimal</c>; null for anything else. The
    /// counter name is a custom identifier, compared as written (CSS Values 4 §4.2).</summary>
    private static CssContentCounter? Counter(FunctionValue function)
    {
        var arguments = Significant(function.Values);
        var styled = arguments is [_, CssToken { Kind: CssTokenKind.Comma }, CssToken style] && style.IsIdent("decimal");
        if ((arguments.Count != 1 && !styled) || arguments[0] is not CssToken { Kind: CssTokenKind.Ident } name)
        {
            return null;
        }

        return name.Text switch
        {
            "page" => new CssContentCounter(PageCounterName.Page),
            "pages" => new CssContentCounter(PageCounterName.Pages),
            _ => null,
        };
    }

    /// <summary><c>image-set(url(…) &lt;resolution&gt;)</c> (or a string in place of the url()); null for anything
    /// else: several candidates, a missing resolution, <c>type()</c>, an image other than a URL.</summary>
    private static CssContentImage? ImageSet(FunctionValue function)
    {
        var arguments = Significant(function.Values);
        if (arguments is not [var image, CssToken { Kind: CssTokenKind.Dimension } resolution] || Dppx(resolution) is not { } dppx)
        {
            return null;
        }

        var url = image switch
        {
            CssToken { Kind: CssTokenKind.Url or CssTokenKind.String } token => token.Text,
            FunctionValue named when named.IsNamed("url") => UrlOf(named),
            _ => null,
        };
        return url is null ? null : new CssContentImage(url, dppx);
    }

    /// <summary>A resolution in dots per CSS pixel (CSS Values 4 §7.4: 1dppx = 1x = 96dpi, 1dpcm = 2.54dpi), more than
    /// zero; null for another unit or a value of 0 or less.</summary>
    private static double? Dppx(CssToken resolution)
    {
        double? perUnit = InfraAscii.ToLower(resolution.Text) switch
        {
            "x" or "dppx" => 1,
            "dpi" => 1.0 / 96,
            "dpcm" => 2.54 / 96,
            _ => null,
        };
        return perUnit is { } unit && resolution.Number > 0 ? resolution.Number * unit : null;
    }

    private static List<ComponentValue> Significant(IReadOnlyList<ComponentValue> values)
    {
        var significant = new List<ComponentValue>(values.Count);
        foreach (var value in values)
        {
            if (value is not CssToken { Kind: CssTokenKind.Whitespace })
            {
                significant.Add(value);
            }
        }

        return significant;
    }
}
