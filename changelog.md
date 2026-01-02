# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.0.7] - Rolling Release

### Added

* **Hyper-Contextual Environmental Grounding**: The client now automatically captures and injects real-time system context into every API request.
  * **Temporal Awareness**: Injects precise local date, time, and timezone offset to eliminate "temporal hallucinations" (e.g., the model now knows exactly what "now" is).
  * **System Context**: Provides the model with the exact OS platform (Linux/Windows/macOS), version, and current user context.
  * **Locale Awareness**: Injects the system culture/locale for appropriate formatting of dates and units.
* **Multi-turn conversation support**: The client maintains conversation context across exchanges within a session.
* **`reset` command**: Allows users to clear conversation history and start a fresh context.
* **`log` command**: Opens the conversation log folder in the system's default file manager.
* **Conversation logging**: All prompts, responses, errors, and session statistics are persisted to timestamped log files.
* **XDG Base Directory compliance**: Log files on Linux are stored in `~/.local/share/gemini-client/logs/`.
* **Context depth indicator**: The prompt displays the current number of conversation turns.
* **System Instructions**: Updated API models to support the `system_instruction` field for deep context injection.

### Technical

* **New Service**: Added `EnvironmentContextService` to dynamically generate system prompts based on the host environment.
* **Architecture**: Upgraded target framework to `.NET 10.0`.
* **Optimization**: Configured Server GC and Concurrent GC for high-throughput streaming.
* **Build**: Established `Directory.Build.props` as the single source of truth for versioning.

## [0.0.6] - 2025-08-09

* Cleaned up changelog to remove extra text.
* Stream response from Gemini in server sent events.

## [0.0.5] - 2025-08-09

### Fixed

* Removed `Console.Clear()` that was destroying terminal scrollback buffer.
* Improved terminal compatibility for Linux/macOS users.

### Changed

* Model selection screen now preserves terminal history.
* Use lower case `changelog` in Github Actions link.

## [0.0.4] - 2025-08-07

### Added

* Interactive console client for Google Gemini AI API.
* Dynamic model discovery and selection.
* Real-time performance metrics (tokens/sec).
* Cross-platform support (Windows/Linux/macOS).
* CI/CD pipeline via GitHub Actions.