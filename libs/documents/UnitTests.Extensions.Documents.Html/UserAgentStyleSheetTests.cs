using Ave.Extensions.Documents.Html;

namespace UnitTests.Extensions.Documents.Html;

public class UserAgentStyleSheetTests
{
    [Fact]
    public void The_user_agent_style_sheet_reads_without_a_diagnostic()
    {
        UserAgentStyleSheet.Result.Diagnostics.Select(error => $"{error.Code}: {error.Message}").Should().BeEmpty();
        UserAgentStyleSheet.Sheet.Rules.Should().NotBeEmpty();
    }
}
