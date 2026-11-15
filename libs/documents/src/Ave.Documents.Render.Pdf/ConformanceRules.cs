using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf.Tagging;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Render.Pdf;

/// <summary>
/// The input a profile forbids. The back-end never changes a document to make it conform — it does not invent a
/// title, guess a language or drop an image without alternative text: it refuses the document with an error that names
/// the rule and the author's line. Everything a profile can ADD (metadata, output intent, structure) is added
/// elsewhere; only what needs the author's knowledge is checked here.
/// </summary>
internal static class ConformanceRules
{
    /// <summary>Checks the document against every rule of the profile and reports ALL violations together.</summary>
    /// <param name="document">The page document.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>The document unchanged, or the violations (several aggregated with <c>Errors.Aggregate</c>).</returns>
    public static Result<PageDocument, Error> Check(PageDocument document, PdfProfile profile)
    {
        var violations = PdfA3(document, profile).Concat(PdfUa1(document, profile)).ToList();
        return violations.Count == 0
            ? Result<PageDocument, Error>.Success(document)
            : Result<PageDocument, Error>.Failure(Errors.Aggregate(violations));
    }

    /// <summary>
    /// Checks the structure tree of a tagged file against the rules that need it: under PDF/UA-1, tables must be regular
    /// grids (<see cref="TableRegularity"/>). All violations are reported together.
    /// </summary>
    /// <param name="tree">The structure tree.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>The tree unchanged, or the violations.</returns>
    public static Result<StructureTree, Error> CheckStructure(StructureTree tree, PdfProfile profile)
    {
        var violations = profile.IsPdfUa1 ? TableRegularity.Violations(tree).ToList() : [];
        return violations.Count == 0
            ? Result<StructureTree, Error>.Success(tree)
            : Result<StructureTree, Error>.Failure(Errors.Aggregate(violations));
    }

    /// <summary>ISO 19005-3 §6.8 (veraPDF rule 6.8-3): every embedded file says how it relates to the document.</summary>
    private static IEnumerable<Error> PdfA3(PageDocument document, PdfProfile profile) =>
        profile.PdfA3 is null
            ? []
            : document.Metadata.Attachments
                .Where(attachment => attachment.Relationship is null)
                .Select(attachment => new Error(
                        DocumentErrorCodes.Pdf.Conformance.PdfA3.AttachmentWithoutRelationship,
                        $"{profile} needs the relationship of attachment '{attachment.FileName}' to the document (ISO 19005-3 §6.8) ({attachment.Source}).")
                    .With(ErrorMetadataKeys.Value, attachment.FileName)
                    .WithSource(attachment.Source));

    /// <summary>
    /// ISO 14289-1: a title (§7.1), a language (§7.2), alternative text for every figure (§7.3), and headings that start
    /// at H1 and never skip a level going down (§7.4.2). Each structure node is checked once, at its first marker.
    /// </summary>
    private static IEnumerable<Error> PdfUa1(PageDocument document, PdfProfile profile)
    {
        if (!profile.IsPdfUa1)
        {
            yield break;
        }

        var nodes = document.Pages
            .SelectMany(page => page.Items.OfType<StructureStart>())
            .DistinctBy(start => start.Node)
            .ToList();
        // The painter's first marker is always the root (role Document), written where the document was; a hand-made
        // page model without markers has no location to report.
        var documentSource = nodes.Count > 0 ? nodes[0].Source : default;

        if (string.IsNullOrWhiteSpace(document.Metadata.Title))
        {
            yield return Violation(DocumentErrorCodes.Pdf.Conformance.PdfUa1.MissingTitle, "PDF/UA-1 needs a document title (ISO 14289-1 §7.1)", documentSource);
        }

        if (document.Metadata.Language is null)
        {
            yield return Violation(DocumentErrorCodes.Pdf.Conformance.PdfUa1.MissingLanguage, "PDF/UA-1 needs the document's language (ISO 14289-1 §7.2)", documentSource);
        }

        foreach (var figure in nodes.Where(start => start.Role is SemanticRole.FigureRole { AlternativeText: null }))
        {
            yield return Violation(
                    DocumentErrorCodes.Pdf.Conformance.PdfUa1.FigureWithoutAlternativeText,
                    "PDF/UA-1 needs alternative text for every image that is not decorative: use .Alt(...) or .Decorative() (ISO 14289-1 §7.3)",
                    figure.Source)
                .With(ErrorMetadataKeys.Node, figure.Node.ToString());
        }

        foreach (var error in Headings(nodes))
        {
            yield return error;
        }
    }

    /// <summary>
    /// §7.4.2 (veraPDF rule 7.4.2-1): "If any heading tags are used, H1 shall be the first"; a descending sequence
    /// "shall not skip an intervening heading level"; going back up to any level is allowed.
    /// </summary>
    private static IEnumerable<Error> Headings(IEnumerable<StructureStart> nodes)
    {
        var previous = 0;
        foreach (var heading in nodes.Where(start => start.Role is SemanticRole.HeadingRole))
        {
            var level = ((SemanticRole.HeadingRole)heading.Role).Level;
            if (previous == 0 && level != 1)
            {
                yield return Violation(DocumentErrorCodes.Pdf.Conformance.PdfUa1.FirstHeadingNotH1, $"PDF/UA-1 needs the first heading to be level 1, not {level} (ISO 14289-1 §7.4.2)", heading.Source)
                    .With(ErrorMetadataKeys.Node, heading.Node.ToString())
                    .With(ErrorMetadataKeys.Value, level.ToString(CultureInfo.InvariantCulture));
            }
            else if (previous != 0 && level > previous + 1)
            {
                yield return Violation(DocumentErrorCodes.Pdf.Conformance.PdfUa1.HeadingLevelSkipped, $"PDF/UA-1 does not allow a level-{level} heading right after a level-{previous} heading (ISO 14289-1 §7.4.2)", heading.Source)
                    .With(ErrorMetadataKeys.Node, heading.Node.ToString())
                    .With(ErrorMetadataKeys.Value, $"{previous}->{level}");
            }

            previous = level;
        }
    }

    private static Error Violation(ErrorCode code, string message, SourceLocation source) =>
        new Error(code, $"{message} ({source}).").WithSource(source);
}
