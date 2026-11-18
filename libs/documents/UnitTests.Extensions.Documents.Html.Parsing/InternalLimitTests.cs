using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Html.Parsing.Tokenizing;
using Ave.Extensions.Documents.Html.Parsing.TreeBuilding;

namespace UnitTests.Extensions.Documents.Html.Parsing;

/// <summary>The limits checked inside the parser, on its internal parts.</summary>
public class InternalLimitTests
{
    [Fact]
    public void A_tree_deeper_than_the_limit_is_found_when_it_is_frozen()
    {
        // The adoption agency algorithm can nest elements deeper than the stack ever was; the freezer checks the depth.
        var document = new MutableDocument();
        MutableParent parent = document;
        for (var i = 0; i < HtmlInputLimits.MaxDepth + 1; i++)
        {
            var element = new MutableElement { Namespace = ElementNamespace.Html, LocalName = "b", Token = new Token { Kind = TokenKind.StartTag, Name = "b" }, Start = i };
            parent.Insert(element, null);
            parent = element;
        }

        var freezer = new TreeFreezer(index => new TextPosition(1, index + 1));
        freezer.Document(document);

        freezer.TooDeepAt.Should().NotBeNull();
        freezer.TooDeepAt!.Start.Should().Be(HtmlInputLimits.MaxDepth);
    }

    [Fact]
    public void The_longest_named_reference_is_the_one_the_lookup_is_bounded_by()
    {
        var table = File.ReadAllLines(Path.Combine(Suites.Folder(["..", "..", "Ave.Extensions.Documents.Html.Parsing", "Tokenizing"]), "named-character-references.txt"))
            .Where(line => line.Length > 0 && line[0] != '#')
            .Select(line => line.Split(' ')[0])
            .ToList();

        table.Should().HaveCount(2231);
        table.Max(name => name.Length).Should().Be(NamedCharacterReferences.LongestName);
        NamedCharacterReferences.Find("CounterClockwiseContourIntegral;").Should().Be("∳");
    }
}
