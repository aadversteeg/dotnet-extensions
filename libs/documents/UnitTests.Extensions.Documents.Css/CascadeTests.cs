using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.Css.Rules;
using static UnitTests.Extensions.Documents.Css.TestTree;

namespace UnitTests.Extensions.Documents.Css;

/// <summary>The cascade of CSS Cascade 5 §6, the defaulting of §7, CSS Nesting 1 and CSS Variables 1, end to end: style
/// sheet text in, computed values out.</summary>
public class CascadeTests
{
    private static TestElement Doc(params object[] body) => E("html", E("body", body));

    [Fact]
    public void The_more_specific_selector_wins_and_among_equals_the_later_one()
    {
        var root = Doc(E("p", A("id", "x"), A("class", "a b")));
        var computed = Compute(root, "#x { color: red } p.a { color: blue } p { color: green } .a.b { margin-top: 1px } .b.a { margin-top: 2px }");

        computed.Value(root["x"], "color").Should().Be("rgb(255, 0, 0)");
        computed.Value(root["x"], "margin-top").Should().Be("1.5pt");
    }

    [Fact]
    public void Important_declarations_reverse_the_origins_and_the_style_attribute_beats_the_sheets()
    {
        var root = Doc(E("p", A("id", "x")), E("div", A("id", "y")), E("span", A("id", "z")));
        var computed = Compute(root,
            "#x { color: red } p { color: blue !important } #y { color: red !important } span { color: blue !important }",
            userAgent: "p { color: green !important } div { color: green }",
            own: element => element.Id switch
            {
                "y" => Style("color: black"),
                "z" => Style("color: black !important"),
                _ => null,
            });

        // The user agent's important declaration beats every author declaration.
        computed.Value(root["x"], "color").Should().Be("rgb(0, 128, 0)");
        // An important author rule beats a normal style attribute.
        computed.Value(root["y"], "color").Should().Be("rgb(255, 0, 0)");
        // Among important author declarations the style attribute wins.
        computed.Value(root["z"], "color").Should().Be("rgb(0, 0, 0)");
    }

    [Fact]
    public void Presentational_hints_lose_to_every_author_rule_and_beat_the_user_agent()
    {
        var root = Doc(E("p", A("id", "x")), E("div", A("id", "y")));
        var computed = Compute(root, "#x { color: red } * { margin-top: 3px }", userAgent: "p, div { color: green; text-align: right }",
            own: element => element.Id is not null ? Hints("color: blue; text-align: center; margin-top: 9px") : null);

        computed.Value(root["x"], "color").Should().Be("rgb(255, 0, 0)");
        computed.Value(root["y"], "color").Should().Be("rgb(0, 0, 255)");
        computed.Value(root["y"], "text-align").Should().Be("center");
        computed.Value(root["y"], "margin-top").Should().Be("2.25pt");
    }

    [Fact]
    public void Revert_rolls_back_to_the_user_agent_and_counts_presentational_hints_as_author()
    {
        var root = Doc(E("p", A("id", "x")));
        var computed = Compute(root, "p { color: red } #x { color: revert; margin-top: revert }", userAgent: "p { color: green }",
            own: _ => Hints("color: blue; margin-top: 9px"));

        computed.Value(root["x"], "color").Should().Be("rgb(0, 128, 0)");
        computed.Value(root["x"], "margin-top").Should().Be("0pt");
    }

    [Fact]
    public void Layers_order_by_first_appearance_unlayered_wins_and_important_reverses()
    {
        var root = Doc(E("p", A("id", "x"), A("class", "a")));
        var css = """
            @layer base, theme;
            @layer theme { p { color: blue; margin-top: 1px !important } }
            @layer base { #x { color: red; margin-top: 2px !important } }
            p { margin-top: 5px }
            @layer { .a { color: green } }
            """;
        var computed = Compute(root, css);

        // theme is after base, so its p beats base's #x despite the specificity; the anonymous layer is after both.
        computed.Value(root["x"], "color").Should().Be("rgb(0, 128, 0)");
        // Important: the earlier layer wins, and important beats the unlayered normal declaration.
        computed.Value(root["x"], "margin-top").Should().Be("1.5pt");
    }

    [Fact]
    public void Revert_layer_rolls_back_to_the_previous_layer()
    {
        var root = Doc(E("p", A("id", "x")));
        var computed = Compute(root, "@layer a { p { color: red } } @layer b { p { color: blue } #x { color: revert-layer } } #x { margin-top: 1px }");

        computed.Value(root["x"], "color").Should().Be("rgb(255, 0, 0)");
    }

