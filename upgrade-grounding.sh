#!/bin/bash
set -e

echo "🚀 Starting Hyper-Contextual Environmental Grounding Upgrade..."

# Ensure we are in the project root
if [ ! -f "LearningByDoing.sln" ]; then
    echo "❌ Error: Please run this script from the root of the repository (where LearningByDoing.sln is located)."
    exit 1
fi

echo "📂 Creating Environmental Grounding Service..."

# 1. Create the Interface
cat <<EOF > GeminiClient/IEnvironmentContextService.cs
// GeminiClient/IEnvironmentContextService.cs
using GeminiClient.Models;

namespace GeminiClient;

public interface IEnvironmentContextService
{
    /// <summary>
    /// Generates a system instruction containing real-time environmental context
    /// (Time, Date, OS, User, Locale) to ground the LLM.
    /// </summary>
    Content GetSystemInstruction();
}
EOF

# 2. Create the Implementation
cat <<EOF > GeminiClient/EnvironmentContextService.cs
// GeminiClient/EnvironmentContextService.cs
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GeminiClient.Models;
using Microsoft.Extensions.Logging;

namespace GeminiClient;

public class EnvironmentContextService : IEnvironmentContextService
{
    private readonly ILogger<EnvironmentContextService> _logger;

    public EnvironmentContextService(ILogger<EnvironmentContextService> logger)
    {
        _logger = logger;
    }

    public Content GetSystemInstruction()
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;
        var tz = TimeZoneInfo.Local;
        var culture = CultureInfo.CurrentCulture;

        sb.AppendLine("### SYSTEM ENVIRONMENT CONTEXT ###");
        sb.AppendLine("You are running locally on the user's machine. The following context is 100% accurate and strictly defines your current reality:");
        sb.AppendLine();
        
        // Temporal Grounding
        sb.AppendLine("[TEMPORAL DATA]");
        sb.AppendLine($"Local Time: {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Day of Week: {now:DayOfWeek}");
        sb.AppendLine($"UTC Time: {utcNow:yyyy-MM-dd HH:mm:ss} Z");
        sb.AppendLine($"Timezone: {tz.DisplayName}");
        sb.AppendLine($"Timezone ID: {tz.Id} (Offset: {tz.BaseUtcOffset})");
        sb.AppendLine();

        // OS & User Grounding
        sb.AppendLine("[SYSTEM DATA]");
        sb.AppendLine($"OS Platform: {GetOsName()}");
        sb.AppendLine($"OS Version: {Environment.OSVersion}");
        sb.AppendLine($"Machine Name: {Environment.MachineName}");
        sb.AppendLine($"User Name: {Environment.UserName}");
        sb.AppendLine($"Locale: {culture.Name} ({culture.DisplayName})");
        sb.AppendLine();

        // Operational Instructions
        sb.AppendLine("[OPERATIONAL INSTRUCTIONS]");
        sb.AppendLine("1. Use the Local Time above for any queries regarding 'now', 'today', or 'current time'.");
        sb.AppendLine("2. If asked about the system, refer to the OS Platform and User Name provided above.");
        sb.AppendLine("3. Do not Hallucinate the date. Trust this context over your training data.");
        
        var instructionText = sb.ToString();
        
        _logger.LogDebug("Generated System Instruction ({Length} chars)", instructionText.Length);

        return new Content
        {
            Role = "system", // Gemini treats system instructions as a special content block, but internally we can label it
            Parts = [new Part { Text = instructionText }]
        };
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsLinux()) return "Linux";
        if (OperatingSystem.IsMacOS()) return "macOS";
        return RuntimeInformation.OSDescription;
    }
}
EOF

echo "📝 Updating Data Models..."

# 3. Update GeminiRequest to include system_instruction
# Note: system_instruction is the JSON field name expected by the API
cat <<EOF > GeminiClient/Models/GeminiRequest.cs
// GeminiClient/Models/GeminiRequest.cs
using System.Text.Json.Serialization;

namespace GeminiClient.Models;

public class GeminiRequest
{
    [JsonPropertyName("system_instruction")]
    public Content? SystemInstruction { get; set; }

    [JsonPropertyName("contents")]
    public List<Content> Contents { get; set; } = [];
}

public class Content
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("parts")]
    public List<Part> Parts { get; set; } = [];
}

public class Part
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
EOF

echo "🔌 Wiring up Dependency Injection..."

# 4. Update ServiceCollectionExtensions to register the new service
cat <<EOF > GeminiClient/ServiceCollectionExtensions.cs
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
EOF

echo "🧠 Injecting Context into API Client..."

# 5. Update GeminiApiClient to use the service and inject the instruction
cat <<EOF > GeminiClient/GeminiApiClient.cs
// GeminiClient/GeminiApiClient.cs
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Web;
using GeminiClient.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeminiClient;

public class GeminiApiClient : IGeminiApiClient
{
    private readonly HttpClient _httpClient;
    private readonly GeminiApiOptions _options;
    private readonly ILogger<GeminiApiClient> _logger;
    private readonly IEnvironmentContextService _contextService;

