namespace Ave.Documents.Core;

/// <summary>
/// A length on the page. Stored in PostScript points: 1/72 inch, the unit of PDF default user space
/// (ISO 32000-1 §8.3.2.3) and of CSS absolute lengths (CSS Values 4 §6.2: 1in = 2.54cm = 72pt).
/// Create one with <see cref="FromPoints"/>, <see cref="FromMillimetres"/>, <see cref="FromCentimetres"/>
/// or the <see cref="LengthExtensions"/> helpers (<c>12.Pt()</c>, <c>2.Cm()</c>).
/// </summary>
/// <remarks>
/// The constructor is private so that a bare number can never be mistaken for a length in an unknown unit.
/// Any value is representable, including negative and non-finite ones; each front-end validates the values
/// it accepts (for example "padding may not be negative") and reports an error with the caller's location.
/// </remarks>
public readonly record struct Length
{
    private const double PointsPerInch = 72.0;
    private const double MillimetresPerInch = 25.4;

    private Length(double points) => Points = points;

    /// <summary>The length in points (1/72 inch).</summary>
    public double Points { get; }

    /// <summary>A length of zero.</summary>
    public static Length Zero => default;

    /// <summary>True when the value is a real number (not NaN and not infinite).</summary>
    public bool IsFinite => double.IsFinite(Points);

    /// <summary>Creates a length from points.</summary>
    /// <param name="points">The length in points (1/72 inch).</param>
    /// <returns>The length.</returns>
    public static Length FromPoints(double points) => new(points);

    /// <summary>Creates a length from millimetres (CSS Values 4 §6.2: 1in = 25.4mm = 72pt).</summary>
    /// <param name="millimetres">The length in millimetres.</param>
    /// <returns>The length.</returns>
    public static Length FromMillimetres(double millimetres) => new(millimetres * PointsPerInch / MillimetresPerInch);

    /// <summary>Creates a length from centimetres (1cm = 10mm).</summary>
    /// <param name="centimetres">The length in centimetres.</param>
    /// <returns>The length.</returns>
    public static Length FromCentimetres(double centimetres) => FromMillimetres(centimetres * 10.0);

    /// <summary>Adds two lengths.</summary>
    /// <param name="left">The first length.</param>
    /// <param name="right">The second length.</param>
    /// <returns>The sum.</returns>
    public static Length operator +(Length left, Length right) => new(left.Points + right.Points);

    /// <summary>Subtracts one length from another.</summary>
    /// <param name="left">The length to subtract from.</param>
    /// <param name="right">The length to subtract.</param>
    /// <returns>The difference.</returns>
    public static Length operator -(Length left, Length right) => new(left.Points - right.Points);

    /// <summary>Scales a length, for example to compute "2em" from a font size.</summary>
    /// <param name="length">The length to scale.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled length.</returns>
    public static Length operator *(Length length, double factor) => new(length.Points * factor);

    /// <summary>Formats the length in points with at most three decimals, for example <c>12.5pt</c>.</summary>
    /// <returns>The text form of the length.</returns>
    public override string ToString() => $"{InvariantNumber.Format(Points)}pt";
}
