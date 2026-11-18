using Ave.Extensions.Documents.Core;
using System.Collections.Generic;

namespace Ave.Extensions.Documents.Html.Parsing.TreeBuilding;

/// <summary>
/// The DOM side of the <c>option</c> and <c>selectedcontent</c> elements that changes the parsed tree (HTML Standard
/// §4.10.10 "the option HTML element children parsed steps", §4.10.17 "the selectedcontent element"): the content of a
/// select's selected option is cloned into the select's <c>selectedcontent</c> elements.
/// </summary>
/// <remarks>
/// <para>With no script running and no user, an option's selectedness follows from the attributes alone: the last
/// option of the list with a <c>selected</c> attribute, otherwise the first option that is not disabled (the
/// selectedness setting algorithm, §4.10.7, for a select without <c>multiple</c> and with display size 1). Options
/// finish parsing in tree order, so the parser keeps both per select as they finish, instead of walking the list.</para>
/// <para>Bounded work: finding an option's select walks its ancestors (at most <see cref="HtmlInputLimits.MaxDepth"/>);
/// every node and character cloned into a selectedcontent is charged to a budget of
/// <see cref="CloneWorkPerCharacter"/> per input character, so many selectedcontent elements times many selected options
/// cannot make the work grow faster than the input. Past the budget the document is refused
/// (<see cref="HtmlErrorCodes.TooLarge"/>).</para>
/// </remarks>
internal sealed partial class TreeBuilder
{
    /// <summary>The clone budget: nodes plus characters cloned, per character of input (at least 65 536 in all).</summary>
    public const int CloneWorkPerCharacter = 4;

    private sealed class SelectState
    {
        /// <summary>The last option with a <c>selected</c> attribute: the selected option of a select that is not
        /// <c>multiple</c>.</summary>
        public MutableElement? LastWithSelected { get; set; }

        /// <summary>The first option that is not disabled: the selected one when none has <c>selected</c> and the
        /// display size is 1.</summary>
        public MutableElement? FirstEnabled { get; set; }

        /// <summary>The <c>selectedcontent</c> elements of the select, filled with a clone of the selected option.
        /// </summary>
        public List<MutableElement> SelectedContents { get; } = [];

        /// <summary>Counts the times an option became the selected one; a selectedcontent filled at an older count is
        /// stale.</summary>
        public int Version { get; set; }

        /// <summary>The option that became the selected one last.</summary>
        public MutableElement? Selected { get; set; }
    }

    /// <summary>The select of each selectedcontent, and the count at which it was filled last.</summary>
    private readonly Dictionary<MutableElement, (SelectState State, int Version)> _contents = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<MutableElement, SelectState> _selects = new(ReferenceEqualityComparer.Instance);
    private long? _cloneBudget;

    /// <summary>Whether the clone budget ran out (the parse is refused).</summary>
    public bool CloneBudgetExceeded { get; private set; }

    /// <summary>The number of input characters, for the clone budget; set by the parser.</summary>
    public int InputLength { get; init; }

    /// <summary>"Finish parsing the children of an element" (§13.2.4.2): the option's children parsed steps. The
    /// standard fills every selectedcontent each time an option becomes the selected one. With no script nothing can see
    /// a selectedcontent between two such times, so the parser only counts them and fills a selectedcontent when it is
    /// stale and something is inserted into it, when its select finishes, or at the end of the input: the tree is the
    /// same, and n selectedcontent elements with n selected options cost n fills, not n².</summary>
    private void FinishChildren(MutableElement element)
    {
        if (element.IsHtml("select") && _selects.TryGetValue(element, out var finished))
        {
            Flush(finished);
            return;
        }

        if (!element.IsHtml("option") || NearestAncestorSelect(element) is not { } select)
        {
            return;
        }

        var state = State(select);
        if (HasAttribute(element, "selected"))
        {
            state.LastWithSelected = element;
        }

        if (state.FirstEnabled is null && !IsDisabled(element))
        {
            state.FirstEnabled = element;
        }

        if (SelectedOption(select, state) == element)
        {
            state.Version++;
            state.Selected = element;
        }
    }

