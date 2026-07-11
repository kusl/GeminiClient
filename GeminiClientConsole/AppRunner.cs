// GeminiClientConsole/AppRunner.cs
using System.Diagnostics;
using System.Text;
using GeminiClient;
using GeminiClient.Models;
using Microsoft.Extensions.Logging;

namespace GeminiClientConsole;

/// <summary>
/// Drives the interactive REPL: reads prompts, dispatches commands, streams/awaits responses,
/// records per-request telemetry (including failures and cancellations), and prints a session
/// summary on exit. Ctrl+C cancels an in-flight request without killing the process; pressing it
/// again at the prompt exits cleanly with a summary.
/// </summary>
public sealed class AppRunner : IDisposable
{
    private readonly IGeminiApiClient _geminiClient;
    private readonly ILogger<AppRunner> _logger;
    private readonly ConsoleModelSelector _modelSelector;
    private readonly ConversationLogger _conversationLogger;

    private readonly SessionStatistics _stats = new();
    private readonly List<Content> _chatHistory = [];
    private readonly CancellationTokenSource _appCts = new();

    private CancellationTokenSource? _requestCts;
    private volatile bool _requestInFlight;
    private bool _streamingEnabled = true;
    private bool _disposed;
    private string _selectedModel = "gemini-2.5-flash";

    public AppRunner(
        IGeminiApiClient geminiClient,
        ILogger<AppRunner> logger,
        ConsoleModelSelector modelSelector,
        ConversationLogger conversationLogger)
    {
        _geminiClient = geminiClient ?? throw new ArgumentNullException(nameof(geminiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _modelSelector = modelSelector ?? throw new ArgumentNullException(nameof(modelSelector));
        _conversationLogger = conversationLogger ?? throw new ArgumentNullException(nameof(conversationLogger));
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Application starting");
        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            PrintBanner();
            _selectedModel = await _modelSelector.SelectModelInteractivelyAsync(_appCts.Token);
            ConsoleSafe.WriteLineColored($"\n✓ Using model: {_selectedModel}", ConsoleColor.Green);

            while (!_appCts.IsCancellationRequested)
            {
                PrintPrompt();

                string? input = await ReadInputAsync(_appCts.Token);
                if (input is null)
                {
                    break; // Ctrl+C while idle, or end-of-input (piped stdin)
                }

                input = input.Trim();
                if (input.Length == 0)
                {
                    ConsoleSafe.WriteLineColored("⚠ Prompt cannot be empty. Type a message or 'help'.", ConsoleColor.Yellow);
                    continue;
                }

                if (await HandleCommandAsync(input, out bool exit))
                {
                    if (exit)
                    {
                        break;
                    }
                    continue;
                }

                _chatHistory.Add(new Content { Role = "user", Parts = [new Part { Text = input }] });

                if (_streamingEnabled)
                {
                    await ProcessPromptStreamingAsync(input);
                }
                else
                {
                    await ProcessPromptAsync(input);
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            DisplaySessionSummary();
            Console.WriteLine("\nGoodbye! 👋");
            _logger.LogInformation("Application finished");
        }
    }

    // ---- Ctrl+C handling ----------------------------------------------------------------------

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Never allow Ctrl+C to hard-terminate the process; we always shut down gracefully so the
        // session summary is written and the conversation log is closed properly.
        e.Cancel = true;

        if (_requestInFlight && _requestCts is not null)
        {
            ConsoleSafe.WriteLineColored(
                "\n⏹ Cancelling current request… (press Ctrl+C again to exit)", ConsoleColor.Yellow);
            TryCancel(_requestCts);
        }
        else
        {
            ConsoleSafe.WriteLineColored("\n⏹ Shutting down…", ConsoleColor.Yellow);
            TryCancel(_appCts);
        }
    }

    private static void TryCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already gone; nothing to do
        }
    }

    private static async Task<string?> ReadInputAsync(CancellationToken cancellationToken)
    {
        Task<string?> readTask = Task.Run(Console.ReadLine);
        Task completed = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        return completed == readTask ? await readTask.ConfigureAwait(false) : null;
    }

