namespace Ave.Documents.Core;

/// <summary>
/// How the lines of a paragraph are placed between its left and right edges: the CSS property
/// <c>text-align</c> (CSS Text 3 §6.1) for left-to-right text. Inherited; the initial value <c>start</c> is
/// <see cref="Left"/> in left-to-right text.
/// </summary>
public enum TextAlign
{
    /// <summary>Lines start at the left edge (<c>left</c>, and <c>start</c> in left-to-right text).</summary>
    Left,

    /// <summary>Lines end at the right edge (<c>right</c>).</summary>
    Right,

    /// <summary>Lines are centred between the edges (<c>center</c>).</summary>
    Center,

    /// <summary>
    /// Lines fill the width by widening the spaces between words (<c>justify</c> with <c>text-justify:
    /// inter-word</c>, CSS Text 3 §6.4). The last line, and a line that ends in a forced break, start at the left
    /// edge (<c>text-align-last: auto</c>).
    /// </summary>
    Justify,
}
