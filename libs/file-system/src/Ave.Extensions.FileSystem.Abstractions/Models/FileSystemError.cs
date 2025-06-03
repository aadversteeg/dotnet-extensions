using System;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Represents an error that occurred during a file system operation.
    /// </summary>
    public class FileSystemError
    {
        /// <summary>
        /// Gets the error message describing what went wrong.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the path related to the error, if applicable.
        /// </summary>
        public string? Path { get; }

        /// <summary>
        /// Gets the type of file system error.
        /// </summary>
        public FileSystemErrorType Type { get; }

        /// <summary>
        /// Gets the underlying exception that caused this error, if any.
        /// </summary>
        public Exception? Exception { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemError"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="path">The path related to the error.</param>
        /// <param name="type">The type of error.</param>
        /// <param name="exception">The underlying exception.</param>
        public FileSystemError(string message, string? path = null, FileSystemErrorType type = FileSystemErrorType.Unknown, Exception? exception = null)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Path = path;
            Type = type;
            Exception = exception;
        }

        /// <summary>
        /// Creates a FileSystemError from an exception.
        /// </summary>
        /// <param name="exception">The exception to convert.</param>
        /// <param name="path">The path related to the error.</param>
        /// <returns>A new FileSystemError instance.</returns>
        public static FileSystemError FromException(Exception exception, string? path = null)
        {
            var type = exception switch
            {
                System.IO.FileNotFoundException => FileSystemErrorType.FileNotFound,
                System.IO.DirectoryNotFoundException => FileSystemErrorType.DirectoryNotFound,
                UnauthorizedAccessException => FileSystemErrorType.AccessDenied,
                System.IO.PathTooLongException => FileSystemErrorType.PathTooLong,
                ArgumentException when exception.Message.Contains("path") => FileSystemErrorType.InvalidPath,
                System.IO.IOException => FileSystemErrorType.IOException,
                NotSupportedException => FileSystemErrorType.NotSupported,
                _ => FileSystemErrorType.Unknown
            };

            return new FileSystemError(exception.Message, path, type, exception);
        }

        /// <summary>
        /// Returns a string representation of this error.
        /// </summary>
        /// <returns>A string describing the error.</returns>
        public override string ToString()
        {
            var result = $"{Type}: {Message}";
            if (!string.IsNullOrEmpty(Path))
            {
                result += $" (Path: {Path})";
            }
            return result;
        }
    }

    /// <summary>
    /// Defines the types of file system errors.
    /// </summary>
    public enum FileSystemErrorType
    {
        /// <summary>
        /// An unknown or unspecified error.
        /// </summary>
        Unknown,

        /// <summary>
        /// The specified file was not found.
        /// </summary>
        FileNotFound,

        /// <summary>
        /// The specified directory was not found.
        /// </summary>
        DirectoryNotFound,

        /// <summary>
        /// The specified path (file or directory) was not found.
        /// </summary>
        PathNotFound,

        /// <summary>
        /// Access to the file or directory was denied.
        /// </summary>
        AccessDenied,

        /// <summary>
        /// The path is too long for the file system.
        /// </summary>
        PathTooLong,

        /// <summary>
        /// The path contains invalid characters or is malformed.
        /// </summary>
        InvalidPath,

        /// <summary>
        /// A general I/O error occurred.
        /// </summary>
        IOException,

        /// <summary>
        /// The operation is not supported by the file system.
        /// </summary>
        NotSupported
    }
}