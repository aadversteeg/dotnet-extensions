using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using Ave.Documents.Core;
using Ave.Documents.Pdf;
using Ave.Documents.Pdf.Xmp;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Pdf;

/// <summary>The PDF layer's conformance building blocks: XMP packets, the sRGB ICC profile, profiles, trees, marked content.</summary>
public class ConformanceBuildingBlockTests
{
    private static XmpPacket Sample() => new(
    [
        new XmpProperty(XmpNamespace.DublinCore, "title", new XmpLanguageAlternative("Timesheet <October> & more")),
        new XmpProperty(XmpNamespace.DublinCore, "creator", new XmpOrderedArray([new XmpText("Aad")])),
        new XmpProperty(XmpNamespace.XmpBasic, "CreateDate", XmpDate.Format(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)))),
        new XmpProperty(XmpNamespace.PdfAIdentification, "part", new XmpText("3")),
        new XmpProperty(XmpNamespace.DublinCore, "language", new XmpUnorderedArray([new XmpText("nl-NL")])),
        XmpSchemaDescription.ContainerProperty([XmpSchemaDescription.PdfUaIdentification]),
    ]);

    [Fact]
    public void An_XMP_packet_matches_its_golden_file() =>
        Golden.Compare("sample.xmp.xml", Encoding.UTF8.GetString(Sample().Write().Value.AsSpan())).Should().BeNull();

    [Fact]
    public void An_XMP_packet_is_well_formed_UTF_8_with_the_packet_header_PDF_A_asks_for()
    {
        var bytes = Sample().Write().Value;
        var text = Encoding.UTF8.GetString(bytes.AsSpan());

        text.Should().StartWith("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        text.Should().NotContain("bytes=").And.NotContain("encoding=");
        text.TrimEnd().Should().EndWith("<?xpacket end=\"w\"?>");
        var xml = XDocument.Parse(text);
        xml.Descendants(XName.Get("title", XmpNamespace.DublinCore.Uri)).Single().Value.Should().Be("Timesheet <October> & more");
    }

    [Fact]
    public void Properties_of_one_namespace_share_one_description_even_when_they_are_not_adjacent()
    {
        var xml = XDocument.Parse(Encoding.UTF8.GetString(Sample().Write().Value.AsSpan()));

        var descriptions = xml.Descendants(XName.Get("Description", "http://www.w3.org/1999/02/22-rdf-syntax-ns#"))
            .Where(element => element.Attribute(XName.Get("about", "http://www.w3.org/1999/02/22-rdf-syntax-ns#")) is not null)
            .ToList();
        descriptions.Should().HaveCount(4, "dc, xmp, pdfaid and pdfaExtension");
        descriptions[0].Elements().Select(element => element.Name.LocalName).Should().Equal("title", "creator", "language");
    }

    [Theory]
    [InlineData(0x01, "title")]
    [InlineData(0xD800, "creator")]
    [InlineData(0xFFFF, "language")]
    public void An_XMP_value_with_a_character_XML_forbids_is_an_error_value_not_an_exception(int forbidden, string property)
    {
        var text = $"a{(char)forbidden}b";
        XmpValue value = property switch
        {
            "title" => new XmpLanguageAlternative(text),
            "creator" => new XmpOrderedArray([new XmpText(text)]),
            _ => new XmpUnorderedArray([new XmpText(text)]),
        };

        var result = new XmpPacket([new XmpProperty(XmpNamespace.DublinCore, property, value)]).Write();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DocumentErrorCodes.Pdf.Write.InvalidText);
        result.Error.Metadata![ErrorMetadataKeys.Property].Should().Be("dc:" + property);
        result.Error.Metadata![ErrorMetadataKeys.Offset].Should().Be(1);
    }

    [Theory]
    [InlineData(0, "2026-11-15T09:22:00Z")]
    [InlineData(1, "2026-11-15T09:22:00+01:00")]
    [InlineData(-5.5, "2026-11-15T09:22:00-05:30")]
    public void XMP_dates_carry_the_same_offset_as_PDF_dates(double hours, string expected) =>
        XmpDate.Format(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(hours))).Text.Should().Be(expected);

    [Fact]
    public void The_sRGB_profile_has_a_version_2_display_header()
    {
        var profile = SrgbIccProfile.Bytes.AsSpan();

        BinaryPrimitives.ReadUInt32BigEndian(profile).Should().Be((uint)profile.Length, "the header's size field");
        BinaryPrimitives.ReadUInt32BigEndian(profile[8..]).Should().Be(0x02100000u, "version 2.1.0");
        Encoding.ASCII.GetString(profile[12..24]).Should().Be("mntrRGB XYZ ");
        Encoding.ASCII.GetString(profile[36..40]).Should().Be("acsp");
        Fixed(profile[68..]).Should().BeApproximately(0.9642, 1e-4, "the PCS illuminant is D50");
    }

    [Fact]
    public void The_sRGB_profile_has_the_nine_required_tags_four_byte_aligned_with_one_shared_curve()
    {
        var tags = Tags(SrgbIccProfile.Bytes.AsSpan().ToArray());

        tags.Keys.Should().BeEquivalentTo(["desc", "cprt", "wtpt", "rXYZ", "gXYZ", "bXYZ", "rTRC", "gTRC", "bTRC"]);
        tags.Values.Should().OnlyContain(tag => tag.Offset % 4 == 0);
        new[] { tags["rTRC"], tags["gTRC"], tags["bTRC"] }.Distinct().Should().ContainSingle();
    }

    [Fact]
    public void The_sRGB_colorants_add_up_to_the_white_point_and_the_curve_is_the_sRGB_function()
    {
        var profile = SrgbIccProfile.Bytes.AsSpan().ToArray();
        var tags = Tags(profile);
        double Component(string tag, int index) => Fixed(profile.AsSpan(tags[tag].Offset + 8 + index * 4));

        for (var index = 0; index < 3; index++)
        {
            (Component("rXYZ", index) + Component("gXYZ", index) + Component("bXYZ", index))
                .Should().BeApproximately(Component("wtpt", index), 3e-4, "linear white (1, 1, 1) must map to the white point");
        }

        var curve = tags["rTRC"].Offset;
        int Entry(int index) => BinaryPrimitives.ReadUInt16BigEndian(profile.AsSpan(curve + 12 + index * 2));
        BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(curve + 8)).Should().Be(1024u);
        Entry(0).Should().Be(0);
        Entry(1023).Should().Be(65535);
        (Entry(511) / 65535.0).Should().BeApproximately(Math.Pow((511 / 1023.0 + 0.055) / 1.055, 2.4), 1.0 / 65535);
    }

    [Fact]
    public void Profiles_add_what_their_standards_require()
    {
        (PdfProfile.Plain.IsTagged, PdfProfile.Plain.HasXmpMetadata, PdfProfile.Plain.HasOutputIntent).Should().Be((false, false, false));
        (PdfProfile.PdfA3b.IsTagged, PdfProfile.PdfA3b.HasXmpMetadata, PdfProfile.PdfA3b.HasOutputIntent).Should().Be((false, true, true));
        (PdfProfile.PdfA3a.IsTagged, PdfProfile.PdfA3a.HasOutputIntent).Should().Be((true, true));
        (PdfProfile.PdfUa1.IsTagged, PdfProfile.PdfUa1.HasXmpMetadata, PdfProfile.PdfUa1.HasOutputIntent).Should().Be((true, true, false));
        PdfProfile.PdfA3b.AndPdfUa1().IsTagged.Should().BeTrue("PDF/UA needs tagging even with level B");
        PdfProfile.PdfA3a.AndPdfUa1().ToString().Should().Be("PDF/A-3a + PDF/UA-1");
        PdfProfile.PdfA3a.AndPdfUa1().Should().Be(PdfProfile.PdfA3a with { IsPdfUa1 = true }, "profiles are values");
    }

    [Fact]
    public void A_name_tree_sorts_its_keys_by_their_bytes()
    {
        var tree = PdfTrees.NameTree([(PdfString.FromText("b.xml"), new PdfInteger(2)), (PdfString.FromText("B.xml"), new PdfInteger(1)), (PdfString.FromText("a.xml"), new PdfInteger(3))]);

        PdfSerializer.Serialize(tree).Value.Should().Be("<< /Names [(B.xml) 1 (a.xml) 3 (b.xml) 2] >>");
    }

    [Fact]
    public void Marked_content_is_written_with_an_inline_property_list() =>
        Encoding.ASCII.GetString(ContentStream.Serialize(
        [
            ContentOperators.BeginMarkedContent("P", PdfDictionary.Of(("MCID", new PdfInteger(0)))),
            ContentOperators.EndMarkedContent(),
        ]).Value.AsSpan()).Should().Be("/P << /MCID 0 >> BDC\nEMC\n");

    private static double Fixed(ReadOnlySpan<byte> value) => BinaryPrimitives.ReadInt32BigEndian(value) / 65536.0;

    private static Dictionary<string, (int Offset, int Size)> Tags(byte[] profile)
    {
        var count = (int)BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(128));
        return Enumerable.Range(0, count).ToDictionary(
            index => Encoding.ASCII.GetString(profile, 132 + index * 12, 4),
            index => ((int)BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(136 + index * 12)), (int)BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(140 + index * 12))));
    }
}
