using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// What every layout call can see besides its own arguments: the fonts, the table that maps a layout algorithm id
/// (<see cref="Box.Layout"/>) to its functions, and the page being laid out. Immutable; the paginator gives each page
/// its own <see cref="Page"/>. The table holds the six algorithms the engine ships; adding one
/// (<see cref="WithAlgorithm(LayoutAlgorithmId, LayoutAlgorithm, IntrinsicSizing)"/>) is internal until a front-end
/// needs it (docs/architecture.md, "The layout contract").
/// </summary>
/// <param name="Fonts">The fonts text may use.</param>
/// <param name="Algorithms">The layout algorithms by id.</param>
/// <param name="Page">The page being laid out, for page counters.</param>
public sealed record LayoutEnvironment(FontSet Fonts, ImmutableDictionary<LayoutAlgorithmId, RegisteredAlgorithm> Algorithms, PageContext Page)
{
    /// <summary>The layout policies of the document being laid out (<see cref="StyledDocument.Profile"/>): the
    /// paginator takes them from the document, so a front-end chooses them in the document model and the entry points
    /// (<see cref="DocumentLayout.Run(StyledDocument, FontSet)"/>) stay as they are.</summary>
    public LayoutProfile Profile { get; internal init; } = LayoutProfile.Default;

    /// <summary>
    /// The default of <see cref="MaxPages"/>: 10 000 pages. The longest document of the samples, a timesheet of
    /// 1 000 times its usual length, has 4 667.
    /// </summary>
    public const int DefaultMaxPages = 10_000;

    /// <summary>
    /// The most pages a document may have (default <see cref="DefaultMaxPages"/>). The work of a layout grows with its
    /// pages, and a few bytes of input can ask for a page each, so the paginator stops before the first page past the
    /// limit: <c>Documents.Layout.TooManyPages</c>. Raise it to render a longer document. A value below 1 is refused
    /// when the layout runs (<c>Documents.Layout.InvalidOptions</c>).
    /// </summary>
    public int MaxPages { get; init; } = DefaultMaxPages;

    /// <summary>How the layout's messages name <see cref="MaxPages"/> (default <c>LayoutEnvironment.MaxPages</c>): a
    /// render call that sets the limit from an option or parameter of its own sets the name of that one, so that the
    /// refusal of a document (<c>Documents.Layout.TooManyPages</c>) or of the value (<c>Documents.Layout.InvalidOptions</c>)
    /// names what its caller sets.</summary>
    public string MaxPagesName { get; init; } = "LayoutEnvironment.MaxPages";

    /// <summary>The environment with the engine's algorithms: <see cref="FlowLayout"/>, <see cref="InlineLayout"/>,
    /// <see cref="TracksLayout"/>, <see cref="ImageLayout"/>, <see cref="MarginBoxesLayout"/> and <see cref="ListItemLayout"/>; page 1 of 1.</summary>
    /// <param name="fonts">The fonts text may use.</param>
    /// <returns>The environment.</returns>
    public static LayoutEnvironment Create(FontSet fonts) => new(fonts, EngineAlgorithms, PageContext.First);

    /// <summary>Whether every registered algorithm is one the engine ships. Those read the page context only to show a
    /// page counter's text (<see cref="InlineLayout"/>), so a body without a page-count counter cannot depend on the
    /// page count, which lets the paginator lay it out once (<see cref="Paginator.Paginate"/>, "One body pass").</summary>
    internal bool HasOnlyEngineAlgorithms =>
        Algorithms.All(pair => EngineAlgorithms.TryGetValue(pair.Key, out var own) && own == pair.Value);

