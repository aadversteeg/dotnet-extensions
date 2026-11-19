using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static UnitTests.Extensions.Documents.Html.Styling;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// The work and memory of the cascade, counted instead of timed: what a resolution charges to its budget
/// (<see cref="CascadeBudget"/>) for the shapes of input whose cost grew faster than the input, at a size and twice that
/// size; the values of the limits; and that a limit ends the resolution where it is reached.
/// </summary>
public class CascadeWorkTests
{
    private static string Doc(string style, string body) => $"<!DOCTYPE html><html><head><style>{style}</style></head><body>{body}</body></html>";

    private static string Repeat(string text, int n) => new StringBuilder(text.Length * n).Insert(0, text, n).ToString();

    /// <summary>Resolves (warn mode) with a budget of its own and returns the budget with what was spent.</summary>
    private static (Result<StyledHtmlDocument, Error> Result, CascadeBudget Budget) Counted(string html, CascadeBudget? budget = null, HtmlStyleOptions? options = null)
    {
        var parsed = Parsed(html);
        budget ??= new CascadeBudget();
        CascadeBudget.ForNextIndex = budget;
        try
        {
            return (HtmlStyleResolver.Resolve(parsed, options ?? Warn), budget);
        }
        finally
        {
            CascadeBudget.ForNextIndex = null;
        }
    }

