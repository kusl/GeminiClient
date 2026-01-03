using System.Runtime.CompilerServices;
using System.Text.Json;
using GeminiMockApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace GeminiMockApi.Controllers;

[ApiController]
[Route("v1beta/models")]
public class GeminiController : ControllerBase
{
    private readonly ILogger<GeminiController> _logger;

    // Configurable "Stress Test" parameters
    private const int STREAM_CHUNKS = 500;      // How many chunks to stream
    private const int CHUNK_DELAY_MS = 10;      // Speed of streaming (lower is faster)
    private const int CHUNK_SIZE = 50;          // Words per chunk

    public GeminiController(ILogger<GeminiController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discovery Endpoint: Returns the fake model so the client can select it.
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
                description: "High-throughput local simulation for stress testing streaming pipelines.",
                supportedGenerationMethods: new[] { "generateContent" }
            )
        });

        return Ok(response);
    }

    /// <summary>
    /// Non-Streaming Endpoint: Returns a massive block of text instantly.
    /// </summary>
    [HttpPost("{model}:generateContent")]
    public IActionResult GenerateContent(string model, [FromBody] GeminiRequest request)
    {
        _logger.LogInformation($"Generating non-streaming content for {model}...");

        var text = GenerateLoremIpsum(500); // Return 500 words

        var response = new GeminiResponse(new List<Candidate>
        {
            new Candidate(new Content(new List<Part> { new Part(text) }))
        });

        return Ok(response);
    }

    /// <summary>
    /// Streaming Endpoint: The Data Hose.
    /// Uses Server-Sent Events (SSE) to flood the client with data.
    /// </summary>
    [HttpPost("{model}:streamGenerateContent")]
    public async Task StreamGenerateContent(string model, [FromBody] GeminiRequest request)
    {
        _logger.LogInformation($"⚡ STARTING STREAM for {model} (Target: {STREAM_CHUNKS} chunks)...");

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var writer = new StreamWriter(Response.Body);

        for (int i = 0; i < STREAM_CHUNKS; i++)
        {
            // Simulate processing time (or remove for pure throughput testing)
            if (CHUNK_DELAY_MS > 0)
                await Task.Delay(CHUNK_DELAY_MS);

            var chunkText = $"[{i + 1}/{STREAM_CHUNKS}]" + GenerateLoremIpsum(CHUNK_SIZE);


            var payload = new GeminiResponse(new List<Candidate>
            {
                new Candidate(new Content(new List<Part> { new Part(chunkText) }))
            });

            // Format as SSE "data: {json}\n\n"
            string json = JsonSerializer.Serialize(payload);
            await writer.WriteAsync($"data: {json}\n\n");
            await writer.FlushAsync();
        }

        // Send completion signal if your client looks for one, 
        // though standard Gemini client just stops when stream closes.
        // await writer.WriteAsync("data: [DONE]\n\n");
        // await writer.FlushAsync();

        _logger.LogInformation("✅ STREAM COMPLETE");
    }

    private static string GenerateLoremIpsum(int wordCount)
    {
        var words = new[] { "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "performance", "testing", "stream", "buffer", "latency", "throughput", "dotnet", "async", "await", "task", "memory", "allocation" };
        var rand = new Random();
        return string.Join(" ", Enumerable.Range(0, wordCount).Select(_ => words[rand.Next(words.Length)]));
    }
}
