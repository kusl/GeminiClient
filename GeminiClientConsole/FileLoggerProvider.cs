// GeminiClientConsole/FileLoggerProvider.cs
using System.Text;
using Microsoft.Extensions.Logging;

namespace GeminiClientConsole;

/// <summary>
/// An <see cref="ILoggerProvider"/> that writes framework/library log messages to a per-session
/// diagnostics file instead of the console. This keeps the interactive console output clean — no
/// stack traces or framework chatter interleaved with prompts — while preserving full diagnostics
/// for bug reports. It owns its own writer, so it has no dependency on other services and cannot
/// be affected by DI initialisation order.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter? _writer;
    private readonly object _lock = new();
    private readonly LogLevel _minLevel;
    private bool _disposed;

    public string? FilePath { get; }

    public FileLoggerProvider(string? directory = null, LogLevel minLevel = LogLevel.Information)
    {
        _minLevel = minLevel;
        try
        {
            string dir = directory ?? ConversationLogger.GetDefaultLogDirectory();
            Directory.CreateDirectory(dir);
            FilePath = Path.Combine(dir, $"diagnostics_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            _writer = new StreamWriter(FilePath, append: true, Encoding.UTF8) { AutoFlush = true };
        }
        catch (Exception ex)
        {
            // Never let logging setup take down the app; fall back to a no-op logger.
            Console.Error.WriteLine($"Warning: could not initialise diagnostics log: {ex.Message}");
            _writer = null;
            FilePath = null;
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal bool IsEnabled(LogLevel level) => _writer is not null && level >= _minLevel && level != LogLevel.None;

    internal void Write(string line)
    {
        if (_writer is null)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _writer.WriteLine(line);
            }
            catch
            {
                // Diagnostics logging must never throw into the app.
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _writer?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!provider.IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            StringBuilder sb = new();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ");
            sb.Append(logLevel.ToString().ToUpperInvariant()).Append(' ');
            sb.Append(category).Append(" — ").Append(message);
            if (exception is not null)
            {
                sb.AppendLine();
                sb.Append(exception);
            }

            provider.Write(sb.ToString());
        }
    }
}
