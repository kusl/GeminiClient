# ADR-0009: Environment-context grounding of the model

- Status: Accepted
- Date: 2026-07-11

## Context

A distinguishing feature of the client is that it grounds the model in the machine's real state by
injecting a system instruction describing the current time, OS, locale, and hardware. The existing
implementation had two correctness bugs: it formatted the day of week with an invalid custom format
string (producing garbled text instead of, say, "Monday"), and it reported the time-zone offset
using the zone's base UTC offset, which ignores daylight saving time and could tell the model the
wrong offset for half the year. It also needs to work identically on Windows, Linux, and macOS for
an unprivileged user, so shelling out or using elevated/native calls is not acceptable.

## Decision

We will build the grounding block from cross-platform managed primitives only, and correct the bugs:

- Day of week is rendered from the `DayOfWeek` value; the current UTC offset is obtained via
  `TimeZoneInfo.GetUtcOffset(now)` so it is daylight-saving-aware, and whether DST is in effect is
  stated explicitly.
- Sections cover time (`[TEMPORAL]`), OS and user (`[SYSTEM]`), locale and measurement system
  (`[LOCALE]`), and runtime and hardware (`[RUNTIME]`), using only managed APIs such as
  `RuntimeInformation`, `Environment`, `GC.GetGCMemoryInfo`, `DriveInfo`, and `RegionInfo`.
- Each section is built defensively: a failure probing one fact is swallowed so it can never break
  request generation.
- The system-instruction content carries no role (Gemini treats it as a dedicated field; a
  "system" role is not valid inside the contents array), and the builder is exposed as a pure,
  static method so the output can be unit-tested deterministically with an injected timestamp.

## Consequences

- The model receives accurate temporal and platform facts, including correct offsets across DST
  transitions, and gives OS-appropriate guidance.
- No elevated privileges or external processes are needed; behaviour is uniform across platforms.
- The injected context contains machine/user/locale details; ADR text notes that it should not be
  echoed verbatim unless the user asks, and it is sent only to the configured API endpoint.
