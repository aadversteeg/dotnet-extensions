using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fonts.Tables;

/// <summary>The errors the table parsers report, built in one place so their messages and metadata agree.</summary>
internal static class FontParseErrors
{
    /// <summary>The table (or file) is shorter than a structure it contains.</summary>
    public static Result<T, Error> Truncated<T>(string table, long needed, int available) =>
        Result<T, Error>.Failure(
            new Error(
                    DocumentErrorCodes.Font.Parse.Truncated,
                    $"Font table '{table}' needs {needed} bytes but has {available}.")
                .With(ErrorMetadataKeys.Table, table)
                .With(ErrorMetadataKeys.Offset, available));

    /// <summary>The table holds a value the OpenType specification does not allow.</summary>
    public static Result<T, Error> InvalidValue<T>(string table, string message, object value) =>
        Result<T, Error>.Failure(
            new Error(DocumentErrorCodes.Font.Parse.InvalidValue, $"Font table '{table}': {message}")
                .With(ErrorMetadataKeys.Table, table)
                .With(ErrorMetadataKeys.Value, value.ToString() ?? string.Empty));

    /// <summary>A required table is absent.</summary>
    public static Result<T, Error> MissingTable<T>(string table) =>
        Result<T, Error>.Failure(
            new Error(DocumentErrorCodes.Font.Parse.MissingTable, $"The font has no '{table}' table.")
                .With(ErrorMetadataKeys.Table, table));

    /// <summary>The file is a font format this reader does not read.</summary>
    public static Result<T, Error> UnsupportedFormat<T>(string message, string value) =>
        Result<T, Error>.Failure(
            new Error(DocumentErrorCodes.Font.Parse.UnsupportedFormat, message)
                .With(ErrorMetadataKeys.Value, value));
}