    private static readonly ImmutableDictionary<LayoutAlgorithmId, RegisteredAlgorithm> EngineAlgorithms =
        ImmutableDictionary<LayoutAlgorithmId, RegisteredAlgorithm>.Empty
            .Add(LayoutAlgorithmId.Flow, new RegisteredAlgorithm(FlowLayout.Layout, FlowLayout.Measure))
            .Add(LayoutAlgorithmId.Inline, new RegisteredAlgorithm(InlineLayout.Layout, InlineLayout.Measure))
            .Add(LayoutAlgorithmId.Tracks, new RegisteredAlgorithm(TracksLayout.Layout, TracksLayout.Measure))
            .Add(LayoutAlgorithmId.Image, new RegisteredAlgorithm(ImageLayout.Layout, ImageLayout.Measure))
            .Add(LayoutAlgorithmId.MarginBoxes, new RegisteredAlgorithm(MarginBoxesLayout.Layout, MarginBoxesLayout.Measure))
            .Add(LayoutAlgorithmId.ListItem, new RegisteredAlgorithm(ListItemLayout.Layout, ListItemLayout.Measure));

    /// <summary>Adds or replaces an algorithm.</summary>
    /// <param name="id">The algorithm id boxes name.</param>
    /// <param name="algorithm">The layout function.</param>
    /// <param name="measure">Its intrinsic widths.</param>
    /// <returns>A new environment.</returns>
    internal LayoutEnvironment WithAlgorithm(LayoutAlgorithmId id, LayoutAlgorithm algorithm, IntrinsicSizing measure) =>
        this with { Algorithms = Algorithms.SetItem(id, new RegisteredAlgorithm(algorithm, measure)) };

    /// <summary>Adds or replaces an algorithm that cannot be measured: putting one of its boxes in an <c>auto</c> or
    /// <c>fill</c> column is a <c>Documents.Layout.InvalidContent</c> error.</summary>
    /// <param name="id">The algorithm id boxes name.</param>
    /// <param name="algorithm">The layout function.</param>
    /// <returns>A new environment.</returns>
    internal LayoutEnvironment WithAlgorithm(LayoutAlgorithmId id, LayoutAlgorithm algorithm) =>
        WithAlgorithm(id, algorithm, (_, box) =>
            Result<IntrinsicWidths, Error>.Failure(LayoutErrors.InvalidContent(box, "this algorithm has no intrinsic widths, so its boxes cannot be sized from content.")));

    /// <summary>The same environment for another page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>A new environment.</returns>
    public LayoutEnvironment OnPage(PageContext page) => this with { Page = page };

    /// <summary>Lays out a box with the algorithm its <see cref="Box.Layout"/> names.</summary>
    /// <param name="box">The box.</param>
    /// <param name="space">The space available.</param>
    /// <param name="incoming">Where to resume, or null.</param>
    /// <returns>The algorithm's result, or <c>Documents.Layout.InvalidContent</c> for an unknown algorithm id.</returns>
    public Result<LayoutResult, Error> Layout(Box box, ConstraintSpace space, BreakToken? incoming) =>
        Algorithms.TryGetValue(box.Layout, out var algorithm)
            ? algorithm.Layout(this, box, space, incoming)
            : Result<LayoutResult, Error>.Failure(UnknownAlgorithm(box));

    /// <summary>Measures a box with the algorithm its <see cref="Box.Layout"/> names.</summary>
    /// <param name="box">The box.</param>
    /// <returns>The box's intrinsic widths, or an error.</returns>
    public Result<IntrinsicWidths, Error> Measure(Box box) =>
        Algorithms.TryGetValue(box.Layout, out var algorithm)
            ? algorithm.Measure(this, box)
            : Result<IntrinsicWidths, Error>.Failure(UnknownAlgorithm(box));

    private static Error UnknownAlgorithm(Box box) =>
        LayoutErrors.InvalidContent(box, "no layout algorithm with this id is registered.");
}

/// <summary>The two functions of one layout algorithm.</summary>
/// <param name="Layout">Lays a box out on a page.</param>
/// <param name="Measure">Gives a box's intrinsic widths.</param>
public sealed record RegisteredAlgorithm(LayoutAlgorithm Layout, IntrinsicSizing Measure);
