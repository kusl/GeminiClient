// GeminiClient.Tests/GeminiModelRankingTests.cs
using GeminiClient;
using GeminiClient.Models;
using Xunit;

namespace GeminiClient.Tests;

public class GeminiModelRankingTests
{
    private static GeminiModel Model(string id) => new() { Name = $"models/{id}" };

    [Fact]
    public void RecommendDefault_PrefersStableFlashOverPreviewAndSpecialised()
    {
        List<GeminiModel> models =
        [
            Model("gemini-omni-flash-preview"),   // preview — must not win
            Model("gemini-2.5-flash-image"),       // specialised — must not win
            Model("gemini-2.5-flash"),             // the sensible default
            Model("gemini-1.5-pro")
        ];

        string? best = GeminiModelRanking.RecommendDefault(models);

        Assert.Equal("gemini-2.5-flash", best);
    }

    [Fact]
    public void RecommendDefault_DoesNotPickPreviewWhenStableExists()
    {
        List<GeminiModel> models =
        [
            Model("gemini-2.0-flash-exp"),
            Model("gemini-2.0-flash")
        ];

        Assert.Equal("gemini-2.0-flash", GeminiModelRanking.RecommendDefault(models));
    }

    [Theory]
    [InlineData("gemini-2.5-flash-image", true)]
    [InlineData("gemini-2.5-flash-tts", true)]
    [InlineData("gemini-robotics-er", true)]
    [InlineData("gemini-2.5-flash", false)]
    public void IsSpecialised_FlagsNonTextModels(string id, bool expected)
    {
        Assert.Equal(expected, GeminiModelRanking.IsSpecialised(id));
    }

    [Theory]
    [InlineData("gemini-2.0-flash-preview", true)]
    [InlineData("gemini-2.0-flash-exp", true)]
    [InlineData("learnlm-2.0-flash", true)]
    [InlineData("gemini-2.5-flash", false)]
    public void IsUnstable_FlagsPreviewAndExperimental(string id, bool expected)
    {
        Assert.Equal(expected, GeminiModelRanking.IsUnstable(id));
    }

    [Fact]
    public void ForInteractiveChat_OrdersGeneralPurposeTextBeforeSpecialised()
    {
        List<GeminiModel> models =
        [
            Model("imagen-3.0-generate"),
            Model("gemini-2.5-flash")
        ];

        IReadOnlyList<GeminiModel> ordered = GeminiModelRanking.ForInteractiveChat(models);

        Assert.Equal("gemini-2.5-flash", ordered[0].GetModelIdentifier());
    }
}
