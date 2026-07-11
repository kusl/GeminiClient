# Architectural Decision Records

This directory records the significant architectural decisions for the Gemini Client, using
[Michael Nygard's ADR format](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions).
Each record captures the context in force at the time, the decision taken, and the consequences —
so the *why* behind the design survives even as the code changes.

New records are added with the next number in sequence and never renumbered. Superseding a decision
means adding a new ADR and marking the old one `Superseded by ADR-XXXX`, rather than editing history.

## Index

| ADR | Title | Status |
| --- | --- | --- |
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-solution-structure-library-console-mock.md) | Solution structure: library, console, mock API | Accepted |
| [0003](0003-versioning-and-central-package-management.md) | Centralized versioning and package management | Accepted |
| [0004](0004-trimming-aot-and-source-generated-json.md) | Trimming-friendly design with source-generated JSON | Accepted |
| [0005](0005-configuration-resolution-for-a-global-cli-tool.md) | Configuration resolution for a global CLI tool | Accepted |
| [0006](0006-logging-to-file-console-reserved-for-ui.md) | Logging to a file; console reserved for the UI | Accepted |
| [0007](0007-typed-api-errors-and-user-messaging.md) | Typed API errors and user-facing messaging | Accepted |
| [0008](0008-transient-retry-timeouts-and-cancellation.md) | Transient retry, timeouts, and cancellation | Accepted |
| [0009](0009-environment-context-grounding.md) | Environment-context grounding of the model | Accepted |
| [0010](0010-model-ranking-and-session-telemetry.md) | Model ranking and session telemetry | Accepted |
| [0011](0011-cross-platform-data-locations-no-root.md) | Cross-platform data locations, no root required | Accepted |
