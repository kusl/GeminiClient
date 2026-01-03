using System.Text.Json;
using GeminiMockApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace GeminiMockApi.Controllers;

[ApiController]
[Route("v1beta/models")]
public class GeminiController : ControllerBase
{
    private readonly ILogger<GeminiController> _logger;

    // Default "Stress Test" parameters
    private const int DEFAULT_STREAM_CHUNKS = 500;
    private const int DEFAULT_CHUNK_DELAY_MS = 10;
    private const int DEFAULT_CHUNK_SIZE = 50;

    public GeminiController(ILogger<GeminiController> logger)
    {
        _logger = logger;
    }

    // ... GetModels and GenerateContent remain the same ...

    /// <summary>
    /// Streaming Endpoint: The Data Hose.
    /// Supports dynamic override via prompt: "chunks,delay,size" (e.g., "100,5,20")
    /// </summary>
    [HttpPost("{model}:streamGenerateContent")]
    public async Task StreamGenerateContent(string model, [FromBody] GeminiRequest request)
    {
        // 1. Extract the prompt text
        string prompt = request.Contents?.LastOrDefault()?.Parts?.FirstOrDefault()?.Text ?? "";
        
        // 2. Determine parameters (Dynamic override or Defaults)
        var (chunks, delay, size) = ParseStressParams(prompt);

        _logger.LogInformation($"⚡ STARTING STREAM: {model} | Chunks: {chunks}, Delay: {delay}ms, Size: {size} words");

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var writer = new StreamWriter(Response.Body);

        for (int i = 0; i < chunks; i++)
        {
            if (delay > 0)
                await Task.Delay(delay);

            var chunkText = $"[{i + 1}/{chunks}] " + GenerateLoremIpsum(size);

            var payload = new GeminiResponse(new List<Candidate>
            {
                new Candidate(new Content(new List<Part> { new Part(chunkText) }))
            });

            string json = JsonSerializer.Serialize(payload);
            await writer.WriteAsync($"data: {json}\n\n");
            await writer.FlushAsync();
        }

        _logger.LogInformation("✅ STREAM COMPLETE");
    }

    /// <summary>
    /// Checks if a string matches "int,int,int" and returns those values, 
    /// otherwise returns the system defaults.
    /// </summary>
    private (int chunks, int delay, int size) ParseStressParams(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (DEFAULT_STREAM_CHUNKS, DEFAULT_CHUNK_DELAY_MS, DEFAULT_CHUNK_SIZE);

        var parts = input.Trim().Split(',');
        if (parts.Length == 3 && 
            int.TryParse(parts[0], out int c) && 
            int.TryParse(parts[1], out int d) && 
            int.TryParse(parts[2], out int s))
        {
            return (c, d, s);
        }

        return (DEFAULT_STREAM_CHUNKS, DEFAULT_CHUNK_DELAY_MS, DEFAULT_CHUNK_SIZE);
    }

    private static string GenerateLoremIpsum(int wordCount)
    {
        var words = new[] { "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "performance", "testing", "stream", "buffer", "latency", "throughput", "dotnet", "async", "await", "task", "memory", "allocation" };
        var rand = new Random();
        return string.Join(" ", Enumerable.Range(0, wordCount).Select(_ => words[rand.Next(words.Length)]));
    }
}
