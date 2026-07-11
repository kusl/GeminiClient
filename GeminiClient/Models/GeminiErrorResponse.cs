// GeminiClient/Models/GeminiErrorResponse.cs
using System.Text.Json.Serialization;

namespace GeminiClient.Models;

/// <summary>
/// Root envelope for a Google Generative Language API error response, e.g.
/// <c>{ "error": { "code": 429, "message": "...", "status": "RESOURCE_EXHAUSTED", "details": [ ... ] } }</c>.
/// </summary>
public sealed class GeminiErrorResponse
{
    [JsonPropertyName("error")]
    public GeminiErrorBody? Error { get; set; }
}

public sealed class GeminiErrorBody
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("details")]
    public List<GeminiErrorDetail>? Details { get; set; }
}

public sealed class GeminiErrorDetail
{
    [JsonPropertyName("@type")]
    public string? Type { get; set; }

    /// <summary>Present on <c>google.rpc.RetryInfo</c>; formatted like <c>"42s"</c> or <c>"42.4s"</c>.</summary>
    [JsonPropertyName("retryDelay")]
    public string? RetryDelay { get; set; }
}
