using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// The pages: size and margins (CSS Paged Media 3 §3: <c>@page { size; margin }</c>) and what every page repeats — a
/// header and a footer, optionally different on the first page (<c>@page :first</c>). Set with the
/// <see cref="PageModifiers"/>: <c>PageSetup.A4.Margin(1.Cm()).Header(…).Footer(…)</c>.
/// </summary>
/// <param name="Width">The page width.</param>
/// <param name="Height">The page height.</param>
/// <param name="Margins">The page margins.</param>
public sealed record PageSetup(Length Width, Length Height, Edges Margins)
{
    /// <summary>ISO 216 A4, 210 × 297 mm, portrait, no margins.</summary>
    public static PageSetup A4 { get; } = new(210.Mm(), 297.Mm(), Edges.Zero);

    /// <summary>The header of every page, or null.</summary>
    public PageAreaSetup? Header { get; init; }

    /// <summary>The footer of every page, or null.</summary>
    public PageAreaSetup? Footer { get; init; }

    /// <summary>The header of the first page instead of <see cref="Header"/>, or null to use <see cref="Header"/>.</summary>
    public PageAreaSetup? FirstPageHeader { get; init; }

    /// <summary>The footer of the first page instead of <see cref="Footer"/>, or null to use <see cref="Footer"/>.</summary>
    public PageAreaSetup? FirstPageFooter { get; init; }
}

/// <summary>A header or footer: its content and, optionally, a fixed height.</summary>
/// <param name="Content">The content; any element, with page counters allowed in its text.</param>
/// <param name="Height">A fixed height, or null to take the content's height. Content taller than a fixed height is an
/// error.</param>
public sealed record PageAreaSetup(Element Content, Length? Height);
