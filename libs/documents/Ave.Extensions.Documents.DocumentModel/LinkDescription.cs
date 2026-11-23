using System.Text;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// THE RULE FOR A LINK'S DESCRIPTION (<see cref="SemanticRole.LinkRole.Description"/>), for every front-end: a link
/// annotation always carries a description, which PDF/UA-1 requires (ISO 14289-1 §7.18.5; veraPDF rule 7.18.5-2, "Links
/// shall contain an alternate description via their Contents key") and which is harmless in every other profile. The
/// first of these that is not blank, with its white space collapsed: the element's title, the link's text, the
/// alternative text of an image inside it; else the URI itself, so a description is never blank.
/// </summary>
public static class LinkDescription
{
    /// <summary>The description by the rule.</summary>
    /// <param name="title">The element's title, or null.</param>
    /// <param name="text">The link's text, or null.</param>
    /// <param name="alternativeText">The alternative text of an image inside the link, or null.</param>
    /// <param name="uri">The URI.</param>
    /// <returns>The description.</returns>
    public static string Of(string? title, string? text, string? alternativeText, string uri)
    {
        foreach (var candidate in new[] { title, text, alternativeText })
        {
            if (Collapsed(candidate) is { Length: > 0 } description)
            {
                return description;
            }
        }

        return uri;
    }

    /// <summary>The text with every stretch of white space one space, and none at either end; one pass.</summary>
    private static string Collapsed(string? text)
    {
        if (text is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
