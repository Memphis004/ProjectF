---
type: concept
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/usually_p_d62559]]"
tags:
aliases:
  - "Wiki ingest pipeline"
  - "Ingest step"
generation_complete: true
---

# wiki ingest

## Definition

"Wiki ingest" is the ingestion pipeline that loads extracted notes into the wikillm source collection. It is the first sequential stage that materializes raw external notes into the wiki, and it is expected to run before any quality checks, which are enforced downstream by the "wiki lint" gate. The note specifies the orchestration and ordering of this step—i.e., when and how it is invoked—not the concrete algorithm or tool that implements it, so it is discussed at a workflow level rather than a technical level.

## Key Characteristics

- Operates on already-extracted notes (for example, roleplay notes pulled from netprobe) and loads them into the wikillm source collection.
- Is positioned as the initial materialization step in the pipeline, preceding any quality-assurance work.
- Is paired with a downstream gate—"wiki lint"—that runs only once ingestion completes, making the two steps explicitly ordered.
- Functions primarily as an orchestration step; the note gives no detail on the inner workings of the ingest implementation.

## Applications

- Loading batched or streaming notes extracted from external tools (such as the netprobe roleplay collection) into the centralized wiki source store.
- Establishing a reproducible, stage-ordered workflow in which data is materialized before validation, i.e., ingest followed by a lint gate.
- Providing a clean seam between extraction and validation so that quality checks can react to fully ingested collections rather than in-flight data.

## Related Concepts

- [[concepts/wiki-lint|wiki lint]]

## Related Entities

## Mentions in Source

- "Run wiki lint after the ingest completes to merge overlapping pages" — [[sources/usually_p|usually_p]]