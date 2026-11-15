using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// A whole document as the author wrote it. An immutable value: it can be stored, shared and asserted on in a
/// unit test without rendering anything — structurally (for example FluentAssertions' BeEquivalentTo) or through
/// the text form of its document model. Two documents with the same content compare equal with <c>==</c>
/// (children are held in a <see cref="ValueList{T}"/>). Made by <see cref="Elements.Document"/>.
/// </summary>
/// <param name="Body">The content, top to bottom.</param>
/// <param name="Page">The page size and margins.</param>
/// <param name="Format">The document's text formatting; must name a font family.</param>
/// <param name="Title">The title, or null.</param>
/// <param name="Author">The author, or null.</param>
/// <param name="Source">Where the document was created.</param>
public sealed record Document(
    ValueList<Element> Body,
    PageSetup Page,
    TextFormat Format,
    string? Title,
    string? Author,
    SourceLocation Source);
