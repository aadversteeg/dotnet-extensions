using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Samples;

/// <summary>
/// The brand kit: everything a customer changes to restyle the timesheet, and no layout. The values are those of the
/// reporting PoC's <c>templates/brand.typ</c>.
/// </summary>
/// <param name="Logo">The logo, a PNG or JPEG file.</param>
/// <param name="Accent">The accent colour of the bands under the header and over the footer.</param>
/// <param name="GridLine">The colour of the table's grid lines.</param>
/// <param name="Muted">The colour of the entry descriptions.</param>
/// <param name="FontFamily">The font family; its regular, bold and italic faces must be in the font set.</param>
/// <param name="Company">The company details for the footer.</param>
public sealed record Brand(ImmutableArray<byte> Logo, Color Accent, Color GridLine, Color Muted, string FontFamily, CompanyDetails Company)
{
    /// <summary>The PoC's brand: orange <c>#FFA500</c>, light-grey lines <c>#D3D3D3</c>, Lato.</summary>
    /// <param name="logo">The logo file.</param>
    /// <returns>The brand.</returns>
    public static Brand Poc(ImmutableArray<byte> logo) => new(
        logo,
        Accent: new Color(0xFF, 0xA5, 0x00),
        GridLine: new Color(0xD3, 0xD3, 0xD3),
        Muted: new Color(0x23, 0x23, 0x23),
        FontFamily: "Lato",
        new CompanyDetails(Iban: "NL00 BANK 0123 4567 89", Vat: "NL000000000B00", Coc: "00000000"));
}

/// <summary>The company's details shown in the footer.</summary>
/// <param name="Iban">The bank account.</param>
/// <param name="Vat">The VAT number.</param>
/// <param name="Coc">The chamber of commerce number.</param>
public sealed record CompanyDetails(string Iban, string Vat, string Coc);
