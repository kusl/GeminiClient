// GeminiClientConsole/AppRunner.cs
using System.Diagnostics;
using System.Text;
using GeminiClient;
using GeminiClient.Models;
using Microsoft.Extensions.Logging;

namespace GeminiClientConsole;

public class AppRunner : IDisposable
{
    private readonly IGeminiApiClient _geminiClient;
    private readonly ILogger<AppRunner> _logger;
    private readonly ConsoleModelSelector _modelSelector;
    private readonly ConversationLogger _conversationLogger;

    // State Management
    private string? _selectedModel;
    private readonly List<ResponseMetrics> _sessionMetrics = [];
    private readonly List<Content> _chatHistory = []; // Main conversation state

    private bool _streamingEnabled = true;
    private bool _disposed;

    public AppRunner(
        IGeminiApiClient geminiClient,
        ILogger<AppRunner> logger,
        ConsoleModelSelector modelSelector,
        ConversationLogger conversationLogger)
    {
        _geminiClient = geminiClient;
        _logger = logger;
        _modelSelector = modelSelector;
        _conversationLogger = conversationLogger;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Application starting...");

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"📝 Conversation log: {_conversationLogger.GetLogFilePath()}");
        Console.ResetColor();
        Console.WriteLine();

        // Select model at startup
        _selectedModel = await _modelSelector.SelectModelInteractivelyAsync();

        while (true)
        {
            Console.WriteLine($"\n📝 Enter prompt ('exit', 'reset', 'model', 'stats', 'log', 'stream' [{(_streamingEnabled ? "ON" : "OFF")}]):");

            // Visual indicator for context depth
            if (_chatHistory.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"   (Context: {_chatHistory.Count / 2} turns)");
                Console.ResetColor();
            }