    private static long Work(string html)
    {
        var (result, budget) = Counted(html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
        return budget.Work;
    }

    /// <summary>Work at twice the size is at most a little over twice the work: linear, not quadratic.</summary>
    private static void Linear(Func<int, string> shape, int n)
    {
        var (small, large) = (Work(shape(n)), Work(shape(2 * n)));

        large.Should().BeLessThanOrEqualTo((long)(2.2 * small) + 1_000, $"work {small} at {n}, {large} at {2 * n}");
    }

    [Fact]
    public void The_limits_have_their_stated_values_and_a_budget_starts_at_them()
    {
        CssInputLimits.MaxCascadeWork.Should().Be(1L << 26);
        CssInputLimits.MaxCascadeMemo.Should().Be(1 << 22);
        CssInputLimits.MaxCascadeEntries.Should().Be(1 << 21);
        CssInputLimits.MaxStyleSheets.Should().Be(1024);
        CssInputLimits.MaxSubstitutionWork.Should().Be(1L << 23);
        var budget = new CascadeBudget();
        (budget.MaxWork, budget.MaxMemo, budget.MaxEntries, budget.MaxStyleSheets, budget.MaxSubstitution)
            .Should().Be((CssInputLimits.MaxCascadeWork, (long)CssInputLimits.MaxCascadeMemo, CssInputLimits.MaxCascadeEntries, CssInputLimits.MaxStyleSheets, CssInputLimits.MaxSubstitutionWork));
    }

    [Fact]
    public void The_work_of_a_small_document_is_counted_where_it_is_done()
    {
        // Pins the places that charge: a change in what is counted changes these numbers. Matching (facts and simple
        // selectors), collecting (rules and declarations) and cascading (one unit per candidate looked at) of the user
        // agent's sheet and the author's one rule, on html, head, style, body and p.
        var plain = Work(Doc("x{color:red}", "<p id=last>x"));
        var matched = Work(Doc("p{color:red}", "<p id=last>x"));
        var two = Work(Doc("p{color:red} #last{color:blue}", "<p id=last>x"));
        var descendant = Work(Doc("div p{color:red}", "<div><p id=last>x</div>"));

        // One more rule that matches the p: one fact, one simple selector, the rule and its declaration collected
        // (1 + 1) and one more candidate for color looked at: 5. The second rule the same: the highest candidate is
        // taken by one scan, without a sort. A descendant combinator adds the facts of the ancestor walk (and the div's
        // own matching and cascade).
        (plain, matched - plain, two - matched, descendant).Should().Be((PlainDocument, MatchedRule, MatchedRule, DescendantDocument));
    }

    // The work of the documents above, counted on this engine (the user agent's sheet included); a change of what is
    // charged where changes them.
    private const long PlainDocument = 248;
    private const long MatchedRule = 5;
    private const long DescendantDocument = 306;

    [Fact]
    public void Every_size_of_the_memo_gives_the_same_result()
    {
        // The memo fills at any point of the cascade: a fact computed and not remembered is computed again, and the
        // facts of one step and element (matched here, or by an ancestor or an earlier sibling) never stand for each
        // other.
        var html = Doc(
            "section p{--v:1} section div p{margin-top:2px} div > .a + .b ~ .c{color:red} div{& .x{margin-left:4px}} p+p+p{padding-top:1px}",
            Repeat("<section><div><p class='a x'><p class='b x'><p class='c x'></div></section><div><p class=c></div>", 3));
        var (full, budget) = Counted(html);
        var expected = Text(full.Value);
        expected.Should().Contain("--v=1").And.Contain("rgb(255, 0, 0)").And.Contain("2px").And.Contain("4px").And.Contain("1px");

        for (var memo = 0L; memo <= budget.Memo; memo++)
        {
            Text(Counted(html, new CascadeBudget { MaxMemo = memo }).Result.Value).Should().Be(expected, $"memo {memo} of {budget.Memo}");
        }
    }

    [Fact]
    public void A_value_through_var_is_read_once_for_the_elements_that_share_its_declaration_and_custom_properties()
    {
        // 200 paragraphs share the root's custom properties: each declaration is substituted and parsed once, so they
        // share one computed value (a family list of any length is kept once, not once per element). The paragraphs
        // that declare their own --f have a table of their own and their own value.
        var styled = Styled(Doc(
            ":root{--f:a,b,c;--m:1px 2px} p{font-family:var(--f);margin:var(--m)} .own{--f:d}",
            Repeat("<p>x</p>", 200) + Repeat("<p class=own>x</p>", 2)));
        var paragraphs = styled.Root.Elements().Where(element => element.Element.LocalName == "p").ToList();
        var shared = paragraphs.Take(200).ToList();
        var own = paragraphs.Skip(200).ToList();

        shared.Select(element => element.Style[PropertyRegistry.FontFamily]).Distinct(ReferenceEqualityComparer.Instance).Should().ContainSingle();
        shared.Select(element => element.Style[PropertyRegistry.Find("margin-left")!]).Distinct(ReferenceEqualityComparer.Instance).Should().ContainSingle();
        (shared[0].Value("font-family"), shared[0].Value("margin-left"), own[0].Value("font-family"), own[1].Value("font-family")).Should().Be(("a, b, c", "2px", "d", "d"));
        own[0].Style[PropertyRegistry.FontFamily].Should().NotBeSameAs(shared[0].Style[PropertyRegistry.FontFamily]);
    }

    [Fact]
    public void The_presentational_hints_of_a_reopened_element_are_made_once_for_all_its_copies()
    {
        // The parser makes the font element again in each of 100 paragraphs, with the one token's attributes: its face is
        // read once, so the copies share one computed family list; their colour and size are the hints' too.
        var styled = Styled("<!DOCTYPE html><title>t</title><p><font face='a,b,c' color=red size=5>x" + Repeat("<p>x", 100));
        var fonts = styled.Root.Elements().Where(element => element.Element.LocalName == "font").ToList();

        fonts.Should().HaveCount(101);
        fonts.Select(element => element.Style[PropertyRegistry.FontFamily]).Distinct(ReferenceEqualityComparer.Instance).Should().ContainSingle();
        fonts.Select(element => (element.Value("font-family"), element.Value("color"), element.Value("font-size"))).Distinct()
            .Should().Equal(("a, b, c", "rgb(255, 0, 0)", "24px"));
    }

    [Fact]
    public void A_cell_takes_the_hints_of_its_own_table_when_the_cells_share_their_attributes()
    {
        // Cells without attributes share one attribute table; their hints also come from the table around them.
        var styled = Styled("<!DOCTYPE html><title>t</title><table cellpadding=3><tr><td>x</table><table cellpadding=7><tr><td>y</table>");

        styled.Root.Elements().Where(element => element.Element.LocalName == "td").Select(element => element.Value("padding-top"))
            .Should().Equal("3px", "7px");
    }

    [Fact]
    public void Every_size_of_the_memo_gives_the_same_result_through_var()
    {
        // The substitutions and what they read as are remembered per declaration and custom-property table; when the memo
        // is full they are made again, with the same result: longhands, a shorthand, a refused and an invalid value,
        // a keyword, revert-layer and a value that differs by table.
        var html = Doc(
            ":root{--f:a,b;--m:1px 2px;--bad:1px 2px 3px 4px 5px} @layer x{p{color:red}} p{font-family:var(--f);margin:var(--m);color:var(--nope, revert-layer);width:var(--bad);display:var(--nope, inherit)} .b{--f:c;--m:3px} .c{text-align:var(--nope)}",
            Repeat("<p>x</p><p class=b>y</p><div><p class='b c'>z</p></div>", 3));
        string Values(StyledHtmlDocument document) => string.Join("\n", document.Root.Elements().Select(element =>
            string.Join(";", new[] { "font-family", "margin-top", "margin-left", "color", "width", "display", "text-align" }.Select(element.Value))));
        var (full, budget) = Counted(html);
        var expected = Values(full.Value);
        expected.Should().Contain("a, b;1px;2px;rgb(255, 0, 0);auto;block;start").And.Contain("c;3px;3px;rgb(255, 0, 0);auto;block;start");

        for (var memo = 0L; memo <= budget.Memo; memo++)
        {
            var result = Counted(html, new CascadeBudget { MaxMemo = memo }).Result;
            Values(result.Value).Should().Be(expected, $"memo {memo} of {budget.Memo}");
            result.Value.Diagnostics.Select(error => error.Message).Should().Equal(full.Value.Diagnostics.Select(error => error.Message), $"memo {memo} of {budget.Memo}");
        }
    }

    [Theory]
    // Each p sets a custom property of its own, so each has a custom-property table of its own; all read one inherited
    // --f through one declaration, directly or through a custom property of the rule. The reading is substituted once,
    // not once per p, so the substituted characters do not grow with the elements.
    [InlineData("p{font-family:var(--f)}")]
    [InlineData("p{--w:var(--f);font-family:var(--w)}")]
    public void A_reading_of_an_inherited_property_is_shared_by_elements_with_custom_properties_of_their_own(string rule)
    {
        long Substituted(int paragraphs) => Counted(Doc(":root{--f:aaaaaaaaaa,bbbbbbbbbb} " + rule, string.Concat(Enumerable.Range(0, paragraphs).Select(i => $"<p style='--v:{i}'>x</p>")))).Budget.Substituted;

        Substituted(1).Should().BeGreaterThan(0);
        Substituted(200).Should().Be(Substituted(1));
    }

    [Fact]
    public void The_resolution_fails_at_exactly_the_substitution_limit()
    {
        // Each div has a custom property of its own that --c reads, so each substitutes --c itself: the work grows
        // with the divs.
        var html = Doc(":root{--a:abcdefghij;--b:var(--a) var(--a)} div{--c:var(--b) var(--z);color:var(--nope, red)}", string.Concat(Enumerable.Range(0, 20).Select(i => $"<div style='--z:{i}'>")));
        var (full, budget) = Counted(html);
        full.IsSuccess.Should().BeTrue();
        var spent = budget.Substituted;

        Counted(html, new CascadeBudget { MaxSubstitution = spent }).Result.IsSuccess.Should().BeTrue();
        var refused = Counted(html, new CascadeBudget { MaxSubstitution = spent - 1 }).Result;
        refused.Error.Code.Should().Be(CssErrorCodes.TooLarge);
        refused.Error.Message.Should().Contain($"more than {spent - 1} characters");
        refused.Error.Metadata![ErrorMetadataKeys.Value].Should().Be(spent - 1);
    }

    [Fact]
    public void A_small_sheet_whose_substitutions_pass_the_limit_ends_the_resolution_with_an_error_that_names_it()
    {
        // Under 1 KB of style sheet: --a19 is 2^20 - 1 characters made by doubling, and each of 16 nested divs
        // substitutes it again into --c beside a custom property of its own: 16 MiB, twice the limit.
        var css = ":root{--a0:a;" + string.Concat(Enumerable.Range(1, 19).Select(i => $"--a{i}:var(--a{i - 1}) var(--a{i - 1});")) + "} div{--c:var(--a19) var(--z)}";
        var body = string.Concat(Enumerable.Range(0, 16).Select(i => $"<div style='--z:{i}'>"));

        var error = Refused(Doc(css, body), Warn);

        css.Length.Should().BeLessThan(1_000);
        error.Code.Should().Be(CssErrorCodes.TooLarge);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be(CssInputLimits.MaxSubstitutionWork);
        error.Message.Should().Contain($"more than {CssInputLimits.MaxSubstitutionWork} characters");
    }

    [Fact]
    public void A_nested_rule_matches_its_own_parent_on_an_element_that_two_parents_match()
    {
        // Two parent rules whose nested rules both ask about the same elements: each nested rule asks its own parent.
        var styled = Styled(Doc(
            ".a{ & {margin-top:1px} & u{margin-left:1px} } .b{ & {margin-top:5px; margin-bottom:5px} & u{margin-right:5px} }",
            "<p class=a id=t1><u id=t2>x</u></p>"));

        (styled.ById("t1").Value("margin-top"), styled.ById("t1").Value("margin-bottom")).Should().Be(("1px", "16px"));
        (styled.ById("t2").Value("margin-left"), styled.ById("t2").Value("margin-right")).Should().Be(("1px", "0px"));
    }

    private static string Text(StyledHtmlDocument document) => string.Join("\n", document.Root.Elements().Select(element =>
        string.Join(";", new[] { "color", "margin-top", "margin-left", "padding-top" }.Select(element.Value)) + " " + string.Join(",", element.Style.CustomProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value.Text))));

