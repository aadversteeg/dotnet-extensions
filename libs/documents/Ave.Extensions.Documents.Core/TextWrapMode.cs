namespace Ave.Extensions.Documents.Core;

/// <summary>
/// Whether text wraps at its soft wrap opportunities: the CSS property <c>text-wrap-mode</c> (CSS Text 4 §5.1, "This property
/// specifies whether inline-level content wraps at unforced soft wrap opportunities"), set with <c>white-space-collapse</c> by
/// the shorthand <c>white-space</c>. Inherited; the initial value is <see cref="Wrap"/>. Forced line breaks break either way.
/// </summary>
public enum TextWrapMode
{
    /// <summary>Lines may break at soft wrap opportunities (<c>wrap</c>; <c>white-space: normal</c>, <c>pre-wrap</c>,
    /// <c>pre-line</c>).</summary>
    Wrap,

    /// <summary>Lines do not break at soft wrap opportunities (<c>nowrap</c>; <c>white-space: nowrap</c> and <c>pre</c>): the text
    /// stays on one line up to a forced break, and overflows its box when it is wider.</summary>
    NoWrap,
}
