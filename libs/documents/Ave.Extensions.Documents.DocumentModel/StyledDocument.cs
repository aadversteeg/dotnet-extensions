using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// A whole document as every front-end hands it to layout: metadata, the page style and one root box.
/// Values are COMPUTED values in the CSS sense (CSS Cascade 4 §4.4): inheritance and defaults were already
/// resolved by the front-end, so layout never looks up a style anywhere else.
/// </summary>
/// <param name="Metadata">Title, author, language and attachments.</param>
/// <param name="Page">The size and margins of every page.</param>
/// <param name="Root">The root box; laid out in the page area of each page.</param>
public sealed record StyledDocument(DocumentMetadata Metadata, PageStyle Page, Box Root);
