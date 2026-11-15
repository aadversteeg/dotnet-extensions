using System.Collections.Immutable;
using Ave.Documents.Fonts;
using Ave.Documents.Model;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Layout;

/// <summary>
/// What every layout call can see besides its own arguments: the fonts, and the table that maps a layout
/// algorithm id (<see cref="Box.Layout"/>) to its function. Immutable; a front-end with its own algorithm adds
/// it with <see cref="WithAlgorithm"/>.
/// </summary>
/// <param name="Fonts">The fonts text may use.</param>
/// <param name="Algorithms">The layout algorithms by id.</param>
public sealed record LayoutEnvironment(FontSet Fonts, ImmutableDictionary<LayoutAlgorithmId, LayoutAlgorithm> Algorithms)
{
    /// <summary>The environment with the stage-0 algorithms: <see cref="FlowLayout"/> and <see cref="InlineLayout"/>.</summary>
    /// <param name="fonts">The fonts text may use.</param>
    /// <returns>The environment.</returns>
    public static LayoutEnvironment Create(FontSet fonts) =>
        new(fonts, ImmutableDictionary<LayoutAlgorithmId, LayoutAlgorithm>.Empty
            .Add(LayoutAlgorithmId.Flow, FlowLayout.Layout)
            .Add(LayoutAlgorithmId.Inline, InlineLayout.Layout));

    /// <summary>Adds or replaces an algorithm.</summary>
    /// <param name="id">The algorithm id boxes name.</param>
    /// <param name="algorithm">The algorithm.</param>
    /// <returns>A new environment.</returns>
    public LayoutEnvironment WithAlgorithm(LayoutAlgorithmId id, LayoutAlgorithm algorithm) =>
        this with { Algorithms = Algorithms.SetItem(id, algorithm) };

    /// <summary>Lays out a box with the algorithm its <see cref="Box.Layout"/> names.</summary>
    /// <param name="box">The box.</param>
    /// <param name="space">The space available.</param>
    /// <param name="incoming">Where to resume, or null.</param>
    /// <returns>The algorithm's result, or <c>Documents.Layout.InvalidContent</c> for an unknown algorithm id.</returns>
    public Result<LayoutResult, Error> Layout(Box box, ConstraintSpace space, BreakToken? incoming) =>
        Algorithms.TryGetValue(box.Layout, out var algorithm)
            ? algorithm(this, box, space, incoming)
            : Result<LayoutResult, Error>.Failure(LayoutErrors.InvalidContent(box, "no layout algorithm with this id is registered."));
}
