# ADR-0005: Configuration resolution for a global CLI tool

- Status: Accepted
- Date: 2026-07-11

## Context

The console is installed as a global command (a symlink on the PATH) and is therefore launched
from arbitrary working directories. The previous host relied on the default builder, whose base
path and JSON discovery are sensitive to the current directory, so configuration could silently
fail to load depending on where the user happened to be. Release archives deliberately do not ship
a populated `appsettings.json` (it would contain a placeholder key), so users need a durable,
per-user place to put their API key that survives reinstalling the binary.

## Decision

We will build configuration explicitly and deterministically, based on the executable location,
with the following precedence (lowest to highest):

1. `appsettings.json` next to the executable (`AppContext.BaseDirectory`), optional.
2. `appsettings.{Environment}.json` next to the executable, optional.
3. A per-user config file in the platform config directory (see ADR-0011), optional — the
   recommended home for a real API key.
4. Environment variables (e.g. `GeminiSettings__ApiKey`).
5. User secrets, in the Development environment only.
6. Command-line arguments.

When required settings are missing, the app fails with a clear message that lists exactly these
options rather than a raw validation stack trace.

## Consequences

- The tool behaves identically regardless of the working directory it is launched from.
- Users have a stable, documented place for secrets that is not overwritten by upgrades, and
  secrets are never required to live beside the binary.
- The precedence is fixed in code; changing it is a deliberate, reviewable act (and should update
  this ADR).
