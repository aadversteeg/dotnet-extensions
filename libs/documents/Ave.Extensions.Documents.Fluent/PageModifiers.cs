using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// Modifiers for <see cref="PageSetup"/>: <c>PageSetup.A4.Margin(1.Cm()).Header(logo).Footer(pageOf)</c>. Each returns a
/// new value with one property set; the order does not matter.
/// </summary>
public static class PageModifiers
{
    /// <summary>Sets the same margin on all four sides of the page.</summary>
    /// <param name="page">The page setup.</param>
    /// <param name="all">The margin; zero or more.</param>
    /// <returns>A copy with the margins set.</returns>
    public static PageSetup Margin(this PageSetup page, Length all) => page with { Margins = Edges.All(all) };

    /// <summary>Sets the four page margins.</summary>
    /// <param name="page">The page setup.</param>
    /// <param name="margins">The margins: top, right, bottom, left.</param>
    /// <returns>A copy with the margins set.</returns>
    public static PageSetup Margins(this PageSetup page, Edges margins) => page with { Margins = margins };

    /// <summary>
    /// Puts a header at the top of every page's area (inside the margins); the body starts below it. The header may
    /// hold <see cref="Elements.PageNumber"/> and <see cref="Elements.PageCount"/>.
    /// </summary>
    /// <param name="page">The page setup.</param>
    /// <param name="content">The header's content.</param>
    /// <param name="height">A fixed height, or null for the content's own height.</param>
    /// <returns>A copy with the header set.</returns>
    public static PageSetup Header(this PageSetup page, Element content, Length? height = null) =>
        page with { Header = new PageAreaSetup(content, height) };

    /// <summary>Puts a footer at the bottom of every page's area (inside the margins); the body ends above it.</summary>
    /// <param name="page">The page setup.</param>
    /// <param name="content">The footer's content.</param>
    /// <param name="height">A fixed height, or null for the content's own height.</param>
    /// <returns>A copy with the footer set.</returns>
    public static PageSetup Footer(this PageSetup page, Element content, Length? height = null) =>
        page with { Footer = new PageAreaSetup(content, height) };

    /// <summary>A different header on the first page (CSS <c>@page :first</c>). To show no header there, pass an empty
    /// block: <c>Block([])</c>.</summary>
    /// <param name="page">The page setup.</param>
    /// <param name="content">The first page's header.</param>
    /// <param name="height">A fixed height, or null for the content's own height.</param>
    /// <returns>A copy with the first-page header set.</returns>
    public static PageSetup FirstPageHeader(this PageSetup page, Element content, Length? height = null) =>
        page with { FirstPageHeader = new PageAreaSetup(content, height) };

    /// <summary>A different footer on the first page (CSS <c>@page :first</c>).</summary>
    /// <param name="page">The page setup.</param>
    /// <param name="content">The first page's footer.</param>
    /// <param name="height">A fixed height, or null for the content's own height.</param>
    /// <returns>A copy with the first-page footer set.</returns>
    public static PageSetup FirstPageFooter(this PageSetup page, Element content, Length? height = null) =>
        page with { FirstPageFooter = new PageAreaSetup(content, height) };
}
