# ADR-0008: Transient retry, timeouts, and cancellation

- Status: Accepted
- Date: 2026-07-11

## Context

Configuration exposed `TimeoutSeconds` and `MaxRetries`, but neither was wired to behaviour: the
HTTP client used its default 100-second timeout and never retried. A single 429/503 therefore
failed the turn even when the server told us exactly how long to wait, and the default timeout
could cut off a long but healthy streaming response. Pressing Ctrl+C killed the process outright,
losing the session summary and any partially streamed text. Cancellation and request timeouts were
also indistinguishable, so a user abort looked like a failure.

## Decision

We will handle transient faults, timeouts, and cancellation deliberately:

- Transient failures (429/503/5xx, excluding the zero-tier quota case) are retried up to
  `MaxRetries` times, honouring the server's `Retry-After` when present (capped) and otherwise using
  exponential backoff with jitter. A fresh request message is created per attempt.
- The typed HTTP client's own timeout is disabled; non-streaming calls apply a per-call timeout of
  `TimeoutSeconds` via a linked token, while streaming relies solely on the caller's token so long
  generations are not truncated.
- The caller's cancellation and an internal timeout use separate tokens, so a user abort is reported
  as a cancellation and a timeout as a timeout.
- The console intercepts Ctrl+C: while a request is in flight it cancels just that request; at the
  prompt it triggers a graceful shutdown. Either way the session summary is printed and logs are
  flushed. Partial streamed text is preserved on cancellation.

## Consequences

- Ordinary rate-limit and overload blips recover automatically without user intervention.
- Long streams are not killed by an unrelated request timeout; short calls still fail fast.
- Ctrl+C is safe and predictable, and statistics distinguish cancellations from failures.
- Retry logic adds complexity and, in the worst case, latency bounded by `MaxRetries` and the cap.
