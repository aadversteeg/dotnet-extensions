using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>
/// Counters (CSS Lists 3 §4): <c>counter-reset</c>, <c>counter-increment</c> and <c>counter-set</c> on elements and on
/// <c>::before</c> and <c>::after</c>, read by <c>counter()</c> and <c>counters()</c> in the <c>content</c> of those
/// pseudo-elements. The <c>list-item</c> counter is read here, never written: its values are the numbers of the lists
/// (<see cref="Run.Number"/>), which this pass takes as they are.
/// </summary>
public static partial class HtmlBoxGenerator
{
    /// <summary>One counter (CSS Lists 3 §4.4): its name, the level of the element that made it (that element's depth
    /// in the tree), whether a counter of its name was live in its parent when it was made, and its value.</summary>
    private sealed class Counter(string name, int level, bool nested)
    {
        public string Name { get; } = name;

        /// <summary>The depth of the element that made it: the counters of one level are made by siblings.</summary>
        public int Level { get; } = level;

        /// <summary>A counter of the same name was live in the parent: the following siblings do not see this one (CSS
        /// Lists 3 §4.4.2, they inherit a sibling's counter only when their parent has none of that name).</summary>
        public bool Nested { get; } = nested;

        public long Value { get; set; }

        /// <summary>A later sibling made a counter of the same name in its place.</summary>
        public bool Replaced { get; set; }
    }

    /// <summary>An element whose children are being counted: what it made, and the counters its children made that
    /// their following siblings see.</summary>
    private sealed class CountFrame(StyledElement element, int depth)
    {
        public StyledElement Element { get; } = element;
        public int Depth { get; } = depth;
        public int Next { get; set; }
        public List<Counter> Own { get; } = [];
        public List<Counter> Layer { get; } = [];
    }

    private sealed partial class Run
    {
        private readonly Dictionary<StyledElement, string> _generatedTexts = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<StyledElement, List<Error>> _counterRefusals = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, List<Counter>> _live = new(System.StringComparer.Ordinal);
        private int _liveCount;
        private bool _tooManyCounters;

        /// <summary>
        /// Counts the counters in one walk over the rendered elements in document order, with an explicit stack, and
        /// keeps the text of every <c>::before</c> and <c>::after</c> that reads them. On each element (CSS Lists 3 §4.4
        /// to §4.6): it sees the counters of its parent, and those of its previous sibling whose name the parent has
        /// none of; then its <c>counter-reset</c> makes a counter (in place of one a previous sibling made, else nested
        /// in the one it sees), its <c>counter-increment</c> adds to the innermost counter of the name and its
        /// <c>counter-set</c> sets it, both making one at 0 when there is none; then come its <c>::before</c> (its own
        /// counter properties, then its content), its children and its <c>::after</c>, which are the first and the last
        /// child. Reading a counter that does not exist makes one at 0 on the pseudo-element. An element of
        /// <c>display: none</c> and its subtree count nothing. Measured: cases <c>gc-counter-*</c> and <c>gc-counters-*</c>.
        /// </summary>
        private void Count(StyledElement root)
        {
            var stack = new Stack<CountFrame>();
            var top = new CountFrame(root, 0);
            Enter(top);
            stack.Push(top);
            while (stack.Count > 0 && !_tooManyCounters)
            {
                var frame = stack.Peek();
                if (frame.Next >= frame.Element.Children.Count)
                {
                    stack.Pop();
                    Leave(frame, stack.Count > 0 ? stack.Peek() : null);
                    continue;
                }

                if (frame.Element.Children[frame.Next++] is not StyledElement child || Display(child) == "none")
                {
                    continue;
                }

                var entered = new CountFrame(child, frame.Depth + 1);
                Enter(entered);
                stack.Push(entered);
            }
        }

        /// <summary>The changes of one element: the list's <c>list-item</c> counter, then reset, increment, set; then,
        /// on a pseudo-element, its text.</summary>
        private void Enter(CountFrame frame)
        {
            var element = frame.Element;
            if (IsListElement(element))
            {
                // HTML Standard §15.3.7: the list elements reset list-item; the numbers are the lists' own.
                Make(frame, "list-item", 0);
            }

            if (_ordinals.TryGetValue(element, out var ordinal))
            {
                (Innermost("list-item") ?? Make(frame, "list-item", 0)).Value = ordinal;
            }

            Change(frame, "counter-reset", (counter, value) => Make(frame, counter, value));
            Change(frame, "counter-increment", (counter, value) => Add(Innermost(counter) ?? Make(frame, counter, 0), value));
            Change(frame, "counter-set", (counter, value) => (Innermost(counter) ?? Make(frame, counter, 0)).Value = value);
            if (element.Generated is { Items.Count: > 0 } generated)
            {
                Text(frame, generated);
            }

            void Add(Counter counter, int value) => counter.Value += value;
        }

        /// <summary>One counter property of an element, each counter in the order written; a value that leaves 32 bits is
        /// refused at the declaration and kept at the bound (Chromium 159 clamps it, measured: case
        /// <c>gc-counter-bound</c>).</summary>
        private void Change(CountFrame frame, string property, System.Action<string, int> change)
        {
            if (frame.Element.Style[PropertyRegistry.Find(property)!] is not CssCounterChanges changes)
            {
                return;
            }

            foreach (var item in changes.Changes)
            {
                change(item.Name, item.Value);
                if (_tooManyCounters)
                {
                    return;
                }

                if (Innermost(item.Name) is { Value: > int.MaxValue or < int.MinValue } counter)
                {
                    CounterRefusal(frame.Element, new Error(HtmlStyleErrorCodes.Unsupported.Counter,
                            $"'{property}: {changes}' takes the counter '{item.Name}' to {counter.Value.ToString(CultureInfo.InvariantCulture)}, beyond a 32-bit integer, where a browser stops at the bound; it is not drawn in this version: count within ±2147483647.")
                        .With(ErrorMetadataKeys.Value, item.Name).With(ErrorMetadataKeys.Property, property)
                        .WithSource(changes.SourceName ?? _sourceName, changes.Position));
                    counter.Value = System.Math.Clamp(counter.Value, int.MinValue, int.MaxValue);
                }
            }
        }

