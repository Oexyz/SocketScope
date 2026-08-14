using System.Text.Json;
using Microsoft.Extensions.Logging;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Platform.Linux.Storage;
using OeXYZ.SocketScope.Platform.Windows.Storage;

namespace OeXYZ.SocketScope.Desktop.Infrastructure;

public sealed class PrivacySafeJsonLoggerProvider : ILoggerProvider
{
    private const int MaximumEntries = 2_048;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly Lock gate = new();
    private readonly List<PrivacySafeLogEntry> entries = [];
    private readonly ISecureFileStore? store;
    private readonly IDisposable? storeLifetime;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task flushLoop;
    private bool dirty;
    private bool disposed;

    public PrivacySafeJsonLoggerProvider(ApplicationDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            (store, storeLifetime) = CreatePrivateStore(paths.Logs);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _ = exception;
        }

        flushLoop = Task.Run(() => FlushLoopAsync(lifetime.Token));
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new PrivacySafeLogger(this, SanitizeIdentifier(categoryName, 160));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        try
        {
            flushLoop.GetAwaiter().GetResult();
            FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _ = exception;
        }
        finally
        {
            lifetime.Dispose();
            storeLifetime?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void Add(string category, LogLevel level, EventId eventId, Exception? exception)
    {
        if (disposed || level < LogLevel.Information)
        {
            return;
        }

        PrivacySafeLogEntry entry = new(
            DateTimeOffset.UtcNow,
            level.ToString(),
            category,
            eventId.Id,
            SanitizeIdentifier(eventId.Name, 96),
            exception is not null);
        lock (gate)
        {
            if (entries.Count == MaximumEntries)
            {
                entries.RemoveAt(0);
            }

            entries.Add(entry);
            dirty = true;
        }
    }

    private async Task FlushLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal provider shutdown.
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (store is null)
        {
            return;
        }

        PrivacySafeLogEntry[] snapshot;
        lock (gate)
        {
            if (!dirty)
            {
                return;
            }

            snapshot = [.. entries];
            dirty = false;
        }

        try
        {
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            await store.WriteAtomicAsync("current.json", content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _ = exception;
            lock (gate)
            {
                dirty = true;
            }
        }
    }

    private static (ISecureFileStore Store, IDisposable Lifetime) CreatePrivateStore(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsSecureFileStore store = new(root);
            return (store, store);
        }

        if (OperatingSystem.IsLinux())
        {
            LinuxSecureFileStore store = new(root);
            return (store, store);
        }

        throw new PlatformNotSupportedException("SocketScope supports Windows and Linux.");
    }

    private static string SanitizeIdentifier(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        char[] result = [.. value
            .Take(maximumLength)
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '_')];
        return new string(result);
    }

    private sealed class PrivacySafeLogger(PrivacySafeJsonLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            _ = state;
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _ = state;
            _ = formatter;
            if (IsEnabled(logLevel))
            {
                owner.Add(category, logLevel, eventId, exception);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        internal static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed record PrivacySafeLogEntry(
        DateTimeOffset TimestampUtc,
        string Level,
        string Category,
        int EventId,
        string EventName,
        bool HasException);
}
