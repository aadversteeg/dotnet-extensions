namespace Ave.Extensions.Documents.Core;

/// <summary>
/// Where a link leads (ISO 32000-1 §12.5.6.5, Table 173: a link annotation's <c>/A</c> action or <c>/Dest</c>). A closed
/// set, one sealed record per kind behind a private constructor: an absolute URI of an accepted scheme (<see cref="Uri"/>),
/// which the PDF back-end writes as a URI action (§12.6.4.7), or a node of the same document (<see cref="Node"/>), which it
/// writes as an explicit destination (§12.3.2.2). The model never holds a name: a front-end resolves a name to its node.
/// The engine never fetches, resolves or opens a URI; it only writes it.
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

    /// <summary>A link to a place inside the same document: the node <paramref name="node"/>, a box or an inline group of
    /// the document's body. Its place is the top of the node's first fragment, on the page that fragment is on.</summary>
    /// <param name="node">The node the link leads to.</param>
    /// <returns>The target.</returns>
    public static LinkTarget Node(NodeId node) => new NodeTarget(node);

    /// <summary>A link to an absolute URI.</summary>
    /// <param name="Value">The URI, as it is written into the file.</param>
    public sealed record UriTarget(string Value) : LinkTarget
    {
        /// <summary>Returns <c>uri="…"</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => $"uri={QuotedText.Quote(Value)}";
    }

    /// <summary>A link to a node of the same document.</summary>
    /// <param name="Value">The node.</param>
    public sealed record NodeTarget(NodeId Value) : LinkTarget
    {
        /// <summary>Returns <c>node=#n</c>.</summary>
        /// <returns>The text form.</returns>
        public override string ToString() => $"node={Value}";
    }
}
