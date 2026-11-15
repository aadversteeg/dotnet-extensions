using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>
/// THE LAYOUT CONTRACT. Every layout algorithm is a pure function with this shape: given a box, the space it
/// may use and where to resume, it returns the fragment for this page and where to resume on the next one.
/// Modelled on Chromium LayoutNG's layout algorithms and the CSS Layout API (docs/architecture.md, Background).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Pure.</b> No state survives a call; everything a later page needs is in the returned break token.
/// The same arguments always give the same result.</item>
/// <item><b>Fragmentation.</b> <paramref name="incoming"/> is null for the first fragment of the box, or the
/// token this same algorithm returned for the previous page. The result's <see cref="LayoutResult.Placed.Continuation"/>
/// is null when the box is complete.</item>
/// <item><b>Progress.</b> When nothing of the box fits, the algorithm returns <see cref="LayoutResult.NoRoom"/>
/// — unless <see cref="ConstraintSpace.IsAtPageStart"/> is true: then nothing fits on any page and it must fail
/// with <c>Documents.Layout.DoesNotFit</c>. This guarantees that pagination always ends.</item>
/// <item><b>Children.</b> The algorithm lays out child boxes through <see cref="LayoutEnvironment.Layout"/>, which
/// picks each child's own algorithm by its id.</item>
/// </list>
/// </remarks>
/// <param name="environment">The fonts, the table of algorithms and the current page.</param>
/// <param name="box">The box to lay out.</param>
/// <param name="space">The space available on the current page.</param>
/// <param name="incoming">Where to resume, or null to start at the beginning of the box.</param>
/// <returns>The fragment and continuation, NoRoom, or an error.</returns>
public delegate Result<LayoutResult, Error> LayoutAlgorithm(
    LayoutEnvironment environment, Box box, ConstraintSpace space, BreakToken? incoming);

/// <summary>
/// The second function of every algorithm (LayoutNG's <c>MinMax</c>): the box's intrinsic widths, which a
/// parent that sizes its children from their content needs — the tracks algorithm for <c>auto</c> and <c>fill</c>
/// columns. Pure, like <see cref="LayoutAlgorithm"/>.
/// </summary>
/// <param name="environment">The fonts, the table of algorithms and the current page.</param>
/// <param name="box">The box to measure.</param>
/// <returns>The widths, or an error (for example a missing glyph).</returns>
public delegate Result<IntrinsicWidths, Error> IntrinsicSizing(LayoutEnvironment environment, Box box);

/// <summary>
/// The intrinsic widths of a box's BORDER box (padding and border included, margins not), CSS Sizing 3 §5: the
/// min-content width is the narrowest the box can be without its content overflowing (for text: the widest word),
/// the max-content width the width it takes when nothing wraps.
/// </summary>
/// <param name="MinContent">The min-content width, in points.</param>
/// <param name="MaxContent">The max-content width, at least <paramref name="MinContent"/>.</param>
public sealed record IntrinsicWidths(double MinContent, double MaxContent)
{
    /// <summary>A box with no content and no insets.</summary>
    public static IntrinsicWidths Zero { get; } = new(0, 0);

    /// <summary>Both widths grown by the same amount (insets or margins).</summary>
    /// <param name="amount">The amount, in points.</param>
    /// <returns>The wider widths.</returns>
    public IntrinsicWidths Plus(double amount) => new(MinContent + amount, MaxContent + amount);
}
