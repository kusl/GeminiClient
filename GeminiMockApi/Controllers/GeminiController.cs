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

    // The Client uses SourceGen (GeminiJsonContext) which expects camelCase.
    private static readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
    };

    public GeminiController(ILogger<GeminiController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetModels()
    {
        var response = new ModelListResponse(new List<GeminiModel>
        {
            new GeminiModel(
                name: "models/gemini-mock-turbo",
                displayName: "Gemini Mock Turbo (Local)",
                description: "Local simulation. Try prompt: '100,20,50' (chunks,delay,words)",
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
        string prompt = request.contents?.LastOrDefault()?.parts?.FirstOrDefault()?.text ?? "";
        var (chunks, delay, size) = ParseStressParams(prompt);

        _logger.LogInformation($"⚡ START: {model} | Chunks: {chunks}, Delay: {delay}ms, Size: {size}");

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        // Use StreamWriter and ensure it stays open until the loop finishes
        await using (var writer = new StreamWriter(Response.Body))
        {
            for (int i = 0; i < chunks; i++)
            {
                // Safety check for cancellation
                if (HttpContext.RequestAborted.IsCancellationRequested) break;

                if (delay > 0) await Task.Delay(delay);

                var chunkText = $"[{i + 1}/{chunks}] " + GenerateLoremIpsum(size);
                var payload = new GeminiResponse(new List<Candidate>
                {
                    new Candidate(new Content(new List<Part> { new Part(chunkText) }))
                });

                string json = JsonSerializer.Serialize(payload, _jsonOptions);
                
                // Write the SSE data format: "data: {json}\n\n"
                await writer.WriteAsync($"data: {json}\n\n");
                await writer.FlushAsync();
            }
        }

        _logger.LogInformation("✅ FINISHED");
    }

    private (int chunks, int delay, int size) ParseStressParams(string input)
    {
        if (!string.IsNullOrWhiteSpace(input))
        {
            var parts = input.Trim().Split(',');
            if (parts.Length == 3 && 
                int.TryParse(parts[0], out int c) && 
                int.TryParse(parts[1], out int d) && 
                int.TryParse(parts[2], out int s))
            {
                // CLAMP: Small values (like 1ms) can cause 502/Gateway errors 
                // because the network buffer saturates too quickly.
                return (c, Math.Max(d, 10), s);
            }
        }

        return (
            chunks: _rng.Next(50, 601),
            delay: _rng.Next(10, 501), // Random default at least 10ms
            size: _rng.Next(10, 101)
        );
    }

    private static string GenerateLoremIpsum(int wordCount)
    {
        var words = new[] { "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "performance", "testing", "stream", "dotnet", "async", "task" };
        return string.Join(" ", Enumerable.Range(0, wordCount).Select(_ => words[_rng.Next(words.Length)]));
    }
}
