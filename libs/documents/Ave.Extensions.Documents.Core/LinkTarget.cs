namespace Ave.Extensions.Documents.Core;

/// <summary>
/// Where a link leads (ISO 32000-1 §12.5.6.5, Table 173: a link annotation's <c>/A</c> action or <c>/Dest</c>). A closed
/// set, one sealed record per kind behind a private constructor: today only an absolute URI of an accepted scheme
/// (<see cref="Uri"/>), which the PDF back-end writes as a URI action (§12.6.4.7). The engine never fetches, resolves or
/// opens a target; it only writes it.
/// </summary>
public abstract record LinkTarget
{
    private LinkTarget()
    {
    }

    /// <summary>A link to an absolute URI, written as it is given. Whether it is accepted is the rule of
    /// <see cref="LinkUris"/>, which every front-end and the back-ends check.</summary>
    /// <param name="uri">The URI, for example <c>https://example.com/a</c> or <c>mailto:info@example.com</c>.</param>
    /// <returns>The target.</returns>
    public static LinkTarget Uri(string uri) => new UriTarget(uri);

    /// <summary>A link to an absolute URI.</summary>
    /// <param name="Value">The URI, as it is written into the file.</param>
    public sealed record UriTarget(string Value) : LinkTarget
    {
        /// <summary>Returns <c>uri="…"</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => $"uri={QuotedText.Quote(Value)}";
    }
}
