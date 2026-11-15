using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileSystemSamples.Common.Options;

namespace FileSystemSamples.Common.Export;

/// <summary>
/// Interface for streaming export operations that handle large datasets efficiently.
/// </summary>
/// <typeparam name="T">The type of data being exported.</typeparam>
public interface IStreamingExporter<T> : IDisposable
{
    /// <summary>
    /// Initializes the streaming export to the specified output.
    /// </summary>
    /// <param name="output">The output stream to write to.</param>
    /// <param name="format">The export format.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    Task InitializeAsync(Stream output, ExportFormat format, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports a batch of items to the stream.
    /// </summary>
    /// <param name="items">The items to export.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    Task ExportBatchAsync(IEnumerable<T> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes the export operation and closes the stream.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    Task FinalizeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total number of items exported so far.
    /// </summary>
    long TotalItemsExported { get; }

    /// <summary>
    /// Gets the current export format.
    /// </summary>
    ExportFormat Format { get; }
}

/// <summary>
/// Base implementation of streaming exporter with common functionality.
/// </summary>
/// <typeparam name="T">The type of data being exported.</typeparam>
public abstract class StreamingExporterBase<T> : IStreamingExporter<T>
{
    protected Stream? _output;
    protected StreamWriter? _writer;
    protected bool _disposed;
    protected bool _initialized;
    private long _totalItemsExported;

    /// <inheritdoc />
    public ExportFormat Format { get; private set; }

    /// <inheritdoc />
    public long TotalItemsExported => _totalItemsExported;

    /// <inheritdoc />
    public virtual async Task InitializeAsync(Stream output, ExportFormat format, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
        if (_initialized)
            throw new InvalidOperationException("Exporter has already been initialized");

        _output = output ?? throw new ArgumentNullException(nameof(output));
        Format = format;
        _writer = new StreamWriter(_output, leaveOpen: true);
        
        await WriteHeaderAsync(cancellationToken);
        _initialized = true;
    }

    /// <inheritdoc />
    public virtual async Task ExportBatchAsync(IEnumerable<T> items, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
        if (!_initialized)
            throw new InvalidOperationException("Exporter must be initialized before exporting");

        if (_writer == null)
            throw new InvalidOperationException("Writer is not initialized");

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteItemAsync(item, cancellationToken);
            Interlocked.Increment(ref _totalItemsExported);
        }

        await _writer.FlushAsync();
    }

    /// <inheritdoc />
    public virtual async Task FinalizeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
        if (!_initialized)
            return;

        if (_writer != null)
        {
            await WriteFooterAsync(cancellationToken);
            await _writer.FlushAsync();
        }
    }

    /// <summary>
    /// Writes the header for the export format.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    protected abstract Task WriteHeaderAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes a single item to the export stream.
    /// </summary>
    /// <param name="item">The item to write.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    protected abstract Task WriteItemAsync(T item, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the footer for the export format.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    protected abstract Task WriteFooterAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the StreamingExporterBase and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _writer?.Dispose();
            _disposed = true;
        }
    }
}