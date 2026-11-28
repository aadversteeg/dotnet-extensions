using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Html;

/// <summary>
/// The caller's resource resolver as one render sees it: each URL is asked at most once, whatever asks for it (a style
/// sheet, an image, a font file) and however often, and the answer is kept for the render; nothing is kept from one
/// render to the next. A URL longer than <see cref="MaxUrlLength"/>, and every distinct URL past
/// <see cref="MaxResources"/>, is not asked: the place that names it reports why (<see cref="Refusal"/>). Style
/// resolution makes the table and leaves it with the styled document it returns; box generation of that document with
/// the same resolver goes on with the same table, so one render asks each URL once.
/// </summary>
internal sealed class ResourceTable
{
    /// <summary>The most distinct URLs one render asks the resolver for: 4 096. A document names a few style sheets,
    /// fonts and images; the reference documents name at most a few dozen.</summary>
    public const int MaxResources = 4_096;

    /// <summary>The longest URL the resolver is asked for: 32 767 characters, the bound of a link's URI
    /// (<see cref="LinkUris.MaxLength"/>).</summary>
    public const int MaxUrlLength = LinkUris.MaxLength;

    /// <summary>The table each styled document was resolved with (kept beside it, not in it, so it takes no part in the
    /// document's equality).</summary>
    private static readonly ConditionalWeakTable<StyledHtmlDocument, ResourceTable> Tables = [];

    private readonly ResourceResolver _resolver;
    private readonly Dictionary<string, HtmlResource?> _asked = new(StringComparer.Ordinal);

    private ResourceTable(ResourceResolver resolver) => _resolver = resolver;

    /// <summary>A resolver that asks the given one at most once per URL: the table the styled document was resolved
    /// with when that was the same resolver, else a new one; null for none.</summary>
    /// <param name="resolver">The caller's resolver.</param>
    /// <param name="styled">The styled document box generation goes on with, or null in style resolution.</param>
    /// <returns>The resolver of one render.</returns>
    public static ResourceResolver? Once(ResourceResolver? resolver, StyledHtmlDocument? styled = null) =>
        resolver is null || resolver.Target is ResourceTable ? resolver
        : styled is not null && Tables.TryGetValue(styled, out var table) && table._resolver == resolver ? table.Get
        : new ResourceTable(resolver).Get;

    /// <summary>Leaves the table of a style resolution with the document it made.</summary>
    /// <param name="styled">The styled document.</param>
    /// <param name="resolver">The resolver the resolution used (<see cref="Once"/>).</param>
    public static void Remember(StyledHtmlDocument styled, ResourceResolver? resolver)
    {
        if (resolver?.Target is ResourceTable table)
        {
            Tables.AddOrUpdate(styled, table);
        }
    }

    /// <summary>Why a URL is not asked of the resolver, or null when it is (or the resolver is not one of a render).</summary>
    /// <param name="resolver">The resolver of the render.</param>
    /// <param name="url">The absolute URL.</param>
    /// <returns>The reason, as the end of a sentence, or null.</returns>
    public static string? Refusal(ResourceResolver? resolver, string url) =>
        resolver?.Target is ResourceTable table ? table.Why(url) : null;

    private string? Why(string url) =>
        url.Length > MaxUrlLength ? $"is longer than the {MaxUrlLength} characters a resource URL may have, so the resolver is not asked"
        : !_asked.ContainsKey(url) && _asked.Count >= MaxResources ? $"is past the {MaxResources} distinct resources one document may name, so the resolver is not asked"
        : null;

    private HtmlResource? Get(string url)
    {
        if (_asked.TryGetValue(url, out var known))
        {
            return known;
        }

        if (Why(url) is not null)
        {
            return null;
        }

        var resource = _resolver(url);
        _asked[url] = resource;
        return resource;
    }
}
