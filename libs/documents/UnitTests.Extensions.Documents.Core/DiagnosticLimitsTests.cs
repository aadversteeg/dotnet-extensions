using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace UnitTests.Extensions.Documents.Core;

/// <summary>One bound for what a render reports (<see cref="DiagnosticLimits"/>): how many diagnostics are kept, and how
/// long a message and an aggregate's messages are.</summary>
public class DiagnosticLimitsTests
{
    private static readonly ErrorCode Code = DocumentErrorCodes.Input.Invalid.Table;

    private static IEnumerable<Error> Many(int count, int length = 10) =>
        Enumerable.Range(0, count).Select(_ => new Error(Code, new string('m', length)));

    [Fact]
    public void The_most_diagnostics_are_kept_and_one_more_is_counted_at_the_end()
    {
        DiagnosticLimits.Kept(Many(DiagnosticLimits.MaxDiagnostics)).Should().HaveCount(DiagnosticLimits.MaxDiagnostics);

        var kept = DiagnosticLimits.Kept(Many(DiagnosticLimits.MaxDiagnostics + 1));

        kept.Should().HaveCount(DiagnosticLimits.MaxDiagnostics + 1);
        kept[^1].Code.Should().Be(DocumentErrorCodes.TooManyDiagnostics);
        (kept[^1].Metadata![ErrorMetadataKeys.Value], kept[^1].Metadata![ErrorMetadataKeys.Refusals]).Should().Be((1, 1));
    }

    [Fact]
    public void A_summary_among_the_diagnostics_joins_the_count_of_the_one_at_the_end()
    {
        var earlier = new Error(DocumentErrorCodes.TooManyDiagnostics, "5 more").With(ErrorMetadataKeys.Value, 5).With(ErrorMetadataKeys.Refusals, 2);

        var kept = DiagnosticLimits.Kept([.. Many(DiagnosticLimits.MaxDiagnostics + 3), earlier], error => false);

        kept[^1].Metadata![ErrorMetadataKeys.Value].Should().Be(8);
        kept[^1].Metadata![ErrorMetadataKeys.Refusals].Should().Be(2, "the three left out here do not refuse");
        kept.Count(error => error.Code == DocumentErrorCodes.TooManyDiagnostics).Should().Be(1);
    }

    [Fact]
    public void A_message_is_cut_at_its_limit_and_an_aggregate_keeps_parts_while_their_messages_fit()
    {
        DiagnosticLimits.Kept(Many(1, DiagnosticLimits.MaxMessageLength)).Single().Message.Should().HaveLength(DiagnosticLimits.MaxMessageLength);
        var cut = DiagnosticLimits.Kept(Many(1, DiagnosticLimits.MaxMessageLength + 1)).Single().Message;
        cut.Should().HaveLength(DiagnosticLimits.MaxMessageLength).And.EndWith("…");

        // 2^20 characters hold 255 messages of 4 096 and their separators; the 256th and later are counted.
        var aggregate = DiagnosticLimits.Aggregate([.. Many(300, DiagnosticLimits.MaxMessageLength)]);

        aggregate.Parts().Should().HaveCount(256);
        aggregate.Parts()[^1].Metadata![ErrorMetadataKeys.Value].Should().Be(45);
        aggregate.Message.Length.Should().BeLessThan(DiagnosticLimits.MaxAggregateLength + 1_000);
        DiagnosticLimits.Aggregate([new Error(Code, "one")]).Message.Should().Be("one");
    }
}
