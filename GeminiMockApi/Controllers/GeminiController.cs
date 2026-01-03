using System.Text.Json;
using GeminiMockApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace GeminiMockApi.Controllers;

[ApiController]
[Route("v1beta/models")]
public class GeminiController : ControllerBase
{
    private readonly ILogger<GeminiController> _logger;
    private static readonly Random _rng = new();

    // camelCase is required for the GeminiClient's SourceGen to work
    private static readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
    };

    public GeminiController(ILogger<GeminiController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discovery Endpoint: Fixes the 404 error from your logs.
    /// </summary>
    [HttpGet]
    public IActionResult GetModels()
    {
        var response = new ModelListResponse(new List<GeminiModel>
        {
            new GeminiModel(
                name: "models/gemini-mock-turbo",
                displayName: "Gemini Mock Turbo (Local)",
                description: "High-throughput local simulation with random defaults.",
                supportedGenerationMethods: new[] { "generateContent" }
            )
        });
        return Ok(response);
    }

    [HttpPost("{model}:generateContent")]
    public IActionResult GenerateContent(string model, [FromBody] GeminiRequest request)
    {
        var text = GenerateLoremIpsum(50);
        var response = new GeminiResponse(new List<Candidate> 
        { 
            new Candidate(new Content(new List<Part> { new Part(text) })) 
        });
        return Ok(response);
    }

    [HttpPost("{model}:streamGenerateContent")]
    public async Task StreamGenerateContent(string model, [FromBody] GeminiRequest request)
    {
        // Extract prompt to check for "chunks,delay,size" overrides
        string prompt = request.contents?.LastOrDefault()?.parts?.FirstOrDefault()?.text ?? "";
        
        var (chunks, delay, size) = ParseStressParams(prompt);

        _logger.LogInformation($"⚡ STREAM START: {model} | Chunks: {chunks}, Delay: {delay}ms, Size: {size} words");

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");

        using var writer = new StreamWriter(Response.Body);

        for (int i = 0; i < chunks; i++)
        {
            if (delay > 0) await Task.Delay(delay);

            var chunkText = $"[{i + 1}/{chunks}] " + GenerateLoremIpsum(size);
            var payload = new GeminiResponse(new List<Candidate>
            {
                new Candidate(new Content(new List<Part> { new Part(chunkText) }))
            });

            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            await writer.WriteAsync($"data: {json}\n\n");
            await writer.FlushAsync();
        }

        _logger.LogInformation("✅ STREAM COMPLETE");
    }

    private (int chunks, int delay, int size) ParseStressParams(string input)
    {
        // Try to parse user override (format: "100,5,20")
        if (!string.IsNullOrWhiteSpace(input))
        {
            var parts = input.Trim().Split(',');
            if (parts.Length == 3 && 
                int.TryParse(parts[0], out int c) && 
                int.TryParse(parts[1], out int d) && 
                int.TryParse(parts[2], out int s))
            {
                return (c, d, s);
            }
        }

        // Kicker: Generate fresh random defaults for every request if no valid override is found
        return (
            chunks: _rng.Next(50, 601),   // 50-600
            delay: _rng.Next(5, 501),    // 5-500ms
            size: _rng.Next(10, 101)     // 10-100 words
        );
    }

    private static string GenerateLoremIpsum(int wordCount)
    {
        var words = new[] { "lorem", "ipsum", "dolor", "sit", "performance", "stress", "latency", "dotnet", "stream", "buffer" };
        return string.Join(" ", Enumerable.Range(0, wordCount).Select(_ => words[_rng.Next(words.Length)]));
    }
}