    [Fact]
    public void Sublayers_come_before_their_parent_layers_own_rules()
    {
        var root = Doc(E("p", A("id", "x")));
        var computed = Compute(root, "@layer a { p { color: red } @layer inner { #x { color: blue } } } @layer a.inner { p { margin-top: 1px } }");

        computed.Value(root["x"], "color").Should().Be("rgb(255, 0, 0)");
        computed.Value(root["x"], "margin-top").Should().Be("0.75pt");
    }

    [Fact]
    public void Inherited_properties_inherit_and_the_keywords_initial_inherit_unset_apply()
    {
        var root = Doc(E("div", A("id", "d"), E("p", A("id", "p")), E("span", A("id", "s")), E("em", A("id", "e"))));
        var computed = Compute(root, "#d { color: red; margin-top: 4px } #p { margin-top: inherit } #s { color: initial } #e { color: unset; margin-top: unset }");

        computed.Value(root["p"], "color").Should().Be("rgb(255, 0, 0)");
        computed.Value(root["p"], "margin-top").Should().Be("3pt");
        computed.Value(root["s"], "color").Should().Be("rgb(0, 0, 0)");
        computed.Value(root["e"], "color").Should().Be("rgb(255, 0, 0)");
        computed.Value(root["e"], "margin-top").Should().Be("0pt");
    }

    [Fact]
    public void Nested_rules_desugar_to_is_with_the_parents_specificity()
    {
        var root = Doc(E("div", A("class", "card"), E("p", A("id", "x"), A("class", "t"))), E("p", A("id", "y"), A("class", "t")));
        var css = """
            .card {
              color: red;
              .t { margin-top: 1px }
              & > p { color: blue }
              @media print { & .t { padding-top: 2px } }
              margin-left: 3px;
            }
            p.t { margin-top: 9px }
            """;
        var computed = Compute(root, css);

        computed.Value(root["x"], "color").Should().Be("rgb(0, 0, 255)");
        // .card .t (0,2,0) beats p.t (0,1,1).
        computed.Value(root["x"], "margin-top").Should().Be("0.75pt");
        computed.Value(root["x"], "padding-top").Should().Be("1.5pt");
        computed.Value(root["y"], "margin-top").Should().Be("6.75pt");
        computed.Value(root["y"], "color").Should().Be("rgb(0, 0, 0)");
        // The declarations after the nested rules still apply to the card.
        computed.Value(root.Children[0].Children[0], "margin-left").Should().Be("2.25pt");
    }

    [Fact]
    public void Media_and_supports_conditions_select_rules()
    {
        var root = Doc(E("p", A("id", "x")));
        var css = """
            @media screen { p { color: red } }
            @media print and (min-width: 100mm) { p { margin-top: 1px } }
            @media (max-width: 100mm) { p { margin-top: 2px } }
            @supports (display: flex) { p { margin-bottom: 1px } }
            @supports not (colour: red) { p { padding-top: 1px } }
            """;
        var computed = Compute(root, css);

        computed.Value(root["x"], "color").Should().Be("rgb(0, 0, 0)");
        computed.Value(root["x"], "margin-top").Should().Be("0.75pt");
        computed.Value(root["x"], "margin-bottom").Should().Be("0.75pt");
        computed.Value(root["x"], "padding-top").Should().Be("0.75pt");
        computed.Coded(CssErrorCodes.Unsupported.Property).Should().ContainSingle(error => error.Message.Contains("colour"));
    }

    [Fact]
    public void Var_substitutes_with_fallbacks_and_cycles_have_no_value()
    {
        var root = Doc(E("p", A("id", "x")));
        var css = """
            :root { --main: red; --gap: 2px; --a: var(--b); --b: var(--a); }
            p { color: var(--main); margin-top: var(--gap); margin-bottom: var(--none, 3px); padding-top: var(--a, 4px); margin: var(--gap) 0 0 0 }
            p { --local: var(--gap) var(--gap) }
            """;
        var computed = Compute(root, css);

        computed.Value(root["x"], "color").Should().Be("rgb(255, 0, 0)");
        computed.Value(root["x"], "margin-top").Should().Be("1.5pt");
        computed.Value(root["x"], "margin-bottom").Should().Be("0pt");
        computed.Value(root["x"], "padding-top").Should().Be("3pt");
        computed[root["x"]].CustomProperties["--local"].Text.Should().Be("2px 2px");
        computed[root["x"]].CustomProperties.Should().NotContainKey("--a");
    }

    [Fact]
    public void A_var_that_does_not_fit_the_property_makes_it_unset_not_the_previous_declaration()
    {
        var root = Doc(E("div", A("id", "d"), E("p", A("id", "x"))));
        var computed = Compute(root, "#d { color: blue } :root { --len: 3px } p { color: red; color: var(--len); margin-top: 1px; margin-top: var(--missing) }");

        computed.Value(root["x"], "color").Should().Be("rgb(0, 0, 255)");
        computed.Value(root["x"], "margin-top").Should().Be("0pt");
        computed.Coded(CssErrorCodes.Invalid.ComputedValue).Should().HaveCount(2);
    }

