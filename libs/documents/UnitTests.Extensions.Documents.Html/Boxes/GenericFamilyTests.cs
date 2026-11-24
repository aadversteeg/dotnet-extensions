using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// The generic families and the initial value of <c>font-family</c> (<see cref="GenericFamilies"/>): a mapped generic
/// family prints exactly as the family it maps to written out, the initial value takes a document's
/// <c>@font-face "Times New Roman"</c> first and then <see cref="GenericFamilies.Standard"/>, and what the options do not
/// map is refused in both modes. Chromium's own print of a generic family uses a font of its system, so the cases are the
/// written-out documents (each equal to Chromium) and the generic documents are made from them here.
/// </summary>
public class GenericFamilyTests
{
    private static readonly FontSet Fonts = new([TestFonts.Regular, TestFonts.Bold, TestFonts.Italic, TestFonts.TheanoDidot]);

    private static string Doc(string body, string css = "") =>
        $"<!doctype html><html lang=\"en\"><head><title>T</title><style>html, body {{ margin: 0 }} {css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html, GenericFamilies generics, UnsupportedPolicy policy = UnsupportedPolicy.Refuse, ResourceResolver? resources = null) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy, Resources = resources }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = Fonts, GenericFamilies = generics, Unsupported = policy, Resources = resources }));

    /// <summary>The family of each text run, in document order.</summary>
    private static List<string> Families(string html, GenericFamilies generics)
    {
        var result = Boxes(html, generics);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return [.. Runs(result.Value.Document.Root).Select(run => run.Style!.FontFamily)];
    }

    private static IEnumerable<TextRun> Runs(Box box) =>
        box.Children.SelectMany(child => child switch { TextRun run => [run], Box inner => Runs(inner), _ => [] });

    private static Error Refusal(string html, GenericFamilies generics, UnsupportedPolicy policy = UnsupportedPolicy.Refuse)
    {
        var result = Boxes(html, generics, policy);
        result.IsFailure.Should().BeTrue("the family is refused in either mode");
        return result.Error.Parts().Single();
    }

