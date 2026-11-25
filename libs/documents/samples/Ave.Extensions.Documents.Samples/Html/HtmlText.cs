using System;
using System.Text;

namespace Ave.Extensions.Documents.Samples;

/// <summary>
/// How the HTML samples put a data value into HTML and CSS. The data is the caller's, and in a real caller's hands
/// untrusted: every value goes through one of these, never into the template as it is.
/// </summary>
public static class HtmlText
{
    /// <summary>
    /// A value as HTML text or as a quoted attribute value: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and
    /// <c>'</c> become character references, so the value can neither end the element nor the attribute it is in. Every
    /// other character stays as it is (the file is UTF-8). <see cref="System.Net.WebUtility.HtmlEncode(string)"/> is as
    /// safe, but also writes the letters U+00A0 to U+00FF as numeric references (<c>é</c> as <c>&amp;#233;</c>): the
    /// same document, other bytes. A CR LF or a lone CR becomes LF, as HTML's input stream reads it (HTML §13.2.3.5),
    /// so the file has one kind of line end.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The escaped value.</returns>
    public static string Escape(string value)
    {
        var text = new StringBuilder(value.Length);
        foreach (var character in LineFeeds(value))
        {
            text.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#x27;",
                _ => character.ToString(),
            });
        }

        return text.ToString();
    }

    /// <summary>
    /// A value as the inside of a double-quoted CSS string in a <c>style</c> element: a backslash and a quote escaped, a
    /// line break as <c>\A</c> (CSS Syntax 3 §4.3.7), and <c>&lt;</c> as <c>\3C</c>, so the value cannot end the style
    /// element (the HTML parser ends it at <c>&lt;/style</c>, whatever the CSS around it).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The escaped value.</returns>
    public static string CssString(string value) => LineFeeds(value)
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("<", "\\3C ", StringComparison.Ordinal)
        .Replace("\n", "\\A ", StringComparison.Ordinal);

    /// <summary>
    /// A text with every line end as LF: a CR LF or a lone CR becomes LF, as HTML's input stream reads it (HTML
    /// §13.2.3.5). Each template's text goes through it as well as each value: a C# raw string takes its line ends from
    /// the source file as it is checked out (CR LF in a Windows checkout), and the measured documents use LF.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with LF line ends.</returns>
    public static string LineFeeds(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
