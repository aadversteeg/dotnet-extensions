using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>
/// The properties ignored by rule: they concern interaction, animation, scrolling or the screen, and have no effect on
/// a static paged document, so a declaration of one is dropped with a diagnostic of class
/// <see cref="DiagnosticClass.Ignored"/> rather than refused. Each was checked against Chromium 159's print rendering
/// (review 39 M10: the page rendered for print with and without the declaration, measured): the values that change it
/// are refused instead (a named animation, zoom other than 1, a resize handle, a scrollbar gutter, will-change that
/// makes a layer).
/// </summary>
public static partial class PropertyRegistry
{
    private const string Interaction = "interaction with a pointer or keyboard; a static document has none";
    private const string Motion = "transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page";
    private const string Scrolling = "scrolling; a paged document does not scroll";
    private const string Screen = "screen rendering hints; they do not change what is drawn on a page";

    private static readonly string[] AnimationKeywords =
    [
        "none", "infinite", "linear", "ease", "ease-in", "ease-out", "ease-in-out", "step-start", "step-end", "normal", "reverse",
        "alternate", "alternate-reverse", "forwards", "backwards", "both", "running", "paused", "replace", "add", "accumulate", "auto",
    ];

    private static IReadOnlyList<IgnoredProperty> IgnoredProperties()
    {
        var ignored = new List<IgnoredProperty>();
        void Add(string reason, params string[] names)
        {
            foreach (var name in names)
            {
                ignored.Add(new IgnoredProperty(name, reason));
            }
        }

        void AddRefusing(string name, string reason, string refused, Func<IReadOnlyList<ComponentValue>, bool> refuses) =>
            ignored.Add(new IgnoredProperty(name, reason) { RefusedValues = refused, Refuses = refuses });

        Add(Interaction,
            "cursor", "pointer-events", "user-select", "-webkit-user-select", "touch-action", "caret", "caret-color",
            "caret-shape", "caret-animation", "accent-color", "appearance", "-webkit-appearance", "-webkit-tap-highlight-color",
            "-webkit-touch-callout", "-webkit-user-drag", "-webkit-user-modify", "field-sizing", "interactivity");
        AddRefusing("resize", Interaction, "any value but none: Chromium 159 prints the resize handle",
            values => !OnlyKeywords(values, "none"));
        Add(Motion,
            "transition", "transition-property", "transition-duration", "transition-timing-function", "transition-delay",
            "transition-behavior", "animation-duration", "animation-timing-function",
            "animation-delay", "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state",
            "animation-composition", "animation-timeline", "animation-range", "animation-range-start", "animation-range-end",
            "view-transition-name", "view-transition-class");
        AddRefusing("animation", Motion, "a value that names an animation: Chromium 159 prints the animated state", NamesAnimation);
        AddRefusing("animation-name", Motion, "any value but none: Chromium 159 prints the animated state",
            values => !OnlyKeywords(values, "none"));
        AddRefusing("will-change", "a hint for the browser's compositor", "any value but auto, scroll-position and contents: Chromium 159 paints the element in a layer of its own, which changes the printed rendering",
            values => !OnlyKeywords(values, "auto", "scroll-position", "contents"));
        Add(Scrolling,
            "scroll-behavior", "scroll-margin", "scroll-margin-top", "scroll-margin-right", "scroll-margin-bottom",
            "scroll-margin-left", "scroll-margin-block", "scroll-margin-inline", "scroll-padding", "scroll-padding-top",
            "scroll-padding-right", "scroll-padding-bottom", "scroll-padding-left", "scroll-padding-block",
            "scroll-padding-inline", "scroll-snap-type", "scroll-snap-align", "scroll-snap-stop", "scroll-timeline",
            "scroll-timeline-name", "scroll-timeline-axis", "view-timeline", "view-timeline-name", "view-timeline-axis",
            "overscroll-behavior", "overscroll-behavior-x", "overscroll-behavior-y", "overscroll-behavior-block",
            "overscroll-behavior-inline", "overflow-anchor", "scrollbar-color", "scrollbar-width");
        AddRefusing("scrollbar-gutter", Scrolling, "any value but auto: Chromium 159 keeps the gutter's width out of the box also when printing",
            values => !OnlyKeywords(values, "auto"));
        Add(Screen,
            "-webkit-font-smoothing", "-moz-osx-font-smoothing", "font-smooth", "text-size-adjust", "-webkit-text-size-adjust",
            "-moz-text-size-adjust", "-ms-text-size-adjust", "forced-color-adjust", "speak", "speak-as");
        AddRefusing("zoom", Screen, "any value but normal, 1 and 100%: Chromium 159 scales the box when printing", values => !IsUnitZoom(values));
        return ignored;
    }

    /// <summary>The values without white space, as written.</summary>
    private static List<ComponentValue> Meaningful(IReadOnlyList<ComponentValue> values) =>
        [.. values.Where(value => value is not CssToken { Kind: CssTokenKind.Whitespace })];

    /// <summary>Whether the value is one of the keywords (or a CSS-wide keyword), possibly a comma-separated list of
    /// them.</summary>
    private static bool OnlyKeywords(IReadOnlyList<ComponentValue> values, params string[] keywords) =>
        Meaningful(values).All(value => value is CssToken { Kind: CssTokenKind.Comma }
            || (value is CssToken { Kind: CssTokenKind.Ident } word
                && (keywords.Contains(Ascii.ToLower(word.Text)) || Ascii.ToLower(word.Text) is "initial" or "inherit" or "unset" or "revert" or "revert-layer")));

    /// <summary>Whether an animation shorthand names an animation: an identifier that is not one of the other
    /// longhands' keywords, or a string.</summary>
    private static bool NamesAnimation(IReadOnlyList<ComponentValue> values) =>
        Meaningful(values).Any(value => value is CssToken { Kind: CssTokenKind.String }
            || (value is CssToken { Kind: CssTokenKind.Ident } word
                && !AnimationKeywords.Contains(Ascii.ToLower(word.Text))
                && Ascii.ToLower(word.Text) is not ("initial" or "inherit" or "unset" or "revert" or "revert-layer")));

    /// <summary>zoom: normal, 1 or 100%, or a CSS-wide keyword.</summary>
    private static bool IsUnitZoom(IReadOnlyList<ComponentValue> values) =>
        Meaningful(values) is [var only] && only switch
        {
            CssToken { Kind: CssTokenKind.Ident } word => Ascii.ToLower(word.Text) is "normal" or "initial" or "inherit" or "unset" or "revert" or "revert-layer",
            CssToken { Kind: CssTokenKind.Number } number => number.Number == 1,
            CssToken { Kind: CssTokenKind.Percentage } percentage => percentage.Number == 100,
            _ => false,
        };
}
