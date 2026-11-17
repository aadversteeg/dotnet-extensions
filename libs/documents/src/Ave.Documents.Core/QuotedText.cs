using System.Text;

namespace Ave.Documents.Core;

/// <summary>
/// Writes text between double quotes with escapes, for the text serialisations of the intermediate models.
/// Every line of a serialisation then stays one line, whatever the document's text contains.
/// </summary>
public static class QuotedText
{
    /// <summary>
    /// Quotes text: <c>"</c> and <c>\</c> get a backslash, line feed, carriage return and tab become <c>\n</c>,
    /// <c>\r</c>, <c>\t</c>, and other control characters become <c>\uXXXX</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The quoted text.</returns>
    public static string Quote(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var character in text)
        {
            builder.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(character) => $"\\u{(int)character:X4}",
                _ => character.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }
}
