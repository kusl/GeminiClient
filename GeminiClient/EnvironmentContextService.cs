// GeminiClient/EnvironmentContextService.cs
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GeminiClient.Models;
using Microsoft.Extensions.Logging;

namespace GeminiClient;

public class EnvironmentContextService : IEnvironmentContextService
{
    private readonly ILogger<EnvironmentContextService> _logger;

    public EnvironmentContextService(ILogger<EnvironmentContextService> logger)
    {
        _logger = logger;
    }

    public Content GetSystemInstruction()
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;
        var tz = TimeZoneInfo.Local;
        var culture = CultureInfo.CurrentCulture;

        sb.AppendLine("### SYSTEM ENVIRONMENT CONTEXT ###");
        sb.AppendLine("You are running locally on the user's machine. The following context is 100% accurate and strictly defines your current reality:");
        sb.AppendLine();

        // Temporal Grounding
        sb.AppendLine("[TEMPORAL DATA]");
        sb.AppendLine($"Local Time: {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Day of Week: {now:DayOfWeek}");
        sb.AppendLine($"UTC Time: {utcNow:yyyy-MM-dd HH:mm:ss} Z");
        sb.AppendLine($"Timezone: {tz.DisplayName}");
        sb.AppendLine($"Timezone ID: {tz.Id} (Offset: {tz.BaseUtcOffset})");
        sb.AppendLine();

        // OS & User Grounding
        sb.AppendLine("[SYSTEM DATA]");
        sb.AppendLine($"OS Platform: {GetOsName()}");
        sb.AppendLine($"OS Version: {Environment.OSVersion}");
        sb.AppendLine($"Machine Name: {Environment.MachineName}");
        sb.AppendLine($"User Name: {Environment.UserName}");
        sb.AppendLine($"Locale: {culture.Name} ({culture.DisplayName})");
        sb.AppendLine();

        // Operational Instructions
        sb.AppendLine("[OPERATIONAL INSTRUCTIONS]");
        sb.AppendLine("1. Use the Local Time above for any queries regarding 'now', 'today', or 'current time'.");
        sb.AppendLine("2. If asked about the system, refer to the OS Platform and User Name provided above.");
        sb.AppendLine("3. Do not Hallucinate the date. Trust this context over your training data.");

        var instructionText = sb.ToString();

        _logger.LogDebug("Generated System Instruction ({Length} chars)", instructionText.Length);

        return new Content
        {
            Role = "system", // Gemini treats system instructions as a special content block, but internally we can label it
            Parts = [new Part { Text = instructionText }]
        };
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsLinux()) return "Linux";
        if (OperatingSystem.IsMacOS()) return "macOS";
        return RuntimeInformation.OSDescription;
    }
}
