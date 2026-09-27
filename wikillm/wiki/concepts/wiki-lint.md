---
type: concept
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/usually_p_d62559]]"
tags:
aliases:
  - "Wiki lint pass"
  - "lint step"
  - "wiki linting"
generation_complete: true
---

# wiki lint

## Definition

**Wiki lint** is the post-ingestion quality and consolidation step for wikillm sources. It is a maintenance pass invoked *after* ingestion completes to reconcile and merge overlapping pages that refer to the same subject, preventing the wiki from accumulating duplicated content as new notes are continuously added.

## Key Characteristics

- Runs **downstream of ingestion** as a dedicated reconciliation pass, not as part of the ingest pipeline itself.
- **Merges overlapping pages** that describe the same subject so the collection stays coherent.
- Targets **duplicate content accumulation** caused by repeatedly adding new notes over time.
- Invoked rather than fully specified in the source; no details about the linting method, exact rules, or tooling are provided.

## Applications

- Consolidating the knowledge collection after a bulk ingestion run.
- Reconciling newly added notes that overlap with existing pages on the same topic.
- Maintaining overall coherence and minimizing redundancy as the wiki grows.

## Related Concepts

- [[concepts/wiki-ingest|wiki ingest]]

## Related Entities

## Mentions in Source

- "Run wiki lint after the ingest completes to merge overlapping pages" — [[sources/usually_p|usually_p]]