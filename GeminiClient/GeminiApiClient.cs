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
        string path = $"/v1beta/models/{modelName}:generateContent";
        var uriBuilder = new UriBuilder(_httpClient.BaseAddress!)
        {
            Path = path,
            Query = $"key={HttpUtility.UrlEncode(apiKey)}"
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
        string path = $"/v1beta/models/{modelName}:streamGenerateContent";
        var uriBuilder = new UriBuilder(_httpClient.BaseAddress!)
        {
            Path = path,
            Query = $"key={HttpUtility.UrlEncode(apiKey)}&alt=sse"
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
