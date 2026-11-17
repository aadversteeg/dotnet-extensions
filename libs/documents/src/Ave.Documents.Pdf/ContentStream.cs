using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Pdf;

/// <summary>Writes content-stream instructions as the bytes of a content stream (ISO 32000-1 §7.8.2).</summary>
public static class ContentStream
{
    /// <summary>
    /// Writes each operation on its own line: operands separated by spaces, then the operator. Fails when an
    /// operand holds a number that is not finite.
    /// </summary>
    /// <param name="operations">The operations, in drawing order.</param>
    /// <returns>The stream bytes (ASCII), a <c>Documents.Pdf.Write.InvalidNumber</c> error, or a
    /// <c>Documents.Pdf.Write.InvalidObject</c> error for a missing operation, operator or operand.</returns>
    public static Result<ImmutableArray<byte>, Error> Serialize(IEnumerable<ContentOperation?>? operations)
    {
        var all = operations.OrEmpty().ToImmutableArray();
        var missing = operations is null or ImmutableArray<ContentOperation?> { IsDefault: true } || all.Any(operation => operation is null || string.IsNullOrWhiteSpace(operation.Operator));
        if (missing)
        {
            return Result<ImmutableArray<byte>, Error>.Failure(
                new Error(PdfErrorCodes.Write.InvalidObject, "A content stream needs its operations, each with an operator.").With(ErrorMetadataKeys.Value, "null"));
        }

        if (PdfSerializer.Problem(all.SelectMany(operation => operation!.Operands)) is { } problem)
        {
            return Result<ImmutableArray<byte>, Error>.Failure(problem);
        }

        var text = string.Concat(all.Select(operation => WriteLine(operation!)));
        return Result<ImmutableArray<byte>, Error>.Success([.. Encoding.ASCII.GetBytes(text)]);
    }

    private static string WriteLine(ContentOperation operation) =>
        string.Concat(operation.Operands.Select(operand => PdfSerializer.Write(operand) + " ")) + operation.Operator + "\n";
}
