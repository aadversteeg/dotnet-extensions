using System.Collections.Generic;
using System.Linq;

namespace Ave.Extensions.Documents.Css.Selectors;

/// <summary>
/// Desugars nested selectors (CSS Nesting 1 §3): the nesting selector <c>&amp;</c> stands for <c>:is(parent)</c>,
/// with the specificity of the most specific parent selector; a relative selector (one that starts with a combinator)
/// and a selector without <c>&amp;</c> get <c>&amp;</c> in front, the latter with the descendant combinator.
/// </summary>
internal static class Nesting
{
    /// <summary>The nested selector list made absolute against its parent's list.</summary>
    public static SelectorList Desugar(SelectorList nested, SelectorList parent)
    {
        var parentIs = new PseudoClassSelector("is", SelectorSupport.Supported) { Selectors = parent };
        var selectors = new List<ComplexSelector>();
        foreach (var selector in nested.Selectors)
        {
            var steps = selector.Steps.Select(step => step with { Compound = Replace(step.Compound, parentIs) }).ToList();
            var relative = steps[0].Combinator is not null;
            if (relative || !Contains(selector))
            {
                if (!relative)
                {
                    steps[0] = steps[0] with { Combinator = Combinator.Descendant };
                }

                steps.Insert(0, new SelectorStep(null, new CompoundSelector([parentIs])));
            }

            selectors.Add(new ComplexSelector([.. steps], selector.Position));
        }

        return new SelectorList([.. selectors]);
    }

    private static CompoundSelector Replace(CompoundSelector compound, PseudoClassSelector parentIs) =>
        new([.. compound.Selectors.Select(simple => Replace(simple, parentIs))]);

    private static SimpleSelector Replace(SimpleSelector simple, PseudoClassSelector parentIs) => simple switch
    {
        NestingSelector => parentIs,
        PseudoClassSelector { Selectors: { } list } pseudo => pseudo with { Selectors = ReplaceAll(list, parentIs) },
        _ => simple,
    };

    private static SelectorList ReplaceAll(SelectorList list, PseudoClassSelector parentIs) =>
        new([.. list.Selectors.Select(selector => selector with { Steps = [.. selector.Steps.Select(step => step with { Compound = Replace(step.Compound, parentIs) })] })]);

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
