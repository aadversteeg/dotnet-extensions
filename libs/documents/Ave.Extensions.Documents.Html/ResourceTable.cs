using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Html;

/// <summary>
/// The caller's resource resolver as one render sees it: each URL is asked at most once, whatever asks for it (a style
/// sheet, an image, a font file) and however often, and the answer is kept for the render; nothing is kept from one
/// render to the next. A URL longer than <see cref="MaxUrlLength"/>, and every distinct URL past
/// <see cref="MaxResources"/>, is not asked: the place that names it reports why (<see cref="Refusal"/>). Style
/// resolution makes the table and leaves it with the styled document it returns; box generation of that document with
/// the same resolver goes on with the same table, so one render asks each URL once. A URL's fragment is not part of
/// it: the resolver is asked, and the answer kept, for the URL without it (<see cref="Urls.WithoutFragment"/>). The
/// resolver is never asked for a URL that is empty without its fragment (a reference such as <c>#top</c> with no base
/// URL), nor for a <c>data:</c> URL: the front-end reads that itself (<see cref="DataUrl"/>), and one it cannot read is
/// not there. Nor is it asked for a URL whose scheme the caller does not allow
/// (<see cref="HtmlStyleOptions.AllowedResourceSchemes"/>). Every place that names a resource finds its URL by one rule,
/// <see cref="Locate"/>: the reference resolved by <see cref="Urls.Resolved"/>, which reads the scheme from the
/// reference itself and refuses a reference that names a host it cannot resolve, then tested here. So the resolver is
/// asked for an absolute URL of an allowed scheme, or, without a base URL, for a reference without a scheme and without
/// a host, as the document writes it. Every test is made on the URL <see cref="Urls.Cleaned"/>.
/// </summary>
internal sealed class ResourceTable
{
    /// <summary>The most distinct URLs one render asks the resolver for: 4 096. A document names a few style sheets,
    /// fonts and images; the reference documents name at most a few dozen.</summary>
    public const int MaxResources = 4_096;

    /// <summary>The longest URL the resolver is asked for: 32 767 characters, the bound of a link's URI
    /// (<see cref="LinkUris.MaxLength"/>).</summary>
    public const int MaxUrlLength = LinkUris.MaxLength;

    /// <summary>The schemes a resource URL may have unless the caller allows others: <c>http</c> and <c>https</c>.</summary>
    internal static readonly ImmutableHashSet<string> DefaultSchemes = ImmutableHashSet.Create(StringComparer.Ordinal, "http", "https");

    /// <summary>The table each styled document was resolved with (kept beside it, not in it, so it takes no part in the
    /// document's equality).</summary>
    private static readonly ConditionalWeakTable<StyledHtmlDocument, ResourceTable> Tables = [];

    private readonly ResourceResolver _resolver;
    private readonly ImmutableHashSet<string> _schemes;
    private readonly string _option;
    private readonly Dictionary<string, HtmlResource?> _asked = new(StringComparer.Ordinal);

    private ResourceTable(ResourceResolver resolver, ImmutableHashSet<string> schemes, string option) =>
        (_resolver, _schemes, _option) = (resolver, schemes, option);

    /// <summary>A resolver that asks the given one at most once per URL, and only for a URL of an allowed scheme: the
    /// table the styled document was resolved with when that was the same resolver with the same schemes, else a new one;
    /// null for none.</summary>
    /// <param name="resolver">The caller's resolver.</param>
    /// <param name="schemes">The schemes the caller allows (<see cref="Schemes"/>).</param>
    /// <param name="option">The option that sets them, for the message of a refused URL.</param>
    /// <param name="styled">The styled document box generation goes on with, or null in style resolution.</param>
    /// <returns>The resolver of one render.</returns>
    public static ResourceResolver? Once(ResourceResolver? resolver, ImmutableHashSet<string> schemes, string option, StyledHtmlDocument? styled = null) =>
        resolver is null || resolver.Target is ResourceTable ? resolver
        : styled is not null && Tables.TryGetValue(styled, out var table) && table._resolver == resolver && table._schemes.SetEquals(schemes) ? table.Get
        : new ResourceTable(resolver, schemes, option).Get;

    /// <summary>The schemes of an option as the table compares them: every name in ASCII lower case, so the comparison
    /// is ASCII case-insensitive on both sides (URL Standard §4.4, scheme state, writes a URL's scheme in lower case).
    /// </summary>
    /// <param name="schemes">The caller's set.</param>
    /// <returns>The set itself when its names are already lower case and compared ordinally, else a lowered copy.</returns>
    /// <exception cref="ArgumentNullException">The set is null.</exception>
    internal static ImmutableHashSet<string> Schemes(ImmutableHashSet<string> schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);
        return schemes.KeyComparer == StringComparer.Ordinal && schemes.All(name => InfraAscii.ToLower(name) == name)
            ? schemes
            : schemes.Select(InfraAscii.ToLower).ToImmutableHashSet(StringComparer.Ordinal);
    }

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

    /// <summary>The URL a reference names and why the resolver is not asked for it (<see cref="Urls.Resolved"/>, then
    /// <see cref="Refusal"/>): the one way from a reference in a document to the resolver. When the reason is null the
    /// URL may be asked; when the URL is null so is what the resolver could be asked.</summary>
    /// <param name="resolver">The resolver of the render, or null.</param>
    /// <param name="reference">The reference as the document writes it.</param>
    /// <param name="baseUrl">The base URL it is resolved against, or null.</param>
    /// <returns>The URL (with its fragment) or null, and the reason as the end of a sentence or null.</returns>
    public static (string? Url, string? Why) Locate(ResourceResolver? resolver, string reference, string? baseUrl)
    {
        var (url, why) = Urls.Resolved(reference, baseUrl);
        return url is null ? (null, why) : (url, Refusal(resolver, url));
    }

    /// <summary>Why a URL is not asked of the resolver, or null when it is (or the resolver is not one of a render).</summary>
    /// <param name="resolver">The resolver of the render.</param>
    /// <param name="url">The absolute URL.</param>
    /// <returns>The reason, as the end of a sentence, or null.</returns>
    private static string? Refusal(ResourceResolver? resolver, string url) =>
        resolver?.Target is ResourceTable table ? table.Why(Urls.WithoutFragment(Urls.Cleaned(url))) : null;

    private string? Why(string url) =>
        url.Length > MaxUrlLength ? $"is longer than the {MaxUrlLength} characters a resource URL may have, so the resolver is not asked"
        : Disallowed(url) is { } scheme ? $"has the scheme '{InputQuote.Of(scheme)}', which the caller does not allow for resources, so the resolver is not asked: allow it with {_option} (default http and https), or name the resource with an allowed scheme"
        : !_asked.ContainsKey(url) && _asked.Count >= MaxResources ? $"is past the {MaxResources} distinct resources one document may name, so the resolver is not asked"
        : null;

    /// <summary>The scheme of a URL that is not allowed, in lower case; null for an allowed one, a <c>data:</c> URL (the
    /// front-end reads it, the resolver is never asked) and a URL with no scheme (a relative reference without a base
    /// URL, handed on as written).</summary>
    private string? Disallowed(string url)
    {
        if (!Urls.HasScheme(url) || DataUrl.Is(url))
        {
            return null;
        }

        var scheme = InfraAscii.ToLower(url[..url.IndexOf(':', StringComparison.Ordinal)]);
        return _schemes.Contains(scheme) ? null : scheme;
    }

    private HtmlResource? Get(string url)
    {
        url = Urls.WithoutFragment(Urls.Cleaned(url));
        if (url.Length == 0 || DataUrl.Is(url))
        {
            return null;
        }

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
