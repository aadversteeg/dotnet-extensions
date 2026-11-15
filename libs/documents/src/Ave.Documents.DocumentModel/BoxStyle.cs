using Ave.Documents.Core;

namespace Ave.Documents.DocumentModel;

/// <summary>The computed box properties of a <see cref="Box"/> that stage 0 implements.</summary>
/// <param name="Margin">Space outside the border edge (CSS Box Model 3 §4). Not negative in stage 0.</param>
/// <param name="Padding">Space between the border edge and the content (CSS Box Model 3 §5). Never negative.</param>
/// <param name="Background">The background colour, or null for transparent (CSS Backgrounds 3 §3.2,
/// initial value <c>transparent</c>). Painted under the border box (initial <c>background-clip: border-box</c>).</param>
/// <param name="BreakBefore">Whether a page break is forced before the box (CSS Fragmentation 3 §3.1).</param>
public sealed record BoxStyle(Edges Margin, Edges Padding, Color? Background, BreakBefore BreakBefore)
{
    /// <summary>The CSS initial values: no margin, no padding, transparent, <c>break-before: auto</c>.</summary>
    public static BoxStyle Initial { get; } = new(Edges.Zero, Edges.Zero, null, BreakBefore.Auto);
}

/// <summary>The values of <c>break-before</c> (CSS Fragmentation 3 §3.1) that stage 0 implements.</summary>
public enum BreakBefore
{
    /// <summary>Neither force nor forbid a break before the box.</summary>
    Auto,

    /// <summary>Always force a page break before the box.</summary>
    Page,
}
