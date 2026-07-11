# ADR-0004: Trimming-friendly design with source-generated JSON

- Status: Accepted
- Date: 2026-07-11

## Context

The console app is published as a self-contained, single-file, trimmed executable so users can
download one binary per platform. Reflection-based JSON serialization and reflection-based options
binding are unsafe under trimming: the linker can remove members that are only accessed
reflectively, causing runtime failures that do not show up in a normal (untrimmed) debug build.

## Decision

We will keep the library trimming-friendly:

- All JSON crosses the boundary through a `System.Text.Json` source-generation context
  (`GeminiJsonContext`); every serialized type — including the error envelope and prompt-feedback
  types — is registered there.
- Options are bound manually from configuration rather than via the reflection-based binder.
- The library sets `IsTrimmable` and keeps trim analysis enabled so violations surface at build time.

## Consequences

- The published binary is smaller and starts faster, and JSON/options work correctly when trimmed.
- Every new serialized type must be added to the source-generation context, and every new option
  must be added to the manual binder — an easy step to forget, so it is called out here.
- The design stays compatible with a future move to full AOT.
