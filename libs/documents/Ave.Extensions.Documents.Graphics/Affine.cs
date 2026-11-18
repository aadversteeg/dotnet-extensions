namespace Ave.Extensions.Documents.Graphics;

/// <summary>
/// An affine transform of the plane, written as the six numbers PDF uses for a matrix (ISO 32000-1 §8.3.3,
/// [a b c d e f]): a point (x, y) maps to (a·x + c·y + e, b·x + d·y + f).
/// </summary>
/// <param name="A">The x scale (and rotation) part for x.</param>
/// <param name="B">The y part for x.</param>
/// <param name="C">The x part for y.</param>
/// <param name="D">The y scale (and rotation) part for y.</param>
/// <param name="E">The x translation.</param>
/// <param name="F">The y translation.</param>
public readonly record struct Affine(double A, double B, double C, double D, double E, double F)
{
    /// <summary>The transform that changes nothing.</summary>
    public static Affine Identity => new(1, 0, 0, 1, 0, 0);

    /// <summary>A scale about the origin.</summary>
    /// <param name="x">The factor for x.</param>
    /// <param name="y">The factor for y.</param>
    /// <returns>The transform.</returns>
    public static Affine Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary>A translation.</summary>
    /// <param name="x">The move in x.</param>
    /// <param name="y">The move in y.</param>
    /// <returns>The transform.</returns>
    public static Affine Translate(double x, double y) => new(1, 0, 0, 1, x, y);

    /// <summary>The determinant a·d − b·c: the factor by which the transform scales areas (negative when it mirrors).</summary>
    public double Determinant => (A * D) - (B * C);

    /// <summary>This transform followed by <paramref name="next"/>: a point is first mapped by this one, then by next.</summary>
    /// <param name="next">The transform applied second.</param>
    /// <returns>The combined transform.</returns>
    public Affine Then(Affine next) => new(
        (A * next.A) + (B * next.C),
        (A * next.B) + (B * next.D),
        (C * next.A) + (D * next.C),
        (C * next.B) + (D * next.D),
        (E * next.A) + (F * next.C) + next.E,
        (E * next.B) + (F * next.D) + next.F);

    /// <summary>The transform that undoes this one, or null when this one maps the plane onto a line or a point (its
    /// determinant is 0, or so small that the inverse is not finite).</summary>
    /// <returns>The inverse, or null.</returns>
    public Affine? Inverse()
    {
        var determinant = Determinant;
        var inverse = new Affine(
            D / determinant,
            -B / determinant,
            -C / determinant,
            A / determinant,
            ((C * F) - (D * E)) / determinant,
            ((B * E) - (A * F)) / determinant);
        return determinant != 0 && inverse.IsFinite ? inverse : null;
    }

    /// <summary>True when all six numbers are finite.</summary>
    public bool IsFinite =>
        double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C) && double.IsFinite(D) && double.IsFinite(E) && double.IsFinite(F);

    /// <summary>The image of a point's x coordinate.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>a·x + c·y + e.</returns>
    public double MapX(double x, double y) => (A * x) + (C * y) + E;

    /// <summary>The image of a point's y coordinate.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>b·x + d·y + f.</returns>
    public double MapY(double x, double y) => (B * x) + (D * y) + F;
}
