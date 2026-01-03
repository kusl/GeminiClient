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
