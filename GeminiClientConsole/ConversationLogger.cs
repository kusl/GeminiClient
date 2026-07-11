// GeminiClientConsole/ConversationLogger.cs
using System.Text;
using GeminiClient;

namespace GeminiClientConsole;

/// <summary>
/// Handles logging of prompts, responses, failures, cancellations, and session statistics to a
/// per-session text file. Thread-safe, with resilient resource management: a failure to write a
/// single entry is reported to stderr but never throws into the caller.
/// </summary>
public class ConversationLogger : IDisposable
{
    private const string Rule = "────────────────────────────────────────────────────────────";

    private readonly string _logDirectory;
    private readonly string _sessionLogPath;
    private readonly StreamWriter _logWriter;
    private readonly object _writeLock = new();
    private bool _disposed;

    public ConversationLogger(string? customDirectory = null)
    {
        _logDirectory = customDirectory ?? GetDefaultLogDirectory();

        try
        {
            Directory.CreateDirectory(_logDirectory);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create log directory: {_logDirectory}", ex);
        }

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _sessionLogPath = Path.Combine(_logDirectory, $"conversation_{timestamp}.txt");

        try
        {
            _logWriter = new StreamWriter(_sessionLogPath, append: true, Encoding.UTF8) { AutoFlush = true };
            WriteSessionHeader();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create log file: {_sessionLogPath}", ex);
        }
    }

    // ---- Public directory helpers (also used by FileLoggerProvider and configuration loading) ----

    /// <summary>The per-user data directory for logs (XDG on Linux, LocalAppData on Windows, App Support on macOS).</summary>
    public static string GetDefaultLogDirectory() => Path.Combine(GetDataDirectory(), "logs");

