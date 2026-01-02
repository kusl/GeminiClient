// GeminiClient/IEnvironmentContextService.cs
using GeminiClient.Models;

namespace GeminiClient;

public interface IEnvironmentContextService
{
    /// <summary>
    /// Generates a system instruction containing real-time environmental context
    /// (Time, Date, OS, User, Locale) to ground the LLM.
    /// </summary>
    Content GetSystemInstruction();
}