        /// <summary>The innermost live counter of a name, or null.</summary>
        private Counter? Innermost(string name) => _live.TryGetValue(name, out var counters) && counters.Count > 0 ? counters[^1] : null;

        /// <summary>Makes a counter on an element (CSS Lists 3 §4.4.1, instantiate a counter): in place of the innermost
        /// one of the name when a previous sibling, or the element itself, made that one; else nested in it.</summary>
        private Counter Make(CountFrame frame, string name, long value)
        {
            if (!_live.TryGetValue(name, out var counters))
            {
                _live[name] = counters = [];
            }

            if (counters.Count > 0 && counters[^1].Level == frame.Depth)
            {
                counters[^1].Replaced = true;
                counters.RemoveAt(counters.Count - 1);
                _liveCount--;
            }

            var counter = new Counter(name, frame.Depth, nested: counters.Count > 0) { Value = value };
            counters.Add(counter);
            frame.Own.Add(counter);
            if (++_liveCount > CssInputLimits.MaxLiveCounters && !_tooManyCounters)
            {
                _tooManyCounters = true;
                Failures.Add(new Error(HtmlStyleErrorCodes.Unsupported.Counter, $"At most {CssInputLimits.MaxLiveCounters} counters are live at one place of a document.")
                    .With(ErrorMetadataKeys.Value, CssInputLimits.MaxLiveCounters.ToString(CultureInfo.InvariantCulture)).WithSource(_sourceName, frame.Element.Element.Position));
            }

            return counter;
        }

        /// <summary>Leaves an element: the counters its children made end here; the ones it made itself stay for its
        /// following siblings, unless a counter of the name was live in the parent.</summary>
        private void Leave(CountFrame frame, CountFrame? parent)
        {
            foreach (var counter in frame.Layer)
            {
                Remove(counter);
            }

            foreach (var counter in frame.Own)
            {
                if (counter.Nested || parent is null)
                {
                    Remove(counter);
                }
                else if (!counter.Replaced)
                {
                    parent.Layer.Add(counter);
                }
            }
        }

        private void Remove(Counter counter)
        {
            if (counter.Replaced)
            {
                return;
            }

            var counters = _live[counter.Name];
            var at = counters.LastIndexOf(counter);
            if (at >= 0)
            {
                counters.RemoveAt(at);
                _liveCount--;
            }
        }

        /// <summary>The text of a pseudo-element's content with its counters read (CSS Lists 3 §4.7): <c>counter()</c> the
        /// innermost counter of the name, <c>counters()</c> all of them, outermost first, joined by the string.</summary>
        private void Text(CountFrame frame, Css.Cascade.GeneratedContent generated)
        {
            var text = new StringBuilder();
            foreach (var item in generated.Items)
            {
                switch (item)
                {
                    case CssContentString literal:
                        text.Append(literal.Value);
                        break;
                    case CssContentNamedCounter counter:
                        text.Append(CounterValueText(counter.Style, (Innermost(counter.Name) ?? Make(frame, counter.Name, 0)).Value));
                        break;
                    case CssContentNamedCounters counters:
                        if (Innermost(counters.Name) is null)
                        {
                            Make(frame, counters.Name, 0);
                        }

                        var levels = _live[counters.Name];
                        if (levels.Count > CssInputLimits.MaxCountersLevels)
                        {
                            GeneratedRefusal(frame.Element, generated, counters.ToString(),
                                $"'{counters}' joins {levels.Count.ToString(CultureInfo.InvariantCulture)} levels, and at most {CssInputLimits.MaxCountersLevels} are drawn; the generated text is left out.");
                            return;
                        }

                        for (var i = 0; i < levels.Count; i++)
                        {
                            text.Append(i == 0 ? string.Empty : counters.Separator).Append(CounterValueText(counters.Style, levels[i].Value));
                        }

                        break;
                }

                if (_tooManyCounters)
                {
                    return;
                }

                if (text.Length > CssInputLimits.MaxGeneratedTextLength)
                {
                    GeneratedRefusal(frame.Element, generated, "content",
                        $"The content of ::{generated.PseudoElement} makes more than {CssInputLimits.MaxGeneratedTextLength} characters of text with its counters; it is not drawn: shorten the strings.");
                    return;
                }
            }

            _generatedTexts[frame.Element] = text.ToString();
        }

        private void GeneratedRefusal(StyledElement element, Css.Cascade.GeneratedContent generated, string value, string message) =>
            CounterRefusal(element, new Error(HtmlStyleErrorCodes.Unsupported.Counter, message).With(ErrorMetadataKeys.Value, value).With(ErrorMetadataKeys.Property, "content")
                .WithSource(generated.SourceName, generated.Position));

        private void CounterRefusal(StyledElement element, Error error)
        {
            if (!_counterRefusals.TryGetValue(element, out var errors))
            {
                _counterRefusals[element] = errors = [];
            }

            errors.Add(error);
        }

        /// <summary>The refusals the counting found on an element, reported when box generation meets it, in document
        /// order with the others.</summary>
        private void ReportCounterRefusals(StyledElement element)
        {
            if (_counterRefusals.Remove(element, out var errors))
            {
                Refusals.AddRange(errors);
            }
        }
    }
}
