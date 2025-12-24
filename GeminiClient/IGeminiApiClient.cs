// GeminiClient/IGeminiApiClient.cs
using GeminiClient.Models;

namespace GeminiClient;

public interface IGeminiApiClient
{
    /// <summary>
    /// Generates content using the specified Gemini model and prompt (single-turn).
    /// </summary>
    Task<string?> GenerateContentAsync(string modelName, string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates content providing full conversation history (multi-turn).
    /// </summary>
    /// <param name="modelName">The name of the model.</param>
    /// <param name="history">The list of previous chat turns (user and model).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> GenerateContentAsync(string modelName, List<Content> history, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates content using streaming (single-turn).
    /// </summary>
    IAsyncEnumerable<string> StreamGenerateContentAsync(string modelName, string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates content using streaming with full conversation history (multi-turn).
    /// </summary>
    /// <param name="modelName">The name of the model.</param>
    /// <param name="history">The list of previous chat turns (user and model).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<string> StreamGenerateContentAsync(string modelName, List<Content> history, CancellationToken cancellationToken = default);
}