    [Fact]
    public void The_resolution_fails_at_exactly_the_work_limit()
    {
        var html = Doc(Repeat("p{color:red}", 50), Repeat("<p>", 50));
        var work = Work(html);

        Counted(html, new CascadeBudget { MaxWork = work }).Result.IsSuccess.Should().BeTrue();
        var (refused, budget) = Counted(html, new CascadeBudget { MaxWork = work - 1 });
        refused.Error.Code.Should().Be(CssErrorCodes.TooLarge);
        budget.Work.Should().BeLessThanOrEqualTo(work);
    }

    [Fact]
    public void Past_the_work_limit_the_resolution_stops_at_once_in_both_modes()
    {
        // 2 000 rules that match all of 2 000 elements: 4 million pairs; a budget of 20 000 is passed after a few
        // elements, and nothing after that is matched or collected.
        var html = Doc(Repeat("*{color:red}", 2_000), Repeat("<p>", 2_000));
        foreach (var policy in new[] { UnsupportedPolicy.Warn, UnsupportedPolicy.Refuse })
        {
            var (result, budget) = Counted(html, new CascadeBudget { MaxWork = 20_000 }, Warn with { Unsupported = policy });

            result.Error.Code.Should().Be(CssErrorCodes.TooLarge);
            budget.Work.Should().BeLessThan(20_000 + 10_000);
        }
    }

