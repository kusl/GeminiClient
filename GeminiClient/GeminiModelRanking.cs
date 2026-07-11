// GeminiClient/GeminiModelRanking.cs
using GeminiClient.Models;

namespace GeminiClient;

/// <summary>
/// Heuristics for ordering models for an interactive text chat. Pure and deterministic so it can
/// be unit-tested. The goal is to surface stable, general-purpose text models first and push
/// specialised/preview variants (image, audio, TTS, robotics, embeddings, previews) down, because
/// they are poor defaults for a text chat and are frequently unavailable on the free tier.
/// </summary>
public static class GeminiModelRanking
{
    private static readonly string[] s_specialisedMarkers =
    [
        "image", "vision", "tts", "audio", "speech", "embedding", "aqa",
        "robotics", "computer-use", "lyria", "music", "deep-research", "antigravity", "nano-banana"
    ];

    private static readonly string[] s_unstableMarkers =
    [
        "preview", "experimental", "-exp", "learnlm"
    ];

    private static readonly (string Token, int Weight)[] s_versionWeights =
    [
        ("3.5", 55), ("3.1", 53), ("3-", 50), ("3.", 50),
        ("2.5", 40), ("2.0", 30), ("1.5", 20)
    ];

    public static bool IsSpecialised(string identifier) => ContainsAny(identifier, s_specialisedMarkers);

    public static bool IsUnstable(string identifier) => ContainsAny(identifier, s_unstableMarkers);

    public static bool IsGeneralPurposeText(string identifier) =>
        !IsSpecialised(identifier)
        && (identifier.Contains("gemini", StringComparison.OrdinalIgnoreCase)
            || identifier.Contains("gemma", StringComparison.OrdinalIgnoreCase));

    /// <summary>Orders the supplied models best-first for interactive text chat.</summary>
    public static IReadOnlyList<GeminiModel> ForInteractiveChat(IEnumerable<GeminiModel> models) =>
        models
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .OrderByDescending(m => IsGeneralPurposeText(m.GetModelIdentifier())) // gemini/gemma text first
            .ThenBy(m => IsSpecialised(m.GetModelIdentifier()))                    // specialised last
            .ThenBy(m => IsUnstable(m.GetModelIdentifier()))                       // stable before preview
            .ThenByDescending(m => Score(m.GetModelIdentifier()))                  // prefer flash, then newer
            .ThenByDescending(m => m.GetModelIdentifier(), StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Picks the best default model identifier (without the "models/" prefix), or null when none.</summary>
    public static string? RecommendDefault(IEnumerable<GeminiModel> models) =>
        ForInteractiveChat(models).FirstOrDefault()?.GetModelIdentifier();

    private static int Score(string id)
    {
        int score = 0;
        if (id.Contains("flash", StringComparison.OrdinalIgnoreCase))
        {
            score += 100; // fast + cheap => sensible default
        }
        if (id.Contains("pro", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }
        if (id.Contains("lite", StringComparison.OrdinalIgnoreCase))
        {
            score -= 10;
        }

        return score + VersionScore(id);
    }

    private static int VersionScore(string id)
    {
        foreach ((string token, int weight) in s_versionWeights)
        {
            if (id.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return weight;
            }
        }

        return 0;
    }

    private static bool ContainsAny(string id, string[] markers)
    {
        foreach (string marker in markers)
        {
            if (id.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
