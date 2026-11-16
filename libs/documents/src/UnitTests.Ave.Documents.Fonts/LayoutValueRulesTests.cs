using Ave.Documents.Fonts;
using Ave.Documents.Fonts.OpenType;

namespace UnitTests.Ave.Documents.Fonts;

/// <summary>
/// The public layout values hold what their binary searches need, whoever makes them: a coverage and a class definition
/// take ranges in glyph order, none overlapping and none ending before it starts ("in startGlyphID order", OpenType
/// "OpenType Layout Common Table Formats"); a range list that breaks this is a programming error and is refused when the
/// value is made, not answered with wrong classes later. A glyph definition table has glyph classes exactly when it
/// is given a glyph class definition.
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
    public void A_coverage_of_ranges_out_of_glyph_order_is_refused_when_made(string what, Coverage.Range[] ranges)
    {
        var make = () => new Coverage([.. ranges]);

        make.Should().Throw<ArgumentException>(what);
        var changed = () => Coverage.Empty with { Ranges = [.. ranges] };
        changed.Should().Throw<ArgumentException>(what + ", also through with");
    }

    public static TheoryData<string, ClassDefinition.Range[]> BadClassDefinitions => new()
    {
        { "out of order", [new(5, 5, 1), new(1, 1, 2)] },
        { "overlapping", [new(1, 5, 1), new(5, 9, 2)] },
        { "ending before it starts", [new(5, 1, 1)] },
    };

    [Theory]
    [MemberData(nameof(BadClassDefinitions))]
    public void A_class_definition_of_ranges_out_of_glyph_order_is_refused_when_made(string what, ClassDefinition.Range[] ranges)
    {
        var make = () => new ClassDefinition([.. ranges]);

        make.Should().Throw<ArgumentException>(what);
    }

    [Fact]
    public void An_unset_range_list_is_no_ranges()
    {
        new Coverage(default).IndexOf(new GlyphId(1)).Should().BeNull();
        new ClassDefinition(default).ClassOf(new GlyphId(1)).Should().Be(0);
    }

    [Fact]
    public void Glyph_definitions_have_glyph_classes_exactly_when_given_a_glyph_class_definition()
    {
        var classes = new ClassDefinition([new ClassDefinition.Range(1, 1, GlyphDefinitions.MarkClass)]);

        new GlyphDefinitions(classes, ClassDefinition.Empty).HasGlyphClasses.Should().BeTrue();
        new GlyphDefinitions(null, ClassDefinition.Empty).HasGlyphClasses.Should().BeFalse();
    }
}
