# ADR-0003: Centralized versioning and package management

- Status: Accepted
- Date: 2026-07-11

## Context

Earlier revisions duplicated the version number and package metadata across several `.csproj`
files, and the project owner was rightly uncomfortable that a release could ship with the version
updated in some files but not others. The repository also targets a single framework version
across all projects and wants consistent dependency versions.

## Decision

We will keep a single source of truth for shared build settings and dependency versions:

- `Directory.Build.props` holds the one `<Version>` (with `FileVersion`/`AssemblyVersion` derived
  from it via `$(Version)`), shared metadata, target framework, and build options. Copyright is
  computed from the current year rather than hard-coded.
- `Directory.Packages.props` holds every dependency version via Central Package Management
  (`ManagePackageVersionsCentrally`), so individual projects reference packages without versions.
- Continuous integration derives the published version from `Directory.Build.props` and appends
  the CI run number (e.g. `0.0.8` → `0.0.8.<run>`); the base version is only ever bumped in that
  one file.

## Consequences

- A release version is changed in exactly one place; the "did I miss a file?" failure mode is gone.
- Dependency versions are consistent across projects and easy to audit.
- The CI version-extraction step depends on the literal `<Version>…</Version>` element remaining
  in `Directory.Build.props`; that format must be preserved.
