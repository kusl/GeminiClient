# ADR-0007: Typed API errors and user-facing messaging

- Status: Accepted
- Date: 2026-07-11

## Context

The original client surfaced failures by calling `EnsureSuccessStatusCode()` and letting a generic
`HttpRequestException` propagate, with call sites sniffing the message text (for example, checking
whether it contained "500"). Meanwhile Gemini returns a rich JSON error envelope — a numeric code,
a `status` string such as `RESOURCE_EXHAUSTED`, a human-readable message, and structured `details`
including `RetryInfo.retryDelay` and free-tier `limit: 0` signals — all of which was being
discarded. The same error was also logged two or three times as it bubbled up.

## Decision

We will model API failures explicitly:

- A `GeminiApiException` carries a categorized `Kind`, the HTTP status, the Google `status` string,
  a server-suggested `RetryAfter`, and flags for transience and the zero-tier quota case.
- A pure, testable `GeminiErrorParser` turns an HTTP status plus response body into that exception,
  parsing the envelope and the retry delay.
- The library logs failures once at debug level and throws the typed exception; the caller decides
  how to present it. The UI maps `Kind` to a single friendly, actionable message (for example,
  suggesting the `model` command when a model is rate-limited or unavailable) and records the rest.

## Consequences

- Behaviour keys off structured fields instead of brittle string matching, and the useful parts of
  the server's response (retry delay, tier information) reach the user.
- Each failure is reported to the user once and logged once.
- New error shapes may require extending the parser and the `Kind` enumeration.
