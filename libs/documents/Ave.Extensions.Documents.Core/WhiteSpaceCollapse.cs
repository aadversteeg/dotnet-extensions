namespace Ave.Extensions.Documents.Core;

/// <summary>
/// Whether and how white space in a text is collapsed: the CSS property <c>white-space-collapse</c> (CSS Text 4), set with
/// <c>text-wrap-mode</c> by the shorthand <c>white-space</c>. Inherited; the initial value is <see cref="Collapse"/>. The values
/// are those Chromium 159 prints (case <c>ws-longhands</c>): it prints <c>preserve-spaces</c> and <c>discard</c> as
/// <c>collapse</c>, so they are not here.
/// </summary>
public enum WhiteSpaceCollapse
{
    /// <summary>Sequences of white space collapse to one space, and a line feed is a space (<c>collapse</c>; <c>white-space:
    /// normal</c> and <c>nowrap</c>).</summary>
    Collapse,

    /// <summary>Spaces are kept as they are, and a line feed is a forced line break (<c>preserve</c>; <c>white-space: pre</c> and
    /// <c>pre-wrap</c>).</summary>
    Preserve,

    /// <summary>Spaces collapse, but a line feed is a forced line break (<c>preserve-breaks</c>; <c>white-space: pre-line</c>).</summary>
    PreserveBreaks,
}
