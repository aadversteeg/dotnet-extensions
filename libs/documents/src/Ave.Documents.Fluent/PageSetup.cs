using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>The size and margins of the pages (CSS Paged Media 3 §3: <c>@page { size; margin }</c>).</summary>
/// <param name="Width">The page width.</param>
/// <param name="Height">The page height.</param>
/// <param name="Margins">The page margins.</param>
public sealed record PageSetup(Length Width, Length Height, Edges Margins)
{
    /// <summary>ISO 216 A4, 210 × 297 mm, portrait, no margins. Set margins with
    /// <c>PageSetup.A4 with { Margins = Edges.All(2.Cm()) }</c>.</summary>
    public static PageSetup A4 { get; } = new(210.Mm(), 297.Mm(), Edges.Zero);
}