    // ---- Commands -----------------------------------------------------------------------------

    private async Task<bool> HandleCommandAsync(string input, out bool exit)
    {
        exit = false;
        switch (input.ToLowerInvariant())
        {
            case "exit":
            case "quit":
                _conversationLogger.LogCommand("exit");
                exit = true;
                return true;

            case "reset":
                _conversationLogger.LogCommand("reset");
                _chatHistory.Clear();
                ConsoleSafe.WriteLineColored("✨ Conversation context cleared. Starting fresh.", ConsoleColor.Green);
                return true;

            case "model":
                _conversationLogger.LogCommand("model");
                _selectedModel = await _modelSelector.SelectModelInteractivelyAsync(_appCts.Token);
                ConsoleSafe.WriteLineColored($"✓ Using model: {_selectedModel}", ConsoleColor.Green);
                return true;

            case "stats":
                _conversationLogger.LogCommand("stats");
                DisplaySessionSummary();
                return true;

            case "log":
                _conversationLogger.LogCommand("log");
                OpenLogLocation();
                return true;

            case "stream":
                _streamingEnabled = !_streamingEnabled;
                _conversationLogger.LogCommand($"stream ({(_streamingEnabled ? "on" : "off")})");
                ConsoleSafe.WriteLineColored(
                    $"✓ Streaming {(_streamingEnabled ? "enabled" : "disabled")}.", ConsoleColor.Green);
                return true;

            case "help":
            case "?":
                PrintHelp();
                return true;

            default:
                return false;
        }
    }

    // ---- Streaming request --------------------------------------------------------------------

    private async Task ProcessPromptStreamingAsync(string prompt)
    {
        _conversationLogger.LogPrompt(prompt, _selectedModel, isStreaming: true);

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_appCts.Token);
        _requestCts = cts;
        _requestInFlight = true;

        Stopwatch timer = Stopwatch.StartNew();
        StringBuilder responseBuilder = new();
        TimeSpan? ttft = null;
        bool anyChunk = false;

        ConsoleSafe.WriteLineColored("\n╭─── Streaming Response ───╮", ConsoleColor.Cyan);

