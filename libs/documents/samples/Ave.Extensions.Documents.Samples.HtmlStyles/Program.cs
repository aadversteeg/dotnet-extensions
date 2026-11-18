// Prints the styled tree of an HTML file and every diagnostic: what the HTML front-end makes of a document before
// layout. Usage: dotnet Ave.Extensions.Documents.Samples.HtmlStyles.dll [file.html] [--refuse]
// Without a file it reads data/invoice.html. Linked style sheets and @import are read from the file's folder: the
// sample is the caller, so it does the file access; the engine opens no file itself.
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

var path = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine(AppContext.BaseDirectory, "data", "invoice.html"));
var policy = args.Contains("--refuse") ? UnsupportedPolicy.Refuse : UnsupportedPolicy.Warn;
var folder = new Uri(Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar);

HtmlResource? ReadFile(string url)
{
    // Only files under the document's folder, and only file URLs.
    var uri = new Uri(url);
    return uri.IsFile && folder.IsBaseOf(uri) && File.Exists(uri.LocalPath) ? new HtmlResource(File.ReadAllBytes(uri.LocalPath)) : null;
}

var output = new StringBuilder();
var result = HtmlParser.Parse(File.ReadAllBytes(path), new HtmlParseOptions(Path.GetFileName(path)))
    .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions
    {
        BaseUrl = new Uri(path).AbsoluteUri,
        Resources = ReadFile,
        Unsupported = policy,
    }));

var exitCode = result.Match(
    styled =>
    {
        Print(styled.Root, 0);
        output.AppendLine();
        output.AppendLine($"{styled.Diagnostics.Count} diagnostics");
        foreach (var error in styled.Diagnostics)
        {
            output.AppendLine(Diagnostic(error));
        }

        return 0;
    },
    error =>
    {
        output.AppendLine("Refused:");
        foreach (var part in error.Code == ErrorCodes.Aggregate ? error.Parts() : [error])
        {
            output.AppendLine(Diagnostic(part));
        }

        return 1;
    });
Console.Write(output.ToString());
return exitCode;

// One line per element: its name and id, then the properties whose computed value is not the initial one (a border
// width without a border style computes to zero, which is left out too).
void Print(StyledElement element, int depth)
{
    var id = element.Element.Attribute("id") is { } value ? "#" + value : string.Empty;
    bool NoBorder(PropertyDefinition property) =>
        property.Name.StartsWith("border-", StringComparison.Ordinal) && property.Name.EndsWith("-width", StringComparison.Ordinal)
        && element.Style.Find(property.Name.Replace("-width", "-style", StringComparison.Ordinal))?.ToString() == "none";
    var changed = PropertyRegistry.Longhands
        .Where(property => !Equals(element.Style[property], property.Initial) && !NoBorder(property))
        .Select(property => $"{property.Name}: {element.Style[property]}");
    output.AppendLine($"{new string(' ', depth * 2)}<{element.Element.LocalName}{id}> {string.Join("; ", changed)}");
    foreach (var child in element.Children)
    {
        switch (child)
        {
            case StyledElement nested:
                Print(nested, depth + 1);
                break;
            case StyledText text when !string.IsNullOrWhiteSpace(text.Text.Data):
                var data = text.Text.Data.Trim().ReplaceLineEndings(" ");
                output.AppendLine($"{new string(' ', (depth + 1) * 2)}\"{(data.Length > 40 ? data[..40] + "…" : data)}\"");
                break;
        }
    }
}

static string Diagnostic(Error error)
{
    var meta = error.Metadata;
    var place = meta is not null && meta.TryGetValue(ErrorMetadataKeys.SourceLine, out var line)
        ? $"{meta.GetValueOrDefault(ErrorMetadataKeys.SourceFile)}:{line}:{meta.GetValueOrDefault(ErrorMetadataKeys.SourceColumn)}"
        : $"{meta?.GetValueOrDefault(ErrorMetadataKeys.SourceFile)}";
    return $"  {place} [{HtmlStyleDiagnostics.ClassOf(error.Code)}] {error.Code}: {error.Message}";
}
