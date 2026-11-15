using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// The defaults the code front-end applies before the author's own formatting, like a browser's user-agent
/// style sheet (CSS Cascade 4 §6.1: user-agent origin, overridden by the author origin).
/// </summary>
public static class UserAgentStyles
{
    /// <summary>The CSS initial value of <c>font-size</c>: <c>medium</c>, 16px = 12pt (CSS Fonts 4 §2.5).</summary>
    public static Length InitialFontSize => 12.Pt();

    /// <summary>
    /// Heading font size as a multiple of the inherited size, from the HTML Standard's rendering section
    /// ("Sections and headings": h1 2em, h2 1.5em, h3 1.17em, h4 1em, h5 0.83em, h6 0.67em). Headings are also
    /// bold there. The HTML margins (h1 0.67em …) are NOT applied: they assume CSS margin collapsing, which
    /// stage 0 does not do, and would give double gaps.
    /// </summary>
    /// <param name="level">The heading level, 1–6.</param>
    /// <returns>The factor.</returns>
    public static double HeadingSizeFactor(int level) => level switch
    {
        1 => 2.0,
        2 => 1.5,
        3 => 1.17,
        4 => 1.0,
        5 => 0.83,
        _ => 0.67,
    };
}
