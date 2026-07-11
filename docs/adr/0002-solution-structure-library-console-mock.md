# ADR-0002: Solution structure — library, console, mock API

- Status: Accepted
- Date: 2026-07-11

## Context

The product is a command-line chat client, but the logic that talks to Gemini (request shaping,
streaming, error interpretation, model listing) is useful independent of any particular front-end,
and needs to be testable without a console attached. Testing against the real API is slow, costs
quota, and cannot deterministically reproduce failures such as 429 or 503.

## Decision

We will keep three projects in one solution:

- `GeminiClient` — a reusable class library holding all API and domain logic (HTTP client, error
  model and parser, model ranking, environment-context builder, and session-statistics
  aggregation). It has no console dependencies.
- `GeminiClientConsole` — a thin executable that owns only user interaction (REPL, rendering,
  command handling, logging to disk) and depends on the library.
- `GeminiMockApi` — a small ASP.NET Core app that mimics the Gemini endpoints, including
  on-demand error scenarios, so the client can be exercised offline.

Pure, reusable logic (including session telemetry) lives in the library so the test project can
reference the library alone.

## Consequences

- The valuable logic is unit-testable in isolation and could back a different UI later.
- Failure handling and retry can be validated deterministically against the mock.
- There is more than one project to build and version, which the central build files (ADR-0003)
  are designed to keep manageable.