    private static GenericFamilies Mapping(string property, string family) => property switch
    {
        nameof(GenericFamilies.Standard) => new GenericFamilies { Standard = family },
        nameof(GenericFamilies.Serif) => new GenericFamilies { Serif = family },
        nameof(GenericFamilies.SansSerif) => new GenericFamilies { SansSerif = family },
        nameof(GenericFamilies.Monospace) => new GenericFamilies { Monospace = family },
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    /// <summary>A case's pages, from its text, on the reference tool's paper, its fonts from its own <c>@font-face</c>
    /// rules.</summary>
    private static Result<PageDocument, Error> CasePages(string name, string html, GenericFamilies generics)
    {
        var baseUrl = new Uri(Path.GetFullPath(Path.Combine(HtmlCases.Folder(), name + ".html"))).AbsoluteUri;
        return HtmlParser.Parse(html, new HtmlParseOptions(name + ".html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Page = HtmlCases.Letter, Resources = HtmlCases.Resource, BaseUrl = baseUrl }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { GenericFamilies = generics, Resources = HtmlCases.Resource, BaseUrl = baseUrl }))
            .OnSuccessBind(boxes => DocumentLayout.Run(boxes.Document, boxes.Fonts));
    }

    /// <summary>
    /// The case is equal to Chromium (its committed status) with the family written out; the same document with a generic
    /// family in its place, mapped to that family, gives the same pages, item for item; without the mapping it is
    /// refused. So the mapping prints exactly as the written-out family, which prints as Chromium does.
    /// </summary>
    [Theory]
    [InlineData("d-typo-theano", "font-family:Theano", "font-family:serif", "Serif", "Theano")]
    [InlineData("d-typo-theano", "font-family:Theano", "font-family:serif", "Standard", "Theano")]
    [InlineData("d-typo-theano", "font-family:Theano", "font-family:sans-serif", "SansSerif", "Theano")]
    [InlineData("d-typo-theano", "font-family:Theano", "font-family:system-ui", "SansSerif", "Theano")]
    [InlineData("d-typo-theano", "font-family:Theano", "font-family:monospace", "Monospace", "Theano")]
    [InlineData("d-typo-theano", "font-family:Theano", "font-family:cursive", "Standard", "Theano")]
    [InlineData("d-typo-theano", "font-family:Theano", "font-family:ui-monospace", "Standard", "Theano")]
    [InlineData("d-typo-theano", "font-family:Theano", "font-family:Nobody, sans-serif", "SansSerif", "Theano")]
    [InlineData("pm-inherit-family", "color: #008000; font-family: Lato;", "color: #008000; font-family: sans-serif;", "SansSerif", "Lato")]
    [InlineData("pm-inherit-family", "@top-left { content: \"Aa\"; }", "@top-left { content: \"Aa\"; font-family: serif; }", "Serif", "Lato")]
    [InlineData("gf-initial-body", "@font-face { font-family: \"Times New Roman\";", "@font-face { font-family: Theano;", "Standard", "Theano")]
    public void A_mapped_generic_family_prints_as_the_family_written_out(string name, string written, string generic, string property, string family)
    {
        HtmlCases.Status(name, "equal").Should().Be("equal", "the written-out document is equal to Chromium");
        var html = File.ReadAllText(Path.Combine(HtmlCases.Folder(), name + ".html"));
        html.Split(written).Length.Should().Be(2, "the family is written once");
        var variant = html.Replace(written, generic, StringComparison.Ordinal);

        var twin = CasePages(name, html, GenericFamilies.None);
        var mapped = CasePages(name, variant, Mapping(property, family));

        twin.IsSuccess.Should().BeTrue(twin.IsFailure ? twin.Error.ToString() : string.Empty);
        mapped.IsSuccess.Should().BeTrue(mapped.IsFailure ? mapped.Error.ToString() : string.Empty);
        mapped.Value.Should().Be(twin.Value, $"'{generic}' mapped to {family} prints as {name}");
        CasePages(name, variant, GenericFamilies.None).Error.Parts().First().Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Font);
    }

    [Theory]
    [InlineData("serif", "GenericFamilies.Serif (or GenericFamilies.Standard)")]
    [InlineData("sans-serif", "GenericFamilies.SansSerif")]
    [InlineData("system-ui", "GenericFamilies.SansSerif")]
    [InlineData("monospace", "GenericFamilies.Monospace")]
    [InlineData("cursive", "GenericFamilies.Standard")]
    [InlineData("fantasy", "GenericFamilies.Standard")]
    [InlineData("math", "GenericFamilies.Standard")]
    [InlineData("emoji", "GenericFamilies.Standard")]
    [InlineData("fangsong", "GenericFamilies.Standard")]
    [InlineData("ui-sans-serif", "GenericFamilies.Standard")]
    public void A_generic_family_the_options_do_not_map_is_refused_in_both_modes_naming_the_option(string keyword, string property)
    {
        var html = Doc($"<p style=\"font-family: {keyword}\">a</p>");
        foreach (var policy in new[] { UnsupportedPolicy.Refuse, UnsupportedPolicy.Warn })
        {
            var refusal = Refusal(html, GenericFamilies.None, policy);
            refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Font);
            refusal.Message.Should().Be($"'font-family: {keyword}' needs a family for the generic family '{keyword}': set {property} in the options (a browser would take a font of the system; there are no system fonts).");
            (refusal.Metadata![ErrorMetadataKeys.SourceLine], refusal.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((1, 107), "at the start tag of the element whose font needs it");
        }
    }

    [Theory]
    [InlineData("serif", "Theano Didot")]
    [InlineData("sans-serif", "Lato")]
    [InlineData("system-ui", "Lato")]
    [InlineData("monospace", "Lato")]
    [InlineData("cursive", "Theano Didot")]
    [InlineData("ui-monospace", "Theano Didot")]
    [InlineData("ui-serif", "Theano Didot")]
    public void Each_generic_family_takes_the_family_of_its_option(string keyword, string family) =>
        Families(Doc($"<p style=\"font-family: {keyword}\">a</p>"), new GenericFamilies { Standard = "Theano Didot", SansSerif = "Lato", Monospace = "Lato" })
            .Should().Equal(family);

    [Fact]
    public void Serif_takes_its_own_option_before_the_standard_family()
    {
        var html = Doc("<p style=\"font-family: serif\">a</p>");
        Families(html, new GenericFamilies { Standard = "Theano Didot", Serif = "Lato" }).Should().Equal("Lato");
        Families(html, new GenericFamilies { Standard = "Theano Didot" }).Should().Equal(["Theano Didot"], "serif takes Standard when Serif is not set");
    }

    [Fact]
    public void A_mapped_name_no_face_answers_is_refused_naming_the_option_and_the_name()
    {
        Families(Doc("<p style=\"font-family: Lato, sans-serif\">a</p>"), new GenericFamilies { SansSerif = "Inter" }).Should().Equal(["Lato"], "the list ends before the generic family");
        Refusal(Doc("<p style=\"font-family: sans-serif\">a</p>"), new GenericFamilies { SansSerif = "Inter" }, UnsupportedPolicy.Warn).Message
            .Should().Be("The family 'Inter' that GenericFamilies.SansSerif maps 'sans-serif' to has no face: give it in a @font-face rule or in the fonts of the options.");
    }

    [Fact]
    public void A_mapped_name_is_looked_up_as_a_written_name_with_the_same_weight_rules()
    {
        var generics = new GenericFamilies { SansSerif = "Lato" };
        var html = Doc("<p style=\"font-family: sans-serif; font-weight: 300\">a</p>");
        Refusal(html, generics).Code.Should().Be(FontErrorCodes.NotFound);
        Refusal(Doc("<p style=\"font-family: Lato; font-weight: 300\">a</p>"), generics).Code.Should().Be(FontErrorCodes.NotFound, "the written name is refused the same way");

        var resources = (ResourceResolver)(url => url == "theano.ttf" ? new HtmlResource(TestFonts.Bytes("TheanoDidot-Regular.ttf")) : null);
        var withRule = Doc("<p style=\"font-family: sans-serif\">a</p>", "@font-face { font-family: Lato; src: url(theano.ttf) }");
        var result = Boxes(withRule, generics, resources: resources);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        Runs(result.Value.Document.Root).Single().Style!.FontFamily.Should().Be("Theano Didot", "the document's @font-face rule of the mapped name comes before the caller's fonts");
    }

    [Fact]
    public void An_unmapped_generic_family_is_refused_even_when_a_later_family_is_supplied() =>
        Refusal(Doc("<p style=\"font-family: Nobody, sans-serif, Lato\">a</p>"), new GenericFamilies { Standard = "Lato" }).Message
            .Should().StartWith("'font-family: Nobody, sans-serif, Lato' needs a family for the generic family 'sans-serif'", "a browser would print the system's sans-serif, not Lato");

    [Fact]
    public void A_list_that_runs_out_without_a_supplied_family_is_refused_in_both_modes()
    {
        foreach (var policy in new[] { UnsupportedPolicy.Refuse, UnsupportedPolicy.Warn })
        {
            var refusal = Refusal(Doc("<p style=\"font-family: Inter, Roboto\">a</p>"), new GenericFamilies { Standard = "Lato" }, policy);
            refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Font);
            refusal.Message.Should().Be("No family of 'font-family: Inter, Roboto' is supplied: give one in a @font-face rule or in the fonts of the options, or end the list with a generic family the options map (there are no system fonts).");
            refusal.Metadata![ErrorMetadataKeys.Value].Should().Be("Inter, Roboto");
        }
    }

    [Fact]
    public void The_initial_value_takes_the_standard_family_also_through_inherit_unset_and_initial()
    {
        var html = Doc("<p>a</p><p style=\"font-family: initial\">b</p><div style=\"font-family: initial\"><p style=\"font-family: inherit\">c</p><p style=\"font-family: unset\">d</p><p style=\"font-family: revert\">e</p></div>", "body { font-family: Lato }");

        Families(html, new GenericFamilies { Standard = "Theano Didot" }).Should().Equal("Lato", "Theano Didot", "Theano Didot", "Theano Didot", "Theano Didot");
        Families(Doc("<p>a</p>"), new GenericFamilies { Standard = "Theano Didot" }).Should().Equal("Theano Didot");
    }

    [Fact]
    public void A_font_face_rule_of_times_new_roman_takes_the_initial_value_before_the_standard_family()
    {
        var resources = (ResourceResolver)(url => url == "lato.ttf" ? new HtmlResource(TestFonts.Bytes("Lato-Regular.ttf")) : null);
        var result = Boxes(Doc("<p>a</p>", "@font-face { font-family: \"Times New Roman\"; src: url(lato.ttf) }"), new GenericFamilies { Standard = "Theano Didot" }, resources: resources);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        Runs(result.Value.Document.Root).Single().Style!.FontFamily.Should().Be("Lato");
    }

    [Fact]
    public void The_initial_value_without_a_standard_family_is_refused_naming_the_option()
    {
        foreach (var policy in new[] { UnsupportedPolicy.Refuse, UnsupportedPolicy.Warn })
        {
            var refusal = Refusal(Doc("<p>a</p>"), new GenericFamilies { SansSerif = "Lato" }, policy);
            refusal.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Font);
            refusal.Message.Should().Be("'font-family' is not set here, and its initial value needs a family: set GenericFamilies.Standard in the options, or give 'Times New Roman' in a @font-face rule or in the fonts of the options (a browser would take a font of the system; there are no system fonts).");
            refusal.Metadata![ErrorMetadataKeys.Value].Should().Be("Times New Roman", "the value as before: the status lines of the refused cases stay");
        }

        Refusal(Doc("<p>a</p>"), new GenericFamilies { Standard = "Inter" }).Message
            .Should().Be("The family 'Inter' that GenericFamilies.Standard maps the initial value of 'font-family' to has no face: give it in a @font-face rule or in the fonts of the options.");
    }

