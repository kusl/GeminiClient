// GeminiClient/Models/GeminiResponse.cs
using System.Text.Json.Serialization;

namespace GeminiClient.Models;

// Basic response structure - Adapt based on the actual Gemini API response
public class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<Candidate> Candidates { get; set; } = [];

    // You might also have properties like "promptFeedback" depending on the request
}

public class Candidate
{
    [JsonPropertyName("content")]
    public Content? Content { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("safetyRatings")]
    public List<SafetyRating> SafetyRatings { get; set; } = [];
}

// Content model is already defined in GeminiRequest.cs, but might differ slightly
// in response, adjust if necessary.

// Part model is already defined in GeminiRequest.cs

public class SafetyRating
{
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("probability")]
    public string? Probability { get; set; }
}
