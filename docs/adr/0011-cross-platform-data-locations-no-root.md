# ADR-0011: Cross-platform data locations, no root required

- Status: Accepted
- Date: 2026-07-11

## Context

The client writes conversation logs and diagnostics and reads an optional per-user configuration
file. It must place these in the conventional per-user location on each platform, work for a normal
unprivileged user, and never require root/administrator or writes to system directories — the tool
installs to a shared location but runs as the user.

## Decision

We will resolve data and configuration directories per platform using managed APIs only:

- Data (logs, diagnostics): Windows `%LOCALAPPDATA%\GeminiClient`; macOS
  `~/Library/Application Support/GeminiClient`; Linux/Unix `$XDG_DATA_HOME` or `~/.local/share`,
  under `gemini-client`.
- Config (optional per-user `appsettings.json`, see ADR-0005): Windows `%APPDATA%\GeminiClient`;
  macOS `~/Library/Application Support/GeminiClient`; Linux/Unix `$XDG_CONFIG_HOME` or `~/.config`,
  under `gemini-client`.
- These paths are computed with `Environment.GetFolderPath` and the XDG environment variables; the
  application only ever creates directories under the user's own profile.

## Consequences

- Logs, diagnostics, and user configuration land where each platform's users and tools expect them.
- The tool runs without elevation and touches nothing outside the user's profile.
- The XDG variables are honoured on Linux, so users who relocate their data/config directories are
  respected.
