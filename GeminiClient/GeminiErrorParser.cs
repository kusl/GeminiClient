// GeminiClient/GeminiErrorParser.cs
using System.Globalization;
using System.Net;
using System.Text.Json;
using GeminiClient.Models;

namespace GeminiClient;

/// <summary>
/// Translates raw Gemini/Google HTTP error responses into a rich <see cref="GeminiApiException"/>.
/// Deliberately free of I/O and logging so it can be unit-tested in isolation.
/// </summary>
public static class GeminiErrorParser
{
    /// <summary>Build a <see cref="GeminiApiException"/> from an HTTP status code and (optional) response body.</summary>
    public static GeminiApiException FromHttpResponse(HttpStatusCode statusCode, string? body, Exception? inner = null)
    {
        GeminiErrorBody? error = TryParseErrorBody(body);
        string apiStatus = error?.Status ?? string.Empty;
        string serverMessage = error?.Message?.Trim() ?? string.Empty;
        TimeSpan? retryAfter = ExtractRetryDelay(error);
        bool quotaZero = IsQuotaZero(serverMessage);
        int code = (int)statusCode;

        GeminiErrorKind kind = code switch
        {
            400 => GeminiErrorKind.BadRequest,
            401 or 403 => GeminiErrorKind.Unauthorized,
            429 => GeminiErrorKind.RateLimited,
            503 => GeminiErrorKind.ServiceUnavailable,
            >= 500 => GeminiErrorKind.ServerError,
            _ => GeminiErrorKind.Unknown
        };

        bool transient =
            kind is GeminiErrorKind.RateLimited or GeminiErrorKind.ServiceUnavailable or GeminiErrorKind.ServerError
            && !quotaZero;

        string message = BuildUserMessage(kind, code, apiStatus, serverMessage, retryAfter, quotaZero);

        return new GeminiApiException(
            message,
            kind,
            statusCode,
            string.IsNullOrEmpty(apiStatus) ? null : apiStatus,
            retryAfter,
            transient,
            quotaZero,
            inner);
    }

    public static GeminiErrorBody? TryParseErrorBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            GeminiErrorResponse? envelope = JsonSerializer.Deserialize(body, GeminiJsonContext.Default.GeminiErrorResponse);
            return envelope?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static TimeSpan? ExtractRetryDelay(GeminiErrorBody? error)
    {
        if (error?.Details is null)
        {
            return null;
        }

        foreach (GeminiErrorDetail detail in error.Details)
        {
            TimeSpan? parsed = ParseDuration(detail.RetryDelay);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>Parses a protobuf-style duration such as <c>"42s"</c> or <c>"42.405s"</c> into a <see cref="TimeSpan"/>.</summary>
    public static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.EndsWith('s'))
        {
            trimmed = trimmed[..^1];
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds >= 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    /// <summary>
    /// Free-tier "not entitled" responses carry <c>limit: 0</c> in the human-readable message,
    /// which means retrying is pointless and the user should switch models.
    /// </summary>
    public static bool IsQuotaZero(string? serverMessage) =>
        !string.IsNullOrEmpty(serverMessage) && serverMessage.Contains("limit: 0", StringComparison.OrdinalIgnoreCase);

    private static string BuildUserMessage(
        GeminiErrorKind kind,
        int code,
        string apiStatus,
        string serverMessage,
        TimeSpan? retryAfter,
        bool quotaZero)
    {
        string retryHint = retryAfter is { } d
            ? $" You can retry in about {Math.Ceiling(d.TotalSeconds):0} second(s)."
            : string.Empty;

        return kind switch
        {
            GeminiErrorKind.RateLimited when quotaZero =>
                "This model isn't available on your current API plan (free-tier limit is 0). " +
                "Pick a different model with the 'model' command, or enable billing for this model.",
            GeminiErrorKind.RateLimited =>
                $"Rate limit reached for this model.{retryHint} You can also switch models with 'model'.",
            GeminiErrorKind.ServiceUnavailable =>
                $"The model is temporarily overloaded (HTTP 503).{retryHint} Try again shortly, or switch models with 'model'.",
            GeminiErrorKind.ServerError =>
                $"The Gemini service reported a server error (HTTP {code}).{retryHint} This is usually temporary.",
            GeminiErrorKind.Unauthorized =>
                $"Authentication failed (HTTP {code}). Check that your API key is valid and has access to this model.",
            GeminiErrorKind.BadRequest =>
                "The request was rejected (HTTP 400)" +
                (string.IsNullOrEmpty(serverMessage) ? "." : $": {Shorten(serverMessage)}"),
            _ => string.IsNullOrEmpty(serverMessage)
                ? $"The Gemini API returned an error (HTTP {code}{(string.IsNullOrEmpty(apiStatus) ? string.Empty : $", {apiStatus}")})."
                : $"The Gemini API returned an error (HTTP {code}): {Shorten(serverMessage)}"
        };
    }

    private static string Shorten(string message, int max = 300)
    {
        string oneLine = message.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..max] + "…";
    }
}