    /// <summary>Fills a selectedcontent again when an option became the selected one since it was filled.</summary>
    /// <param name="parent">A node something is about to be inserted into.</param>
    /// <returns>Whether it was filled (its old children are gone).</returns>
    private bool Refresh(MutableParent parent)
    {
        if (parent is not MutableElement element || !_contents.TryGetValue(element, out var entry) || entry.Version == entry.State.Version)
        {
            return false;
        }

        _contents[element] = (entry.State, entry.State.Version);
        Fill(element, entry.State.Selected);
        return true;
    }

    /// <summary>Fills the stale selectedcontent elements of a select.</summary>
    private void Flush(SelectState state)
    {
        foreach (var selectedContent in state.SelectedContents)
        {
            Refresh(selectedContent);
        }
    }

    /// <summary>"The end" (§13.2.7): the selects that never finished (left open, or taken off the stack by the adoption
    /// agency) are filled too.</summary>
    private void FlushSelects()
    {
        foreach (var state in _selects.Values)
        {
            Flush(state);
        }
    }

    /// <summary>The selectedcontent insertion steps: a selectedcontent that is not disabled belongs to its select and
    /// takes the content of the select's selected option.</summary>
    private void ElementInserted(MutableElement element)
    {
        if (!element.IsHtml("selectedcontent") || SelectOfSelectedContent(element) is not { } select)
        {
            return;
        }

        var state = State(select);
        state.SelectedContents.Add(element);
        _contents[element] = (state, state.Version);
        Fill(element, SelectedOption(select, state));
    }

    private SelectState State(MutableElement select)
    {
        if (!_selects.TryGetValue(select, out var state))
        {
            state = new SelectState();
            _selects[select] = state;
        }

        return state;
    }

    /// <summary>"Get the nearest ancestor select" (§4.10.10).</summary>
    private static MutableElement? NearestAncestorSelect(MutableElement element)
    {
        var optgroups = 0;
        for (var ancestor = element.Parent as MutableElement; ancestor is not null; ancestor = ancestor.Parent as MutableElement)
        {
            if (ancestor.Namespace != ElementNamespace.Html)
            {
                continue;
            }

            switch (ancestor.LocalName)
            {
                case "datalist" or "hr" or "option":
                    return null;
                case "optgroup" when ++optgroups > 1:
                    return null;
                case "select":
                    return ancestor;
            }
        }

        return null;
    }

    /// <summary>"Recalculate a selectedcontent element's disabledness" (§4.10.17): its select, or null when it is
    /// disabled (inside an option or another selectedcontent, or inside two selects) or in no select.</summary>
    private static MutableElement? SelectOfSelectedContent(MutableElement selectedContent)
    {
        MutableElement? select = null;
        for (var ancestor = selectedContent.Parent as MutableElement; ancestor is not null; ancestor = ancestor.Parent as MutableElement)
        {
            if (ancestor.IsHtml("select"))
            {
                if (select is not null)
                {
                    return null;
                }

                select = ancestor;
            }
            else if (ancestor.IsHtml("option") || ancestor.IsHtml("selectedcontent"))
            {
                return null;
            }
        }

        return select;
    }

    /// <summary>The option whose selectedness is true; null for a select with <c>multiple</c> (whose selectedcontent
    /// elements are not kept up to date, §4.10.17), or whose display size is not 1 while no option has the attribute.</summary>
    private static MutableElement? SelectedOption(MutableElement select, SelectState state) =>
        HasAttribute(select, "multiple") ? null : state.LastWithSelected ?? (DisplaySizeIsOne(select) ? state.FirstEnabled : null);

