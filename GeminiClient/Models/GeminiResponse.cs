// GeminiClient/Models/GeminiResponse.cs
using System.Text.Json.Serialization;

namespace GeminiClient.Models;

/// <summary>Response structure returned by the Gemini generateContent / streamGenerateContent endpoints.</summary>
public class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<Candidate> Candidates { get; set; } = [];

    [JsonPropertyName("promptFeedback")]
    public PromptFeedback? PromptFeedback { get; set; }

    [JsonPropertyName("usageMetadata")]
    public UsageMetadata? UsageMetadata { get; set; }
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

public class SafetyRating
{
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("probability")]
    public string? Probability { get; set; }

    [JsonPropertyName("blocked")]
    public bool? Blocked { get; set; }
}

/// <summary>
/// Feedback about the prompt itself. When a prompt is blocked, <see cref="BlockReason"/> is set
/// and the response contains no candidates.
/// </summary>
public class PromptFeedback
{
    [JsonPropertyName("blockReason")]
    public string? BlockReason { get; set; }

    [JsonPropertyName("safetyRatings")]
    public List<SafetyRating> SafetyRatings { get; set; } = [];
}

/// <summary>Token accounting returned by the API. All fields are optional depending on the model.</summary>
public class UsageMetadata
{
    [JsonPropertyName("promptTokenCount")]
    public int? PromptTokenCount { get; set; }

    [JsonPropertyName("candidatesTokenCount")]
    public int? CandidatesTokenCount { get; set; }

    [JsonPropertyName("totalTokenCount")]
    public int? TotalTokenCount { get; set; }
}
