using System.Collections.Generic;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>
/// The properties ignored by rule: they concern interaction, animation, scrolling or the screen, and have no effect on
/// a static paged document, so a declaration of one is dropped with a diagnostic of class
/// <see cref="DiagnosticClass.Ignored"/> rather than refused. Animations are among them: a printed page shows the
/// state before any animation runs, which is the state without it.
/// </summary>
public static partial class PropertyRegistry
{
    private const string Interaction = "interaction with a pointer or keyboard; a static document has none";
    private const string Motion = "animation and transition; a static document shows the state without them";
    private const string Scrolling = "scrolling; a paged document does not scroll";
    private const string Screen = "screen rendering hints; they do not change what is drawn on a page";

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

        Add(Interaction,
            "cursor", "pointer-events", "user-select", "-webkit-user-select", "touch-action", "resize", "caret", "caret-color",
            "caret-shape", "caret-animation", "accent-color", "appearance", "-webkit-appearance", "-webkit-tap-highlight-color",
            "-webkit-touch-callout", "-webkit-user-drag", "-webkit-user-modify", "field-sizing", "interactivity");
        Add(Motion,
            "transition", "transition-property", "transition-duration", "transition-timing-function", "transition-delay",
            "transition-behavior", "animation", "animation-name", "animation-duration", "animation-timing-function",
            "animation-delay", "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state",
            "animation-composition", "animation-timeline", "animation-range", "animation-range-start", "animation-range-end",
            "will-change", "view-transition-name", "view-transition-class");
        Add(Scrolling,
            "scroll-behavior", "scroll-margin", "scroll-margin-top", "scroll-margin-right", "scroll-margin-bottom",
            "scroll-margin-left", "scroll-margin-block", "scroll-margin-inline", "scroll-padding", "scroll-padding-top",
            "scroll-padding-right", "scroll-padding-bottom", "scroll-padding-left", "scroll-padding-block",
            "scroll-padding-inline", "scroll-snap-type", "scroll-snap-align", "scroll-snap-stop", "scroll-timeline",
            "scroll-timeline-name", "scroll-timeline-axis", "view-timeline", "view-timeline-name", "view-timeline-axis",
            "overscroll-behavior", "overscroll-behavior-x", "overscroll-behavior-y", "overscroll-behavior-block",
            "overscroll-behavior-inline", "overflow-anchor", "scrollbar-color", "scrollbar-width", "scrollbar-gutter");
        Add(Screen,
            "-webkit-font-smoothing", "-moz-osx-font-smoothing", "font-smooth", "text-size-adjust", "-webkit-text-size-adjust",
            "-moz-text-size-adjust", "-ms-text-size-adjust", "forced-color-adjust", "speak", "speak-as", "zoom");
        return ignored;
    }
}
