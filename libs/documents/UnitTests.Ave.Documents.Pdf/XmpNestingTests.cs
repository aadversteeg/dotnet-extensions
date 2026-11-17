using Ave.Documents.Core;
using Ave.Documents.Pdf;
using Ave.Documents.Pdf.Xmp;

namespace UnitTests.Ave.Documents.Pdf;

/// <summary>
/// XMP values nest arrays and structures at most <see cref="XmpPacket.MaxNestingDepth"/> levels deep, as PDF objects do
/// in the serializer (review 35: an ordered array nested 5 000 deep ended the process with a stack overflow). The
/// packet writer refuses a deeper value with an error value before it walks the values for anything else.
/// </summary>
public class XmpNestingTests
{
    /// <summary>A value of <paramref name="depth"/> containers of the given kind, each holding the next; the innermost
    /// holds one text. The outermost container is level 1.</summary>
    private static XmpValue Nested(int depth, Func<XmpValue, XmpValue> container) =>
        Enumerable.Range(0, depth).Aggregate((XmpValue)new XmpText("deep"), (inner, _) => container(inner));

    private static XmpValue Ordered(XmpValue inner) => new XmpOrderedArray([inner]);

    private static XmpValue Unordered(XmpValue inner) => new XmpUnorderedArray([inner]);

    private static XmpValue Structure(XmpValue inner) => new XmpStructure([new XmpProperty(XmpNamespace.XmpBasic, "field", inner)]);

    private static XmpPacket Packet(XmpValue value) =>
        new([new XmpProperty(XmpNamespace.DublinCore, "title", new XmpLanguageAlternative("Title")), new XmpProperty(XmpNamespace.DublinCore, "creator", value)]);

    public static TheoryData<string> Containers => new() { "ordered array", "unordered array", "structure", "mixed" };

    private static Func<XmpValue, XmpValue> Container(string kind) => kind switch
    {
        "ordered array" => Ordered,
        "unordered array" => Unordered,
        "structure" => Structure,
        "mixed" => inner => inner switch
        {
            XmpOrderedArray => Structure(inner),
            XmpStructure => Unordered(inner),
            _ => Ordered(inner),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a container kind"),
    };

    [Theory]
    [MemberData(nameof(Containers))]
    public void A_value_nested_as_deep_as_the_limit_is_written(string kind)
    {
        Packet(Nested(XmpPacket.MaxNestingDepth, Container(kind))).Write().IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Containers))]
    public void A_value_nested_one_level_deeper_than_the_limit_is_an_error_value(string kind)
    {
        var error = Packet(Nested(XmpPacket.MaxNestingDepth + 1, Container(kind))).Write().Error;

        error.Code.Should().Be(PdfErrorCodes.Write.NestedTooDeep);
        error.Message.Should().Be("The XMP property dc:creator nests arrays and structures more than 64 levels deep.");
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("64");
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("dc:creator");
    }

    [Theory]
    [InlineData(5_000)]
    [InlineData(100_000)]
    public void A_value_nested_far_deeper_is_an_error_value_not_a_stack_overflow(int depth)
    {
        Packet(Nested(depth, Ordered)).Write().Error.Code.Should().Be(PdfErrorCodes.Write.NestedTooDeep);
        Packet(Nested(depth, Structure)).Write().Error.Code.Should().Be(PdfErrorCodes.Write.NestedTooDeep);
    }
}
