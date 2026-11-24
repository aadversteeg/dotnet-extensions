using Ave.Extensions.Documents.Core;

namespace UnitTests.Extensions.Documents.Core;

/// <summary>
/// THE RULE FOR A DOCUMENT'S METADATA TEXT (<see cref="MetadataTexts"/>): the characters XML 1.0 allows, less the control
/// characters other than tab, line feed and carriage return and less U+007F to U+009F; an author or a description of at
/// most 4 096 UTF-16 code units, a title without a bound of its own.
/// </summary>
public class MetadataTextsTests
{
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x0B)]
    [InlineData(0x0C)]
    [InlineData(0x1F)]
    [InlineData(0x7F)]
    [InlineData(0x80)]
    [InlineData(0x85)]
    [InlineData(0x9F)]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    public void A_control_character_or_one_XML_does_not_allow_is_refused_at_its_position(int character)
    {
        var text = $"Hours {(char)character} week";

        MetadataTexts.IndexOfForbidden(text).Should().Be(6);
        MetadataTexts.TitleProblem(text).Should().Contain($"U+{character:X4} at position 6");
        MetadataTexts.Problem(text).Should().Contain($"U+{character:X4} at position 6");
    }

    [Fact]
    public void Tab_line_feed_carriage_return_the_characters_around_the_controls_and_a_surrogate_pair_are_allowed()
    {
        var text = "A\tB\nC\rD  ~ ퟿� " + char.ConvertFromUtf32(0x1F600);

        MetadataTexts.IndexOfForbidden(text).Should().BeNull();
        MetadataTexts.TitleProblem(text).Should().BeNull();
        MetadataTexts.Problem(text).Should().BeNull();
    }

    [Fact]
    public void The_first_forbidden_character_is_the_one_reported()
    {
        MetadataTexts.IndexOfForbidden("a\u0085b\u0001").Should().Be(1, "U+0085 before U+0001");
        MetadataTexts.IndexOfForbidden("a\u0001b\u0085").Should().Be(1, "U+0001 before U+0085");
    }

    [Fact]
    public void An_author_or_a_description_up_to_the_bound_is_allowed_a_longer_one_refused_and_a_title_has_no_bound()
    {
        var longest = new string('a', MetadataTexts.MaxLength);

        MetadataTexts.MaxLength.Should().Be(4_096);
        MetadataTexts.Problem(longest).Should().BeNull();
        MetadataTexts.Problem(longest + "a").Should().Be("it is 4097 characters long; at most 4096 are allowed");
        MetadataTexts.TitleProblem(new string('a', 20_000)).Should().BeNull("the title is bounded by PDF/A's string limit when the PDF is written");
    }

    [Fact]
    public void The_problems_of_a_document_name_each_field_in_order_with_the_offset_of_its_character()
    {
        var problems = MetadataTexts.Problems("Title\u0007", new string('a', 4_097), "Des\u0090cription").ToList();

        problems.Select(problem => (problem.Property, problem.Offset)).Should().Equal(("title", 5), ("author", null), ("description", 3));
        MetadataTexts.Problems("Hours", null, null).Should().BeEmpty();
        MetadataTexts.Problems(null, null, null).Should().BeEmpty();
    }
}
