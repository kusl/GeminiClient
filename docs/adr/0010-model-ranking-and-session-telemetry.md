# ADR-0010: Model ranking and session telemetry

- Status: Accepted
- Date: 2026-07-11

## Context

Two related problems surfaced. First, model selection sorted by name, which could surface a preview
or specialised (image/TTS/robotics/embedding) model as the default — and one such preview model was
unavailable on the free tier, producing an immediate failure for a new user. Second, session
statistics counted only successful responses: failed and cancelled attempts, and how long they took
to fail, were ignored entirely, so the summary painted an inaccurately rosy picture.

## Decision

We will make selection and telemetry honest and testable:

- A pure `GeminiModelRanking` orders models for interactive text chat: general-purpose text models
  first, specialised variants demoted, unstable/preview variants demoted, then a preference for
  `flash` and newer versions. The default suggestion is the top-ranked model, and preview/specialised
  entries are labelled in the picker so the choice is transparent.
- Session telemetry records every attempt as a `RequestRecord` with an outcome (success, empty,
  failed, cancelled), elapsed time, optional time-to-first-token, and an error category. The
  `SessionStatistics` aggregate reports success/failure/cancel counts, success rate, success-time
  distribution, average time-to-first-token, average time-to-failure, and per-category failure
  counts. Both types live in the library so they can be unit-tested.

## Consequences

- New users get a sensible, usually-available default, and understand why a preview model isn't it.
- The session summary and the logged statistics reflect reality, including failure frequency and
  latency-to-failure — directly addressing the reported gap.
- The ranking is heuristic and name-based; as model naming evolves, the marker lists may need
  updating (they are covered by tests).