    /// <summary>The display size of a select is 1 unless it has a size attribute that parses as another non-negative
    /// integer (§4.10.7; the rules for parsing non-negative integers, §2.3.4.2).</summary>
    private static bool DisplaySizeIsOne(MutableElement select)
    {
        var size = Attribute(select, "size");
        if (size is null)
        {
            return true;
        }

        var start = 0;
        while (start < size.Length && InfraAscii.IsWhitespace(size[start]))
        {
            start++;
        }

        if (start < size.Length && size[start] == '+')
        {
            start++;
        }

        var end = start;
        while (end < size.Length && InfraAscii.IsDigit(size[end]))
        {
            end++;
        }

        // Not a number: the display size is 1 (no multiple). A number: its value.
        return end == start || size[start..end].TrimStart('0') == "1";
    }

    /// <summary>An option is disabled when it has a disabled attribute or is a child of a disabled optgroup (§4.10.10).</summary>
    private static bool IsDisabled(MutableElement option) =>
        HasAttribute(option, "disabled") || (option.Parent is MutableElement parent && parent.IsHtml("optgroup") && HasAttribute(parent, "disabled"));

    /// <summary>"Update a selectedcontent": clear it, then "clone an option into a selectedcontent" — deep clones of
    /// the option's children, without script, SVG script, frame, iframe, embed and object elements.</summary>
    private void Fill(MutableElement selectedContent, MutableElement? option)
    {
        foreach (var child in selectedContent.Children())
        {
            child.Remove();
        }

        if (option is not null)
        {
            CloneChildren(option, selectedContent);
        }
    }

    /// <summary>Deep clones of a parent's children (DOM "clone a node" with subtree), appended to a target, with an
    /// explicit stack so the depth of the option's content cannot overflow the thread's stack. Every node and
    /// character is charged to the clone budget.</summary>
    private void CloneChildren(MutableParent source, MutableParent target)
    {
        var pending = new Stack<(MutableNode? Next, MutableParent Target)>();
        pending.Push((source.First, target));
        while (pending.Count > 0 && !CloneBudgetExceeded)
        {
            var (next, into) = pending.Pop();
            if (next is null)
            {
                continue;
            }

            pending.Push((next.Next, into));
            if (CloneOne(next) is not { } clone)
            {
                continue;
            }

            into.Insert(clone, null);
            if (next is MutableElement element && clone is MutableElement copy)
            {
                if (element.Content is not null)
                {
                    pending.Push((element.Content.First, copy.Content!));
                }

                pending.Push((element.First, copy));
            }
        }
    }

    /// <summary>A shallow clone, or null when the budget runs out.</summary>
    private MutableNode? CloneOne(MutableNode node)
    {
        if (!Charge(1 + node switch { MutableText text => text.Data.Length, MutableElement element => element.Attributes.Count, _ => 0 }))
        {
            return null;
        }

        switch (node)
        {
            case MutableElement element:
                // Chromium 159 clones every element, script, object, iframe and embed too (review 39 HD-12, measured).
                var copy = new MutableElement
                {
                    Namespace = element.Namespace,
                    LocalName = element.LocalName,
                    Token = element.Token,
                    Start = element.Start,
                    Content = element.Content is null ? null : new MutableFragment { Start = element.Content.Start },
                };
                copy.ShareAttributesOf(element);
                Made(element: true);
                return copy;
            case MutableText original:
                var textCopy = new MutableText { Start = original.Start };
                textCopy.Data.Append(original.Data);
                Made(element: false);
                return textCopy;
            case MutableComment comment:
                Made(element: false);
                return new MutableComment { Data = comment.Data, Start = comment.Start };
            case MutableProcessingInstruction instruction:
                Made(element: false);
                return new MutableProcessingInstruction { Target = instruction.Target, Data = instruction.Data, Start = instruction.Start };
            default:
                return null;
        }
    }

    private bool Charge(long work)
    {
        _cloneBudget ??= System.Math.Max(65_536L, (long)CloneWorkPerCharacter * InputLength);
        _cloneBudget -= work;
        if (_cloneBudget < 0)
        {
            CloneBudgetExceeded = true;
            _stopped = true;
            return false;
        }

        return true;
    }

    private static string? Attribute(MutableElement element, string name) => element.Attribute(name);

    private static bool HasAttribute(MutableElement element, string name) => Attribute(element, name) is not null;
}