    [Fact]
    public void A_written_times_new_roman_nobody_supplies_is_an_unsupplied_name_not_the_standard_family()
    {
        var generics = new GenericFamilies { Standard = "Theano Didot" };
        var refusal = Refusal(Doc("<p style='font-family: \"Times New Roman\"'>a</p>", "body { font-family: Lato }"), generics);

        refusal.Message.Should().Be("No family of 'font-family: Times New Roman' is supplied: give one in a @font-face rule or in the fonts of the options, or end the list with a generic family the options map (there are no system fonts).");
        Families(Doc("<p>a</p><p style='font-family: \"Times New Roman\", serif'>b</p>", "body { font-family: Lato }"), generics)
            .Should().Equal("Lato", "Theano Didot");
    }

    [Fact]
    public void A_margin_box_takes_the_generic_families_through_the_same_rules()
    {
        var css = "@page { size: 400pt 300pt; margin: 60pt 50pt; font-size: 10pt;\n@top-left { content: \"Aa\"; font-family: monospace } @top-right { content: \"Bb\" } }";
        var html = Doc("<p style=\"font-family: Lato\">one</p>", css);

        var result = Boxes(html, new GenericFamilies { Standard = "Theano Didot", Monospace = "Lato" });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        Runs((Box)result.Value.Document.Page.Template.Header!.Content).Select(run => run.Style!.FontFamily).Should().Equal("Lato", "Theano Didot");

        var box = Refusal(html, new GenericFamilies { Standard = "Theano Didot" });
        box.Message.Should().StartWith("'font-family: monospace' needs a family for the generic family 'monospace': set GenericFamilies.Monospace");
        (box.Metadata![ErrorMetadataKeys.SourceLine], box.Metadata[ErrorMetadataKeys.SourceColumn]).Should().Be((2, 13), "at the margin box's content declaration");
    }

