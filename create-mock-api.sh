#!/bin/bash
set -e

# ==============================================================================
# Gemini Mock API Generator
# Creates a high-throughput fake Gemini server for performance testing
# ==============================================================================

echo "🚀 Initializing Gemini Mock API Project..."

# 1. Create the Web API Project
# We use --no-https to simplify local testing (no cert validation needed)
if [ ! -d "GeminiMockApi" ]; then
    dotnet new webapi -n GeminiMockApi -f net10.0 --no-https
else
    echo "Directory GeminiMockApi already exists, skipping creation..."
fi

# 2. Add to Solution
if [ -f "LearningByDoing.sln" ]; then
    echo "📦 Adding project to solution..."
    dotnet sln LearningByDoing.sln add GeminiMockApi/GeminiMockApi.csproj
else
    echo "⚠️  Solution file not found. Skipping sln add."
fi

# 3. Create Data Models (Simplified versions of the real ones)
echo "📝 Generating Data Models..."
mkdir -p GeminiMockApi/Models
cat <<EOF > GeminiMockApi/Models/GeminiDTOs.cs
namespace GeminiMockApi.Models;

public record ModelListResponse(List<GeminiModel> models);

public record GeminiModel(
    string name, 
    string displayName, 
    string description, 
    string[] supportedGenerationMethods,
    string version = "1.0.0"
);

// Minimal request structure to accept payloads without validation errors
public record GeminiRequest(List<object> contents);

public record GeminiResponse(List<Candidate> candidates);
public record Candidate(Content content, string finishReason = "STOP", int index = 0);
public record Content(List<Part> parts, string role = "model");
public record Part(string text);
EOF

# 4. Create the Controller
# This implements the exact routes used by GeminiClient
echo "⚙️  Generating Controller Logic..."
cat <<EOF > GeminiMockApi/Controllers/GeminiController.cs
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

            var chunkText = \$\" [{i+1}/{STREAM_CHUNKS}] \" + GenerateLoremIpsum(CHUNK_SIZE);
            
            var payload = new GeminiResponse(new List<Candidate> 
            { 
                new Candidate(new Content(new List<Part> { new Part(chunkText) })) 
            });

            // Format as SSE "data: {json}\n\n"
            string json = JsonSerializer.Serialize(payload);
            await writer.WriteAsync(\$"data: {json}\n\n");
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
EOF

# 5. Update Program.cs
echo "🔧 Configuring Startup..."
cat <<EOF > GeminiMockApi/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Add CORS to allow requests from anywhere during testing
builder.Services.AddCors(options => options.AddPolicy("AllowAll", policy => 
    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors("AllowAll");
app.MapControllers();

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("=================================================");
Console.WriteLine("   ♊ GEMINI MOCK API RUNNING");
Console.WriteLine("   📡 Listening on: http://localhost:5000");
Console.WriteLine("=================================================");
Console.ResetColor();

app.Run("http://localhost:5000");
EOF

echo ""
echo "✅ Mock API Created Successfully!"
echo "================================================="
echo "INSTRUCTIONS:"
echo "1. Run the Mock API in a separate terminal:"
echo "   dotnet run --project GeminiMockApi"
echo ""
echo "2. Configure your Client (GeminiClientConsole/appsettings.json) or Environment:"
echo "   BaseUrl: \"http://localhost:5000/\""
echo "   ApiKey:  \"any-text-works-locally\""
echo ""
echo "3. Run the Client:"
echo "   dotnet run --project GeminiClientConsole"
echo "================================================="
