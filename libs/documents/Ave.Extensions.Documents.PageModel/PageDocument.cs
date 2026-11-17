using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.PageModel;

/// <summary>
/// The laid-out document: every page with what to draw on it. The output of layout and the input of every
/// back-end (PDF now; a raster preview and SVG later). It does not depend on the document model, so a future
/// PDF reader can produce the same model and share the back-ends.
/// </summary>
/// <param name="Metadata">What the document says about itself (title, author, language) and its attachments, for
/// the output file.</param>
/// <param name="Pages">The pages, in order.</param>
public sealed record PageDocument(DocumentMetadata Metadata, ValueList<Page> Pages);

/// <summary>
/// One page: its size and its display list.
/// </summary>
/// <param name="Size">The page size in points.</param>
/// <param name="Items">What to draw, in painting order: later items are painted over earlier ones.</param>
public sealed record Page(Size Size, ValueList<DrawItem> Items);
