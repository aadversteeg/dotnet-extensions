using System;
using System.Collections.Generic;
using System.Linq;

namespace Ave.Extensions.Documents.Css.Selectors;

/// <summary>
/// Reads nested selectors (CSS Nesting 1 §3): the nesting selector <c>&amp;</c> stands for the parent rule's selector
/// list, as <c>:is()</c> would, with the specificity of the most specific parent selector; a relative selector (one that
/// starts with a combinator) and a selector without <c>&amp;</c> get <c>&amp;</c> in front, the latter with the
/// descendant combinator. The parent list is referenced (<see cref="ParentSelector"/>), never written out, so neither
/// the selectors nor their specificity grow with the nesting.
/// </summary>
internal static class Nesting
{
    /// <summary>The nested selector list made absolute against its parent's list.</summary>
    public static SelectorList Desugar(SelectorList nested, SelectorList parent)
    {
        var reference = new ParentSelector(parent, Specificity.Max(parent), Depth(parent) + 1);
        var selectors = new List<ComplexSelector>();
        foreach (var selector in nested.Selectors)
        {
            var steps = selector.Steps.Select(step => step with { Compound = Replace(step.Compound, reference) }).ToList();
            var relative = steps[0].Combinator is not null;
            if (relative || !Contains(selector))
            {
                if (!relative)
                {
                    steps[0] = steps[0] with { Combinator = Combinator.Descendant };
                }

                steps.Insert(0, new SelectorStep(null, new CompoundSelector([reference])));
            }

            selectors.Add(new ComplexSelector([.. steps], selector.Position));
        }

        return new SelectorList([.. selectors]);
    }

    /// <summary>How deeply selector lists nest in a list: one level per pseudo-class argument and per parent rule (a
    /// parent reference carries its own depth, so this does not walk the parents).</summary>
    public static int Depth(SelectorList list)
    {
        var deepest = 0;
        foreach (var selector in list.Selectors)
        {
            foreach (var step in selector.Steps)
            {
                foreach (var simple in step.Compound.Selectors)
                {
                    var depth = simple switch
                    {
                        ParentSelector parent => parent.Depth,
                        PseudoClassSelector { Selectors: { } inner } => Depth(inner) + 1,
                        _ => 0,
                    };
                    deepest = Math.Max(deepest, depth);
                }
            }
        }

        return deepest;
    }

    private static CompoundSelector Replace(CompoundSelector compound, ParentSelector reference) =>
        new([.. compound.Selectors.Select(simple => Replace(simple, reference))]);

    private static SimpleSelector Replace(SimpleSelector simple, ParentSelector reference) => simple switch
    {
        NestingSelector => reference,
        PseudoClassSelector { Selectors: { } list } pseudo when Contains(list) => pseudo with { Selectors = ReplaceAll(list, reference) },
        _ => simple,
    };

    private static SelectorList ReplaceAll(SelectorList list, ParentSelector reference) =>
        new([.. list.Selectors.Select(selector => selector with { Steps = [.. selector.Steps.Select(step => step with { Compound = Replace(step.Compound, reference) })] })]);

    private static bool Contains(SelectorList list) => list.Selectors.Any(Contains);

    /// <summary>Whether the nesting selector is anywhere in the selector, in pseudo-class arguments too.</summary>
    private static bool Contains(ComplexSelector selector) =>
        selector.Steps.Any(step => step.Compound.Selectors.Any(Contains));

    private static bool Contains(SimpleSelector simple) => simple switch
    {
        NestingSelector => true,
        PseudoClassSelector { Selectors: { } list } => list.Selectors.Any(Contains),
        _ => false,
    };
}
