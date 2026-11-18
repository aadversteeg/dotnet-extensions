using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The computed value of <c>line-height</c> (CSS 2.1 §10.8.1; CSS Inline 3 §5.1): the preferred height of an inline box,
/// which on a paragraph box is also the height of its strut, so the least height of its lines. An inherited property:
/// a number inherits as a number, so it scales with a descendant's own font size, while a length — and a percentage,
/// which the front-end computes to a length of the element's own font size — inherits as that length ("they inherit
/// differently: the first one inherits as a number, which will lead to different line heights if descendants have
/// different font sizes; the last two as inherit as absolute lengths", CSS Inline 3 §5.1).
/// </summary>
public abstract record LineHeight
{
    // The three cases are the whole set: layout handles every one of them.
    private LineHeight()
    {
    }

    /// <summary>The used line height for a font size, or null for <see cref="Normal"/>, whose height comes from the
    /// font's own metrics.</summary>
    /// <param name="fontSize">The font size of the box, in points.</param>
    /// <returns>The height in points, or null.</returns>
    public abstract double? HeightFor(double fontSize);

    /// <summary><c>normal</c>, the initial value: the font's ascent, descent and line gap (CSS Inline 3 §5.1: "Determine
    /// the preferred line height automatically based on the metrics of the used font"; §3.2.2 for the line gap).</summary>
    public sealed record Normal : LineHeight
    {
        private Normal()
        {
        }

        /// <summary>The one value.</summary>
        public static Normal Instance { get; } = new();

        /// <inheritdoc/>
        public override double? HeightFor(double fontSize) => null;

        /// <inheritdoc/>
        public override string ToString() => "normal";
    }

    /// <summary>A <c>&lt;number&gt;</c>: "The used value of the property is this number multiplied by the element's font
    /// size" (CSS 2.1 §10.8.1). Not negative.</summary>
    /// <param name="Value">The number.</param>
    public sealed record Factor(double Value) : LineHeight
    {
        /// <inheritdoc/>
        public override double? HeightFor(double fontSize) => Value * fontSize;

        /// <inheritdoc/>
        public override string ToString() => InvariantNumber.Format(Value);
    }

    /// <summary>A <c>&lt;length&gt;</c>, or a percentage computed to one: "The specified length is used in the calculation
    /// of the line box height" (CSS 2.1 §10.8.1). Not negative.</summary>
    /// <param name="Value">The length.</param>
    public sealed record Fixed(Length Value) : LineHeight
    {
        /// <inheritdoc/>
        public override double? HeightFor(double fontSize) => Value.Points;

        /// <inheritdoc/>
        public override string ToString() => Value.ToString();
    }
}
