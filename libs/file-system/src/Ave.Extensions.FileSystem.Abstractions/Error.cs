using System;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Represents an error with a code and message.
    /// </summary>
    public readonly record struct Error : IEquatable<Error>
    {
        /// <summary>
        /// Gets the error code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the error message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Initializes a new instance of the Error record.
        /// </summary>
        /// <param name="code">The error code.</param>
        /// <param name="message">The error message.</param>
        public Error(string code, string message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        /// <summary>
        /// Returns the string representation of the error.
        /// </summary>
        /// <returns>The error message.</returns>
        public override string ToString() => Message;

    }
}