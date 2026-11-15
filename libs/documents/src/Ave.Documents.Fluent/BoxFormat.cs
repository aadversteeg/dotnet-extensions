using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>The box properties of a block or paragraph. Not inherited (CSS: margin, padding and background
/// are not inherited properties); null means the CSS initial value.</summary>
/// <param name="Margin">Space outside the box; zero when null.</param>
/// <param name="Padding">Space between the box edge and its content; zero when null.</param>
/// <param name="Background">The background colour; transparent when null.</param>
public sealed record BoxFormat(Edges? Margin = null, Edges? Padding = null, Color? Background = null)
{
    /// <summary>No box properties set.</summary>
    public static BoxFormat None { get; } = new();
}
