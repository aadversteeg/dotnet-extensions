using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Ave.Documents.Core;

public class ResultCollectionsTests
{
    private static readonly ErrorCode Code = DocumentErrorCodes.Input.Invalid.Length;

    private static Result<int, Error> Half(int value) =>
        value % 2 == 0
            ? Result<int, Error>.Success(value / 2)
            : Result<int, Error>.Failure(new Error(Code, $"{value} is odd"));

    [Fact]
    public void TraverseAll_returns_all_values_in_order_when_every_item_succeeds()
    {
        var result = new[] { 2, 4, 6 }.TraverseAll(Half);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void TraverseAll_of_an_empty_list_succeeds_with_an_empty_list() =>
        Array.Empty<int>().TraverseAll(Half).Value.Should().BeEmpty();

    [Fact]
    public void TraverseAll_reports_a_single_failure_with_its_own_code()
    {
        var result = new[] { 2, 3, 4 }.TraverseAll(Half);

        result.Error.Code.Should().Be(Code);
        result.Error.Message.Should().Be("3 is odd");
    }

    [Fact]
    public void TraverseAll_reports_every_failure_not_only_the_first()
    {
        var result = new[] { 1, 2, 3, 5 }.TraverseAll(Half);

        result.Error.Code.Should().Be(DocumentErrorCodes.Multiple);
        result.Error.Message.Should().Be("3 errors: 1 is odd; 3 is odd; 5 is odd");
        ResultCollections.Parts(result.Error).Should().HaveCount(3);
    }

    [Fact]
    public void Combine_flattens_nested_multiple_errors()
    {
        var inner = ResultCollections.Combine([new Error(Code, "a"), new Error(Code, "b")]);
        var combined = ResultCollections.Combine([inner, new Error(Code, "c")]);

        ResultCollections.Parts(combined).Select(error => error.Message).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Combine_of_nothing_is_a_programming_error()
    {
        var act = () => ResultCollections.Combine(new List<Error>());
        act.Should().Throw<ArgumentException>();
    }
}
