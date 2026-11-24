using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Parsing;

/// <summary>Where each character of a text node was written (<see cref="HtmlText.PositionOf"/>).</summary>
public class TextPositionTests
{
    private static List<HtmlText> Texts(string html)
    {
        var document = HtmlParser.Parse(html, new HtmlParseOptions("t.html")).Match(result => result.Document, error => throw new InvalidOperationException(error.ToString()));
        var texts = new List<HtmlText>();
        var pending = new Stack<HtmlNode>(document.Children.Reverse());
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case HtmlText text:
                    texts.Add(text);
                    break;
                case HtmlElement element:
                    foreach (var child in element.Children.Reverse())
                    {
                        pending.Push(child);
                    }

                    break;
            }
        }

        return texts;
    }

    [Fact]
    public void A_character_is_at_its_own_place_after_line_feeds_and_character_references()
    {
        // Line 1: "<p>ab&amp;c" — a at column 4, the ampersand of &amp; at 6, c at 11; line 2: "d&#x2714;e" — d at 1,
        // the reference at 2, e at 10; after a CR LF pair (one line feed in the text) f is on line 3, column 1.
        var text = Texts("<p>ab&amp;c\nd&#x2714;e\r\nf</p>").Single();

        text.Data.Should().Be("ab&c\nd✔e\nf");
        text.PositionOf(0).Should().Be(new TextPosition(1, 4));
        text.PositionOf(2).Should().Be(new TextPosition(1, 6));
        text.PositionOf(3).Should().Be(new TextPosition(1, 11));
        text.PositionOf(5).Should().Be(new TextPosition(2, 1));
        text.PositionOf(6).Should().Be(new TextPosition(2, 2));
        text.PositionOf(7).Should().Be(new TextPosition(2, 10));
        text.PositionOf(9).Should().Be(new TextPosition(3, 1));
    }

    [Fact]
    public void A_text_written_in_one_run_keeps_no_places_of_its_own()
    {
        var text = Texts("<p>abc\ndef</p>").Single();

        text.Should().Be(new HtmlText("abc\ndef", new TextPosition(1, 4)), "a text whose characters follow one another compares as one made in code");
        text.PositionOf(5).Should().Be(new TextPosition(2, 2));
    }

    [Fact]
    public void Text_added_to_a_node_from_another_place_is_at_that_place()
    {
        // Foster parenting: "x" and "y" inside the table go before it, into the text "a" in front of the table.
        var text = Texts("<body>a<table>x<tr><td>z</td></tr>y</table>").First();

        text.Data.Should().Be("axy");
        text.PositionOf(0).Should().Be(new TextPosition(1, 7));
        text.PositionOf(1).Should().Be(new TextPosition(1, 15));
        text.PositionOf(2).Should().Be(new TextPosition(1, 35));
    }

    [Fact]
    public void A_text_made_in_code_counts_from_its_position()
    {
        var text = new HtmlText("ab\ncd", new TextPosition(4, 7));

        text.PositionOf(1).Should().Be(new TextPosition(4, 8));
        text.PositionOf(4).Should().Be(new TextPosition(5, 2));
    }
}
