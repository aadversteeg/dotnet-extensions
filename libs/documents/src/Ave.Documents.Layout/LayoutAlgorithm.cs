using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>
/// THE LAYOUT CONTRACT. Every layout algorithm is a pure function with this shape: given a box, the space it
/// may use and where to resume, it returns the fragment for this page and where to resume on the next one.
/// Modelled on Chromium LayoutNG's layout algorithms (research §6.1) and the CSS Layout API.
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
/// <param name="environment">The fonts and the table of algorithms.</param>
/// <param name="box">The box to lay out.</param>
/// <param name="space">The space available on the current page.</param>
/// <param name="incoming">Where to resume, or null to start at the beginning of the box.</param>
/// <returns>The fragment and continuation, NoRoom, or an error.</returns>
public delegate Result<LayoutResult, Error> LayoutAlgorithm(
    LayoutEnvironment environment, Box box, ConstraintSpace space, BreakToken? incoming);
