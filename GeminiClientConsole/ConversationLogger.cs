// GeminiClientConsole/ConversationLogger.cs
using System.Text;

namespace GeminiClientConsole;

/// <summary>
/// Handles logging of all prompts, responses, and errors to text files.
/// Thread-safe implementation with proper resource management.
/// </summary>
public class ConversationLogger : IDisposable
{
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
            _logWriter = new StreamWriter(_sessionLogPath, append: true, Encoding.UTF8)
            {
                AutoFlush = true
            };
            WriteSessionHeader();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create log file: {_sessionLogPath}", ex);
        }
    }

    private void WriteSessionHeader()
    {
        var header = new StringBuilder();
        header.AppendLine("════════════════════════════════════════════════════════════");
        header.AppendLine("           GEMINI CONVERSATION LOG");
        header.AppendLine("════════════════════════════════════════════════════════════");
        header.AppendLine($"Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        header.AppendLine($"Log File: {_sessionLogPath}");
        header.AppendLine("════════════════════════════════════════════════════════════");
        header.AppendLine();

        lock (_writeLock)
        {
            _logWriter.Write(header.ToString());
        }
    }

    private static string GetDefaultLogDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GeminiClient",
                "logs");
        }
        else if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(
                home,
                "Library",
                "Application Support",
                "GeminiClient",
                "logs");
        }
        else // Linux / Unix - XDG Compliance
        {
            string? xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

            if (string.IsNullOrWhiteSpace(xdgDataHome))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                xdgDataHome = Path.Combine(home, ".local", "share");
            }

            return Path.Combine(xdgDataHome, "gemini-client", "logs");
        }
    }

    public void LogPrompt(string prompt, string modelName, bool isStreaming)
    {
        if (string.IsNullOrEmpty(prompt) || string.IsNullOrEmpty(modelName))
        {
            return;
        }

        var entry = new StringBuilder();
        entry.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] PROMPT");
        entry.AppendLine($"Model: {modelName}");
        entry.AppendLine($"Mode: {(isStreaming ? "Streaming" : "Standard")}");
        entry.AppendLine("────────────────────────────────────────────────────────────");
        entry.AppendLine(prompt);
        entry.AppendLine("────────────────────────────────────────────────────────────");
        entry.AppendLine();

        WriteToLog(entry.ToString());
    }

    public void LogResponse(string response, TimeSpan elapsedTime, string modelName)
    {
        if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(modelName))
        {
            return;
        }

        var entry = new StringBuilder();
        entry.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RESPONSE");
        entry.AppendLine($"Model: {modelName}");
        entry.AppendLine($"Elapsed Time: {FormatElapsedTime(elapsedTime)}");
        entry.AppendLine($"Characters: {response.Length:N0}");
        entry.AppendLine($"Words: {response.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length:N0}");
        entry.AppendLine("────────────────────────────────────────────────────────────");
        entry.AppendLine(response);
        entry.AppendLine("────────────────────────────────────────────────────────────");
        entry.AppendLine();

        WriteToLog(entry.ToString());
    }

    public void LogError(Exception exception, string modelName, string? prompt = null)
    {
        if (exception == null || string.IsNullOrEmpty(modelName))
        {
            return;
        }

        var entry = new StringBuilder();
        entry.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR");
        entry.AppendLine($"Model: {modelName}");
        entry.AppendLine($"Error Type: {exception.GetType().Name}");
        entry.AppendLine($"Error Message: {exception.Message}");

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            entry.AppendLine("Original Prompt:");
            entry.AppendLine(prompt);
        }

        if (exception.InnerException != null)
        {
            entry.AppendLine($"Inner Exception: {exception.InnerException.Message}");
        }

        entry.AppendLine("Stack Trace:");
        entry.AppendLine(exception.StackTrace);
        entry.AppendLine("────────────────────────────────────────────────────────────");
        entry.AppendLine();

        WriteToLog(entry.ToString());
    }

    public void LogCommand(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return;
        }

        var entry = new StringBuilder();
        entry.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] COMMAND: {command}");
        entry.AppendLine();

        WriteToLog(entry.ToString());
    }

    public void LogSessionStats(int totalRequests, TimeSpan avgResponseTime,
        TimeSpan sessionDuration, Dictionary<string, int> modelUsage)
    {
        modelUsage ??= [];

        var entry = new StringBuilder();
        entry.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SESSION STATISTICS");
        entry.AppendLine("────────────────────────────────────────────────────────────");
        entry.AppendLine($"Total Requests: {totalRequests}");
        entry.AppendLine($"Average Response Time: {FormatElapsedTime(avgResponseTime)}");
        entry.AppendLine($"Session Duration: {FormatElapsedTime(sessionDuration)}");
        entry.AppendLine();
        entry.AppendLine("Model Usage:");
        foreach (var kvp in modelUsage.OrderByDescending(x => x.Value))
        {
            entry.AppendLine($"  - {kvp.Key}: {kvp.Value} requests");
        }
        entry.AppendLine("────────────────────────────────────────────────────────────");
        entry.AppendLine();

        WriteToLog(entry.ToString());
    }

    private void WriteToLog(string content)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConversationLogger));
        }

        lock (_writeLock)
        {
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

    public string GetLogFilePath() => _sessionLogPath;

    public string GetLogDirectory() => _logDirectory;

    private static string FormatElapsedTime(TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < 1000)
            return $"{elapsed.TotalMilliseconds:F0}ms";
        else if (elapsed.TotalSeconds < 60)
            return $"{elapsed.TotalSeconds:F2}s";
        else if (elapsed.TotalMinutes < 60)
            return $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        else
            return $"{elapsed.Hours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}s";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_writeLock)
            {
                try
                {
                    _logWriter.WriteLine();
                    _logWriter.WriteLine("════════════════════════════════════════════════════════════");
                    _logWriter.WriteLine($"Session Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
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
                }
            }

            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
