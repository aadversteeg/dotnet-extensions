using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// A width of a block box other than <c>auto</c> (CSS Sizing 3 §3.1, <c>&lt;length-percentage [0,∞]&gt;</c>), for
/// <see cref="BoxStyle.SpecifiedWidth"/>, <see cref="BoxStyle.MinWidth"/> and <see cref="BoxStyle.MaxWidth"/>. It
/// measures the content box or the border box, as <see cref="BoxStyle.BoxSizing"/> says.
/// </summary>
public abstract record SizeValue
{
    // The two cases are the whole set: layout resolves both.
    private SizeValue()
    {
    }

    /// <summary>A length (CSS Values 4 §6). Not negative, at most <see cref="ModelLimits.MaxLength"/>.</summary>
    /// <param name="Width">The length.</param>
    public sealed record Fixed(Length Width) : SizeValue;

    /// <summary>A percentage of the width of the containing block's content box (CSS 2.1 §10.2: "The percentage is
    /// calculated with respect to the width of the generated box's containing block"). Not negative, finite.</summary>
    /// <param name="Percent">The percentage: 50 is half the containing block's width.</param>
    public sealed record Percentage(double Percent) : SizeValue;
}

/// <summary>The values of <see cref="BoxStyle.BoxSizing"/>: which box a width measures (CSS Box Model 3 §4.3,
/// <c>box-sizing</c>).</summary>
public enum BoxSizing
{
    /// <summary>The content box: padding and border are added to it (<c>content-box</c>, the initial value).</summary>
    ContentBox,

    /// <summary>The border box: padding and border are inside it, and the content box is what they leave, never less
    /// than 0 (<c>border-box</c>).</summary>
    BorderBox,
}

/// <summary>The values of <see cref="BoxStyle.AutoMargins"/>: which horizontal margins are <c>auto</c> (CSS 2.1
/// §10.3.3). <see cref="BoxStyle.Margin"/> holds 0 for an auto side.</summary>
public enum AutoMargins
{
    /// <summary>Neither (the initial value).</summary>
    None,

    /// <summary><c>margin-left: auto</c>: the box goes to the right end of its containing block.</summary>
    Left,

    /// <summary><c>margin-right: auto</c>: the box stays at its left margin, as without it.</summary>
    Right,

    /// <summary>Both: the box is centred in its containing block.</summary>
    Both,
}
