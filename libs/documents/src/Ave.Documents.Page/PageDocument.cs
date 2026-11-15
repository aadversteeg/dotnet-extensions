using System.Collections.Immutable;
using Ave.Documents.Core;

namespace Ave.Documents.Page;

/// <summary>
/// The laid-out document: every page with what to draw on it. The output of layout and the input of every
/// back-end (PDF now; a raster preview and SVG later). It does not depend on the document model, so a future
/// PDF reader can produce the same model and share the back-ends.
/// </summary>
/// <param name="Info">Descriptive information for the output file.</param>
/// <param name="Pages">The pages, in order.</param>
public sealed record PageDocument(DocumentInfo Info, ImmutableArray<PageContent> Pages);

/// <summary>Descriptive information about the document (written to the PDF document information).</summary>
/// <param name="Title">The title, or null.</param>
/// <param name="Author">The author, or null.</param>
public sealed record DocumentInfo(string? Title, string? Author);

/// <summary>
/// One page: its size and its display list. (Named PageContent, not Page, because Page is the last part of
/// this namespace and the type would hide it in sibling namespaces.)
/// </summary>
/// <param name="Size">The page size in points.</param>
/// <param name="Items">What to draw, in painting order: later items are painted over earlier ones.</param>
public sealed record PageContent(Size Size, ImmutableArray<DrawItem> Items);
