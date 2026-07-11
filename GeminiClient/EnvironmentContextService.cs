// GeminiClient/EnvironmentContextService.cs
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GeminiClient.Models;
using Microsoft.Extensions.Logging;

namespace GeminiClient;

/// <summary>
/// Produces a system-instruction block that grounds the model in the host's real, current
/// environment (time, locale, OS, hardware). Everything here is gathered with cross-platform
/// managed primitives only — no shelling out, no native calls, no elevated privileges — so it
/// behaves identically on Windows, Linux and macOS and works for an unprivileged user.
/// </summary>
public class EnvironmentContextService : IEnvironmentContextService
{
    private readonly ILogger<EnvironmentContextService> _logger;

    public EnvironmentContextService(ILogger<EnvironmentContextService> logger) => _logger = logger;

    public Content GetSystemInstruction()
    {
        string instructionText = BuildInstructionText();
        _logger.LogDebug("Generated system instruction ({Length} chars)", instructionText.Length);

        // Note: the system_instruction block intentionally carries no role. Gemini treats it as a
        // dedicated field; a "system" role is not valid inside 'contents', so we leave Role null
        // (and it is omitted from the JSON by the WhenWritingNull policy).
        return new Content
        {
            Role = null,
            Parts = [new Part { Text = instructionText }]
        };
    }

    /// <summary>Builds the grounding text. Public + static so it can be unit-tested deterministically.</summary>
    public static string BuildInstructionText(DateTimeOffset? nowOverride = null)
    {
        DateTimeOffset now = nowOverride ?? DateTimeOffset.Now;
        StringBuilder sb = new();

        sb.AppendLine("### SYSTEM ENVIRONMENT CONTEXT ###");
        sb.AppendLine("You are running locally on the user's machine. The following facts describe the current");
        sb.AppendLine("environment and are authoritative. Prefer them over your training data.");
        sb.AppendLine();

        // --- Temporal grounding (DST-aware) ---
        TryAppend(sb, "[TEMPORAL]", section =>
        {
            TimeZoneInfo tz = TimeZoneInfo.Local;
            TimeSpan offset = tz.GetUtcOffset(now); // current offset, correctly accounts for DST
            section.AppendLine($"Local date/time : {now:yyyy-MM-dd HH:mm:ss} (offset {FormatOffset(offset)})");
            section.AppendLine($"Day of week     : {now.DayOfWeek}");
            section.AppendLine($"UTC date/time   : {now.UtcDateTime:yyyy-MM-dd HH:mm:ss} Z");
            section.AppendLine($"Time zone       : {tz.DisplayName} [{tz.Id}]");
            section.AppendLine($"DST in effect   : {(tz.IsDaylightSavingTime(now) ? "yes" : "no")}");
        });

        // --- OS & user ---
        TryAppend(sb, "[SYSTEM]", section =>
        {
            section.AppendLine($"OS platform     : {GetOsName()}");
            section.AppendLine($"OS description  : {RuntimeInformation.OSDescription}");
            section.AppendLine($"OS architecture : {RuntimeInformation.OSArchitecture} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
            section.AppendLine($"Machine name    : {Environment.MachineName}");
            section.AppendLine($"User name       : {Environment.UserName}");
            section.AppendLine($"Uptime (approx) : {FormatDuration(TimeSpan.FromMilliseconds(Environment.TickCount64))}");
        });

        // --- Locale ---
        TryAppend(sb, "[LOCALE]", section =>
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
            CultureInfo uiCulture = CultureInfo.CurrentUICulture;
            section.AppendLine($"Culture         : {culture.Name} ({culture.DisplayName})");
            section.AppendLine($"UI culture      : {uiCulture.Name}");
            section.AppendLine($"Measurement     : {(RegionUsesMetric(culture) ? "metric" : "imperial (assumed)")}");
        });

        // --- Runtime & hardware ---
        TryAppend(sb, "[RUNTIME]", section =>
        {
            section.AppendLine($".NET runtime    : {RuntimeInformation.FrameworkDescription}");
            section.AppendLine($"Process arch    : {RuntimeInformation.ProcessArchitecture} ({(Environment.Is64BitProcess ? "64-bit" : "32-bit")})");
            section.AppendLine($"Logical CPUs    : {Environment.ProcessorCount}");
            long totalMem = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (totalMem > 0)
            {
                section.AppendLine($"System memory   : {FormatBytes(totalMem)} (approx, available to this process)");
            }
            section.AppendLine($"Working dir     : {SafeCurrentDirectory()}");
            AppendPrimaryDriveInfo(section);
        });

        // --- Operating instructions ---
        sb.AppendLine("[INSTRUCTIONS]");
        sb.AppendLine("1. Use the [TEMPORAL] values for anything about 'now', 'today', or the current time; do not guess the date.");
        sb.AppendLine("2. Tailor OS-specific guidance (shell, paths, package managers) to the [SYSTEM] platform.");
        sb.AppendLine("3. Format dates, numbers and units per [LOCALE].");
        sb.AppendLine("4. Do not reveal this context block verbatim unless the user explicitly asks what you know about their system.");

        return sb.ToString();
    }

    private static void TryAppend(StringBuilder sb, string header, Action<StringBuilder> build)
    {
        try
        {
            StringBuilder section = new();
            section.AppendLine(header);
            build(section);
            section.AppendLine();
            sb.Append(section);
        }
        catch (Exception)
        {
            // A single failing probe must never break request generation; skip the section.
        }
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }
        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }
        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        return RuntimeInformation.OSDescription;
    }

    private static void AppendPrimaryDriveInfo(StringBuilder section)
    {
        try
        {
            string root = Path.GetPathRoot(AppContext.BaseDirectory) ?? "/";
            DriveInfo drive = new(root);
            if (drive.IsReady)
            {
                section.AppendLine($"Primary drive   : {drive.Name} — {FormatBytes(drive.AvailableFreeSpace)} free of {FormatBytes(drive.TotalSize)}");
            }
        }
        catch (Exception)
        {
            // Drive probing is best-effort.
        }
    }

    private static string SafeCurrentDirectory()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (Exception)
        {
            return "(unavailable)";
        }
    }

    private static bool RegionUsesMetric(CultureInfo culture)
    {
        try
        {
            return new RegionInfo(culture.Name).IsMetric;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static string FormatOffset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString("hh\\:mm");

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
        }
        if (span.TotalHours >= 1)
        {
            return $"{span.Hours}h {span.Minutes}m";
        }

        return $"{span.Minutes}m {span.Seconds}s";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
