using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>A default-ignorable character is in the text for line breaking but is never drawn on the page.</summary>
public class InvisibleGlyphTests
{
    [Fact]
    public void A_soft_hyphen_in_mid_line_draws_no_glyph()
    {
        var text = "co" + (char)0x00AD + "operate";

        var page = DocumentLayout.Run(Document(Paragraph(1, text)), TestFonts.Set).Value.Pages.Single();

        var run = page.Items.OfType<global::Ave.Extensions.Documents.PageModel.GlyphRun>().Single();
        run.Glyphs.Select(glyph => glyph.Text).Should().Equal("c", "o", "o", "p", "e", "r", "a", "t", "e");
    }
}
