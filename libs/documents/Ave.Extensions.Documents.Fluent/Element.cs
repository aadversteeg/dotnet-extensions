using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// A piece of a document as the author wrote it: an immutable value, created by the functions in
/// <see cref="Elements"/> and shaped by the modifiers in <see cref="TextModifiers"/> and
/// <see cref="BoxModifiers"/>. Elements hold SPECIFIED values (what the author set, or null to inherit); the
/// translation to the document model computes the final values (<see cref="DocumentTranslator"/>).
/// </summary>
/// <remarks>
/// Formatting comes from two places, and the precedence between them is fixed, so it never depends on the order
/// of the calls: the element's own <c>Format</c> (set by modifiers) wins over its shared <c>Style</c> (set by
/// <c>.With(style)</c>), which wins over what the parent passes down.
/// </remarks>
public abstract record Element
{
    // Only the cases in this assembly exist, so the translator handles every one.
    private protected Element(SourceLocation source) => Source = source;

    /// <summary>Where the element was created: the file and line of the call.</summary>
    public SourceLocation Source { get; }
}

/// <summary>A block: stacks its children vertically. Made by <see cref="Elements.Block"/>.</summary>
/// <param name="Children">The child elements.</param>
/// <param name="Format">What the modifiers set on this block.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>, or <see cref="Fluent.Style.Empty"/>.</param>
/// <param name="Source">Where the element was created.</param>
public sealed record BlockElement(ValueList<Element> Children, Style Format, Style Style, SourceLocation Source)
    : Element(Source), IBoxFormatted<BlockElement>
{
    /// <summary>Applies a shared style. The block's own modifier values still win over it.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public BlockElement With(Style style) => this with { Style = style };

    /// <inheritdoc/>
    BlockElement IFormatted<BlockElement>.WithFormat(Style format) => this with { Format = format };
}

/// <summary>A paragraph or heading: lines of text. Made by <see cref="Elements.Paragraph(string, string, int)"/>
/// and <see cref="Elements.Heading"/>.</summary>
/// <param name="Spans">The text, in pieces that may each have their own formatting.</param>
/// <param name="Role">Paragraph or heading.</param>
/// <param name="Format">What the modifiers set on this paragraph.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>, or <see cref="Fluent.Style.Empty"/>.</param>
/// <param name="Source">Where the element was created.</param>
public sealed record ParagraphElement(ValueList<Span> Spans, SemanticRole Role, Style Format, Style Style, SourceLocation Source)
    : Element(Source), IBoxFormatted<ParagraphElement>
{
    /// <summary>Applies a shared style. The paragraph's own modifier values still win over it.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public ParagraphElement With(Style style) => this with { Style = style };

    /// <summary>The name links lead here by (<see cref="Anchor"/>, <see cref="Elements.LinkTo"/>), or null.</summary>
    public string? AnchorName { get; internal init; }

    /// <summary>Makes this paragraph or heading a place a link inside the document leads to: <c>LinkTo(name, text)</c>
    /// anywhere in the body leads to the top of its first line box's paragraph, on the page where it starts. A name is
    /// unique in the document; it is never written into the file.</summary>
    /// <param name="name">The name, 1 to <see cref="Elements.MaxAnchorLength"/> characters.</param>
    /// <returns>A copy with the anchor.</returns>
    public ParagraphElement Anchor(string name) => this with { AnchorName = name };

    /// <inheritdoc/>
    ParagraphElement IFormatted<ParagraphElement>.WithFormat(Style format) => this with { Format = format };
}

/// <summary>Forces the element after it to start on a new page. Made by <see cref="Elements.PageBreak"/>.</summary>
/// <param name="Source">Where the element was created.</param>
public sealed record PageBreakElement(SourceLocation Source) : Element(Source);

/// <summary>
/// A piece of text inside a paragraph. Made by <see cref="Elements.Text"/> and <see cref="Elements.Bold"/>, or, for
/// text that only layout knows, by <see cref="Elements.PageNumber"/> and <see cref="Elements.PageCount"/>; or an image in
/// the line, made by <see cref="Elements.InlineImage"/>.
/// Only text modifiers apply to a span; a shared style with box properties is reported as an input error.
/// </summary>
/// <param name="Text">The text; empty for a page counter.</param>
/// <param name="Format">What the modifiers set on this span.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>, or <see cref="Fluent.Style.Empty"/>.</param>
/// <param name="Source">Where the span was created.</param>
public sealed record Span(string Text, Style Format, Style Style, SourceLocation Source) : IFormatted<Span>
{
    /// <summary>The page counter the span shows instead of <see cref="Text"/>, or null for literal text.</summary>
    public DocumentModel.PageCounter? Counter { get; init; }

    /// <summary>The image the span shows in the line instead of text, or null for text.</summary>
    public ImageElement? Image { get; init; }

    /// <summary>Where the span leads when it is a link (<see cref="Elements.Link"/>), or null. Set only by
    /// <see cref="Elements.Link"/>: one span is one link.</summary>
    public LinkTarget? Link { get; internal init; }

    /// <summary>The anchor the span leads to when it is a link inside the document (<see cref="Elements.LinkTo"/>), or
    /// null. Set only by <see cref="Elements.LinkTo"/>.</summary>
    public string? LinkAnchor { get; internal init; }

    /// <summary>Applies a shared style (its text part). The span's own modifier values still win over it.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public Span With(Style style) => this with { Style = style };

    /// <inheritdoc/>
    Span IFormatted<Span>.WithFormat(Style format) => this with { Format = format };
}
