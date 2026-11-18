using Ave.Extensions.Documents.Core;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Ave.Extensions.Documents.Css.Selectors;

/// <summary>
/// The pseudo-classes and pseudo-elements the selector parser knows, and what the engine does with each. A name that is
/// not in these tables is invalid, as in a browser that does not know it (Selectors 4 §3.9: "UAs must treat as invalid
/// any pseudo-classes … for which they have no usable level of support"); the tables follow the names Chromium 159
/// accepts, so a selector Chromium keeps is never dropped here as invalid: it is supported, never matches, or is refused.
/// </summary>
internal static class PseudoClasses
{
    /// <summary>One known name.</summary>
    /// <param name="Name">The name, lowercase, without colons or parenthesis.</param>
    /// <param name="Functional">Whether it takes arguments, <c>:name(…)</c>.</param>
    /// <param name="Support">What the engine does with it.</param>
    /// <param name="Note">Why, for the profile document.</param>
    public sealed record Entry(string Name, bool Functional, SelectorSupport Support, string Note);

    private static readonly Entry[] ClassEntries =
    [
        // Supported: the tree-structural pseudo-classes, the logical combinations, links (Selectors 4 §4, §13, §9).
        new("root", false, SelectorSupport.Supported, "Selectors 4 §13.1"),
        new("empty", false, SelectorSupport.Supported, "Selectors 4 §13.2"),
        new("first-child", false, SelectorSupport.Supported, "Selectors 4 §13.4.3"),
        new("last-child", false, SelectorSupport.Supported, "Selectors 4 §13.4.4"),
        new("only-child", false, SelectorSupport.Supported, "Selectors 4 §13.4.5"),
        new("nth-child", true, SelectorSupport.Supported, "Selectors 4 §13.4.1, with `of S`"),
        new("nth-last-child", true, SelectorSupport.Supported, "Selectors 4 §13.4.2, with `of S`"),
        new("first-of-type", false, SelectorSupport.Supported, "Selectors 4 §13.5.3"),
        new("last-of-type", false, SelectorSupport.Supported, "Selectors 4 §13.5.4"),
        new("only-of-type", false, SelectorSupport.Supported, "Selectors 4 §13.5.5"),
        new("nth-of-type", true, SelectorSupport.Supported, "Selectors 4 §13.5.1"),
        new("nth-last-of-type", true, SelectorSupport.Supported, "Selectors 4 §13.5.2"),
        new("not", true, SelectorSupport.Supported, "Selectors 4 §4.3"),
        new("is", true, SelectorSupport.Supported, "Selectors 4 §4.2, forgiving"),
        new("where", true, SelectorSupport.Supported, "Selectors 4 §4.4, forgiving, specificity zero"),
        new("link", false, SelectorSupport.Supported, "Selectors 4 §8.2: an a or area with href (no link is visited in a new document)"),
        new("any-link", false, SelectorSupport.Supported, "Selectors 4 §8.1: the same as :link, since nothing is visited"),
        new("-webkit-any-link", false, SelectorSupport.Supported, "Chromium's legacy name of :any-link"),
        new("-webkit-any", true, SelectorSupport.Unsupported, "Chromium's legacy :is() for compound selectors"),

        // Never true in a static document nobody looks at (user action, time, playback, focus, history, fragment).
        new("hover", false, SelectorSupport.NeverMatches, "user action (Selectors 4 §10.1)"),
        new("active", false, SelectorSupport.NeverMatches, "user action (§10.2)"),
        new("focus", false, SelectorSupport.NeverMatches, "user action (§10.3)"),
        new("focus-visible", false, SelectorSupport.NeverMatches, "user action (§10.4)"),
        new("focus-within", false, SelectorSupport.NeverMatches, "user action (§10.5)"),
        new("visited", false, SelectorSupport.NeverMatches, "history (§8.2): nothing is visited"),
        new("target", false, SelectorSupport.NeverMatches, "the document has no URL fragment (§8.4)"),
        new("current", false, SelectorSupport.NeverMatches, "time-dimensional (§11.1): no media timeline"),
        new("past", false, SelectorSupport.NeverMatches, "time-dimensional (§11.2)"),
        new("future", false, SelectorSupport.NeverMatches, "time-dimensional (§11.3)"),
        new("playing", false, SelectorSupport.NeverMatches, "resource state (§11.4): nothing plays"),
        new("paused", false, SelectorSupport.NeverMatches, "resource state (§11.4)"),
        new("seeking", false, SelectorSupport.NeverMatches, "resource state (§11.4)"),
        new("buffering", false, SelectorSupport.NeverMatches, "resource state (§11.4)"),
        new("stalled", false, SelectorSupport.NeverMatches, "resource state (§11.4)"),
        new("muted", false, SelectorSupport.NeverMatches, "resource state (§11.5)"),
        new("volume-locked", false, SelectorSupport.NeverMatches, "resource state (§11.5)"),
        new("fullscreen", false, SelectorSupport.NeverMatches, "display state: nothing is shown full screen"),
        new("picture-in-picture", false, SelectorSupport.NeverMatches, "display state"),
        new("popover-open", false, SelectorSupport.NeverMatches, "a popover opens by script or user action"),
        new("modal", false, SelectorSupport.NeverMatches, "a modal dialog opens by script"),
        new("autofill", false, SelectorSupport.NeverMatches, "the browser fills forms in; nothing is filled"),
        new("-webkit-autofill", false, SelectorSupport.NeverMatches, "Chromium's legacy name of :autofill"),
        new("user-valid", false, SelectorSupport.NeverMatches, "after user interaction only (§12.3.3)"),
        new("user-invalid", false, SelectorSupport.NeverMatches, "after user interaction only (§12.3.3)"),
        new("xr-overlay", false, SelectorSupport.NeverMatches, "an immersive session"),
        new("active-view-transition", false, SelectorSupport.NeverMatches, "a view transition runs by script"),
        new("active-view-transition-type", true, SelectorSupport.NeverMatches, "a view transition runs by script"),
        new("interest-source", false, SelectorSupport.NeverMatches, "user interest (hover or focus)"),
        new("interest-target", false, SelectorSupport.NeverMatches, "user interest (hover or focus)"),
        new("-webkit-drag", false, SelectorSupport.NeverMatches, "user action"),
        new("-webkit-full-screen", false, SelectorSupport.NeverMatches, "Chromium's legacy :fullscreen"),
        new("-webkit-full-screen-ancestor", false, SelectorSupport.NeverMatches, "Chromium's legacy full-screen state"),
        new("-webkit-full-page-media", false, SelectorSupport.NeverMatches, "a media file shown as the page"),
        new("window-inactive", false, SelectorSupport.NeverMatches, "window focus"),
        new("horizontal", false, SelectorSupport.NeverMatches, "Chromium's scroll bar states: a page has no scroll bars"),
        new("vertical", false, SelectorSupport.NeverMatches, "Chromium's scroll bar states"),
        new("decrement", false, SelectorSupport.NeverMatches, "Chromium's scroll bar states"),
        new("increment", false, SelectorSupport.NeverMatches, "Chromium's scroll bar states"),
        new("start", false, SelectorSupport.NeverMatches, "Chromium's scroll bar states"),
        new("end", false, SelectorSupport.NeverMatches, "Chromium's scroll bar states"),
        new("double-button", false, SelectorSupport.NeverMatches, "Chromium's scroll bar states"),
        new("single-button", false, SelectorSupport.NeverMatches, "Chromium's scroll bar states"),
        new("no-button", false, SelectorSupport.NeverMatches, "Chromium's scroll bar states"),
        new("corner-present", false, SelectorSupport.NeverMatches, "Chromium's scroll bar states"),

        // Valid, may match in a static document, not implemented: refused.
        new("has", true, SelectorSupport.Unsupported, "Selectors 4 §4.5"),
        new("lang", true, SelectorSupport.Unsupported, "Selectors 4 §7.2"),
        new("dir", true, SelectorSupport.Unsupported, "Selectors 4 §7.1"),
        new("scope", false, SelectorSupport.Unsupported, "Selectors 4 §13.6"),
        new("defined", false, SelectorSupport.Unsupported, "custom elements"),
        new("state", true, SelectorSupport.Unsupported, "custom element states"),
        new("host", false, SelectorSupport.Unsupported, "shadow trees"),
        new("host", true, SelectorSupport.Unsupported, "shadow trees"),
        new("host-context", true, SelectorSupport.Unsupported, "shadow trees"),
        new("open", false, SelectorSupport.Unsupported, "open details, dialog and select (a static state of their attributes)"),
        new("checked", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("default", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("disabled", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("enabled", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("indeterminate", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("required", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("optional", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("read-only", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("read-write", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("valid", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("invalid", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("in-range", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("out-of-range", false, SelectorSupport.Unsupported, "form controls are not supported"),
        new("placeholder-shown", false, SelectorSupport.Unsupported, "form controls are not supported"),
    ];

    private static readonly Entry[] ElementEntries =
    [
        new("before", false, SelectorSupport.Unsupported, "generated content"),
        new("after", false, SelectorSupport.Unsupported, "generated content"),
        new("first-line", false, SelectorSupport.Unsupported, "CSS Pseudo 4 §2.1"),
        new("first-letter", false, SelectorSupport.Unsupported, "CSS Pseudo 4 §2.2"),
        new("marker", false, SelectorSupport.Unsupported, "list markers are styled by list-style-type only"),
        new("placeholder", false, SelectorSupport.NeverMatches, "the placeholder text of a form control, which the engine refuses: never drawn in an accepted document"),
        new("selection", false, SelectorSupport.NeverMatches, "selected text: nothing is selected on paper"),
        new("target-text", false, SelectorSupport.Unsupported, "highlight pseudo-elements"),
        new("spelling-error", false, SelectorSupport.Unsupported, "highlight pseudo-elements"),
        new("grammar-error", false, SelectorSupport.Unsupported, "highlight pseudo-elements"),
        new("search-text", false, SelectorSupport.Unsupported, "highlight pseudo-elements"),
        new("highlight", true, SelectorSupport.Unsupported, "highlight pseudo-elements"),
        new("backdrop", false, SelectorSupport.Unsupported, "top layer"),
        new("file-selector-button", false, SelectorSupport.Unsupported, "form controls"),
        new("cue", false, SelectorSupport.Unsupported, "media"),
        new("cue", true, SelectorSupport.Unsupported, "media"),
        new("part", true, SelectorSupport.Unsupported, "shadow trees"),
        new("slotted", true, SelectorSupport.Unsupported, "shadow trees"),
        new("details-content", false, SelectorSupport.Unsupported, "details"),
        new("picker", true, SelectorSupport.Unsupported, "form controls"),
        new("-webkit-scrollbar", false, SelectorSupport.NeverMatches, "scrollbars: a printed page has none (Chromium 159's print rendering does not change, measured)"),
        new("-webkit-scrollbar-button", false, SelectorSupport.NeverMatches, "scrollbars: a printed page has none"),
        new("-webkit-scrollbar-thumb", false, SelectorSupport.NeverMatches, "scrollbars: a printed page has none"),
        new("-webkit-scrollbar-track", false, SelectorSupport.NeverMatches, "scrollbars: a printed page has none"),
        new("-webkit-scrollbar-track-piece", false, SelectorSupport.NeverMatches, "scrollbars: a printed page has none"),
        new("-webkit-scrollbar-corner", false, SelectorSupport.NeverMatches, "scrollbars: a printed page has none"),
        new("-webkit-resizer", false, SelectorSupport.NeverMatches, "the resize handle, drawn only for resize other than none, which is refused"),
        new("picker-icon", false, SelectorSupport.Unsupported, "form controls"),
        new("checkmark", false, SelectorSupport.Unsupported, "form controls"),
        new("column", false, SelectorSupport.Unsupported, "multi-column"),
        new("scroll-marker", false, SelectorSupport.Unsupported, "scrolling"),
        new("scroll-marker-group", false, SelectorSupport.Unsupported, "scrolling"),
        new("scroll-button", true, SelectorSupport.Unsupported, "scrolling"),
        new("view-transition", false, SelectorSupport.Unsupported, "view transitions"),
        new("view-transition-group", true, SelectorSupport.Unsupported, "view transitions"),
        new("view-transition-image-pair", true, SelectorSupport.Unsupported, "view transitions"),
        new("view-transition-old", true, SelectorSupport.Unsupported, "view transitions"),
        new("view-transition-new", true, SelectorSupport.Unsupported, "view transitions"),
        new("view-transition-group-children", true, SelectorSupport.Unsupported, "view transitions"),
    ];

    private static readonly FrozenDictionary<(string, bool), Entry> Classes =
        ClassEntries.ToFrozenDictionary(entry => (entry.Name, entry.Functional));

    private static readonly FrozenDictionary<(string, bool), Entry> Elements =
        ElementEntries.ToFrozenDictionary(entry => (entry.Name, entry.Functional));

    /// <summary>The pseudo-classes about an element's place in the tree, or its contents: not allowed after an
    /// element-backed pseudo-element (Chromium 159).</summary>
    private static readonly FrozenSet<string> Structural = FrozenSet.Create(
        "root", "empty", "first-child", "last-child", "only-child", "nth-child", "nth-last-child", "first-of-type",
        "last-of-type", "only-of-type", "nth-of-type", "nth-last-of-type", "has", "host", "host-context", "scope");

    private static readonly FrozenSet<string> UserAction = FrozenSet.Create("hover", "focus", "active", "focus-visible", "focus-within", "not");

    private static readonly FrozenSet<string> ElementBacked = FrozenSet.Create("part", "details-content", "picker");

    /// <summary>
    /// Whether a pseudo-class may follow a pseudo-element in a compound selector (Selectors 4 §3.6.3 leaves this to each
    /// pseudo-element; these are the rules Chromium 159 applies, measured): <c>:is()</c> and <c>:where()</c> after any;
    /// after an element-backed one (<c>::part()</c>, <c>::details-content</c>, <c>::picker()</c>) every pseudo-class but
    /// the structural ones; after <c>::file-selector-button</c>, <c>::scroll-marker</c> and <c>::cue</c> the user-action
    /// ones; after Chromium's scroll bar pseudo-elements those and its scroll bar states; after <c>::selection</c>
    /// <c>:window-inactive</c>; after the others none.
    /// </summary>
    /// <param name="pseudoElement">The pseudo-element, lowercase.</param>
    /// <param name="pseudoClass">The pseudo-class, lowercase.</param>
    /// <returns>Whether it is allowed.</returns>
    public static bool AllowedAfter(string pseudoElement, string pseudoClass)
    {
        if (pseudoClass is "is" or "where")
        {
            return true;
        }

        if (ElementBacked.Contains(pseudoElement))
        {
            return !Structural.Contains(pseudoClass);
        }

        if (pseudoElement is "file-selector-button" or "scroll-marker" or "cue")
        {
            return UserAction.Contains(pseudoClass);
        }

        if (pseudoElement.StartsWith("-webkit-", StringComparison.Ordinal))
        {
            return pseudoClass is "hover" or "active" or "not" or "window-inactive" or "disabled" or "enabled" or "horizontal" or "vertical"
                or "decrement" or "increment" or "start" or "end" or "double-button" or "single-button" or "no-button" or "corner-present";
        }

        return pseudoElement == "selection" && pseudoClass == "window-inactive";
    }

    /// <summary>Whether a pseudo-element may follow another (Chromium 159, measured): a tree-abiding one after
    /// <c>::part()</c>, <c>::slotted()</c>, <c>::details-content</c> and <c>::picker()</c>, and <c>::marker</c> after
    /// <c>::before</c> and <c>::after</c> (CSS Pseudo 4 §4.2).</summary>
    /// <param name="first">The first pseudo-element, lowercase.</param>
    /// <param name="second">The one after it, lowercase.</param>
    /// <returns>Whether it is allowed.</returns>
    public static bool AllowedAfterPseudoElement(string first, string second) =>
        (ElementBacked.Contains(first) || first == "slotted")
            ? second is "before" or "after" or "marker" or "placeholder" or "first-line" or "first-letter" or "file-selector-button" or "backdrop" or "selection" or "details-content"
            : first is "before" or "after" && second == "marker";

    /// <summary>Every known pseudo-class, for the profile document.</summary>
    public static IReadOnlyList<Entry> AllClasses => ClassEntries;

    /// <summary>Every known pseudo-element, for the profile document.</summary>
    public static IReadOnlyList<Entry> AllElements => ElementEntries;

    /// <summary>The pseudo-class of this name, or null when it is not known (an invalid selector).</summary>
    /// <param name="name">The name, any case.</param>
    /// <param name="functional">Whether it was written as a function.</param>
    /// <returns>The entry, or null.</returns>
    public static Entry? FindClass(string name, bool functional) =>
        Classes.TryGetValue((InfraAscii.ToLower(name), functional), out var entry) ? entry : null;

    /// <summary>The pseudo-element of this name, or null when it is not known. Names starting with <c>-webkit-</c> are
    /// Chromium's own pseudo-elements (scroll bars, form parts): known, and unsupported.</summary>
    /// <param name="name">The name, any case.</param>
    /// <param name="functional">Whether it was written as a function.</param>
    /// <returns>The entry, or null.</returns>
    public static Entry? FindElement(string name, bool functional)
    {
        var lower = InfraAscii.ToLower(name);
        if (Elements.TryGetValue((lower, functional), out var entry))
        {
            return entry;
        }

        return lower.StartsWith("-webkit-", StringComparison.Ordinal)
            ? new Entry(lower, functional, SelectorSupport.Unsupported, "a Chromium pseudo-element")
            : null;
    }
}
