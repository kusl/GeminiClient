// GeminiClient/GeminiApiException.cs
using System.Net;

namespace GeminiClient;

/// <summary>
/// Categorises API failures so callers can react (and message the user) appropriately,
/// without brittle string matching on HTTP error text.
/// </summary>
public enum GeminiErrorKind
{
    Unknown = 0,
    Network,             // transport failure: DNS, connection reset, no internet
    Timeout,             // request exceeded the configured timeout
    Cancelled,           // caller cancelled (e.g. Ctrl+C)
    BadRequest,          // 400 - malformed request
    Unauthorized,        // 401/403 - invalid/expired API key or no permission
    RateLimited,         // 429 - quota/rate limit
    ServiceUnavailable,  // 503 - model overloaded / temporary
    ServerError,         // 500/5xx - server side
    InvalidResponse,     // a 2xx response that could not be parsed
    ContentBlocked       // response withheld (safety / recitation / etc.)
}

/// <summary>
/// A rich exception describing a failed Gemini API interaction. Carries enough structured
/// context (status code, Google status string, server-suggested retry delay, transience,
/// free-tier quota state) for the UI to present a friendly, actionable message and for the
/// client to make retry decisions — without re-parsing HTTP error text.
/// </summary>
public class GeminiApiException : Exception
{
    public GeminiErrorKind Kind { get; }
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The Google <c>status</c> string, e.g. <c>RESOURCE_EXHAUSTED</c>, when available.</summary>
    public string? ApiStatus { get; }

    /// <summary>The server-suggested delay before retrying, when the response provided one.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>True when retrying after a delay is likely to succeed.</summary>
    public bool IsTransient { get; }

    /// <summary>
    /// True when the failure is a free-tier "limit: 0" condition — i.e. the model is not
    /// available on the caller's plan. Retrying will not help; switching models will.
    /// </summary>
    public bool IsQuotaZero { get; }

    public GeminiApiException(string message)
        : base(message) => Kind = GeminiErrorKind.Unknown;

    public GeminiApiException(string message, Exception innerException)
        : base(message, innerException) => Kind = GeminiErrorKind.Unknown;

    public GeminiApiException(
        string message,
        GeminiErrorKind kind,
        HttpStatusCode? statusCode = null,
        string? apiStatus = null,
        TimeSpan? retryAfter = null,
        bool isTransient = false,
        bool isQuotaZero = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
        ApiStatus = apiStatus;
        RetryAfter = retryAfter;
        IsTransient = isTransient;
        IsQuotaZero = isQuotaZero;
    }
}