            Console.Write("> ");
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠ Prompt cannot be empty");
                Console.ResetColor();
                continue;
            }

            // Commands
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                _conversationLogger.LogCommand("exit");
                DisplaySessionSummary();
                Console.WriteLine("\nGoodbye! 👋");
                break;
            }

            if (input.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                _conversationLogger.LogCommand("reset");
                _chatHistory.Clear();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✨ Conversation context cleared. Starting fresh.");
                Console.ResetColor();
                continue;
            }

            if (input.Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                _conversationLogger.LogCommand("model");
                _selectedModel = await _modelSelector.SelectModelInteractivelyAsync();
                continue;
            }

            if (input.Equals("stats", StringComparison.OrdinalIgnoreCase))
            {
                _conversationLogger.LogCommand("stats");
                DisplaySessionSummary();
                continue;
            }

            if (input.Equals("log", StringComparison.OrdinalIgnoreCase))
            {
                _conversationLogger.LogCommand("log");
                OpenLogFolder();
                continue;
            }

            if (input.Equals("stream", StringComparison.OrdinalIgnoreCase))
            {
                _streamingEnabled = !_streamingEnabled;
                _conversationLogger.LogCommand($"stream ({(_streamingEnabled ? "enabled" : "disabled")})");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Streaming {(_streamingEnabled ? "enabled" : "disabled")}");
                Console.ResetColor();
                continue;
            }

            // Processing
            // 1. Add User Prompt to History
            _chatHistory.Add(new Content
            {
                Role = "user",
                Parts = [new Part { Text = input }]
            });

            if (_streamingEnabled)
            {
                await ProcessPromptStreamingAsync(input);
            }
            else
            {
                await ProcessPromptAsync(input);
            }
        }

        _logger.LogInformation("Application finished");
    }

    private async Task ProcessPromptStreamingAsync(string prompt)
    {
        _conversationLogger.LogPrompt(prompt, _selectedModel!, isStreaming: true);

        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n╭─── Streaming Response ───╮");
            Console.ResetColor();

            var totalTimer = Stopwatch.StartNew();
            var responseBuilder = new StringBuilder();
            bool firstChunkReceived = false;

            // Pass full history here instead of just 'prompt'
            await foreach (string chunk in _geminiClient.StreamGenerateContentAsync(_selectedModel!, _chatHistory))
            {
                if (!firstChunkReceived)
                {
                    firstChunkReceived = true;
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.WriteLine($"⚡ First response: {totalTimer.ElapsedMilliseconds}ms");
                    Console.ResetColor();
                    Console.WriteLine();
                }

                Console.Write(chunk);
                responseBuilder.Append(chunk);
            }

            totalTimer.Stop();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╰────────────────╯");
            Console.ResetColor();

            string completeResponse = responseBuilder.ToString();

            // 2. Add Model Response to History
            _chatHistory.Add(new Content
            {
                Role = "model",
                Parts = [new Part { Text = completeResponse }]
            });

            // Log response
            _conversationLogger.LogResponse(completeResponse, totalTimer.Elapsed, _selectedModel!);

            var metrics = new ResponseMetrics
            {
                Model = _selectedModel!,
                PromptLength = prompt.Length,
                ResponseLength = completeResponse.Length,
                ElapsedTime = totalTimer.Elapsed,
                Timestamp = DateTime.Now
            };

            _sessionMetrics.Add(metrics);
            DisplayStreamingMetrics(metrics, completeResponse);
        }
        catch (Exception ex)
        {
            // If request fails, remove the last user prompt so the history doesn't get out of sync/corrupted
            if (_chatHistory.Count > 0 && _chatHistory.Last().Role == "user")
            {
                _chatHistory.RemoveAt(_chatHistory.Count - 1);
            }

            _conversationLogger.LogError(ex, _selectedModel!, prompt);
            HandleException(ex);
        }
    }

    private async Task ProcessPromptAsync(string prompt)
    {
        _conversationLogger.LogPrompt(prompt, _selectedModel!, isStreaming: false);
        Task? animationTask = null;
        try
        {
            animationTask = ShowProgressAnimation();
            var totalTimer = Stopwatch.StartNew();

            // Pass full history here
            string? result = await _geminiClient.GenerateContentAsync(_selectedModel!, _chatHistory);

            totalTimer.Stop();
            _isAnimating = false;
            if (animationTask != null) await animationTask;
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");

            if (result != null)
            {
                // 2. Add Model Response to History
                _chatHistory.Add(new Content
                {
                    Role = "model",
                    Parts = [new Part { Text = result }]
                });

                _conversationLogger.LogResponse(result, totalTimer.Elapsed, _selectedModel!);
                var metrics = new ResponseMetrics
                {
                    Model = _selectedModel!,
                    PromptLength = prompt.Length,
                    ResponseLength = result.Length,
                    ElapsedTime = totalTimer.Elapsed,
                    Timestamp = DateTime.Now
                };
                _sessionMetrics.Add(metrics);

                DisplayResponse(result, metrics);
            }
            else
            {
                // Rollback history on empty response
                if (_chatHistory.Count > 0) _chatHistory.RemoveAt(_chatHistory.Count - 1);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ No response received (took {FormatElapsedTime(totalTimer.Elapsed)})");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            // Rollback history on error
            if (_chatHistory.Count > 0) _chatHistory.RemoveAt(_chatHistory.Count - 1);

            _conversationLogger.LogError(ex, _selectedModel!, prompt);
            _isAnimating = false;
            if (animationTask != null) await animationTask;
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
            HandleException(ex);
        }
    }

    private void HandleException(Exception ex)
    {
        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.Message.Contains("500"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Server Error: The model '{_selectedModel}' is experiencing issues.");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"💡 Tip: Try switching to a different model using the 'model' command.");
                Console.ResetColor();
                _logger.LogError(httpEx, "Server error from Gemini API");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Network Error: {httpEx.Message}");
                Console.ResetColor();
                _logger.LogError(httpEx, "HTTP error during content generation");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ Unexpected Error: {ex.Message}");
            Console.ResetColor();
            _logger.LogError(ex, "Error during content generation");
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            string logDirectory = _conversationLogger.GetLogDirectory();
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", logDirectory);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", logDirectory);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", logDirectory);
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Opened log folder: {logDirectory}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠ Could not open folder: {ex.Message}");
            Console.WriteLine($"📁 Log location: {_conversationLogger.GetLogDirectory()}");
            Console.ResetColor();
        }
    }

    private bool _isAnimating = false;
    private async Task ShowProgressAnimation()
    {
        _isAnimating = true;
        string[] spinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
        int spinnerIndex = 0;
        DateTime startTime = DateTime.Now;

        while (_isAnimating)
        {
            TimeSpan elapsed = DateTime.Now - startTime;
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"\r{spinner[spinnerIndex]} Generating response... [{elapsed:mm\\:ss\\.ff}]");
            Console.ResetColor();
            spinnerIndex = (spinnerIndex + 1) % spinner.Length;
            await Task.Delay(100);
        }
    }

    private void DisplayResponse(string response, ResponseMetrics metrics)
    {
        int wordCount = response.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        double tokensPerSecond = EstimateTokens(response) / Math.Max(metrics.ElapsedTime.TotalSeconds, 0.001);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n╭─── Response ─── ⏱ {FormatElapsedTime(metrics.ElapsedTime)} ───╮");
        Console.ResetColor();

        Console.WriteLine(response);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╰────────────────╯");
        Console.ResetColor();

        DisplayMetrics(metrics, wordCount, tokensPerSecond);
    }

    private void DisplayStreamingMetrics(ResponseMetrics metrics, string response)
    {
        int wordCount = response.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        double tokensPerSecond = EstimateTokens(response) / Math.Max(metrics.ElapsedTime.TotalSeconds, 0.001);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"📊 Streaming Performance Metrics:");

        string speedBar = CreateSpeedBar(tokensPerSecond);
        Console.WriteLine($"   └─ Total Time: {FormatElapsedTime(metrics.ElapsedTime)}");
        Console.WriteLine($"   └─ Words: {wordCount} | Characters: {metrics.ResponseLength:N0}");
        Console.WriteLine($"   └─ Est. Tokens: ~{EstimateTokens(metrics.ResponseLength)} | Speed: {tokensPerSecond:F1} tokens/s {speedBar}");
        Console.WriteLine($"   └─ Mode: 🌊 Streaming (real-time)");
        if (_sessionMetrics.Count > 1)
        {
            var avgTime = TimeSpan.FromMilliseconds(_sessionMetrics.Average(m => m.ElapsedTime.TotalMilliseconds));
            string comparison = metrics.ElapsedTime < avgTime ? "🟢 faster" : "🔴 slower";
            Console.WriteLine($"   └─ Session Avg: {FormatElapsedTime(avgTime)} ({comparison})");
        }

        Console.ResetColor();
    }

    private void DisplayMetrics(ResponseMetrics metrics, int wordCount, double tokensPerSecond)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"📊 Performance Metrics:");

        string speedBar = CreateSpeedBar(tokensPerSecond);
        Console.WriteLine($"   └─ Response Time: {FormatElapsedTime(metrics.ElapsedTime)}");
        Console.WriteLine($"   └─ Words: {wordCount} | Characters: {metrics.ResponseLength:N0}");
        Console.WriteLine($"   └─ Est. Tokens: ~{EstimateTokens(metrics.ResponseLength)} | Speed: {tokensPerSecond:F1} tokens/s {speedBar}");
        if (_sessionMetrics.Count > 1)
        {
            var avgTime = TimeSpan.FromMilliseconds(_sessionMetrics.Average(m => m.ElapsedTime.TotalMilliseconds));
            string comparison = metrics.ElapsedTime < avgTime ? "🟢 faster" : "🔴 slower";
            Console.WriteLine($"   └─ Session Avg: {FormatElapsedTime(avgTime)} ({comparison})");
        }

        Console.ResetColor();
    }

    private static string CreateSpeedBar(double tokensPerSecond)
    {
        int barLength = Math.Min((int)(tokensPerSecond / 10), 10);
        string bar = new string('█', barLength) + new string('░', 10 - barLength);
        string speedRating = tokensPerSecond switch
        {
            < 10 => "🐌",
            < 30 => "🚶",
            < 50 => "🏃",
            < 100 => "🚀",
            _ => "⚡"
        };
        return $"[{bar}] {speedRating}";
    }

    private void DisplaySessionSummary()
    {
        if (_sessionMetrics.Count == 0)
        {
            Console.WriteLine("\n📈 No requests made yet in this session.");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n╔═══ Session Statistics ═══╗");
        Console.ResetColor();
        int totalRequests = _sessionMetrics.Count;
        var avgResponseTime = TimeSpan.FromMilliseconds(_sessionMetrics.Average(m => m.ElapsedTime.TotalMilliseconds));
        TimeSpan minResponseTime = _sessionMetrics.Min(m => m.ElapsedTime);
        TimeSpan maxResponseTime = _sessionMetrics.Max(m => m.ElapsedTime);
        int totalChars = _sessionMetrics.Sum(m => m.ResponseLength);
        TimeSpan sessionDuration = DateTime.Now - _sessionMetrics.First().Timestamp;

        Console.WriteLine($"  📊 Total Requests: {totalRequests}");
        Console.WriteLine($"  ⏱  Average Response: {FormatElapsedTime(avgResponseTime)}");
        Console.WriteLine($"  🚀 Fastest: {FormatElapsedTime(minResponseTime)}");
        Console.WriteLine($"  🐌 Slowest: {FormatElapsedTime(maxResponseTime)}");
        Console.WriteLine($"  📝 Total Output: {totalChars:N0} characters");
        Console.WriteLine($"  ⏰ Session Duration: {FormatElapsedTime(sessionDuration)}");
        Console.WriteLine($"  🌊 Streaming: {(_streamingEnabled ? "Enabled" : "Disabled")}");
        Console.WriteLine($"  💭 Context Depth: {_chatHistory.Count / 2} turns");

        var modelUsage = _sessionMetrics.GroupBy(m => m.Model)
            .Select(g => new { Model = g.Key, Count = g.Count(), AvgTime = g.Average(m => m.ElapsedTime.TotalSeconds) })
            .OrderByDescending(m => m.Count);
        Console.WriteLine("\n  🤖 Models Used:");
        foreach (var usage in modelUsage)
        {
            Console.WriteLine($"     └─ {usage.Model}: {usage.Count} requests (avg {usage.AvgTime:F2}s)");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╚════════════════════════╝");
        Console.ResetColor();

        var modelUsageDict = modelUsage.ToDictionary(m => m.Model, m => m.Count);
        _conversationLogger.LogSessionStats(totalRequests, avgResponseTime, sessionDuration, modelUsageDict);
    }

    private static string FormatElapsedTime(TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < 1000)
            return $"{elapsed.TotalMilliseconds:F0}ms";
        else if (elapsed.TotalSeconds < 60)
            return $"{elapsed.TotalSeconds:F2}s";
        else
            return $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
    }

    private static int EstimateTokens(string text) => text.Length / 4;
    private static int EstimateTokens(int charCount) => charCount / 4;

    public void Dispose()
    {
        if (!_disposed)
        {
            _conversationLogger?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private class ResponseMetrics
    {
        public string Model { get; set; } = string.Empty;
        public int PromptLength { get; set; }
        public int ResponseLength { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
