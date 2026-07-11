// GeminiClient.Tests/EnvironmentContextServiceTests.cs
using GeminiClient;
using Xunit;

namespace GeminiClient.Tests;

public class EnvironmentContextServiceTests
{
    [Fact]
    public void BuildInstructionText_ContainsAllSections()
    {
        string text = EnvironmentContextService.BuildInstructionText();

        Assert.Contains("[TEMPORAL]", text);
        Assert.Contains("[SYSTEM]", text);
        Assert.Contains("[LOCALE]", text);
        Assert.Contains("[RUNTIME]", text);
        Assert.Contains("[INSTRUCTIONS]", text);
    }

    [Fact]
    public void BuildInstructionText_RendersDayOfWeekAsName_NotGarbledFormat()
    {
        // 2026-01-05 is a Monday. The original bug used "{now:DayOfWeek}", which is an invalid
        // custom format string ('D','a','y'...) and produced garbled output rather than the day name.
        DateTimeOffset monday = new(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);

        string text = EnvironmentContextService.BuildInstructionText(monday);

        Assert.Contains("Monday", text);
        Assert.DoesNotContain("DayOfWeek", text);
    }

    [Fact]
    public void BuildInstructionText_IncludesAnOffsetLine()
    {
        string text = EnvironmentContextService.BuildInstructionText(DateTimeOffset.Now);

        // DST-correct offset is rendered as "(offset +HH:mm)" / "(offset -HH:mm)".
        Assert.Contains("offset ", text);
    }
}