    [Fact]
    public void Shorthands_set_every_longhand_and_reset_the_ones_they_omit()
    {
        var root = Doc(E("p", A("id", "x")));
        var computed = Compute(root, "p { font-style: italic; line-height: 3; font: bold 20px/1.5 Arial, sans-serif; margin: 1px 2px; border: 2px solid red; border-top-color: blue }");
        var p = root["x"];

        computed.Value(p, "font-style").Should().Be("normal");
        computed.Value(p, "font-weight").Should().Be("700");
        computed.Value(p, "font-size").Should().Be("15pt");
        computed.Value(p, "line-height").Should().Be("1.5");
        computed.Value(p, "font-family").Should().Be("\"Arial\", sans-serif");
        computed.Value(p, "margin-left").Should().Be("1.5pt");
        computed.Value(p, "margin-bottom").Should().Be("0.75pt");
        computed.Value(p, "border-left-width").Should().Be("1.5pt");
        computed.Value(p, "border-top-color").Should().Be("rgb(0, 0, 255)");
        computed.Value(p, "border-bottom-color").Should().Be("rgb(255, 0, 0)");
    }

    [Fact]
    public void Font_sizes_resolve_keywords_ems_rems_and_percentages()
    {
        var root = Doc(E("div", A("id", "d"), E("p", A("id", "x")), E("span", A("id", "s")), E("em", A("id", "e"))));
        var computed = Compute(root, "html { font-size: 20px } #d { font-size: 2em } #x { font-size: 50%; margin-top: 2em; line-height: 150% } #s { font-size: 1rem } #e { font-size: larger }");

        computed.Value(root["d"], "font-size").Should().Be("30pt");
        computed.Value(root["x"], "font-size").Should().Be("15pt");
        computed.Value(root["x"], "margin-top").Should().Be("30pt");
        computed.Value(root["x"], "line-height").Should().Be("22.5pt");
        computed.Value(root["s"], "font-size").Should().Be("15pt");
        computed.Value(root["e"], "font-size").Should().Be("36pt");
    }

    [Fact]
    public void Border_widths_are_zero_without_a_style_and_snap_to_whole_pixels()
    {
        var root = Doc(E("p", A("id", "a")), E("p", A("id", "b")), E("p", A("id", "c")));
        var computed = Compute(root, "#a { border-top-width: 4px } #b { border: 0.5px solid } #c { border: 2.7px solid; border-left: thick solid }");

        computed.Value(root["a"], "border-top-width").Should().Be("0pt");
        computed.Value(root["b"], "border-top-width").Should().Be("0.75pt");
        computed.Value(root["c"], "border-top-width").Should().Be("1.5pt");
        computed.Value(root["c"], "border-left-width").Should().Be("3.75pt");
    }

    [Fact]
    public void The_root_is_blockified()
    {
        var root = Doc();
        var computed = Compute(root, "html { display: inline } body { display: inline }");

        computed.Value(root, "display").Should().Be("block");
        computed.Value(root.Children[0], "display").Should().Be("inline");
    }

    [Fact]
    public void A_refused_value_is_reported_when_it_would_win_and_the_next_declaration_is_used()
    {
        var root = Doc(E("p", A("id", "x")), E("div"));
        var computed = Compute(root, "p { display: block; display: flex } div { display: grid } div { display: block } #x { cursor: pointer; float: left; frobnicate: 1 } span { zoom-level: 2 }");

        computed.Value(root["x"], "display").Should().Be("block");
        computed.Value(root["x"], "float").Should().Be("none");
        computed.Coded(CssErrorCodes.Unsupported.Value).Select(error => error.Message).Should().BeEquivalentTo([
            "'display: flex' is not supported; the declaration is skipped.",
            "'float: left' is not supported (only none is); the declaration is skipped.",
        ]);
        computed.Coded(CssErrorCodes.Unsupported.Property).Should().ContainSingle(error => error.Message.Contains("frobnicate"));
        computed.Coded(CssErrorCodes.Ignored.Property).Should().ContainSingle(error => error.Message.Contains("cursor"));
    }

