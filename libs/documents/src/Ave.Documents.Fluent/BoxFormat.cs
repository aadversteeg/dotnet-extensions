using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// The box properties of a block or paragraph as specified (CSS: margin, padding and background are not
/// inherited). Null means "not set".
/// </summary>
/// <remarks>
/// Each side has its own property, and <see cref="Margin"/> / <see cref="Padding"/> set all four sides at once.
/// When both are set, the side wins, whichever modifier was called first: precedence is a fixed rule, not call
/// order, so that modifiers are order-free (see <see cref="BoxModifiers"/>).
/// </remarks>
/// <param name="Margin">The margin on all four sides.</param>
/// <param name="MarginTop">The top margin.</param>
/// <param name="MarginRight">The right margin.</param>
/// <param name="MarginBottom">The bottom margin.</param>
/// <param name="MarginLeft">The left margin.</param>
/// <param name="Padding">The padding on all four sides.</param>
/// <param name="PaddingTop">The top padding.</param>
/// <param name="PaddingRight">The right padding.</param>
/// <param name="PaddingBottom">The bottom padding.</param>
/// <param name="PaddingLeft">The left padding.</param>
/// <param name="Background">The background colour.</param>
public sealed record BoxFormat(
    Length? Margin = null,
    Length? MarginTop = null,
    Length? MarginRight = null,
    Length? MarginBottom = null,
    Length? MarginLeft = null,
    Length? Padding = null,
    Length? PaddingTop = null,
    Length? PaddingRight = null,
    Length? PaddingBottom = null,
    Length? PaddingLeft = null,
    Color? Background = null)
{
    /// <summary>No box properties set.</summary>
    public static BoxFormat None { get; } = new();

    /// <summary>True when no property is set.</summary>
    public bool IsEmpty => this == None;
}
