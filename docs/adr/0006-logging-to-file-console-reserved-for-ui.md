# ADR-0006: Logging to a file; console reserved for the UI

- Status: Accepted
- Date: 2026-07-11

## Context

A runtime incident showed the root cause of a confusing session: the logger wrote to the console
at the same time the app was rendering a streaming response and reading prompts. Asynchronous log
lines (including multi-line stack traces and large HTTP error bodies) interleaved with the UI,
producing garbled output and phantom prompts. For an interactive console, log output on the same
stream as the UI is actively harmful, yet diagnostics are still needed for bug reports.

## Decision

We will reserve the console for user interaction and send all framework and library logs to a
per-session diagnostics file:

- No console logging provider is registered. A custom file logging provider writes to
  `diagnostics_<timestamp>.log` in the per-user log directory (ADR-0011). It owns its writer, so
  it has no dependency-injection ordering constraints and never throws into the app.
- The interactive UI renders through a redirect-aware console helper, and user-facing errors are
  shown as short, friendly messages while full detail (structured error fields, stack traces) goes
  to the diagnostics file and the conversation log.

## Consequences

- Streaming output and prompts stay clean; diagnostics are complete and out of the way.
- Reproducing a problem means asking for the diagnostics file rather than a screenshot of scrambled
  console text.
- There is a small amount of custom logging code to maintain instead of using the console provider.
