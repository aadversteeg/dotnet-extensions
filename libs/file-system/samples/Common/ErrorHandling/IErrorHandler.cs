using System;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.Functional;

namespace FileSystemSamples.Common.ErrorHandling;

/// <summary>
/// Interface for handling errors with retry logic and graceful degradation.
/// </summary>
public interface IErrorHandler
{
    /// <summary>
    /// Executes an operation with retry logic for transient errors.
    /// </summary>
    /// <typeparam name="T">The type of the operation result.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result<T, ErrorInfo>> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if an exception represents a transient error that should be retried.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <returns>True if the error is transient and should be retried; otherwise, false.</returns>
    bool IsTransientError(Exception exception);

    /// <summary>
    /// Gets the error handling options.
    /// </summary>
    ErrorHandlingOptions Options { get; }
}

/// <summary>
/// Configuration options for error handling behavior.
/// </summary>
public class ErrorHandlingOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the initial retry delay in milliseconds.
    /// </summary>
    public int InitialRetryDelayMs { get; set; } = 100;

    /// <summary>
    /// Gets or sets the multiplier for exponential backoff.
    /// </summary>
    public double RetryMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets whether to continue processing after encountering errors.
    /// </summary>
    public bool ContinueOnError { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum delay between retries in milliseconds.
    /// </summary>
    public int MaxRetryDelayMs { get; set; } = 5000;
}

/// <summary>
/// Information about an error that occurred during processing.
/// </summary>
public class ErrorInfo
{
    /// <summary>
    /// Gets the error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the exception that caused the error, if any.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Gets the error code or category.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Gets whether this error is considered recoverable.
    /// </summary>
    public bool IsRecoverable { get; }

    /// <summary>
    /// Gets the number of retry attempts made.
    /// </summary>
    public int RetryAttempts { get; }

    /// <summary>
    /// Gets the timestamp when the error occurred.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the ErrorInfo class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code or category.</param>
    /// <param name="exception">The exception that caused the error.</param>
    /// <param name="isRecoverable">Whether this error is considered recoverable.</param>
    /// <param name="retryAttempts">The number of retry attempts made.</param>
    public ErrorInfo(string message, string errorCode, Exception? exception = null, 
        bool isRecoverable = false, int retryAttempts = 0)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
        Exception = exception;
        IsRecoverable = isRecoverable;
        RetryAttempts = retryAttempts;
        Timestamp = DateTime.UtcNow;
    }
}