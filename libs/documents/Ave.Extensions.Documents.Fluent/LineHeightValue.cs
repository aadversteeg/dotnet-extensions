using System.Globalization;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// A <c>line-height</c> as the author specifies it (CSS 2.1 §10.8.1: <c>normal | &lt;number&gt; | &lt;length&gt; |
/// &lt;percentage&gt;</c>), set with <see cref="TextModifiers.LineHeight{T}(T, double)"/> and its overloads. The
/// translator computes it per element (<see cref="Computed"/>): a percentage becomes a length of the element's own font
/// size, which then inherits as that length; a number stays a number and scales with each descendant's font size.
/// </summary>
public abstract record LineHeightValue
{
    // The four cases of CSS 2.1 are the whole set.
    private LineHeightValue()
    {
    }

    /// <summary>The computed value (CSS 2.1 §10.8.1, "Computed value: for &lt;length&gt; and &lt;percentage&gt; the absolute
    /// value; otherwise as specified").</summary>
    /// <param name="fontSize">The element's computed font size.</param>
    /// <returns>The computed line height.</returns>
    internal abstract LineHeight Computed(Length fontSize);

    /// <summary>The value as a check reports it.</summary>
    internal abstract string Describe();

    /// <summary>Whether the value is allowed: not negative ("Negative values are illegal"), finite, and at most
    /// <see cref="InputLimits.MaxLength"/> (in points for a length, as a multiple of the font size for a number, in
    /// percent of it divided by 100 for a percentage), the bound of every length in the front-end.</summary>
    internal abstract bool IsValid { get; }

    /// <summary><c>normal</c>: the font's own line spacing.</summary>
    public sealed record Normal : LineHeightValue
    {
        private Normal()
        {
        }

        /// <summary>The one value.</summary>
        public static Normal Instance { get; } = new();

        internal override LineHeight Computed(Length fontSize) => LineHeight.Normal.Instance;

        internal override string Describe() => "normal";

        internal override bool IsValid => true;
    }

    /// <summary>A <c>&lt;number&gt;</c>: a multiple of the font size, inherited as the number.</summary>
    /// <param name="Value">The number.</param>
    public sealed record Number(double Value) : LineHeightValue
    {
        internal override LineHeight Computed(Length fontSize) => new LineHeight.Factor(Value);

        internal override string Describe() => Value.ToString(CultureInfo.InvariantCulture);

        internal override bool IsValid => double.IsFinite(Value) && Value >= 0 && Value <= InputLimits.MaxLength.Points;
    }

    /// <summary>A <c>&lt;length&gt;</c>.</summary>
    /// <param name="Value">The length.</param>
    public sealed record Fixed(Length Value) : LineHeightValue
    {
        internal override LineHeight Computed(Length fontSize) => new LineHeight.Fixed(Value);

        internal override string Describe() => Value.ToString();

        internal override bool IsValid => double.IsFinite(Value.Points) && Value.Points >= 0 && Value.Points <= InputLimits.MaxLength.Points;
    }

    /// <summary>A <c>&lt;percentage&gt;</c> of the element's own font size, inherited as the length it computes to.</summary>
    /// <param name="Percent">The percentage, for example 120 for <c>120%</c>.</param>
    public sealed record Percentage(double Percent) : LineHeightValue
    {
        internal override LineHeight Computed(Length fontSize) => new LineHeight.Fixed(Length.FromPoints(Percent / 100 * fontSize.Points));

        internal override string Describe() => Percent.ToString(CultureInfo.InvariantCulture) + "%";

        internal override bool IsValid => double.IsFinite(Percent) && Percent >= 0 && Percent / 100 <= InputLimits.MaxLength.Points;
    }
}
