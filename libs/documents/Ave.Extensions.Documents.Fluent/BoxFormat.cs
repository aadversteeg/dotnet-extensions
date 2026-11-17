using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// The box properties of an element as specified (CSS: margin, padding, border, background and the break properties
/// are not inherited). Null means "not set".
/// </summary>
/// <remarks>
/// Each side has its own property, and <see cref="Margin"/> / <see cref="Padding"/> / <see cref="Border"/> set all four
/// sides at once. When both are set, the side wins, whichever modifier was called first: precedence is a fixed rule,
/// not call order, so that modifiers are order-free (see <see cref="BoxModifiers"/>).
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
/// <param name="Border">The border on all four sides.</param>
/// <param name="BorderTop">The top border.</param>
/// <param name="BorderRight">The right border.</param>
/// <param name="BorderBottom">The bottom border.</param>
/// <param name="BorderLeft">The left border.</param>
/// <param name="KeepTogether">True for <c>break-inside: avoid</c>.</param>
/// <param name="KeepWithNext">True for <c>break-after: avoid</c>.</param>
/// <param name="KeepWithPrevious">True for <c>break-before: avoid</c>.</param>
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
    Color? Background = null,
    BorderSide? Border = null,
    BorderSide? BorderTop = null,
    BorderSide? BorderRight = null,
    BorderSide? BorderBottom = null,
    BorderSide? BorderLeft = null,
    bool? KeepTogether = null,
    bool? KeepWithNext = null,
    bool? KeepWithPrevious = null)
{
    /// <summary>No box properties set.</summary>
    public static BoxFormat None { get; } = new();

    /// <summary>True when no property is set.</summary>
    public bool IsEmpty => this == None;
}
