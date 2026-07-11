// GeminiClient.Tests/SessionStatisticsTests.cs
using GeminiClient;
using Xunit;

namespace GeminiClient.Tests;

public class SessionStatisticsTests
{
    private static RequestRecord Success(string model, double seconds, double? ttft = null) => new()
    {
        Model = model,
        Streaming = ttft.HasValue,
        Outcome = RequestOutcome.Success,
        Elapsed = TimeSpan.FromSeconds(seconds),
        TimeToFirstToken = ttft.HasValue ? TimeSpan.FromSeconds(ttft.Value) : null,
        ResponseLength = 100
    };

    private static RequestRecord Failure(string model, double seconds, string category) => new()
    {
        Model = model,
        Streaming = false,
        Outcome = RequestOutcome.Failed,
        Elapsed = TimeSpan.FromSeconds(seconds),
        ErrorCategory = category
    };

    [Fact]
    public void Counts_IncludeFailuresAndCancellations()
    {
        SessionStatistics stats = new();
        stats.Record(Success("m", 2));
        stats.Record(Failure("m", 1, "RateLimited"));
        stats.Record(new RequestRecord { Model = "m", Streaming = false, Outcome = RequestOutcome.Cancelled, Elapsed = TimeSpan.FromSeconds(3) });

        Assert.Equal(3, stats.TotalAttempts);
        Assert.Equal(1, stats.SuccessCount);
        Assert.Equal(1, stats.FailureCount);
        Assert.Equal(1, stats.CancelledCount);
    }

    [Fact]
    public void AverageFailureTime_IsComputedFromFailuresOnly()
    {
        SessionStatistics stats = new();
        stats.Record(Success("m", 10));            // must not affect failure timing
        stats.Record(Failure("m", 2, "ServerError"));
        stats.Record(Failure("m", 4, "ServerError"));

        Assert.NotNull(stats.AverageFailureTime);
        Assert.Equal(3, stats.AverageFailureTime!.Value.TotalSeconds, precision: 3);
    }

    [Fact]
    public void SuccessRate_IsFractionOfAttempts()
    {
        SessionStatistics stats = new();
        stats.Record(Success("m", 1));
        stats.Record(Success("m", 1));
        stats.Record(Failure("m", 1, "Network"));
        stats.Record(Failure("m", 1, "Network"));

        Assert.Equal(0.5, stats.SuccessRate, precision: 3);
    }

    [Fact]
    public void AverageTimeToFirstToken_UsesOnlySuccessfulStreamingRecords()
    {
        SessionStatistics stats = new();
        stats.Record(Success("m", 5, ttft: 1));
        stats.Record(Success("m", 5, ttft: 3));
        stats.Record(Failure("m", 1, "Timeout"));

        Assert.NotNull(stats.AverageTimeToFirstToken);
        Assert.Equal(2, stats.AverageTimeToFirstToken!.Value.TotalSeconds, precision: 3);
    }

    [Fact]
    public void FailureCategories_AreGroupedAndCounted()
    {
        SessionStatistics stats = new();
        stats.Record(Failure("m", 1, "RateLimited"));
        stats.Record(Failure("m", 1, "RateLimited"));
        stats.Record(Failure("m", 1, "ServiceUnavailable"));

        Assert.Equal(2, stats.FailureCategories["RateLimited"]);
        Assert.Equal(1, stats.FailureCategories["ServiceUnavailable"]);
    }

    [Fact]
    public void ModelUsage_TracksPerModelAttemptsAndSuccesses()
    {
        SessionStatistics stats = new();
        stats.Record(Success("flash", 1));
        stats.Record(Failure("flash", 1, "Network"));
        stats.Record(Success("pro", 2));

        ModelUsage flash = Assert.Single(stats.ModelUsage, u => u.Model == "flash");
        Assert.Equal(2, flash.Attempts);
        Assert.Equal(1, flash.Successes);
    }

    [Fact]
    public void FastestAndSlowest_ConsiderSuccessesOnly()
    {
        SessionStatistics stats = new();
        stats.Record(Success("m", 5));
        stats.Record(Success("m", 1));
        stats.Record(Success("m", 3));
        stats.Record(Failure("m", 100, "ServerError")); // ignored for success timings

        Assert.Equal(1, stats.FastestSuccessTime!.Value.TotalSeconds, precision: 3);
        Assert.Equal(5, stats.SlowestSuccessTime!.Value.TotalSeconds, precision: 3);
    }
}