    /// <summary>Root per-user *data* directory for the application.</summary>
    public static string GetDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GeminiClient");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "GeminiClient");
        }

        // Linux / Unix — XDG_DATA_HOME (default ~/.local/share).
        string? xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(xdgDataHome))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            xdgDataHome = Path.Combine(home, ".local", "share");
        }

        return Path.Combine(xdgDataHome, "gemini-client");
    }

    /// <summary>Root per-user *config* directory (XDG_CONFIG_HOME on Linux, AppData/App Support elsewhere).</summary>
    public static string GetConfigDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GeminiClient");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "GeminiClient");
        }

        string? xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            xdgConfigHome = Path.Combine(home, ".config");
        }

        return Path.Combine(xdgConfigHome, "gemini-client");
    }

    // ---- Logging API --------------------------------------------------------------------------

    public void LogPrompt(string prompt, string modelName, bool isStreaming)
    {
        if (string.IsNullOrEmpty(prompt) || string.IsNullOrEmpty(modelName))
        {
            return;
        }

        StringBuilder entry = new();
        entry.AppendLine($"[{Now()}] PROMPT");
        entry.AppendLine($"Model: {modelName}");
        entry.AppendLine($"Mode: {(isStreaming ? "Streaming" : "Standard")}");
        entry.AppendLine(Rule);
        entry.AppendLine(prompt);
        entry.AppendLine(Rule);
        entry.AppendLine();
        WriteToLog(entry.ToString());
    }

    public void LogResponse(string response, TimeSpan elapsedTime, string modelName,
        TimeSpan? timeToFirstToken = null, bool streaming = false)
    {
        if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(modelName))
        {
            return;
        }

        StringBuilder entry = new();
        entry.AppendLine($"[{Now()}] RESPONSE");
        entry.AppendLine($"Model: {modelName}");
        entry.AppendLine($"Mode: {(streaming ? "Streaming" : "Standard")}");
        if (timeToFirstToken is { } ttft)
        {
            entry.AppendLine($"Time to First Token: {FormatElapsedTime(ttft)}");
        }
        entry.AppendLine($"Elapsed Time: {FormatElapsedTime(elapsedTime)}");
        entry.AppendLine($"Characters: {response.Length:N0}");
        entry.AppendLine($"Words: {CountWords(response):N0}");
        entry.AppendLine(Rule);
        entry.AppendLine(response);
        entry.AppendLine(Rule);
        entry.AppendLine();
        WriteToLog(entry.ToString());
    }

    /// <summary>Logs a failed attempt, including how long it took to fail and structured error context.</summary>
    public void LogFailure(Exception exception, string modelName, string? prompt, TimeSpan elapsed)
    {
        if (exception is null || string.IsNullOrEmpty(modelName))
        {
            return;
        }

        StringBuilder entry = new();
        entry.AppendLine($"[{Now()}] FAILURE");
        entry.AppendLine($"Model: {modelName}");
        entry.AppendLine($"Time to Failure: {FormatElapsedTime(elapsed)}");
        entry.AppendLine($"Error Type: {exception.GetType().Name}");

        if (exception is GeminiApiException gex)
        {
            entry.AppendLine($"Kind: {gex.Kind}");
            if (gex.StatusCode is { } status)
            {
                entry.AppendLine($"HTTP Status: {(int)status} {status}");
            }
            if (!string.IsNullOrEmpty(gex.ApiStatus))
            {
                entry.AppendLine($"API Status: {gex.ApiStatus}");
            }
            if (gex.RetryAfter is { } retry)
            {
                entry.AppendLine($"Retry After: {FormatElapsedTime(retry)}");
            }
            entry.AppendLine($"Transient: {gex.IsTransient} | QuotaZero: {gex.IsQuotaZero}");
        }

        entry.AppendLine($"Message: {exception.Message}");

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            entry.AppendLine("Original Prompt:");
            entry.AppendLine(prompt);
        }

        if (exception.InnerException is not null)
        {
            entry.AppendLine($"Inner Exception: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
        }

        entry.AppendLine("Stack Trace:");
        entry.AppendLine(exception.StackTrace ?? "(none)");
        entry.AppendLine(Rule);
        entry.AppendLine();
        WriteToLog(entry.ToString());
    }

    public void LogCancellation(string modelName, string? prompt, TimeSpan elapsed)
    {
        StringBuilder entry = new();
        entry.AppendLine($"[{Now()}] CANCELLED");
        entry.AppendLine($"Model: {modelName}");
        entry.AppendLine($"Elapsed Before Cancel: {FormatElapsedTime(elapsed)}");
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            entry.AppendLine("Original Prompt:");
            entry.AppendLine(prompt);
        }
        entry.AppendLine(Rule);
        entry.AppendLine();
        WriteToLog(entry.ToString());
    }

    public void LogCommand(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return;
        }

        WriteToLog($"[{Now()}] COMMAND: {command}{Environment.NewLine}{Environment.NewLine}");
    }

    /// <summary>Writes a full session-statistics block, including failures and latency breakdowns.</summary>
    public void LogSessionStats(SessionStatistics stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        StringBuilder entry = new();
        entry.AppendLine($"[{Now()}] SESSION STATISTICS");
        entry.AppendLine(Rule);
        entry.AppendLine($"Attempts: {stats.TotalAttempts} (success {stats.SuccessCount}, failed {stats.FailureCount}, cancelled {stats.CancelledCount}, empty {stats.EmptyCount})");
        entry.AppendLine($"Success Rate: {stats.SuccessRate:P0}");
        if (stats.AverageSuccessTime is { } avg)
        {
            entry.AppendLine($"Response Time (success): avg {FormatElapsedTime(avg)}, fastest {FormatElapsedTime(stats.FastestSuccessTime!.Value)}, slowest {FormatElapsedTime(stats.SlowestSuccessTime!.Value)}");
        }
        if (stats.AverageTimeToFirstToken is { } ttft)
        {
            entry.AppendLine($"Avg Time to First Token: {FormatElapsedTime(ttft)}");
        }
        if (stats.FailureCount > 0 && stats.AverageFailureTime is { } aft)
        {
            entry.AppendLine($"Avg Time to Failure: {FormatElapsedTime(aft)}");
        }
        entry.AppendLine($"Total Output: {stats.TotalResponseCharacters:N0} characters");
        entry.AppendLine($"Session Duration: {FormatElapsedTime(stats.SessionDuration)}");

        if (stats.FailureCategories.Count > 0)
        {
            entry.AppendLine();
            entry.AppendLine("Failures by type:");
            foreach (KeyValuePair<string, int> failure in stats.FailureCategories.OrderByDescending(x => x.Value))
            {
                entry.AppendLine($"  - {failure.Key}: {failure.Value}");
            }
        }

        entry.AppendLine();
        entry.AppendLine("Model usage:");
        foreach (ModelUsage usage in stats.ModelUsage)
        {
            string avgStr = usage.AverageSuccessTime is { } t ? $", avg {FormatElapsedTime(t)}" : string.Empty;
            entry.AppendLine($"  - {usage.Model}: {usage.Attempts} attempt(s), {usage.Successes} ok{avgStr}");
        }

        entry.AppendLine(Rule);
        entry.AppendLine();
        WriteToLog(entry.ToString());
    }

    public string GetLogFilePath() => _sessionLogPath;

    public string GetLogDirectory() => _logDirectory;

    // ---- Internals ----------------------------------------------------------------------------

    private void WriteSessionHeader()
    {
        StringBuilder header = new();
        header.AppendLine("════════════════════════════════════════════════════════════");
        header.AppendLine("           GEMINI CONVERSATION LOG");
        header.AppendLine("════════════════════════════════════════════════════════════");
        header.AppendLine($"Session Started: {Now()}");
        header.AppendLine($"Log File: {_sessionLogPath}");
        header.AppendLine("════════════════════════════════════════════════════════════");
        header.AppendLine();

        lock (_writeLock)
        {
            _logWriter.Write(header.ToString());
        }
    }

    private void WriteToLog(string content)
    {
        lock (_writeLock)
        {
            if (_disposed)
            {
                return; // logging after disposal is a no-op rather than an exception
            }

            try
            {
                _logWriter.Write(content);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string FormatElapsedTime(TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < 1000)
        {
            return $"{elapsed.TotalMilliseconds:F0}ms";
        }
        if (elapsed.TotalSeconds < 60)
        {
            return $"{elapsed.TotalSeconds:F2}s";
        }
        if (elapsed.TotalMinutes < 60)
        {
            return $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        }

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}s";
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _logWriter.WriteLine();
                _logWriter.WriteLine("════════════════════════════════════════════════════════════");
                _logWriter.WriteLine($"Session Ended: {Now()}");
                _logWriter.WriteLine("════════════════════════════════════════════════════════════");
                _logWriter.Flush();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error writing session footer: {ex.Message}");
            }
            finally
            {
                _logWriter.Dispose();
                _disposed = true;
            }
        }

        GC.SuppressFinalize(this);
    }
}