    [Theory]
    [InlineData("", "is empty")]
    [InlineData("  ", "is empty")]
    [InlineData("La\u0000to", "holds a control character")]
    [InlineData("La\nto", "holds a control character")]
    [InlineData("serif", "is a generic family itself: name a family a face has")]
    [InlineData("Sans-Serif", "is a generic family itself: name a family a face has")]
    public void A_mapped_name_is_checked_once_as_input(string name, string problem)
    {
        var options = HtmlRenderOptions.Default with { Fonts = Fonts, GenericFamilies = new GenericFamilies { Monospace = name } };

        var result = HtmlRendering.ToPages(Doc("<p style=\"font-family: Lato\">a</p>"), options);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(HtmlStyleErrorCodes.InvalidOptions);
        result.Error.Message.Should().Be($"The family name of GenericFamilies.Monospace {problem}.");
        result.Error.Metadata![ErrorMetadataKeys.Property].Should().Be("GenericFamilies.Monospace");
    }

    [Fact]
    public void A_mapped_name_longer_than_a_font_s_family_can_be_is_refused_as_input()
    {
        var result = Boxes(Doc("<p style=\"font-family: Lato\">a</p>"), new GenericFamilies { Standard = new string('a', GenericFamilies.MaxNameLength + 1), Serif = new string('a', GenericFamilies.MaxNameLength) });

        result.Error.Parts().Select(error => error.Message).Should().Equal($"The family name of GenericFamilies.Standard is longer than {GenericFamilies.MaxNameLength} characters.");
    }

    [Fact]
    public void The_render_options_pass_the_generic_families_to_box_generation()
    {
        var options = HtmlRenderOptions.Default with { Fonts = Fonts, GenericFamilies = new GenericFamilies { Standard = "Theano Didot", SansSerif = "Lato" } };

        var pages = HtmlRendering.ToPages(Doc("<p>a</p><p style=\"font-family: sans-serif\">b</p>"), options);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        HtmlRendering.ToPages(Doc("<p>a</p>"), options with { GenericFamilies = GenericFamilies.None }).Error.Code.Should().Be(HtmlStyleErrorCodes.Unsupported.Font);
    }
}