    public GeminiApiClient(
        HttpClient httpClient, 
        IOptions<GeminiApiOptions> options, 
        ILogger<GeminiApiClient> logger,
        IEnvironmentContextService contextService)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextService = contextService ?? throw new ArgumentNullException(nameof(contextService));

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new ArgumentException("ApiKey is missing in GeminiApiOptions.");
        }
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new ArgumentException("BaseUrl is missing in GeminiApiOptions.");
        }
    }

    // Convenience method for single-turn (stateless) requests
    public Task<string?> GenerateContentAsync(string modelName, string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        // Create a single-turn history
        List<Content> history = [
            new Content { Role = "user", Parts = [new Part { Text = prompt }] }
        ];
        return GenerateContentAsync(modelName, history, cancellationToken);
    }

    // Multi-turn (stateful) implementation
    public async Task<string?> GenerateContentAsync(string modelName, List<Content> history, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(history);

        string? apiKey = _options.ApiKey;
        string path = \$"/v1beta/models/{modelName}:generateContent";
        var uriBuilder = new UriBuilder(_httpClient.BaseAddress!)
        {
            Path = path,
            Query = \$"key={HttpUtility.UrlEncode(apiKey)}"
        };
        Uri requestUri = uriBuilder.Uri;

        // INJECT SYSTEM INSTRUCTION HERE
        var requestBody = new GeminiRequest
        {
            Contents = history,
            SystemInstruction = _contextService.GetSystemInstruction()
        };

        _logger.LogInformation("Sending request to Gemini API: {Uri} with {Count} history items", requestUri, history.Count);

        try
        {
            string jsonString = JsonSerializer.Serialize(requestBody, GeminiJsonContext.Default.GeminiRequest);
            using var jsonContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.PostAsync(requestUri, jsonContent, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Gemini API request failed with status code {StatusCode}. Response: {ErrorContent}",
                    response.StatusCode, errorContent);
                _ = response.EnsureSuccessStatusCode();
            }

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            GeminiResponse? geminiResponse = JsonSerializer.Deserialize(responseJson, GeminiJsonContext.Default.GeminiResponse);

            string? generatedText = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            _logger.LogInformation("Successfully received response from Gemini API.");
            return generatedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Gemini API.");
            throw;
        }
    }

    // Convenience method for single-turn streaming
    public IAsyncEnumerable<string> StreamGenerateContentAsync(string modelName, string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        List<Content> history = [
            new Content { Role = "user", Parts = [new Part { Text = prompt }] }
        ];
        return StreamGenerateContentAsync(modelName, history, cancellationToken);
    }

    // Multi-turn (stateful) streaming implementation
    public async IAsyncEnumerable<string> StreamGenerateContentAsync(
        string modelName,
        List<Content> history,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(history);

        string? apiKey = _options.ApiKey;
        string path = \$"/v1beta/models/{modelName}:streamGenerateContent";
        var uriBuilder = new UriBuilder(_httpClient.BaseAddress!)
        {
            Path = path,
            Query = \$"key={HttpUtility.UrlEncode(apiKey)}&alt=sse"
        };
        Uri requestUri = uriBuilder.Uri;

        // INJECT SYSTEM INSTRUCTION HERE
        var requestBody = new GeminiRequest
        {
            Contents = history,
            SystemInstruction = _contextService.GetSystemInstruction()
        };

        _logger.LogInformation("Sending streaming request to Gemini API: {Uri} with {Count} history items", requestUri, history.Count);

        string jsonString = JsonSerializer.Serialize(requestBody, GeminiJsonContext.Default.GeminiRequest);
        using var jsonContent = new StringContent(jsonString, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = jsonContent
        };
        
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

        HttpResponseMessage response;
        Stream stream;
        StreamReader reader;

        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Gemini API streaming request failed with status code {StatusCode}. Response: {ErrorContent}",
                    response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }

            stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            reader = new StreamReader(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing stream from Gemini API.");
            throw;
        }

        using (response)
        using (stream)
        using (reader)
        {
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(':')) continue;

                if (line.StartsWith("data: "))
                {
                    string jsonData = line.Substring(6);
                    if (jsonData == "[DONE]") break;

                    string? textChunk = null;
                    try
                    {
                        GeminiResponse? streamResponse = JsonSerializer.Deserialize(jsonData, GeminiJsonContext.Default.GeminiResponse);
                        textChunk = streamResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse SSE data.");
                        continue;
                    }

                    if (!string.IsNullOrEmpty(textChunk))
                    {
                        yield return textChunk;
                    }
                }
            }
        }

        _logger.LogInformation("Successfully completed streaming.");
    }
}
EOF

echo "✅ Upgrade Complete!"
echo "Your Gemini Client now supports Hyper-Contextual Environmental Grounding."
echo "Run 'dotnet run --project GeminiClientConsole' to test it."
