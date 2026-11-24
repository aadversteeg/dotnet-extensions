using Ave.Extensions.Documents.Core;

namespace UnitTests.Extensions.Documents.Core;

/// <summary>
/// THE RULE FOR A LINK'S URI (<see cref="LinkUris"/>): an absolute URI of http, https, mailto or tel, compared without
/// regard to case, printable ASCII exactly as it will be written; everything else refused with the scheme and the four
/// accepted ones named.
/// </summary>
public class LinkUrisTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/a%20b?q=%C3%A9#frag")]
    [InlineData("HTTPS://Example.COM/a")]
    [InlineData("mailto:info@example.com")]
    [InlineData("MailTo:info@example.com")]
    [InlineData("tel:+31-20-1234567")]
    public void An_absolute_URI_of_an_accepted_scheme_is_accepted(string uri) =>
        LinkUris.Problem(uri).Should().BeNull();

    [Theory]
    [InlineData("javascript:alert(1)", "javascript")]
    [InlineData("JavaScript:alert(1)", "javascript")]
    [InlineData("vbscript:msgbox", "vbscript")]
    [InlineData("file:///etc/hosts", "file")]
    [InlineData("data:text/plain,hi", "data")]
    [InlineData("ftp://example.com/file", "ftp")]
    [InlineData("blob:https://example.com/1", "blob")]
    public void A_URI_of_another_scheme_is_refused_with_its_scheme_and_the_accepted_ones(string uri, string scheme)
    {
        var problem = LinkUris.Problem(uri);

        problem.Should().Contain($"'{scheme}'").And.Contain("http, https, mailto and tel");
    }

    [Theory]
    [InlineData(" https://example.com")]
    [InlineData("\thttps://example.com")]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("ht\u0001tps://example.com")]
    [InlineData("javascript\u0000:alert(1)")]
    [InlineData("https://exa mple.com")]
    [InlineData("https://exämple.com")]
    [InlineData("https://example.com\u007F")]
    public void A_URI_with_a_space_a_control_character_or_a_character_beyond_ascii_is_refused(string uri) =>
        LinkUris.Problem(uri).Should().Contain("printable ASCII");

    [Theory]
    [InlineData("page.html")]
    [InlineData("/root.html")]
    [InlineData("//host.example/p")]
    [InlineData("#frag")]
    [InlineData(":nothing")]
    [InlineData("1http://example.com")]
    [InlineData("ja_va:x")]
    public void A_relative_URI_or_one_without_a_scheme_of_the_right_form_is_refused(string uri) =>
        LinkUris.Problem(uri).Should().Contain("no scheme");

    [Theory]
    [InlineData("http:")]
    [InlineData("https:///path")]
    [InlineData("https:example.com")]
    public void A_web_URI_without_a_host_is_refused(string uri) =>
        LinkUris.Problem(uri).Should().Contain("names no host");

    [Fact]
    public void An_empty_URI_and_one_past_the_limit_are_refused_and_one_at_the_limit_is_accepted()
    {
        var atLimit = "https://example.com/" + new string('a', LinkUris.MaxLength - "https://example.com/".Length);

        LinkUris.Problem(null).Should().NotBeNull();
        LinkUris.Problem(string.Empty).Should().NotBeNull();
        LinkUris.Problem(atLimit).Should().BeNull();
        LinkUris.Problem(atLimit + "a").Should().Contain("32767");
    }
}
