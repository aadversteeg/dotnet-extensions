namespace Ave.Documents.Core;

/// <summary>
/// Short unit helpers so code reads like a style sheet: <c>12.Pt()</c>, <c>2.5.Cm()</c>, <c>15.Mm()</c>.
/// </summary>
/// <remarks>
/// There is an <see cref="int"/> and a <see cref="double"/> overload of each because C# does not apply the
/// implicit int-to-double conversion to the receiver of an extension method, so <c>12.Pt()</c> would not compile
/// with only the double version.
/// </remarks>
public static class LengthExtensions
{
    /// <summary>A length in points.</summary>
    /// <param name="value">The number of points.</param>
    /// <returns>The length.</returns>
    public static Length Pt(this double value) => Length.FromPoints(value);

    /// <summary>A length in points.</summary>
    /// <param name="value">The number of points.</param>
    /// <returns>The length.</returns>
    public static Length Pt(this int value) => Length.FromPoints(value);

    /// <summary>A length in millimetres.</summary>
    /// <param name="value">The number of millimetres.</param>
    /// <returns>The length.</returns>
    public static Length Mm(this double value) => Length.FromMillimetres(value);

    /// <summary>A length in millimetres.</summary>
    /// <param name="value">The number of millimetres.</param>
    /// <returns>The length.</returns>
    public static Length Mm(this int value) => Length.FromMillimetres(value);

    /// <summary>A length in centimetres.</summary>
    /// <param name="value">The number of centimetres.</param>
    /// <returns>The length.</returns>
    public static Length Cm(this double value) => Length.FromCentimetres(value);

    /// <summary>A length in centimetres.</summary>
    /// <param name="value">The number of centimetres.</param>
    /// <returns>The length.</returns>
    public static Length Cm(this int value) => Length.FromCentimetres(value);
}
