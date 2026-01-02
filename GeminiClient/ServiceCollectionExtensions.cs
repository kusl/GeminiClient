// GeminiClient/ServiceCollectionExtensions.cs
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GeminiClient;

public static class ServiceCollectionExtensions
{
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "GeminiApiOptions is preserved and only contains primitive types")]
    public static IServiceCollection AddGeminiApiClient(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(configurationSection);

        // Manual configuration binding to avoid trimming issues
        services.Configure<GeminiApiOptions>(options =>
        {
            options.ApiKey = configurationSection["ApiKey"];
            options.BaseUrl = configurationSection["BaseUrl"] ?? "https://generativelanguage.googleapis.com/";
            options.DefaultModel = configurationSection["DefaultModel"];
            options.ModelPreference = configurationSection["ModelPreference"];

            if (int.TryParse(configurationSection["TimeoutSeconds"], out int timeout))
                options.TimeoutSeconds = timeout;
            else
                options.TimeoutSeconds = 30;

            if (int.TryParse(configurationSection["MaxRetries"], out int retries))
                options.MaxRetries = retries;
            else
                options.MaxRetries = 3;

            if (bool.TryParse(configurationSection["EnableDetailedLogging"], out bool logging))
                options.EnableDetailedLogging = logging;
        });

        // Add validation
        services.AddSingleton<IValidateOptions<GeminiApiOptions>, GeminiApiOptionsValidator>();

        // Add memory cache for model caching
        services.TryAddSingleton<IMemoryCache, MemoryCache>();

        // REGISTER NEW GROUNDING SERVICE
        services.TryAddSingleton<IEnvironmentContextService, EnvironmentContextService>();

        // Register ModelService with HttpClient
        _ = services.AddHttpClient<IModelService, ModelService>((serviceProvider, client) =>
        {
            GeminiApiOptions options = serviceProvider.GetRequiredService<IOptions<GeminiApiOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
                throw new InvalidOperationException("Gemini BaseUrl is not configured.");
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        // Register GeminiApiClient with HttpClient
        _ = services.AddHttpClient<IGeminiApiClient, GeminiApiClient>((serviceProvider, client) =>
        {
            GeminiApiOptions options = serviceProvider.GetRequiredService<IOptions<GeminiApiOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
                throw new InvalidOperationException("Gemini BaseUrl is not configured.");
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        return services;
    }
}
