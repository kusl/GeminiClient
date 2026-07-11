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

    // The client uses SourceGen (GeminiJsonContext) which expects camelCase.
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
                description: "Local simulation. Prompts: '100,20,50' (chunks,delay,words); 'simulate:429|quota|503|500|400|block'",
                supportedGenerationMethods: new[] { "generateContent" }
            )
        });
        return Ok(response);
    }

    [HttpPost("{model}:generateContent")]
    public IActionResult GenerateContent(string model, [FromBody] GeminiRequest request)
    {
        string prompt = ExtractPrompt(request);

        if (TryBuildErrorResult(prompt, out IActionResult? error, out string? blockedJson))
        {
            if (blockedJson is not null)
            {
                // A 200 response whose prompt was blocked (no candidates).
                return Content(blockedJson, "application/json");
            }
            return error!;
        }

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
        string prompt = ExtractPrompt(request);

        // Error simulations must be returned BEFORE switching to the SSE content type.
        if (TryBuildErrorResult(prompt, out IActionResult? error, out string? blockedJson))
        {
            if (blockedJson is not null)
            {
                Response.StatusCode = StatusCodes.Status200OK;
                Response.ContentType = "application/json";
                await Response.WriteAsync(blockedJson);
                return;
            }

            if (error is ObjectResult objectResult)
            {
                Response.StatusCode = objectResult.StatusCode ?? 500;
                Response.ContentType = "application/json";
                await Response.WriteAsync((string)objectResult.Value!);
            }
            return;
        }

        var (chunks, delay, size) = ParseStressParams(prompt);
        _logger.LogInformation("START: {Model} | Chunks: {Chunks}, Delay: {Delay}ms, Size: {Size}", model, chunks, delay, size);

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        await using (var writer = new StreamWriter(Response.Body))
        {
            for (int i = 0; i < chunks; i++)
            {
                if (HttpContext.RequestAborted.IsCancellationRequested)
                {
                    break;
                }

                if (delay > 0)
                {
                    await Task.Delay(delay);
                }

                var chunkText = $"[{i + 1}/{chunks}] " + GenerateLoremIpsum(size);
                var payload = new GeminiResponse(new List<Candidate>
                {
                    new Candidate(new Content(new List<Part> { new Part(chunkText) }))
                });

                string json = JsonSerializer.Serialize(payload, _jsonOptions);
                await writer.WriteAsync($"data: {json}\n\n");
                await writer.FlushAsync();
            }
        }

        _logger.LogInformation("FINISHED");
    }

    private static string ExtractPrompt(GeminiRequest request) =>
        request.contents?.LastOrDefault()?.parts?.FirstOrDefault()?.text ?? string.Empty;

    /// <summary>
    /// Recognises "simulate:*" prompts and produces the matching Google-style error envelope so the
    /// client's error handling and retry logic can be exercised without hitting the real API.
    /// Returns true when the prompt is a simulation. <paramref name="blockedJson"/> is set for the
    /// content-blocked case (a 200 with no candidates).
    /// </summary>
    private bool TryBuildErrorResult(string prompt, out IActionResult? error, out string? blockedJson)
    {
        error = null;
        blockedJson = null;

        string trimmed = prompt.Trim().ToLowerInvariant();
        if (!trimmed.StartsWith("simulate:"))
        {
            return false;
        }

        string which = trimmed["simulate:".Length..].Trim();
        _logger.LogInformation("Simulating error scenario: {Scenario}", which);

        switch (which)
        {
            case "429":
                error = new ObjectResult(RateLimitEnvelope(quotaZero: false)) { StatusCode = 429 };
                return true;
            case "quota":
                error = new ObjectResult(RateLimitEnvelope(quotaZero: true)) { StatusCode = 429 };
                return true;
            case "503":
                error = new ObjectResult(SimpleEnvelope(503, "UNAVAILABLE",
                    "The model is overloaded. Please try again later."))
                { StatusCode = 503 };
                return true;
            case "500":
                error = new ObjectResult(SimpleEnvelope(500, "INTERNAL",
                    "An internal error occurred."))
                { StatusCode = 500 };
                return true;
            case "400":
                error = new ObjectResult(SimpleEnvelope(400, "INVALID_ARGUMENT",
                    "Request contains an invalid argument."))
                { StatusCode = 400 };
                return true;
            case "block":
                blockedJson = BlockedResponseJson();
                return true;
            default:
                return false;
        }
    }

    private static string RateLimitEnvelope(bool quotaZero)
    {
        string message = quotaZero
            ? "You exceeded your current quota. limit: 0. Please check your plan and billing details."
            : "Resource has been exhausted (e.g. check quota). Please retry in 42.4s.";

        // google.rpc.RetryInfo carries the server-suggested delay.
        var envelope = new
        {
            error = new
            {
                code = 429,
                message,
                status = "RESOURCE_EXHAUSTED",
                details = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["@type"] = "type.googleapis.com/google.rpc.RetryInfo",
                        ["retryDelay"] = "42s"
                    }
                }
            }
        };
        return JsonSerializer.Serialize(envelope, _jsonOptions);
    }

    private static string SimpleEnvelope(int code, string status, string message)
    {
        var envelope = new { error = new { code, message, status } };
        return JsonSerializer.Serialize(envelope, _jsonOptions);
    }

    private static string BlockedResponseJson()
    {
        var payload = new
        {
            promptFeedback = new
            {
                blockReason = "SAFETY",
                safetyRatings = new object[]
                {
                    new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", probability = "HIGH", blocked = true }
                }
            }
        };
        return JsonSerializer.Serialize(payload, _jsonOptions);
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
                // Clamp: very small delays can saturate the network buffer and cause 502s.
                return (c, Math.Max(d, 10), s);
            }
        }

        return (
            chunks: _rng.Next(50, 601),
            delay: _rng.Next(10, 501),
            size: _rng.Next(10, 101)
        );
    }

    private static string GenerateLoremIpsum(int wordCount)
    {
        var words = new[] { "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "performance", "testing", "stream", "dotnet", "async", "task" };
        return string.Join(" ", Enumerable.Range(0, wordCount).Select(_ => words[_rng.Next(words.Length)]));
    }
}