    [Fact]
    public void Revert_layer_in_many_layers_costs_the_layers_once()
    {
        Linear(n => Doc(Repeat("@layer{p{color:revert-layer}}", n), Repeat("<p>", 20)), 2_000);
    }

    [Theory]
    [InlineData(":not({0})")]
    [InlineData("[lang|='{0}']")]
    [InlineData("[title^='{0}' i]")]
    [InlineData("[title$='{0}']")]
    [InlineData("[title*='{0}']")]
    [InlineData("[title='{0}' i]")]
    public void A_long_name_or_value_that_cannot_match_by_its_length_costs_nothing_for_its_length(string selector)
    {
        // The selector's name is made lowercase once, and a value shorter than the one wanted is not compared: the
        // work does not grow with the length (it was one copy of the name or value per test).
        long WorkOf(int length) => Work(Doc(string.Format(System.Globalization.CultureInfo.InvariantCulture, selector, new string('A', length)) + "{color:red}", Repeat("<p lang=b title=b>", 200)));

        WorkOf(40_000).Should().Be(WorkOf(20_000));
    }

    [Theory]
    [InlineData("[{0}]", "x")]
    [InlineData("[title*=b]", "{0}")]
    [InlineData("[title*=b i]", "{0}")]
    public void A_test_that_compares_a_long_name_or_value_is_charged_its_length(string selector, string title)
    {
        // A name looked up is hashed, a value searched is read: one unit per 64 characters, so doubling the length
        // about doubles the work beyond the base.
        string Long(string text, int length) => string.Format(System.Globalization.CultureInfo.InvariantCulture, text, new string('A', length));
        long WorkOf(int length) => Work(Doc(Repeat(Long(selector, length) + "{color:red}", 20), Repeat($"<p title='{Long(title, length)}'>", 100)));

        var (small, large) = (WorkOf(20_000), WorkOf(40_000));

        large.Should().BeGreaterThan(small + (20L * 100 * 20_000 / 64 / 2), $"work {small} at 20 000, {large} at 40 000");
        large.Should().BeLessThanOrEqualTo((long)(2.2 * small) + 1_000);
    }

