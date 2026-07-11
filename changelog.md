# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to follow
semantic versioning. The base version lives solely in `Directory.Build.props`; CI appends the
run number (e.g. `0.0.8` → `0.0.8.<run>`).

## [0.0.8] - 2026-07-11

A reliability-focused release: better error handling, honest session statistics (failures and
time-to-failure are now counted), richer and more correct environment grounding, and safer
cross-platform behaviour. See `docs/adr/` for the reasoning behind these changes.

### Added
- Typed error model: `GeminiApiException` with a categorized `Kind`, HTTP status, Google `status`
  string, server-suggested `RetryAfter`, and transience / zero-tier-quota flags, plus a pure,
  unit-tested `GeminiErrorParser` that reads Gemini's JSON error envelope (including
  `RetryInfo.retryDelay` and `limit: 0`).
- Bounded, automatic retry of transient failures (429/503/5xx) honouring the server's `Retry-After`
  (capped) with exponential backoff and jitter; `MaxRetries` is now actually used.
- Graceful cancellation: Ctrl+C cancels an in-flight request without killing the process (press
  again at the prompt to exit); partial streamed text is preserved and the session summary is always
  printed.
- Session telemetry that records every attempt (success / empty / failed / cancelled) with elapsed
  time, time-to-first-token, and an error category; the summary now reports success rate, average
  time-to-failure, average time-to-first-token, and failures grouped by type.
- Model ranking (`GeminiModelRanking`) that prefers stable, general-purpose text models and demotes
  preview/specialised variants; the picker labels preview and specialised models.
- A per-session diagnostics log file and a redirect-aware console helper.
- A per-user `appsettings.json` location for durable configuration, plus environment-variable,
  user-secrets (Development), and command-line configuration sources.
- Prompt-feedback / block detection so a blocked prompt reports a clear reason instead of an empty
  response; token-usage metadata is parsed and logged.
- Mock API error simulations (`simulate:429|quota|503|500|400|block`) for offline testing of the
  new error handling.
- A `help` command and a `GeminiClient.Tests` xUnit project covering the error parser, model
  ranking, session statistics, and environment-context builder.

### Changed
- The console no longer registers a console logging provider; all framework/library logs go to the
  diagnostics file so they never interleave with the UI. User-facing errors are shown as short,
  friendly, actionable messages.
- Configuration is resolved relative to the executable (not the current directory), making the
  installed global tool robust to the working directory it is launched from.
- Streaming requests no longer carry an overall HTTP timeout (long generations are legitimate);
  non-streaming requests apply a per-call timeout derived from `TimeoutSeconds`.
- Copyright year in build metadata is computed dynamically.

### Fixed
- Garbled console output during streaming caused by asynchronous log lines writing to the console
  concurrently with the UI.
- Environment grounding rendered the day of week via an invalid format string; it now shows the
  correct day name.
- Environment grounding reported the time zone's base UTC offset, ignoring daylight saving time; it
  now reports the current (DST-aware) offset.
- The same error was logged multiple times as it propagated; failures are now logged once.
- `Console.WindowWidth` could throw when output was redirected; console access is now redirect-safe.

## [0.0.7]
- Environment-context grounding, multi-turn conversation support, `reset` command, and
  XDG-compliant logging.

## [0.0.6]
- Real-time streaming support with Server-Sent Events.

## [0.0.5]
- Improved terminal compatibility by removing destructive console clears.

## [0.0.4]
- Initial interactive console client with dynamic model discovery.
