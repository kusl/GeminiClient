# ADR-0001: Record architecture decisions

- Status: Accepted
- Date: 2026-07-11

## Context

The client is developed largely with the help of LLMs and has accumulated non-obvious design
choices — trimming constraints, a hand-rolled configuration pipeline, streaming/cancellation
trade-offs — that are easy to "fix" incorrectly later because the reasoning isn't written down.
The project owner has also expressed a desire to avoid repeating mistakes (for example, versions
that once lived in several files at once).

## Decision

We will keep Architecture Decision Records in `docs/adr/`, one Markdown file per decision, using
Michael Nygard's format (Status / Context / Decision / Consequences). Records are numbered
sequentially, are immutable once accepted, and are superseded rather than rewritten.

## Consequences

- The rationale for a design is discoverable next to the code, which lowers the risk of a
  well-intentioned change reintroducing a solved problem.
- There is a small, ongoing cost: meaningful changes should come with an ADR.
- ADRs are documentation, not enforcement; code review remains responsible for keeping the code
  and the records aligned.