    [Fact]
    public void Many_references_in_one_custom_property_cost_their_number()
    {
        static string Shape(int n)
        {
            var css = new StringBuilder("p{--a:");
            for (var i = 0; i < n; i++)
            {
                css.Append($"var(--x{i}) ");
            }

            css.Append(';');
            for (var i = 0; i < n; i++)
            {
                css.Append($"--x{i}:var(--z,1);");
            }

            return Doc(css.Append('}').ToString(), Repeat("<p>", 5));
        }

        Linear(Shape, 5_000);
    }

    [Fact]
    public void The_members_of_a_cycle_are_marked_once_and_charged_so_the_work_limit_ends_their_reporting()
    {
        // One name that reads n others, each of which reads it back: n cycles through one name. Each further leaf costs
        // the same 9 steps whatever n, 2 of them for reporting it (its mark and the step over the member below it), as
        // the members already marked are stepped over, not walked again; and a budget without those 2 steps ends the
        // resolution with the work limit's error.
        static string Fan(int n) => Doc(":root{--a0:" + string.Join(" ", Enumerable.Range(0, n).Select(i => $"var(--l{i})")) + ";"
            + string.Concat(Enumerable.Range(0, n).Select(i => $"--l{i}:var(--a0);")) + "} p{width:var(--a0, 7px)}", "<p id=last>x");
        var (small, large) = (Work(Fan(1_000)), Work(Fan(2_000)));

        (large - small).Should().Be(9 * 1_000);
        var (result, budget) = Counted(Fan(2_000), new CascadeBudget { MaxWork = small + 7 * 1_000 });
        result.Error.Code.Should().Be(CssErrorCodes.TooLarge);
        budget.Work.Should().BeLessThanOrEqualTo(large);
    }

    [Fact]
    public void The_memo_never_holds_more_than_its_limit()
    {
        var html = Doc("div > .a + .b ~ .c{color:red} p+p+p{padding-top:1px} section p{--v:1}", Repeat("<section><div><p class=a><p class=b><p class=c></div></section>", 20));
        var full = Counted(html).Budget.Memo;

        foreach (var memo in new[] { 0L, 1, 7, full / 2, full - 1 })
        {
            var (result, budget) = Counted(html, new CascadeBudget { MaxMemo = memo });
            result.IsSuccess.Should().BeTrue();
            budget.Memo.Should().BeLessThanOrEqualTo(memo, $"memo {memo} of {full}");
        }
    }

    [Fact]
    public void The_work_limit_ends_one_matching_call_with_a_small_memo()
    {
        // A chain of ~ over 60 siblings with a memo of 64 facts: past the memo the facts are computed again, inside the
        // work limit, so the call ends at the limit and not after millions of steps.
        var (result, budget) = Counted(Doc("u ~ i ~ i ~ i ~ i ~ b{color:red}", Repeat("<i></i>", 60) + "<b></b>"), new CascadeBudget { MaxWork = 3_000, MaxMemo = 64 });

        result.Error.Code.Should().Be(CssErrorCodes.TooLarge);
        budget.Work.Should().BeLessThanOrEqualTo(3_000 + 100);
    }

