using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// A piece of a document as the author wrote it: an immutable value, created by the functions in
/// <see cref="Elements"/>. Elements hold SPECIFIED values (what the author set, or null to inherit); the
/// translation to the document model computes the final values (<see cref="DocumentTranslator"/>).
/// </summary>
public abstract record Element
{
    // Only the cases in this assembly exist, so the translator handles every one.
    private protected Element(SourceLocation source) => Source = source;

    /// <summary>Where the element was created: the file and line of the call.</summary>
    public SourceLocation Source { get; }
}

/// <summary>A block: stacks its children vertically. Made by <see cref="Elements.Block"/>.</summary>
/// <param name="Children">The child elements.</param>
/// <param name="Box">Margin, padding and background.</param>
/// <param name="Format">Text formatting inherited by the children; null fields inherit from the parent.</param>
/// <param name="Source">Where the element was created.</param>
public sealed record BlockElement(ValueList<Element> Children, BoxFormat Box, TextFormat Format, SourceLocation Source) : Element(Source);

/// <summary>A paragraph or heading: lines of text. Made by <see cref="Elements.Paragraph(string, BoxFormat?, TextFormat?, string, int)"/>
/// and <see cref="Elements.Heading"/>.</summary>
/// <param name="Spans">The text, in pieces that may each have their own formatting.</param>
/// <param name="Role">Paragraph or heading.</param>
/// <param name="Box">Margin, padding and background.</param>
/// <param name="Format">Text formatting for the spans; null fields inherit from the parent.</param>
/// <param name="Source">Where the element was created.</param>
public sealed record ParagraphElement(ValueList<Span> Spans, SemanticRole Role, BoxFormat Box, TextFormat Format, SourceLocation Source) : Element(Source);

/// <summary>Forces the element after it to start on a new page. Made by <see cref="Elements.PageBreak"/>.</summary>
/// <param name="Source">Where the element was created.</param>
public sealed record PageBreakElement(SourceLocation Source) : Element(Source);

/// <summary>A piece of text inside a paragraph. Made by <see cref="Elements.Text"/> and <see cref="Elements.Bold"/>.</summary>
/// <param name="Text">The text.</param>
/// <param name="Format">Its formatting; null fields inherit from the paragraph.</param>
/// <param name="Source">Where the span was created.</param>
public sealed record Span(string Text, TextFormat Format, SourceLocation Source);
