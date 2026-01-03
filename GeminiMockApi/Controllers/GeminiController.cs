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

    // Use camelCase serialization to match the GeminiClient's expectations
    private static readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
    };

    public GeminiController(ILogger<GeminiController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discovery Endpoint: Returns the fake model so the client can select it.
    /// Fixes the 404 error seen in logs.
    /// </summary>
    [HttpGet]
    public IActionResult GetModels()
    {
        _logger.LogInformation("Client requested model list.");
        
        var response = new ModelListResponse(new List<GeminiModel>
        {
            new GeminiModel(
                name: "models/gemini-mock-turbo",
                displayName: "Gemini Mock Turbo (Local)",
                description: "High-throughput local simulation for stress testing.",
                supportedGenerationMethods: new[] { "generateContent" }
            )
        });

        return Ok(response);
    }

    /// <summary>
    /// Non-Streaming Endpoint.
    /// </summary>
    [HttpPost("{model}:generateContent")]
    public IActionResult GenerateContent(string model, [FromBody] GeminiRequest request)
    {
        var prompt = request.contents?.LastOrDefault()?.parts?.FirstOrDefault()?.text ?? "";
        _logger.LogInformation($"Generating non-streaming content for {model}. Prompt length: {prompt.Length}");
        
        var text = GenerateLoremIpsum(100);
        var response = new GeminiResponse(new List<Candidate> 
        { 
            new Candidate(new Content(new List<Part> { new Part(text) })) 
        });

        return Ok(response);
    }

    /// <summary>
    /// Streaming Endpoint: Supports dynamic override via prompt: "chunks,delay,size" (e.g., "100,5,20")
    /// </summary>
    [HttpPost("{model}:streamGenerateContent")]
    public async Task StreamGenerateContent(string model, [FromBody] GeminiRequest request)
    {
        // 1. Extract the prompt text
        string prompt = request.contents?.LastOrDefault()?.parts?.FirstOrDefault()?.text ?? "";
        
        // 2. Determine parameters (Dynamic override or Defaults)
        var (chunks, delay, size) = ParseStressParams(prompt);

        _logger.LogInformation($"⚡ STARTING STREAM: {model} | Chunks: {chunks}, Delay: {delay}ms, Size: {size}");

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        using var writer = new StreamWriter(Response.Body);

        for (int i = 0; i < chunks; i++)
        {
            if (delay > 0)
                await Task.Delay(delay);

            var chunkText = $"[{i + 1}/{chunks}] " + GenerateLoremIpsum(size);

            var payload = new GeminiResponse(new List<Candidate>
            {
                new Candidate(new Content(new List<Part> { new Part(chunkText) }))
            });

            // Serialize with camelCase options
            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            await writer.WriteAsync($"data: {json}\n\n");
            await writer.FlushAsync();
        }

        _logger.LogInformation("✅ STREAM COMPLETE");
    }

    private (int chunks, int delay, int size) ParseStressParams(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) 
            return (DEFAULT_STREAM_CHUNKS, DEFAULT_CHUNK_DELAY_MS, DEFAULT_CHUNK_SIZE);

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
        var words = new[] { "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "performance", "testing", "stream", "dotnet", "async", "await", "task" };
        var rand = new Random();
        return string.Join(" ", Enumerable.Range(0, wordCount).Select(_ => words[rand.Next(words.Length)]));
    }
}
