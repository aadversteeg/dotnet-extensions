using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.Functional;
using Microsoft.Extensions.Logging;

namespace FileSystemSamples.Common.ErrorHandling;

/// <summary>
/// Handles errors with retry logic and graceful degradation for filesystem operations.
/// </summary>
public class ErrorHandler : IErrorHandler
{
    private readonly ILogger<ErrorHandler> _logger;
    private readonly ErrorHandlingOptions _options;

    /// <summary>
    /// Initializes a new instance of the ErrorHandler class.
    /// </summary>
    /// <param name="logger">Logger for error information.</param>
    /// <param name="options">Configuration options for error handling behavior.</param>
    public ErrorHandler(ILogger<ErrorHandler> logger, ErrorHandlingOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new ErrorHandlingOptions();
    }

    /// <inheritdoc />
    public ErrorHandlingOptions Options => _options;

    /// <inheritdoc />
    public async Task<Result<T, ErrorInfo>> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));

        var attempts = 0;
        var delay = _options.InitialRetryDelayMs;

        while (attempts <= _options.MaxRetries)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var result = await operation(cancellationToken);
                
                if (attempts > 0)
                {
                    _logger.LogInformation("Operation succeeded after {Attempts} retry attempts", attempts);
                }
                
                return Result<T, ErrorInfo>.Success(result);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Operation was cancelled after {Attempts} attempts", attempts);
                return Result<T, ErrorInfo>.Failure(new ErrorInfo(
                    "Operation was cancelled",
                    "OperationCancelled",
                    retryAttempts: attempts));
            }
            catch (Exception ex)
            {
                attempts++;
                
                if (!IsTransientError(ex) || attempts > _options.MaxRetries)
                {
                    _logger.LogError(ex, "Operation failed permanently after {Attempts} attempts: {Message}", 
                        attempts, ex.Message);
                    
                    return Result<T, ErrorInfo>.Failure(new ErrorInfo(
                        ex.Message,
                        GetErrorCode(ex),
                        ex,
                        IsTransientError(ex),
                        attempts - 1));
                }

                _logger.LogWarning(ex, "Transient error on attempt {Attempts}/{MaxAttempts}: {Message}. Retrying in {DelayMs}ms",
                    attempts, _options.MaxRetries + 1, ex.Message, delay);

                if (delay > 0)
                {
                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Retry delay was cancelled");
                        return Result<T, ErrorInfo>.Failure(new ErrorInfo(
                            "Operation was cancelled during retry delay",
                            "OperationCancelled",
                            retryAttempts: attempts - 1));
                    }
                }

                // Exponential backoff with max delay cap
                delay = Math.Min((int)(delay * _options.RetryMultiplier), _options.MaxRetryDelayMs);
            }
        }

        // This should never be reached due to the loop condition, but added for completeness
        return Result<T, ErrorInfo>.Failure(new ErrorInfo(
            "Maximum retry attempts exceeded",
            "MaxRetriesExceeded",
            retryAttempts: _options.MaxRetries));
    }

    /// <inheritdoc />
    public bool IsTransientError(Exception exception)
    {
        return exception switch
        {
            IOException ioEx => IsTransientIOException(ioEx),
            UnauthorizedAccessException => true, // Might be temporary due to file locks
            TimeoutException => true,
            TaskCanceledException => false, // Cancellation is not transient
            OperationCanceledException => false, // Cancellation is not transient
            ArgumentNullException => false, // Logic errors are not transient (more specific than ArgumentException)
            ArgumentException => false, // Logic errors are not transient
            NotSupportedException => false, // Feature not supported is not transient
            _ => false // Unknown exceptions are not considered transient by default
        };
    }

    private static bool IsTransientIOException(IOException ioException)
    {
        var message = ioException.Message?.ToLowerInvariant() ?? "";
        
        // Common transient I/O error patterns
        return message.Contains("being used by another process") ||
               message.Contains("access is denied") ||
               message.Contains("sharing violation") ||
               message.Contains("network path") ||
               message.Contains("device not ready") ||
               message.Contains("disk full") ||
               ioException.HResult == -2147024864; // ERROR_SHARING_VIOLATION
    }

    private static string GetErrorCode(Exception exception)
    {
        return exception switch
        {
            DirectoryNotFoundException => "DirectoryNotFound",
            FileNotFoundException => "FileNotFound",
            PathTooLongException => "PathTooLong",
            IOException => "IOException",
            UnauthorizedAccessException => "UnauthorizedAccess",
            TimeoutException => "Timeout",
            TaskCanceledException => "TaskCancelled",
            OperationCanceledException => "OperationCancelled",
            ArgumentNullException => "ArgumentNull",
            ArgumentException => "InvalidArgument",
            NotSupportedException => "NotSupported",
            _ => exception.GetType().Name
        };
    }
}