        try
        {
            await foreach (string chunk in _geminiClient.StreamGenerateContentAsync(_selectedModel, _chatHistory, cts.Token))
            {
                if (!anyChunk)
                {
                    anyChunk = true;
                    ttft = timer.Elapsed;
                    ConsoleSafe.WriteLineColored($"⚡ First token in {FormatElapsedTime(ttft.Value)}", ConsoleColor.DarkGreen);
                }

                Console.Write(chunk);
                responseBuilder.Append(chunk);
            }

            timer.Stop();
            EndStreamFrame();

            string completeResponse = responseBuilder.ToString();
            if (!anyChunk || completeResponse.Length == 0)
            {
                RollbackLastUserTurn();
                RecordAndWarnEmpty(prompt, timer.Elapsed, streaming: true);
                return;
            }

            _chatHistory.Add(new Content { Role = "model", Parts = [new Part { Text = completeResponse }] });
            _conversationLogger.LogResponse(completeResponse, timer.Elapsed, _selectedModel, ttft, streaming: true);

            RequestRecord record = new()
            {
                Model = _selectedModel,
                Streaming = true,
                Outcome = RequestOutcome.Success,
                Elapsed = timer.Elapsed,
                TimeToFirstToken = ttft,
                PromptLength = prompt.Length,
                ResponseLength = completeResponse.Length
            };
            _stats.Record(record);
            DisplayMetrics("Streaming Performance", record, completeResponse);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            timer.Stop();
            EndStreamFrame();
            HandleCancellation(prompt, timer.Elapsed, responseBuilder, ttft, streaming: true, anyChunk);
        }
        catch (GeminiApiException ex)
        {
            timer.Stop();
            EndStreamFrame();
            HandleApiFailure(ex, prompt, timer.Elapsed, streaming: true);
        }
        catch (Exception ex)
        {
            timer.Stop();
            EndStreamFrame();
            HandleUnexpectedFailure(ex, prompt, timer.Elapsed, streaming: true);
        }
        finally
        {
            _requestInFlight = false;
            _requestCts = null;
        }
    }

    // ---- Non-streaming request ----------------------------------------------------------------

    private async Task ProcessPromptAsync(string prompt)
    {
        _conversationLogger.LogPrompt(prompt, _selectedModel, isStreaming: false);

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_appCts.Token);
        _requestCts = cts;
        _requestInFlight = true;

        using CancellationTokenSource animationCts = new();
        Task animation = ShowProgressAnimationAsync(animationCts.Token);
        Stopwatch timer = Stopwatch.StartNew();

        try
        {
            string? result = await _geminiClient.GenerateContentAsync(_selectedModel, _chatHistory, cts.Token);
            timer.Stop();
            await StopAnimationAsync(animationCts, animation);

            if (!string.IsNullOrEmpty(result))
            {
                _chatHistory.Add(new Content { Role = "model", Parts = [new Part { Text = result }] });
                _conversationLogger.LogResponse(result, timer.Elapsed, _selectedModel);

                RequestRecord record = new()
                {
                    Model = _selectedModel,
                    Streaming = false,
                    Outcome = RequestOutcome.Success,
                    Elapsed = timer.Elapsed,
                    PromptLength = prompt.Length,
                    ResponseLength = result.Length
                };
                _stats.Record(record);
                DisplayResponse(result, record);
            }
            else
            {
                RollbackLastUserTurn();
                RecordAndWarnEmpty(prompt, timer.Elapsed, streaming: false);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            timer.Stop();
            await StopAnimationAsync(animationCts, animation);
            HandleCancellation(prompt, timer.Elapsed, new StringBuilder(), null, streaming: false, anyChunk: false);
        }
        catch (GeminiApiException ex)
        {
            timer.Stop();
            await StopAnimationAsync(animationCts, animation);
            HandleApiFailure(ex, prompt, timer.Elapsed, streaming: false);
        }
        catch (Exception ex)
        {
            timer.Stop();
            await StopAnimationAsync(animationCts, animation);
            HandleUnexpectedFailure(ex, prompt, timer.Elapsed, streaming: false);
        }
        finally
        {
            _requestInFlight = false;
            _requestCts = null;
        }
    }

    // ---- Outcome handlers (UI-frame agnostic) -------------------------------------------------

    private void HandleCancellation(string prompt, TimeSpan elapsed, StringBuilder partial, TimeSpan? ttft, bool streaming, bool anyChunk)
    {
        if (streaming && anyChunk && partial.Length > 0)
        {
            // Preserve what the user already saw so follow-up turns keep context.
            _chatHistory.Add(new Content { Role = "model", Parts = [new Part { Text = partial.ToString() }] });
        }
        else
        {
            RollbackLastUserTurn();
        }

        ConsoleSafe.WriteLineColored($"⏹ Request cancelled after {FormatElapsedTime(elapsed)}.", ConsoleColor.Yellow);
        _conversationLogger.LogCancellation(_selectedModel, prompt, elapsed);

        _stats.Record(new RequestRecord
        {
            Model = _selectedModel,
            Streaming = streaming,
            Outcome = RequestOutcome.Cancelled,
            Elapsed = elapsed,
            TimeToFirstToken = ttft,
            PromptLength = prompt.Length,
            ResponseLength = partial.Length
        });
    }

    private void HandleApiFailure(GeminiApiException ex, string prompt, TimeSpan elapsed, bool streaming)
    {
        RollbackLastUserTurn();

        string icon = ex.Kind switch
        {
            GeminiErrorKind.RateLimited => "⏳",
            GeminiErrorKind.ServiceUnavailable => "🟠",
            GeminiErrorKind.Unauthorized => "🔑",
            GeminiErrorKind.ContentBlocked => "🚫",
            GeminiErrorKind.Timeout => "⌛",
            GeminiErrorKind.Network => "📡",
            GeminiErrorKind.BadRequest => "✋",
            _ => "❌"
        };

        ConsoleSafe.WriteLineColored($"{icon} {ex.Message}", ConsoleColor.Red);

        if (ex.RetryAfter is { } retry
            && ex.Kind is GeminiErrorKind.RateLimited or GeminiErrorKind.ServiceUnavailable
            && !ex.IsQuotaZero)
        {
            ConsoleSafe.WriteLineColored($"   ↻ Suggested wait: ~{Math.Ceiling(retry.TotalSeconds):0}s", ConsoleColor.DarkYellow);
        }

        if (ex.Kind is GeminiErrorKind.RateLimited or GeminiErrorKind.ServiceUnavailable or GeminiErrorKind.Unauthorized)
        {
            ConsoleSafe.WriteLineColored("   💡 Tip: use the 'model' command to switch to a different model.", ConsoleColor.DarkGray);
        }

        _conversationLogger.LogFailure(ex, _selectedModel, prompt, elapsed);
        _logger.LogWarning(ex, "Request to {Model} failed ({Kind})", _selectedModel, ex.Kind);

        _stats.Record(new RequestRecord
        {
            Model = _selectedModel,
            Streaming = streaming,
            Outcome = RequestOutcome.Failed,
            Elapsed = elapsed,
            PromptLength = prompt.Length,
            ResponseLength = 0,
            ErrorCategory = ex.Kind.ToString()
        });
    }

    private void HandleUnexpectedFailure(Exception ex, string prompt, TimeSpan elapsed, bool streaming)
    {
        RollbackLastUserTurn();
        ConsoleSafe.WriteLineColored($"❌ Unexpected error: {ex.Message}", ConsoleColor.Red);
        _conversationLogger.LogFailure(ex, _selectedModel, prompt, elapsed);
        _logger.LogError(ex, "Unexpected error during generation for {Model}", _selectedModel);

        _stats.Record(new RequestRecord
        {
            Model = _selectedModel,
            Streaming = streaming,
            Outcome = RequestOutcome.Failed,
            Elapsed = elapsed,
            PromptLength = prompt.Length,
            ResponseLength = 0,
            ErrorCategory = ex.GetType().Name
        });
    }

    private void RecordAndWarnEmpty(string prompt, TimeSpan elapsed, bool streaming)
    {
        ConsoleSafe.WriteLineColored(
            $"⚠ No content returned (took {FormatElapsedTime(elapsed)}). It may have been empty or filtered.",
            ConsoleColor.Yellow);
        _conversationLogger.LogResponse("(no content returned)", elapsed, _selectedModel, null, streaming);

        _stats.Record(new RequestRecord
        {
            Model = _selectedModel,
            Streaming = streaming,
            Outcome = RequestOutcome.Empty,
            Elapsed = elapsed,
            PromptLength = prompt.Length,
            ResponseLength = 0
        });
    }

    // ---- Display ------------------------------------------------------------------------------

    private static void EndStreamFrame()
    {
        Console.WriteLine();
        ConsoleSafe.WriteLineColored("╰──────────────────────────╯", ConsoleColor.Cyan);
    }

    private void DisplayResponse(string response, RequestRecord record)
    {
        ConsoleSafe.WriteLineColored($"\n╭─── Response ── ⏱ {FormatElapsedTime(record.Elapsed)} ──╮", ConsoleColor.Cyan);
        Console.WriteLine(response);
        ConsoleSafe.WriteLineColored("╰──────────────────────────╯", ConsoleColor.Cyan);
        DisplayMetrics("Performance", record, response);
    }

    private void DisplayMetrics(string title, RequestRecord record, string responseText)
    {
        int wordCount = CountWords(responseText);
        double seconds = Math.Max(record.Elapsed.TotalSeconds, 0.001);
        double tokensPerSecond = EstimateTokens(record.ResponseLength) / seconds;

        ConsoleSafe.SetColor(ConsoleColor.DarkGray);
        Console.WriteLine($"📊 {title}:");
        if (record.Streaming && record.TimeToFirstToken is { } ttft)
        {
            Console.WriteLine($"   └─ Time to first token : {FormatElapsedTime(ttft)}");
        }
        Console.WriteLine($"   └─ Total time          : {FormatElapsedTime(record.Elapsed)}");
        Console.WriteLine($"   └─ Words / characters  : {wordCount} / {record.ResponseLength:N0}");
        Console.WriteLine($"   └─ Est. tokens / speed : ~{EstimateTokens(record.ResponseLength)} / {tokensPerSecond:F1} tok/s {CreateSpeedBar(tokensPerSecond)}");

        if (_stats.SuccessCount > 1 && _stats.AverageSuccessTime is { } avg)
        {
            string comparison = record.Elapsed < avg ? "🟢 faster than avg" : "🔴 slower than avg";
            Console.WriteLine($"   └─ Session avg         : {FormatElapsedTime(avg)} ({comparison})");
        }
        ConsoleSafe.ResetColor();
    }

    private void DisplaySessionSummary()
    {
        if (_stats.TotalAttempts == 0)
        {
            Console.WriteLine("\n📈 No requests were made this session.");
            return;
        }

        ConsoleSafe.WriteLineColored("\n╔══════ Session Statistics ══════╗", ConsoleColor.Cyan);
        Console.WriteLine($"  📊 Attempts: {_stats.TotalAttempts}  (✅ {_stats.SuccessCount}  ❌ {_stats.FailureCount}  ⏹ {_stats.CancelledCount}  ⚠ {_stats.EmptyCount})");
        Console.WriteLine($"  🎯 Success rate: {_stats.SuccessRate:P0}");

        if (_stats.AverageSuccessTime is { } avg)
        {
            Console.WriteLine($"  ⏱  Success time: avg {FormatElapsedTime(avg)} · 🚀 {FormatElapsedTime(_stats.FastestSuccessTime!.Value)} · 🐌 {FormatElapsedTime(_stats.SlowestSuccessTime!.Value)}");
        }
        if (_stats.AverageTimeToFirstToken is { } ttft)
        {
            Console.WriteLine($"  ⚡ Avg time to first token: {FormatElapsedTime(ttft)}");
        }
        if (_stats.FailureCount > 0 && _stats.AverageFailureTime is { } aft)
        {
            Console.WriteLine($"  💥 Avg time to failure: {FormatElapsedTime(aft)}");
        }

        Console.WriteLine($"  📝 Total output: {_stats.TotalResponseCharacters:N0} characters");
        Console.WriteLine($"  ⏰ Session duration: {FormatElapsedTime(_stats.SessionDuration)}");
        Console.WriteLine($"  🌊 Streaming: {(_streamingEnabled ? "enabled" : "disabled")}");
        Console.WriteLine($"  💭 Context depth: {CountTurns()} turn(s)");

        IReadOnlyDictionary<string, int> failures = _stats.FailureCategories;
        if (failures.Count > 0)
        {
            Console.WriteLine("\n  ⚠ Failures by type:");
            foreach (KeyValuePair<string, int> failure in failures.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"     └─ {failure.Key}: {failure.Value}");
            }
        }

        Console.WriteLine("\n  🤖 Models used:");
        foreach (ModelUsage usage in _stats.ModelUsage)
        {
            string avgStr = usage.AverageSuccessTime is { } t ? $", avg {FormatElapsedTime(t)}" : string.Empty;
            Console.WriteLine($"     └─ {usage.Model}: {usage.Attempts} attempt(s), {usage.Successes} ok{avgStr}");
        }

        ConsoleSafe.WriteLineColored("╚════════════════════════════════╝", ConsoleColor.Cyan);
        _conversationLogger.LogSessionStats(_stats);
    }

    // ---- Progress animation -------------------------------------------------------------------

    private static async Task ShowProgressAnimationAsync(CancellationToken token)
    {
        if (Console.IsOutputRedirected)
        {
            return; // don't emit spinner control characters when piped
        }

        string[] frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
        int i = 0;
        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            while (!token.IsCancellationRequested)
            {
                ConsoleSafe.SetColor(ConsoleColor.DarkCyan);
                Console.Write($"\r{frames[i]} Generating… [{sw.Elapsed:mm\\:ss\\.f}]");
                ConsoleSafe.ResetColor();
                i = (i + 1) % frames.Length;
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on completion
        }
    }

    private static async Task StopAnimationAsync(CancellationTokenSource cts, Task animation)
    {
        cts.Cancel();
        try
        {
            await animation.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        ConsoleSafe.ClearLine();
    }

    // ---- Banner / prompt / help ---------------------------------------------------------------

    private void PrintBanner()
    {
        ConsoleSafe.WriteLineColored("╔════════════════════════════════════════╗", ConsoleColor.Cyan);
        ConsoleSafe.WriteLineColored("║        Gemini Client Console           ║", ConsoleColor.Cyan);
        ConsoleSafe.WriteLineColored("╚════════════════════════════════════════╝", ConsoleColor.Cyan);
        ConsoleSafe.WriteLineColored($"📝 Conversation log: {_conversationLogger.GetLogFilePath()}", ConsoleColor.DarkCyan);
    }

    private void PrintPrompt()
    {
        Console.WriteLine();
        ConsoleSafe.WriteLineColored(
            $"Enter a prompt or command (exit · reset · model · stats · log · stream[{(_streamingEnabled ? "ON" : "OFF")}] · help):",
            ConsoleColor.DarkGray);
        if (_chatHistory.Count > 0)
        {
            ConsoleSafe.WriteLineColored($"(context: {CountTurns()} turn(s))", ConsoleColor.DarkGray);
        }
        Console.Write("> ");
    }

    private static void PrintHelp()
    {
        ConsoleSafe.WriteLineColored("\nAvailable commands:", ConsoleColor.Cyan);
        Console.WriteLine("  exit / quit  — end the session (a summary is printed)");
        Console.WriteLine("  reset        — clear the conversation context");
        Console.WriteLine("  model        — choose a different model");
        Console.WriteLine("  stats        — show session statistics so far");
        Console.WriteLine("  log          — open the folder containing the conversation log");
        Console.WriteLine("  stream       — toggle streaming vs. standard responses");
        Console.WriteLine("  help / ?     — show this help");
        Console.WriteLine("Anything else is sent to the model as a prompt.");
        Console.WriteLine("Tip: press Ctrl+C to cancel a running request; again to exit.");
    }

    private void OpenLogLocation()
    {
        string dir = _conversationLogger.GetLogDirectory();
        ConsoleSafe.WriteLineColored($"📁 Log folder: {dir}", ConsoleColor.Cyan);
        ConsoleSafe.WriteLineColored($"📝 Current log: {_conversationLogger.GetLogFilePath()}", ConsoleColor.DarkCyan);

        try
        {
            ProcessStartInfo psi = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }
                : OperatingSystem.IsMacOS()
                    ? new ProcessStartInfo("open", $"\"{dir}\"") { UseShellExecute = false }
                    : new ProcessStartInfo("xdg-open", $"\"{dir}\"") { UseShellExecute = false };
            Process.Start(psi);
        }
        catch (Exception)
        {
            // Opening a file browser is best-effort; the path is already printed above.
        }
    }

    // ---- Small helpers ------------------------------------------------------------------------

    private void RollbackLastUserTurn()
    {
        if (_chatHistory.Count > 0 && _chatHistory[^1].Role == "user")
        {
            _chatHistory.RemoveAt(_chatHistory.Count - 1);
        }
    }

    private int CountTurns() => _chatHistory.Count(c => c.Role == "user");

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static int EstimateTokens(int characterCount) => Math.Max(0, characterCount / 4);

    private static int EstimateTokens(string text) => EstimateTokens(text.Length);

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

    private static string CreateSpeedBar(double tokensPerSecond)
    {
        int bars = Math.Clamp((int)(tokensPerSecond / 10), 0, 10);
        return "[" + new string('█', bars) + new string('░', 10 - bars) + "]";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Only dispose what this instance owns. The ConversationLogger is a container-managed
        // singleton and is disposed by the DI container on host shutdown.
        TryCancel(_appCts);
        _appCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