    [Fact]
    public void Every_work_limit_below_the_work_of_a_document_fails_with_the_work_limits_error()
    {
        // A rule whose :not() argument is where the limit can be passed, and a value that would be refused as too deep
        // if the rule were kept: a match must not be kept past the limit, so every smaller limit gives TooLarge.
        const string html = "<!DOCTYPE html><html class=a style=\"--d: ((((((((((((((((((((((((((((((((((((((((1px))))))))))))))))))))))))))))))))))))))))\"><head class=a><style class=a>:not(.a) { margin-left: calc(((((((((((((((((((((((((((((((var(--d)))))))))))))))))))))))))))))))) }</style></head><body class=a></body></html>";
        var work = Work(html);

        for (var limit = 1L; limit < work; limit++)
        {
            Counted(html, new CascadeBudget { MaxWork = limit }).Result.Error.Code.Should().Be(CssErrorCodes.TooLarge, $"limit {limit} of {work}");
        }
    }

    [Fact]
    public void The_cascade_charges_each_candidate_it_walks_past_a_roll_back()
    {
        // Each further rule p{color:revert} costs the same 18 steps; one of them is the look at its candidate, which
        // the cascade walks past once an earlier one has rolled the author's declarations back.
        static string Reverts(int n) => Doc(Repeat("p{color:revert}", n), "<p id=last>x");

        (Work(Reverts(2_000)) - Work(Reverts(1_000))).Should().Be(18 * 1_000);
    }

    [Fact]
    public void A_chain_of_one_compound_through_next_sibling_is_linear_in_the_siblings()
    {
        // p + p + … + p (2 000 steps) on twice and four times as many siblings: a run length per element.
        Linear(n => Doc(string.Join("+", Enumerable.Repeat("p", 2_000)) + "{color:red}", Repeat("<p>", n)), 4_000);
    }

    [Fact]
    public void A_chain_of_different_compounds_through_next_sibling_costs_its_steps_times_the_elements_and_ends_at_the_limit()
    {
        // .a + .b + .a + … : no linear structure; the work is the steps times the elements that start a match.
        static string Shape(int steps, int siblings) =>
            Doc(string.Join("+", Enumerable.Range(0, steps).Select(i => i % 2 == 0 ? ".a" : ".b")) + "{color:red}",
                string.Concat(Enumerable.Range(0, siblings).Select(i => i % 2 == 0 ? "<p class=a>" : "<p class=b>")));

        var (small, large) = (Work(Shape(200, 20_000)), Work(Shape(400, 20_000)));
        // Twice the steps, nearly twice the work (the rest is the elements' own).
        large.Should().BeGreaterThan((long)(1.6 * small), $"work {small} at 200 steps, {large} at 400");

        // 2 000 steps on 40 000 siblings: about 76 million units, past the limit, refused.
        var (result, _) = Counted(Shape(2_000, 40_000));
        result.Error.Code.Should().Be(CssErrorCodes.TooLarge);
    }

    [Fact]
    public void A_sheet_linked_many_times_is_fetched_once_and_counted_every_time_up_to_the_limit()
    {
        var fetched = 0;
        HtmlResource? Sheet(string url)
        {
            fetched++;
            return new HtmlResource(Encoding.UTF8.GetBytes("x{margin-top:1px}#last{margin-left:1px}"));
        }

        var options = Warn with { Resources = Sheet, BaseUrl = "https://example.org/" };
        string Links(int n) => $"<!DOCTYPE html><html><head>{Repeat("<link rel=stylesheet href=s.css>", n)}</head><body><p id=last>x</body></html>";

        // The user-agent sheet and the links: 1 + 1 023 sheets are inside the limit, one more is past it.
        var (inside, budget) = Counted(Links(CssInputLimits.MaxStyleSheets - 1), options: options);
        inside.IsSuccess.Should().BeTrue();
        budget.StyleSheets.Should().Be(CssInputLimits.MaxStyleSheets);
        fetched.Should().Be(1);
        Counted(Links(CssInputLimits.MaxStyleSheets), options: options).Result.Error.Code.Should().Be(CssErrorCodes.TooLarge);
    }

