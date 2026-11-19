namespace Ave.Extensions.Documents.Core;

/// <summary>
/// A line drawn along a run of text: one value of the CSS property <c>text-decoration-line</c> (CSS Text Decoration 3 §2.1).
/// Only solid lines of the automatic thickness and position exist (<c>text-decoration-style: solid</c>,
/// <c>text-decoration-thickness: auto</c>, <c>text-underline-offset: auto</c>, <c>text-underline-position: auto</c>).
/// </summary>
public enum TextDecorationLine
{
    /// <summary>A line under the text, below the baseline (<c>underline</c>).</summary>
    Underline,

    /// <summary>A line through the middle of the text (<c>line-through</c>).</summary>
    LineThrough,

    /// <summary>A line over the text, at its top (<c>overline</c>).</summary>
    Overline,
}
