namespace Ave.Extensions.Documents.Core;

/// <summary>
/// The thickness of a font, on the numeric scale CSS uses (CSS Fonts 4 §2.2 "font-weight": 1–1000, normal is
/// 400, bold is 700). OpenType's <c>OS/2.usWeightClass</c> uses the same scale, so a requested weight and a
/// font's weight compare directly.
/// </summary>
/// <param name="Value">The weight, 1–1000. Front-ends validate the range.</param>
public readonly record struct FontWeight(int Value)
{
    /// <summary>Normal weight, 400 (CSS keyword <c>normal</c>).</summary>
    public static FontWeight Regular => new(400);

    /// <summary>Bold weight, 700 (CSS keyword <c>bold</c>).</summary>
    public static FontWeight Bold => new(700);

    /// <summary>True when the weight is in the range CSS allows, 1–1000.</summary>
    public bool IsValid => Value is >= 1 and <= 1000;

    /// <summary>Formats as the number, for example <c>700</c>.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
