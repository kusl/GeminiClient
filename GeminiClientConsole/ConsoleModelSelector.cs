// GeminiClientConsole/ConsoleModelSelector.cs
using GeminiClient;
using GeminiClient.Models;
using Microsoft.Extensions.Logging;

namespace GeminiClientConsole;

/// <summary>
/// Presents the available models and lets the user pick one. Ordering and the default suggestion
/// come from <see cref="GeminiModelRanking"/> so stable, general-purpose text models are preferred
/// and preview/specialised variants are clearly labelled and never chosen by default.
/// </summary>
public class ConsoleModelSelector
{
    private const string FallbackDefault = "gemini-2.5-flash";

    private readonly IModelService _modelService;
    private readonly ILogger<ConsoleModelSelector> _logger;
    private IReadOnlyList<GeminiModel> _cachedModels = [];

    public ConsoleModelSelector(IModelService modelService, ILogger<ConsoleModelSelector> logger)
    {
        _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SelectModelInteractivelyAsync(CancellationToken cancellationToken = default)
    {
        await LoadModelsWithSpinnerAsync(cancellationToken);

        string defaultName = GeminiModelRanking.RecommendDefault(_cachedModels) ?? FallbackDefault;

        Console.WriteLine();
        ConsoleSafe.WriteLineColored("🤖 Available Gemini models:", ConsoleColor.White);
        ConsoleSafe.WriteLineColored("═══════════════════════════", ConsoleColor.DarkGray);

        for (int i = 0; i < _cachedModels.Count; i++)
        {
            GeminiModel model = _cachedModels[i];
            string id = model.GetModelIdentifier();
            string description = model.Description ?? model.DisplayName ?? "Google Gemini model";
            if (description.Length > 60)
            {
                description = description[..57] + "...";
            }

            string tag = GeminiModelRanking.IsSpecialised(id)
                ? " (specialised)"
                : GeminiModelRanking.IsUnstable(id) ? " (preview)" : string.Empty;

            ConsoleSafe.WriteColored($"  [{i + 1}] ", ConsoleColor.Cyan);
            ConsoleSafe.WriteColored(id, ConsoleColor.White);
            ConsoleSafe.WriteLineColored($"{tag} — {description}", ConsoleColor.DarkGray);
        }

        if (_cachedModels.Count == 0)
        {
            ConsoleSafe.WriteLineColored($"  (no models returned; will use {defaultName})", ConsoleColor.DarkGray);
            return defaultName;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine();
            ConsoleSafe.WriteColored(
                $"Select a model (1-{_cachedModels.Count}) or press Enter for default [{defaultName}]: ",
                ConsoleColor.Yellow);

            string? input = await ReadLineWithTimeoutAsync(TimeSpan.FromMinutes(5), cancellationToken);

            if (string.IsNullOrWhiteSpace(input))
            {
                _logger.LogInformation("Model selected: {Model} (default)", defaultName);
                return defaultName;
            }

            if (int.TryParse(input.Trim(), out int selection) && selection >= 1 && selection <= _cachedModels.Count)
            {
                string selected = _cachedModels[selection - 1].GetModelIdentifier();
                _logger.LogInformation("Model selected: {Model}", selected);
                return selected;
            }

            ConsoleSafe.WriteLineColored(
                $"❌ Invalid selection. Choose a number between 1 and {_cachedModels.Count}.", ConsoleColor.Red);
        }

        return defaultName;
    }

    private async Task LoadModelsWithSpinnerAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource spinnerCts = new();
        Task spinner = ShowLoadingSpinnerAsync(spinnerCts.Token);

        try
        {
            await RefreshModelCacheAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh model list; using fallback list");
        }
        finally
        {
            spinnerCts.Cancel();
            try
            {
                await spinner;
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            ConsoleSafe.ClearLine();
        }
    }

    private async Task RefreshModelCacheAsync(CancellationToken cancellationToken)
    {
        if (_cachedModels.Count > 0)
        {
            return;
        }

        List<GeminiModel> models = [];
        try
        {
            IReadOnlyList<GeminiModel> fetched =
                await _modelService.GetModelsByCapabilityAsync(ModelCapability.TextGeneration, cancellationToken);
            models = [.. fetched];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch models from API. Using fallback list.");
        }

        if (models.Count == 0)
        {
            models =
            [
                new GeminiModel { Name = "models/gemini-2.5-flash", DisplayName = "Gemini 2.5 Flash", Description = "Fast and efficient (fallback)" },
                new GeminiModel { Name = "models/gemini-2.0-flash", DisplayName = "Gemini 2.0 Flash", Description = "Balanced performance (fallback)" },
                new GeminiModel { Name = "models/gemini-1.5-pro", DisplayName = "Gemini 1.5 Pro", Description = "High capability (fallback)" }
            ];
        }

        _cachedModels = GeminiModelRanking.ForInteractiveChat(models);
    }

    private static async Task ShowLoadingSpinnerAsync(CancellationToken token)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        string[] frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
        int i = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                ConsoleSafe.SetColor(ConsoleColor.DarkCyan);
                Console.Write($"\r{frames[i]} Fetching available models…");
                ConsoleSafe.ResetColor();
                i = (i + 1) % frames.Length;
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task<string?> readTask = Task.Run(Console.ReadLine);
        Task timeoutTask = Task.Delay(timeout, cancellationToken);

        Task completed = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);
        if (completed != readTask)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ConsoleSafe.WriteLineColored("\n⏰ Selection timed out — using default model.", ConsoleColor.Yellow);
            }
            return null;
        }

        return await readTask.ConfigureAwait(false);
    }
}
