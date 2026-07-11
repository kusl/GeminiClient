// GeminiClient/GeminiApiClient.cs
using System.Net;
using System.Net.Http.Headers;
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
    private static readonly TimeSpan s_maxRetryDelay = TimeSpan.FromSeconds(60);

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

    // ---- Convenience (single-turn) overloads -------------------------------------------------

    public Task<string?> GenerateContentAsync(string modelName, string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        return GenerateContentAsync(modelName, SingleTurn(prompt), cancellationToken);
    }

    public IAsyncEnumerable<string> StreamGenerateContentAsync(string modelName, string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        return StreamGenerateContentAsync(modelName, SingleTurn(prompt), cancellationToken);
    }

    // ---- Multi-turn (stateful) implementations ------------------------------------------------

    public async Task<string?> GenerateContentAsync(string modelName, List<Content> history, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(history);

        Uri requestUri = BuildRequestUri(modelName, "generateContent", sse: false);
        string json = SerializeRequest(history);

        using CancellationTokenSource timeoutCts = CreateLinkedTimeout(cancellationToken, out CancellationToken effective);

        _logger.LogInformation("Sending request to {Model} with {Count} history item(s)", modelName, history.Count);

        using HttpResponseMessage response = await SendWithRetryAsync(
            () => CreateJsonRequest(HttpMethod.Post, requestUri, json),
            HttpCompletionOption.ResponseContentRead,
            "generateContent",
            effective,
            cancellationToken);

        string body = await SafeReadBodyAsync(response, effective);

        GeminiResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(body, GeminiJsonContext.Default.GeminiResponse);
        }
        catch (JsonException ex)
        {
            throw new GeminiApiException("The API returned a response that could not be parsed.",
                GeminiErrorKind.InvalidResponse, innerException: ex);
        }

        ThrowIfBlocked(parsed);
        LogUsage(parsed);

        string? text = parsed?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        _logger.LogInformation("Received response from {Model}", modelName);
        return text;
    }

    public async IAsyncEnumerable<string> StreamGenerateContentAsync(
        string modelName,
        List<Content> history,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(history);

        Uri requestUri = BuildRequestUri(modelName, "streamGenerateContent", sse: true);
        string json = SerializeRequest(history);

        _logger.LogInformation("Opening stream to {Model} with {Count} history item(s)", modelName, history.Count);

        // Establishing the response is the retryable phase (429/503 surface here, before any
        // token is yielded). Streaming has no overall timeout — long generations are legitimate,
        // and the caller can cancel via the supplied token (e.g. Ctrl+C).
        HttpResponseMessage response = await SendWithRetryAsync(
            () => CreateSseRequest(requestUri, json),
            HttpCompletionOption.ResponseHeadersRead,
            "streamGenerateContent",
            cancellationToken,
            cancellationToken);

        using (response)
        using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        using (StreamReader reader = new(stream))
        {
            while (true)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(':'))
                {
                    continue; // comment / keep-alive
                }
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                string payload = line["data:".Length..].TrimStart();
                if (payload == "[DONE]")
                {
                    break;
                }

                string? textChunk = TryExtractChunk(payload);
                if (!string.IsNullOrEmpty(textChunk))
                {
                    yield return textChunk;
                }
            }
        }

        _logger.LogInformation("Stream completed for {Model}", modelName);
    }

    // ---- Request plumbing ---------------------------------------------------------------------

    private static List<Content> SingleTurn(string prompt) =>
        [new Content { Role = "user", Parts = [new Part { Text = prompt }] }];

    private Uri BuildRequestUri(string modelName, string method, bool sse)
    {
        string query = $"key={HttpUtility.UrlEncode(_options.ApiKey)}";
        if (sse)
        {
            query += "&alt=sse";
        }

        UriBuilder builder = new(_httpClient.BaseAddress!)
        {
            Path = $"/v1beta/models/{modelName}:{method}",
            Query = query
        };
        return builder.Uri;
    }

    private string SerializeRequest(List<Content> history)
    {
        GeminiRequest requestBody = new()
        {
            Contents = history,
            SystemInstruction = _contextService.GetSystemInstruction()
        };
        return JsonSerializer.Serialize(requestBody, GeminiJsonContext.Default.GeminiRequest);
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri, string json) =>
        new(method, uri)
        {
            // Disposed together with the request message.
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpRequestMessage CreateSseRequest(Uri uri, string json)
    {
        HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, uri, json);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return request;
    }

    /// <summary>
    /// Sends a request with bounded retry on transient failures, honouring the server's
    /// Retry-After when present. Returns a successful response or throws a <see cref="GeminiApiException"/>.
    /// A fresh <see cref="HttpRequestMessage"/> is created per attempt because messages are single-use.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        HttpCompletionOption completionOption,
        string operation,
        CancellationToken effectiveToken,
        CancellationToken callerToken)
    {
        int maxAttempts = Math.Max(1, _options.MaxRetries + 1);
        GeminiApiException? lastError = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Only the caller's own cancellation should surface as OperationCanceledException.
            callerToken.ThrowIfCancellationRequested();

            HttpResponseMessage? response = null;
            HttpRequestMessage request = requestFactory();
            try
            {
                response = await _httpClient.SendAsync(request, completionOption, effectiveToken);
            }
            catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
            {
                throw; // genuine caller cancellation
            }
            catch (OperationCanceledException oce)
            {
                lastError = new GeminiApiException("The request timed out before the server responded.",
                    GeminiErrorKind.Timeout, isTransient: false, innerException: oce);
            }
            catch (HttpRequestException hre)
            {
                lastError = new GeminiApiException("Could not reach the Gemini API. Check your internet connection.",
                    GeminiErrorKind.Network, isTransient: true, innerException: hre);
            }
            finally
            {
                request.Dispose();
            }

            if (lastError is null && response is not null)
            {
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                string errorBody = await SafeReadBodyAsync(response, effectiveToken);
                HttpStatusCode status = response.StatusCode;
                response.Dispose();

                lastError = GeminiErrorParser.FromHttpResponse(status, errorBody);
                _logger.LogDebug("{Operation} attempt {Attempt}/{Max} failed: HTTP {Code} ({Kind})",
                    operation, attempt, maxAttempts, (int)status, lastError.Kind);
            }
            else
            {
                _logger.LogDebug("{Operation} attempt {Attempt}/{Max} failed: {Kind}",
                    operation, attempt, maxAttempts, lastError!.Kind);
            }

            bool canRetry = attempt < maxAttempts && lastError.IsTransient && !callerToken.IsCancellationRequested;
            if (!canRetry)
            {
                break;
            }

            TimeSpan delay = ComputeRetryDelay(attempt, lastError.RetryAfter);
            _logger.LogDebug("Retrying {Operation} in {Delay:0.#}s", operation, delay.TotalSeconds);

            try
            {
                await Task.Delay(delay, effectiveToken);
            }
            catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
            {
                // The caller (e.g. Ctrl+C) cancelled while we were waiting to retry.
                throw;
            }
            catch (OperationCanceledException)
            {
                // The overall request timeout elapsed during the back-off wait. Surface it as a
                // timeout rather than letting a raw cancellation escape and be mislabelled.
                throw new GeminiApiException(
                    "The request timed out while waiting to retry after a transient error. "
                        + "You can try again or increase TimeoutSeconds.",
                    GeminiErrorKind.Timeout,
                    isTransient: false,
                    innerException: lastError);
            }
        }

        throw lastError!;
    }

    private static TimeSpan ComputeRetryDelay(int attempt, TimeSpan? serverSuggested)
    {
        if (serverSuggested is { } suggested && suggested > TimeSpan.Zero)
        {
            return suggested < s_maxRetryDelay ? suggested : s_maxRetryDelay;
        }

        // Exponential backoff (1s, 2s, 4s, ...) with up to +50% jitter, capped.
        double baseSeconds = Math.Min(Math.Pow(2, attempt - 1), s_maxRetryDelay.TotalSeconds);
        double jitter = Random.Shared.NextDouble() * 0.5 * baseSeconds;
        double total = Math.Min(baseSeconds + jitter, s_maxRetryDelay.TotalSeconds);
        return TimeSpan.FromSeconds(total);
    }

    private CancellationTokenSource CreateLinkedTimeout(CancellationToken caller, out CancellationToken effective)
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        if (_options.TimeoutSeconds > 0)
        {
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        }

        effective = cts.Token;
        return cts;
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private string? TryExtractChunk(string jsonData)
    {
        try
        {
            GeminiResponse? streamResponse = JsonSerializer.Deserialize(jsonData, GeminiJsonContext.Default.GeminiResponse);
            return streamResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Skipped an unparseable SSE data line.");
            return null;
        }
    }

    private static void ThrowIfBlocked(GeminiResponse? response)
    {
        if (response is null)
        {
            return;
        }

        string? blockReason = response.PromptFeedback?.BlockReason;
        bool hasCandidates = response.Candidates is { Count: > 0 };

        if (!string.IsNullOrEmpty(blockReason) && !hasCandidates)
        {
            throw new GeminiApiException(
                $"The prompt was blocked by the API (reason: {blockReason}). Try rephrasing your request.",
                GeminiErrorKind.ContentBlocked);
        }

        Candidate? first = hasCandidates ? response.Candidates[0] : null;
        string? finish = first?.FinishReason;
        bool hasText = !string.IsNullOrEmpty(first?.Content?.Parts?.FirstOrDefault()?.Text);

        if (!hasText
            && !string.IsNullOrEmpty(finish)
            && !finish.Equals("STOP", StringComparison.OrdinalIgnoreCase)
            && !finish.Equals("MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
        {
            throw new GeminiApiException(
                $"The response was withheld (finish reason: {finish}).",
                GeminiErrorKind.ContentBlocked);
        }
    }

    private void LogUsage(GeminiResponse? response)
    {
        if (response?.UsageMetadata is { } usage)
        {
            _logger.LogDebug("Token usage — prompt: {Prompt}, candidates: {Candidates}, total: {Total}",
                usage.PromptTokenCount, usage.CandidatesTokenCount, usage.TotalTokenCount);
        }
    }
}
