namespace Ave.Extensions.Documents.Core;

/// <summary>
/// How the items of a list are numbered or marked: the values of the list attribute <c>ListNumbering</c> of a PDF
/// <c>L</c> structure element (ISO 32000-1 §14.8.5.5, Table 347), carried by <see cref="SemanticRole.List"/>.
/// </summary>
public enum ListNumbering
{
    /// <summary>No label (<c>None</c>, the default of the attribute).</summary>
    None,

    /// <summary>A filled round bullet (<c>Disc</c>).</summary>
    Disc,

    /// <summary>An open round bullet (<c>Circle</c>).</summary>
    Circle,

    /// <summary>A filled square bullet (<c>Square</c>).</summary>
    Square,

    /// <summary>Decimal numbers (<c>Decimal</c>).</summary>
    Decimal,

    /// <summary>Upper-case roman numerals (<c>UpperRoman</c>).</summary>
    UpperRoman,

    /// <summary>Lower-case roman numerals (<c>LowerRoman</c>).</summary>
    LowerRoman,

    /// <summary>Upper-case letters (<c>UpperAlpha</c>).</summary>
    UpperAlpha,

    /// <summary>Lower-case letters (<c>LowerAlpha</c>).</summary>
    LowerAlpha,
}
