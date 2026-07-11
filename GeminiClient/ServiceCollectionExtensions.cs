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

        // Manual configuration binding to avoid trimming issues with reflection-based binders.
        services.Configure<GeminiApiOptions>(options =>
        {
            options.ApiKey = configurationSection["ApiKey"];
            options.BaseUrl = configurationSection["BaseUrl"] ?? "https://generativelanguage.googleapis.com/";
            options.DefaultModel = configurationSection["DefaultModel"];
            options.ModelPreference = configurationSection["ModelPreference"];

            options.TimeoutSeconds = int.TryParse(configurationSection["TimeoutSeconds"], out int timeout) ? timeout : 100;
            options.MaxRetries = int.TryParse(configurationSection["MaxRetries"], out int retries) ? retries : 3;

            if (bool.TryParse(configurationSection["EnableDetailedLogging"], out bool logging))
            {
                options.EnableDetailedLogging = logging;
            }
        });

        services.AddSingleton<IValidateOptions<GeminiApiOptions>, GeminiApiOptionsValidator>();
        services.TryAddSingleton<IMemoryCache, MemoryCache>();
        services.TryAddSingleton<IEnvironmentContextService, EnvironmentContextService>();

        // ModelService: a normal bounded per-request timeout is appropriate (it only lists models).
        _ = services.AddHttpClient<IModelService, ModelService>((serviceProvider, client) =>
        {
            GeminiApiOptions options = serviceProvider.GetRequiredService<IOptions<GeminiApiOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new InvalidOperationException("Gemini BaseUrl is not configured.");
            }

            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 300));
        });

        // GeminiApiClient: the same typed client serves both streaming and non-streaming calls.
        // HttpClient.Timeout is a single budget covering the *entire* operation, including reading
        // the response body — which would truncate long SSE streams. So we disable it here and let
        // GeminiApiClient apply a per-call timeout to non-streaming requests (via a linked token),
        // while streaming relies solely on the caller's cancellation token.
        _ = services.AddHttpClient<IGeminiApiClient, GeminiApiClient>((serviceProvider, client) =>
        {
            GeminiApiOptions options = serviceProvider.GetRequiredService<IOptions<GeminiApiOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new InvalidOperationException("Gemini BaseUrl is not configured.");
            }

            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        return services;
    }
}
