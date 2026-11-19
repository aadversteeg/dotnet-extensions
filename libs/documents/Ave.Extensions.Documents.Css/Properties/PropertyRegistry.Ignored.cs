using Ave.Extensions.Documents.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>
/// The properties ignored by rule: they concern interaction, animation, scrolling or the screen, and have no effect on
/// a static paged document, so a declaration of one is dropped with a diagnostic of class
/// <see cref="DiagnosticClass.Ignored"/> rather than refused. Each was checked against Chromium 159's print rendering
/// (the page rendered for print with and without the declaration, measured): the values that change it
/// are refused instead (a named animation, zoom other than 1, a resize handle, a scrollbar gutter, will-change that
/// makes a layer).
/// </summary>
public static partial class PropertyRegistry
{
    private const string Interaction = "interaction with a pointer or keyboard; a static document has none";
    private const string Motion = "transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page";
    private const string Scrolling = "scrolling; a paged document does not scroll";
    private const string Screen = "screen rendering hints; they do not change what is drawn on a page";

    /// <summary>The keywords of the animation shorthand other than a name, each with the longhand it sets (CSS
    /// Animations 1 §3.10). Chromium 159 takes a keyword whose longhand is already set as the name (measured:
    /// <c>100s ease ease</c> runs @keyframes ease), and add, replace, accumulate and auto are names there too.</summary>
    private static readonly Dictionary<string, string> AnimationKeywords = new(StringComparer.Ordinal)
    {
        ["linear"] = "timing", ["ease"] = "timing", ["ease-in"] = "timing", ["ease-out"] = "timing", ["ease-in-out"] = "timing",
        ["step-start"] = "timing", ["step-end"] = "timing", ["infinite"] = "count", ["normal"] = "direction", ["reverse"] = "direction",
        ["alternate"] = "direction", ["alternate-reverse"] = "direction", ["none"] = "fill", ["forwards"] = "fill",
        ["backwards"] = "fill", ["both"] = "fill", ["running"] = "state", ["paused"] = "state",
    };

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
            "-webkit-touch-callout", "-webkit-user-drag", "field-sizing", "interactivity");
        AddRefusing("-webkit-user-modify", Interaction, "any value but read-only: Chromium 159 wraps and keeps the text as it does editable text (measured: read-write-plaintext-only makes white-space-collapse preserve)",
            values => !OnlyKeywords(values, "read-only"));
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
                && (keywords.Contains(InfraAscii.ToLower(word.Text)) || InfraAscii.ToLower(word.Text) is "initial" or "inherit" or "unset" or "revert" or "revert-layer")));

    /// <summary>Whether an animation shorthand names an animation, or may: in one of its comma-separated layers a
    /// string, an identifier that is not one of the other longhands' keywords, a keyword whose longhand that layer has
    /// already set (see <see cref="AnimationKeywords"/>), or a function other than the timing functions cubic-bezier(),
    /// steps() and linear() (a var() or env() can hold a name: Chromium 159, measured, runs <c>--a: k; animation:
    /// var(--a) 100s</c>).</summary>
    private static bool NamesAnimation(IReadOnlyList<ComponentValue> values)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in Meaningful(values))
        {
            switch (value)
            {
                case CssToken { Kind: CssTokenKind.Comma }:
                    set.Clear();
                    break;
                case CssToken { Kind: CssTokenKind.String }:
                    return true;
                case CssToken { Kind: CssTokenKind.Ident } word:
                    var keyword = InfraAscii.ToLower(word.Text);
                    if (keyword is "initial" or "inherit" or "unset" or "revert" or "revert-layer")
                    {
                        break;
                    }

                    if (!AnimationKeywords.TryGetValue(keyword, out var longhand) || !set.Add(longhand))
                    {
                        return true;
                    }

                    break;
                case FunctionValue function when function.IsNamed("cubic-bezier") || function.IsNamed("steps") || function.IsNamed("linear"):
                    if (!set.Add("timing"))
                    {
                        return true;
                    }

                    break;
                case FunctionValue or SimpleBlock:
                    return true;
            }
        }

        return false;
    }

    /// <summary>zoom: normal, 1 or 100%, or a CSS-wide keyword.</summary>
    private static bool IsUnitZoom(IReadOnlyList<ComponentValue> values) =>
        Meaningful(values) is [var only] && only switch
        {
            CssToken { Kind: CssTokenKind.Ident } word => InfraAscii.ToLower(word.Text) is "normal" or "initial" or "inherit" or "unset" or "revert" or "revert-layer",
            CssToken { Kind: CssTokenKind.Number } number => number.Number == 1,
            CssToken { Kind: CssTokenKind.Percentage } percentage => percentage.Number == 100,
            _ => false,
        };
}
