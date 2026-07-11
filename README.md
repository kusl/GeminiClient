# Gemini Client Console

An interactive command-line client for Google's Gemini API, built on .NET 10. It streams responses
in real time, keeps multi-turn conversation context, grounds the model in your machine's real
environment (time, OS, locale, hardware), and reports honest per-session statistics — including
failures and how long they took.

> This project contains code generated with the help of large language models and is experimental.
> It is licensed under **AGPL-3.0-or-later**.

## Features

- **Real-time streaming** over Server-Sent Events, with live metrics (time-to-first-token, tokens/s).
- **Multi-turn conversations** with in-session context; `reset` starts fresh.
- **Environment grounding**: the model is told the current local date/time (with a daylight-saving-
  aware UTC offset), OS and architecture, locale and measurement system, and runtime/hardware facts,
  gathered with cross-platform managed APIs only — no elevated privileges.
- **Robust error handling**: Gemini's error envelope is parsed into typed, categorized errors;
  transient failures (429/503/5xx) are retried automatically, honouring the server's suggested
  retry delay; free-tier "not available on your plan" responses are recognised and explained.
- **Honest telemetry**: every attempt is recorded — successes, empties, failures, and cancellations
  — so the session summary shows success rate, average time-to-failure, average time-to-first-token,
  and failures grouped by type.
- **Sensible model selection**: stable, general-purpose text models are preferred; preview and
  specialised (image/TTS/robotics/…) models are labelled and never chosen as the default.
- **Clean console, complete logs**: the console is reserved for the UI; all diagnostics go to a
  per-session log file, so nothing interleaves with your prompts and responses.
- **Safe Ctrl+C**: cancels the current request without killing the app (press again at the prompt to
  exit); partial output is kept and a summary is always printed.

## Getting started

Prerequisites: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/kusl/GeminiClient.git
cd GeminiClient
dotnet build
dotnet run --project GeminiClientConsole
```

## Configuration

The app needs a Gemini API key in the `GeminiSettings` section. Configuration is resolved relative
to the executable (so the tool works no matter which directory you launch it from), with the
following precedence (lowest to highest):

1. `appsettings.json` next to the executable (ships with a placeholder key).
2. `appsettings.{Environment}.json` next to the executable.
3. A per-user config file (recommended home for your real key — survives reinstalls):
   - Linux: `$XDG_CONFIG_HOME/gemini-client/appsettings.json` (default `~/.config/gemini-client/…`)
   - macOS: `~/Library/Application Support/GeminiClient/appsettings.json`
   - Windows: `%APPDATA%\GeminiClient\appsettings.json`
4. Environment variables, e.g. `GeminiSettings__ApiKey=your-key`.
5. `dotnet user-secrets` (Development only).
6. Command-line arguments.

Example `appsettings.json`:

```json
{
  "GeminiSettings": {
    "ApiKey": "YOUR_GEMINI_API_KEY_HERE",
    "BaseUrl": "https://generativelanguage.googleapis.com/",
    "DefaultModel": "gemini-2.5-flash",
    "TimeoutSeconds": 100,
    "MaxRetries": 3
  }
}
```

`TimeoutSeconds` applies to non-streaming requests (streaming has no overall timeout so long
generations aren't cut off). `MaxRetries` bounds automatic retries of transient failures.

## Commands

| Command | Description |
| --- | --- |
| `exit` / `quit` | End the session and print a summary |
| `reset` | Clear the conversation context and start fresh |
| `model` | Choose a different model |
| `stats` | Show session statistics so far |
| `log` | Open the folder containing the conversation and diagnostics logs |
| `stream` | Toggle streaming vs. standard responses |
| `help` / `?` | Show the command help |

Anything else you type is sent to the model. Press **Ctrl+C** to cancel a running request; press it
again at the prompt to exit.

## Logs and diagnostics

Each session writes a human-readable conversation log and a separate diagnostics log (framework and
library messages, full error detail) to the per-user data directory:

- Linux: `$XDG_DATA_HOME/gemini-client/logs/` (default `~/.local/share/gemini-client/logs/`)
- macOS: `~/Library/Application Support/GeminiClient/logs/`
- Windows: `%LOCALAPPDATA%\GeminiClient\logs\`

If you hit a problem, the diagnostics log is the thing to attach to a bug report.

## Project structure

- **`GeminiClient/`** — reusable library: HTTP client, streaming, typed error model and parser,
  model ranking, environment-context builder, and session-statistics aggregation.
- **`GeminiClientConsole/`** — the interactive CLI: REPL, rendering, commands, and logging.
- **`GeminiMockApi/`** — a local mock of the Gemini endpoints, including on-demand error scenarios
  (`simulate:429|quota|503|500|400|block`) for offline testing.
- **`GeminiClient.Tests/`** — xUnit tests for the pure library logic.
- **`Directory.Build.props` / `Directory.Packages.props`** — single source of truth for the version
  and for dependency versions.
- **`docs/adr/`** — Architecture Decision Records explaining the design.

### Running the tests

The test project is included but not yet wired into the solution. Add it once and run:

```bash
dotnet sln add GeminiClient.Tests/GeminiClient.Tests.csproj
dotnet test
```

## Versioning

The version is defined once, in `Directory.Build.props`. Continuous integration appends the build
run number to produce the published version (e.g. `0.0.8` → `0.0.8.<run>`).

## License

Licensed under the **GNU Affero General Public License v3.0 or later** (AGPL-3.0-or-later).
