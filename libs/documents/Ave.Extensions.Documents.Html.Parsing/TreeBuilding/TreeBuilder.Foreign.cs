using Ave.Extensions.Documents.Html.Parsing.Tokenizing;

namespace Ave.Extensions.Documents.Html.Parsing.TreeBuilding;

/// <summary>The rules for parsing tokens in foreign content (HTML Standard §13.2.6.5): SVG and MathML.</summary>
internal sealed partial class TreeBuilder
{
    private void ForeignContent(Input t)
    {
        switch (t.Kind)
        {
            case TokenKind.Characters:
                if (t.Char == 0)
                {
                    Error("unexpected-null-character", t);
                    InsertCharacter(0xFFFD, t.Source);
                    return;
                }

                InsertCharacter(t.Char, t.Source);
                if (!t.IsWhitespace && t.Char != 0xFFFD)
                {
                    _framesetOk = false;
                }

                return;
            case TokenKind.Comment:
                InsertComment(t.Token!);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!);
                return;
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
            case TokenKind.StartTag:
                ForeignStartTag(t);
                return;
            default:
                ForeignEndTag(t);
                return;
        }
    }

    private void ForeignStartTag(Input t)
    {
        var token = t.Token!;
        var breaksOut = ElementTables.BreakOutOfForeignContent.Contains(token.Name!)
            || (token.Name == "font" && (token.Attribute("color") is not null || token.Attribute("face") is not null || token.Attribute("size") is not null));
        if (breaksOut)
        {
            BreakOutOfForeignContent(t);
            return;
        }

        var adjusted = AdjustedCurrentNode!;
        string? localName = null;
        if (adjusted.Namespace == ElementNamespace.MathMl)
        {
            AdjustMathMlAttributes(token);
        }
        else if (adjusted.Namespace == ElementNamespace.Svg)
        {
            if (ElementTables.SvgElementNames.TryGetValue(token.Name!, out var svgName))
            {
                localName = svgName;
            }

            AdjustSvgAttributes(token);
        }

        InsertForeignElement(token, adjusted.Namespace, localName);
        if (token.SelfClosing)
        {
            // An SVG script start tag with the flag acts like its end tag; with no script to run, that is a pop too.
            Pop();
            token.SelfClosingAcknowledged = true;
        }
    }

    /// <summary>A start tag (or br, p end tag) that leaves foreign content: pop to an HTML element or an integration
    /// point, then reprocess in the current insertion mode.</summary>
    private void BreakOutOfForeignContent(Input t)
    {
        Error(t.Kind == TokenKind.StartTag ? "unexpected-html-start-tag-in-foreign-content" : "unexpected-html-end-tag-in-foreign-content", t);
        while (CurrentNode is { } node && !(ElementTables.IsMathMlTextIntegrationPoint(node) || ElementTables.IsHtmlIntegrationPoint(node) || node.Namespace == ElementNamespace.Html))
        {
            Pop();
        }

        ProcessIn(_mode, t);
    }

    private void ForeignEndTag(Input t)
    {
        if (t.IsEnd("br", "p"))
        {
            BreakOutOfForeignContent(t);
            return;
        }

        // "An end tag whose tag name is script, if the current node is an SVG script element": with no script to run,
        // it pops the script, which the steps below do as well.
        var name = t.Name!;
        var index = _stack.Count - 1;
        var node = _stack[index];
        if (Ascii.ToLower(node.LocalName) != name)
        {
            Error("misnested-end-tag", t);
        }

        while (true)
        {
            if (index == 0)
            {
                return;
            }

            if (Ascii.ToLower(node.LocalName) == name)
            {
                while (Pop() != node)
                {
                }

                return;
            }

            index--;
            node = _stack[index];
            if (node.Namespace == ElementNamespace.Html)
            {
                ProcessIn(_mode, t);
                return;
            }
        }
    }

    /// <summary>"Adjust MathML attributes" (§13.2.6.1): definitionurl becomes definitionURL.</summary>
    private static void AdjustMathMlAttributes(Token token)
    {
        foreach (var attribute in token.Attributes)
        {
            if (attribute.Name == "definitionurl")
            {
                attribute.Name = "definitionURL";
            }
        }

        token.AttributesRenamed();
    }

    /// <summary>"Adjust SVG attributes" (§13.2.6.1): the table of camel-case attribute names.</summary>
    private static void AdjustSvgAttributes(Token token)
    {
        foreach (var attribute in token.Attributes)
        {
            if (ElementTables.SvgAttributeNames.TryGetValue(attribute.Name, out var adjusted))
            {
                attribute.Name = adjusted;
            }
        }

        token.AttributesRenamed();
    }
}
