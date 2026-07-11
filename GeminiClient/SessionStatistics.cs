// GeminiClient/SessionStatistics.cs
namespace GeminiClient;

/// <summary>The final disposition of a single attempt to generate a response.</summary>
public enum RequestOutcome
{
    Success,
    Empty,      // a 2xx response that contained no usable content
    Failed,     // an error (network, HTTP, parse, blocked, ...)
    Cancelled   // the caller cancelled before completion
}

/// <summary>
/// An immutable record of one attempt to generate a response — whether it succeeded, failed,
/// returned nothing, or was cancelled — including how long it took to reach that outcome. Failed
/// and cancelled attempts are first-class so session statistics can report them (and their latency),
/// rather than silently counting only successes.
/// </summary>
public sealed record RequestRecord
{
    public required string Model { get; init; }
    public required bool Streaming { get; init; }
    public required RequestOutcome Outcome { get; init; }

    /// <summary>Wall-clock time from request start to this outcome (success, failure, or cancellation).</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>For successful streaming responses, the latency until the first token arrived.</summary>
    public TimeSpan? TimeToFirstToken { get; init; }

    public int PromptLength { get; init; }
    public int ResponseLength { get; init; }

    /// <summary>A short category for failures (e.g. the <see cref="GeminiErrorKind"/> name), otherwise null.</summary>
    public string? ErrorCategory { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public bool IsSuccess => Outcome == RequestOutcome.Success;
    public bool IsFailure => Outcome == RequestOutcome.Failed;
}

/// <summary>Per-model usage rollup.</summary>
public sealed record ModelUsage(string Model, int Attempts, int Successes, TimeSpan? AverageSuccessTime);

/// <summary>
/// Aggregates <see cref="RequestRecord"/>s into session-level statistics. Pure and side-effect free
/// so it can be unit-tested; a UI layer is responsible for formatting/display.
/// </summary>
public sealed class SessionStatistics
{
    private readonly List<RequestRecord> _records = [];

    public IReadOnlyList<RequestRecord> Records => _records;

    public void Record(RequestRecord record) => _records.Add(record);

    public int TotalAttempts => _records.Count;
    public int SuccessCount => _records.Count(r => r.Outcome == RequestOutcome.Success);
    public int FailureCount => _records.Count(r => r.Outcome == RequestOutcome.Failed);
    public int CancelledCount => _records.Count(r => r.Outcome == RequestOutcome.Cancelled);
    public int EmptyCount => _records.Count(r => r.Outcome == RequestOutcome.Empty);

    public double SuccessRate => TotalAttempts == 0 ? 0d : (double)SuccessCount / TotalAttempts;

    public long TotalResponseCharacters => _records.Where(r => r.IsSuccess).Sum(r => (long)r.ResponseLength);

    public TimeSpan? AverageSuccessTime => AverageOf(_records.Where(r => r.IsSuccess));
    public TimeSpan? FastestSuccessTime => MinOf(_records.Where(r => r.IsSuccess));
    public TimeSpan? SlowestSuccessTime => MaxOf(_records.Where(r => r.IsSuccess));

    /// <summary>Average time-to-failure across failed attempts — surfaces "how long did failures take".</summary>
    public TimeSpan? AverageFailureTime => AverageOf(_records.Where(r => r.IsFailure));

    public TimeSpan? AverageTimeToFirstToken
    {
        get
        {
            List<TimeSpan> ttfts = _records
                .Where(r => r.IsSuccess && r.TimeToFirstToken.HasValue)
                .Select(r => r.TimeToFirstToken!.Value)
                .ToList();
            return ttfts.Count == 0 ? null : TimeSpan.FromTicks((long)ttfts.Average(t => t.Ticks));
        }
    }

    public TimeSpan SessionDuration =>
        _records.Count == 0 ? TimeSpan.Zero : DateTimeOffset.Now - _records[0].Timestamp;

    public IReadOnlyDictionary<string, int> FailureCategories =>
        _records
            .Where(r => r.IsFailure && !string.IsNullOrEmpty(r.ErrorCategory))
            .GroupBy(r => r.ErrorCategory!)
            .ToDictionary(g => g.Key, g => g.Count());

    public IReadOnlyList<ModelUsage> ModelUsage =>
        _records
            .GroupBy(r => r.Model)
            .Select(g => new ModelUsage(
                g.Key,
                g.Count(),
                g.Count(r => r.Outcome == RequestOutcome.Success),
                AverageOf(g.Where(r => r.IsSuccess))))
            .OrderByDescending(m => m.Attempts)
            .ToList();

    private static TimeSpan? AverageOf(IEnumerable<RequestRecord> records)
    {
        List<RequestRecord> list = records.ToList();
        return list.Count == 0 ? null : TimeSpan.FromTicks((long)list.Average(r => r.Elapsed.Ticks));
    }

    private static TimeSpan? MinOf(IEnumerable<RequestRecord> records)
    {
        List<RequestRecord> list = records.ToList();
        return list.Count == 0 ? null : list.Min(r => r.Elapsed);
    }

    private static TimeSpan? MaxOf(IEnumerable<RequestRecord> records)
    {
        List<RequestRecord> list = records.ToList();
        return list.Count == 0 ? null : list.Max(r => r.Elapsed);
    }
}
