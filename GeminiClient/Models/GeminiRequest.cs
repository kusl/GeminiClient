// GeminiClient/Models/GeminiRequest.cs
using System.Text.Json.Serialization;

namespace GeminiClient.Models;

public class GeminiRequest
{
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