    [Fact]
    public void The_entries_of_the_index_are_counted_and_limited()
    {
        var html = Doc(Repeat("p{margin-top:1px}", 1_000), "<p>");
        var (_, budget) = Counted(html);

        // The author's 1 000 rules, one selector and one declaration each, after the user agent's.
        var userAgent = Counted(Doc(string.Empty, "<p>")).Budget.Entries;
        (budget.Entries - userAgent).Should().Be(2_000);
        Counted(html, new CascadeBudget { MaxEntries = (int)budget.Entries - 1 }).Result.Error.Code.Should().Be(CssErrorCodes.TooLarge);
    }

    [Fact]
    public void A_declaration_of_all_is_one_entry_and_a_repeated_one_is_kept_once()
    {
        var once = Counted(Doc("p{all:unset}", "<p id=last>")).Budget.Entries;
        var repeated = Counted(Doc("p{" + Repeat("all:unset;", 10_000) + "}", "<p id=last>"));

        repeated.Budget.Entries.Should().Be(once);
        repeated.Result.Value.ById("last").Value("display").Should().Be("inline");
    }

    [Fact]
    public void Elements_with_the_same_custom_properties_share_one_table()
    {
        var css = "html{" + string.Concat(Enumerable.Range(0, 2_000).Select(i => $"--c{i}:{i};")) + "} p{--own:1}";
        var styled = Styled(Doc(css, Repeat("<p>y", 2_000)));

        var tables = styled.Root.Elements().Where(element => element.Element.LocalName == "p").Select(element => element.Style.CustomProperties).Distinct(ReferenceEqualityComparer.Instance).Count();
        tables.Should().Be(1);
        styled.Root.Elements().First(element => element.Element.LocalName == "p").Style.CustomProperties.Should().HaveCount(2_001);
    }

    [Fact]
    public void An_unused_fallback_costs_nothing_however_long()
    {
        long WorkOf(int length) => Counted(Doc("*{--a:1;width:var(--a," + Repeat(" 0", length) + ")}", Repeat("<p>", 200))).Budget.Substituted;

        WorkOf(200_000).Should().Be(WorkOf(100_000));
    }

    [Fact]
    public void A_full_memo_changes_no_result()
    {
        // The memo full from the start: every fact and table is computed again, inside the work limit, with the same
        // results (matching facts, parent lists, of S tables, runs, custom tables, substitutions).
        var html = Doc(
            "section p{--v:1} div > .a + .b + .c{color:red} p:nth-child(2n of .x){margin-top:3px} div{& .x{margin-left:4px}} p+p+p{padding-top:1px} p{--w:var(--v) 2}",
            Repeat("<section><div><p class='a x'><p class='b x'><p class='c x'></div></section>", 20));
        var full = Counted(html).Result.Value;
        var empty = Counted(html, new CascadeBudget { MaxMemo = 0 }).Result.Value;

        Text(empty).Should().Be(Text(full));
        Text(full).Should().Contain("rgb(255, 0, 0)").And.Contain("3px").And.Contain("4px").And.Contain("1px").And.Contain("--w=1 2");
    }

    [Fact]
    public void Facts_remembered_and_counted_ancestors_match_after_a_sibling()
    {
        // i + b u: the fact "b follows an i" is needed for u through the descendant combinator.
        Styled(Doc("i + b u{color:red}", "<p><i></i><b><u id=t>x</u></b></p>")).ById("t").Value("color").Should().Be("rgb(255, 0, 0)");
    }

    [Fact]
    public void Elements_made_again_for_one_token_share_their_attributes_when_styled()
    {
        // Styling reads the shared attributes once (index, classes, handlers, style attribute), not once per element.
        var html = "<!DOCTYPE html><p><b class='k' style='margin-top:2px'" + string.Concat(Enumerable.Range(0, 4_000).Select(i => $" a{i}")) + ">" + Repeat("<p>x", 4_000);
        var parsed = Parsed(html);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var styled = HtmlStyleResolver.Resolve(parsed, Warn).Value;

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(400L << 20);
        styled.Root.Elements().Where(element => element.Element.LocalName == "b").Should().HaveCountGreaterThan(3_999)
            .And.OnlyContain(element => element.Value("margin-top") == "2px");
    }
}