    [Fact]
    public void Invalid_declarations_are_dropped_and_reported_where_they_are()
    {
        var root = Doc(E("p", A("id", "x")));
        var computed = Compute(root, "p {\n  color: red;\n  color: 12px;\n  text-align: -internal-center;\n  -moz-box-sizing: border-box }");

        computed.Value(root["x"], "color").Should().Be("rgb(255, 0, 0)");
        computed.Value(root["x"], "text-align").Should().Be("start");
        var invalid = computed.Coded(CssErrorCodes.Invalid.Value).ToList();
        invalid.Should().HaveCount(2);
        invalid[0].Metadata![Ave.Extensions.Documents.Core.ErrorMetadataKeys.SourceLine].Should().Be(3);
        invalid[0].Metadata![Ave.Extensions.Documents.Core.ErrorMetadataKeys.SourceColumn].Should().Be(3);
        computed.Coded(CssErrorCodes.Invalid.Property).Should().ContainSingle();
    }

    [Fact]
    public void Internal_values_are_accepted_from_the_user_agent()
    {
        var root = Doc(E("div", A("style", ""), E("th", A("id", "x"))), E("table", E("th", A("id", "y"))));
        var computed = Compute(root, "div { text-align: right }", userAgent: "th { text-align: -internal-center }");

        computed.Value(root["x"], "text-align").Should().Be("right");
        computed.Value(root["y"], "text-align").Should().Be("center");
    }

    [Fact]
    public void Imports_are_loaded_through_the_caller_in_their_layer_and_cycles_are_skipped()
    {
        var root = Doc(E("p", A("id", "x")));
        var sheets = new Dictionary<string, StyleSheet>
        {
            ["a.css"] = Sheet("@import 'b.css'; p { color: red; margin-top: 1px }", "a.css"),
            ["b.css"] = Sheet("@import 'a.css'; p { color: blue; margin-bottom: 1px }", "b.css"),
        };
        var asked = new List<string>();
        StyleSheet? Load(ImportRule import, StyleSheet importer)
        {
            asked.Add(import.Url);
            return sheets[import.Url];
        }

        var computed = Compute(root, "@import url(a.css) layer(lib); @import 'print.css' screen; p { color: green }", imports: Load);

        computed.Value(root["x"], "color").Should().Be("rgb(0, 128, 0)");
        computed.Value(root["x"], "margin-top").Should().Be("0.75pt");
        computed.Value(root["x"], "margin-bottom").Should().Be("0.75pt");
        asked.Should().Equal("a.css", "b.css", "a.css");
        computed.Coded(CssErrorCodes.Invalid.Rule).Should().ContainSingle(error => error.Message.Contains("imports itself"));
    }

    [Fact]
    public void Relative_units_need_font_metrics_for_ex_and_ch()
    {
        var root = Doc(E("p", A("id", "x")), E("div", A("id", "y")));
        var css = "#x { margin-top: 2ex } #y { margin-top: 2ch; width: 10vw }";
        var without = Compute(root, css);
        var with = Compute(root, css, values: ValueContext.A4 with { FontMetrics = (_, _, _, size) => new FontMetrics(size / 2, size / 4) });

        without.Value(root["x"], "margin-top").Should().Be("0pt");
        without.Coded(CssErrorCodes.Unsupported.Value).Should().HaveCount(2);
        with.Value(root["x"], "margin-top").Should().Be("12pt");
        with.Value(root["y"], "margin-top").Should().Be("6pt");
        with.Value(root["y"], "width").Should().Be(Ave.Extensions.Documents.Core.Length.FromMillimetres(21).ToString());
    }

    [Fact]
    public void The_all_shorthand_resets_everything_but_direction()
    {
        var root = Doc(E("div", A("id", "d"), E("p", A("id", "x"))));
        var computed = Compute(root, "#d { color: red } p { margin-top: 1px; all: initial } p { margin-left: 2px }");

        computed.Value(root["x"], "color").Should().Be("rgb(0, 0, 0)");
        computed.Value(root["x"], "margin-top").Should().Be("0pt");
        computed.Value(root["x"], "margin-left").Should().Be("1.5pt");
        computed.Value(root["x"], "display").Should().Be("inline");
    }

    [Fact]
    public void Media_queries_see_the_page_size()
    {
        var root = Doc(E("p", A("id", "x")));
        var computed = Compute(root, "@media (min-width: 200mm) { p { color: red } }", media: new MediaContext("print", Ave.Extensions.Documents.Core.Length.FromMillimetres(148), Ave.Extensions.Documents.Core.Length.FromMillimetres(210)));

        computed.Value(root["x"], "color").Should().Be("rgb(0, 0, 0)");
    }

    [Fact]
    public void A_math_function_the_engine_does_not_evaluate_is_refused_in_every_property()
    {
        var root = Doc(E("p", A("id", "x")));
        var computed = Compute(root, "p { font-weight: calc(1em / 1px); margin-top: round(1.5px); color: rgb(random(0, 255) 0 0) }");

        computed.Value(root["x"], "font-weight").Should().Be("400");
        computed.Coded(CssErrorCodes.Unsupported.Calc).Should().HaveCount(3);
    }
}
