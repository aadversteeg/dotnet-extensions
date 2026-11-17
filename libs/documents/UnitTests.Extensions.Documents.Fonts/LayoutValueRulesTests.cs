using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Fonts.OpenType;

namespace UnitTests.Extensions.Documents.Fonts;

/// <summary>
/// The public layout values hold what their binary searches need, whoever makes them: a coverage and a class definition
/// take ranges in glyph order, none overlapping and none ending before it starts ("in startGlyphID order", OpenType
/// "OpenType Layout Common Table Formats"). They are made only by a factory that answers ranges breaking this with an
/// error value (<c>Documents.Font.InvalidRanges</c>), never with an exception or with wrong classes later; there is no
/// other way to set the ranges. A glyph definition table has glyph classes exactly when it is given a glyph class
/// definition.
/// </summary>
public class LayoutValueRulesTests
{
    public static TheoryData<string, Coverage.Range[]> BadCoverages => new()
    {
        { "out of order", [new(5, 5, 0), new(1, 1, 1)] },
        { "overlapping", [new(1, 5, 0), new(5, 9, 5)] },
        { "ending before it starts", [new(5, 1, 0)] },
    };

    [Theory]
    [MemberData(nameof(BadCoverages))]
    public void A_coverage_of_ranges_out_of_glyph_order_is_an_error_value(string what, Coverage.Range[] ranges)
    {
        var made = Coverage.Of([.. ranges]);

        made.Error.Code.Should().Be(FontErrorCodes.InvalidRanges, what);
        made.Error.Metadata![global::Ave.Extensions.Documents.Core.ErrorMetadataKeys.Value].Should().Be(what == "out of order" || what == "overlapping" ? "1" : "0", what + ": the first range that breaks the order");
    }

    public static TheoryData<string, ClassDefinition.Range[]> BadClassDefinitions => new()
    {
        { "out of order", [new(5, 5, 1), new(1, 1, 2)] },
        { "overlapping", [new(1, 5, 1), new(5, 9, 2)] },
        { "ending before it starts", [new(5, 1, 1)] },
    };

    [Theory]
    [MemberData(nameof(BadClassDefinitions))]
    public void A_class_definition_of_ranges_out_of_glyph_order_is_an_error_value(string what, ClassDefinition.Range[] ranges) =>
        ClassDefinition.Of([.. ranges]).Error.Code.Should().Be(FontErrorCodes.InvalidRanges, what);

    [Fact]
    public void Ranges_in_glyph_order_make_a_value_that_finds_them()
    {
        Coverage.Of([new(1, 3, 0), new(7, 7, 3)]).Value.IndexOf(new GlyphId(7)).Should().Be(3);
        ClassDefinition.Of([new(1, 3, 2), new(4, 4, 5)]).Value.ClassOf(new GlyphId(4)).Should().Be(5);
    }

    [Fact]
    public void An_unset_range_list_is_no_ranges()
    {
        Coverage.Of(default).Value.IndexOf(new GlyphId(1)).Should().BeNull();
        ClassDefinition.Of(default).Value.ClassOf(new GlyphId(1)).Should().Be(0);
    }

    [Fact]
    public void Glyph_definitions_have_glyph_classes_exactly_when_given_a_glyph_class_definition()
    {
        var classes = ClassDefinition.Of([new ClassDefinition.Range(1, 1, GlyphDefinitions.MarkClass)]).Value;

        new GlyphDefinitions(classes, ClassDefinition.Empty).HasGlyphClasses.Should().BeTrue();
        new GlyphDefinitions(null, ClassDefinition.Empty).HasGlyphClasses.Should().BeFalse();
    }
}
