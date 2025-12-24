// GeminiClientConsole/ConsoleModelSelector.cs
using GeminiClient;
using GeminiClient.Models;
using Microsoft.Extensions.Logging;

namespace GeminiClientConsole;

public class ConsoleModelSelector
{
    private readonly IModelService _modelService;
    private readonly ILogger<ConsoleModelSelector> _logger;
    private List<GeminiModel> _cachedModels = [];

    public ConsoleModelSelector(IModelService modelService, ILogger<ConsoleModelSelector> logger)
    {
        _modelService = modelService;
        _logger = logger;
    }

    public async Task<string> SelectModelInteractivelyAsync()
    {
        // Show loading animation while fetching model availability
        Task loadingTask = ShowModelLoadingAnimationAsync();

        try
        {
            // Fetch real models from the API
            await RefreshModelCacheAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh model list");
        }
        finally
        {
            _isLoadingModels = false;
            await loadingTask;
            // Clear loading line
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
        }

        Console.WriteLine("🤖 Available Gemini Models:");
        Console.WriteLine("═══════════════════════════");

        // Animate model list display
        for (int i = 0; i < _cachedModels.Count; i++)
        {
            var model = _cachedModels[i];
            var modelName = model.GetModelIdentifier();
            var description = model.Description ?? model.DisplayName ?? "Google Gemini Model";

            // Truncate long descriptions for console display
            if (description.Length > 60) description = description[..57] + "...";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"  [{i + 1}] ");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(modelName);
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" - {description}");
            Console.ResetColor();

            // Small delay for smooth animation
            await Task.Delay(30);
        }

        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            string defaultName = _cachedModels.FirstOrDefault()?.GetModelIdentifier() ?? "gemini-2.5-flash";
            Console.Write($"Select a model (1-{_cachedModels.Count}) or press Enter for default [{defaultName}]: ");
            Console.ResetColor();

            // Use async console reading with timeout
            string? input = await ReadLineWithTimeoutAsync(TimeSpan.FromMinutes(5));

            // Default selection
            if (string.IsNullOrWhiteSpace(input))
            {
                await ShowSelectionConfirmationAsync(defaultName, isDefault: true);
                _logger.LogInformation("Model selected: {Model} (default)", defaultName);
                return defaultName;
            }

            // Parse user input
            if (int.TryParse(input.Trim(), out int selection) &&
                selection >= 1 && selection <= _cachedModels.Count)
            {
                string selectedModel = _cachedModels[selection - 1].GetModelIdentifier();
                await ShowSelectionConfirmationAsync(selectedModel, isDefault: false);
                _logger.LogInformation("Model selected: {Model}", selectedModel);
                return selectedModel;
            }

            // Invalid input
            await ShowErrorMessageAsync($"❌ Invalid selection. Please choose a number between 1 and {_cachedModels.Count}.");
        }
    }

    private async Task RefreshModelCacheAsync()
    {
        if (_cachedModels.Count > 0) return; // Already cached

        try
        {
            // Fetch models capable of content generation
            var models = await _modelService.GetModelsByCapabilityAsync(ModelCapability.TextGeneration);

            // Filter and sort for better UX
            _cachedModels = models
                .Where(m => !string.IsNullOrEmpty(m.Name))
                // Prioritize newer models
                .OrderByDescending(m => m.Name!.Contains("flash"))
                .ThenByDescending(m => m.Name!.Contains("pro"))
                .ThenByDescending(m => m.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch models from API. Using fallback list.");
        }

        // Fallback if API fails or returns nothing
        if (_cachedModels.Count == 0)
        {
            _cachedModels =
            [
                new GeminiModel { Name = "models/gemini-2.5-flash", DisplayName = "Gemini 2.5 Flash", Description = "Fast and efficient (Fallback)" },
                new GeminiModel { Name = "models/gemini-2.0-flash", DisplayName = "Gemini 2.0 Flash", Description = "Balanced performance (Fallback)" },
                new GeminiModel { Name = "models/gemini-1.5-pro", DisplayName = "Gemini 1.5 Pro", Description = "High capability (Fallback)" }
            ];
        }
    }

    private bool _isLoadingModels = false;
    private async Task ShowModelLoadingAnimationAsync()
    {
        _isLoadingModels = true;
        string[] frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
        int frameIndex = 0;

        while (_isLoadingModels)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"\r{frames[frameIndex]} Fetching available models from API...");
            Console.ResetColor();
            frameIndex = (frameIndex + 1) % frames.Length;
            await Task.Delay(100);
        }
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(TimeSpan timeout)
    {
        Task<string?> readTask = Task.Run(() => Console.ReadLine());
        var timeoutTask = Task.Delay(timeout);

        Task completedTask = await Task.WhenAny(readTask, timeoutTask);
        if (completedTask == timeoutTask)
        {
            Console.WriteLine("\n⏰ Selection timeout - using default model.");
            return null;
        }

        return await readTask;
    }

    private static async Task ShowSelectionConfirmationAsync(string modelName, bool isDefault)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("✓ Selected: ");
        Console.ResetColor();

        // Animate the model name
        foreach (char c in modelName)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(c);
            await Task.Delay(30);
        }

        if (isDefault)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" (default)");
        }

        Console.ResetColor();
        Console.WriteLine();

        await Task.Delay(200);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("🎉 Ready to go!");
        Console.ResetColor();
        await Task.Delay(300);
    }

    private static async Task ShowErrorMessageAsync(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        for (int i = 0; i < 3; i++)
        {
            Console.Write("\r" + message);
            await Task.Delay(200);
            Console.Write("\r" + new string(' ', message.Length));
            await Task.Delay(100);
        }
        Console.WriteLine("\r" + message);
        Console.ResetColor();
        await Task.Delay(500);
    }
}
