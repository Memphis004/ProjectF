---
type: source
created: 2026-09-28
updated: 2026-09-28
source_file: "[[sources/usually_p.md]]"
tags: [other]
aliases: ["Roleplay note ingestion pipeline", "wikillm source refresh from netprobe"]
contentHash: 87-9c312691
generation_complete: true
---

# usually_p - Summary

## Source

- Original file: [[sources/usually_p.md]]
- Ingested: 2026-09-28

## Core Content

This note is the first extraction round of a process that refreshes the wikillm source collection from roleplay (บทเรีย) notes harvested by an upstream tool called netprobe. The original vault note instructs the operator to update the wikillm sources with those roleplay notes and to let a wiki ingest step load them into the collection. Immediately after ingestion, the operator should run a wiki lint pass to merge overlapping pages and prevent the wiki from accumulating duplicated content. The note is procedural rather than analytical: it does not explain what wikillm is, how it is implemented, or how netprobe produces its notes — only that these components are linked in a fixed pipeline (netprobe → wiki ingest → wiki lint). Its primary function is to establish the initial seed set of entities and concepts for later rounds, so downstream pages are generated from grounded source material. Because the note is written as a task instruction rather than a description, most items appear at the named level and receive modest substance while still defining an ingest-to-consolidate workflow specific to this vault. The Thai term บทเรีย is shorthand for the roleplay content type being ingested.

## Key Entities

- No concrete entities are named in this source.

## Key Concepts

- [[concepts/wiki-ingest|wiki-ingest]] — the ingestion step that loads extracted notes into the wikillm source collection and materializes raw external notes before any quality-check stage.
- [[concepts/wiki-lint|wiki-lint]] — the post-ingestion maintenance pass that reconciles pages referring to the same subject so the wiki does not accumulate duplicated content.
- [[concepts/usually_p|usually_p]] — the source note itself, describing the ingest-to-consolidate pipeline and serving as the initial seed round for later extraction rounds.

## Main Points

- Update wikillm sources with roleplay (บทเรีย) notes harvested from netprobe during the first extraction round.
- Let "wiki ingest" load the roleplay notes into the wikillm source collection before any quality checks occur.
- Run "wiki lint" after ingestion completes to merge overlapping pages and prevent duplicate content.
- The workflow is procedural: a fixed pipeline (netprobe → wiki ingest → wiki lint) described at the orchestration level without technical definitions.
- Thai term บทเรีย is shorthand for the roleplay content type being ingested.

## Mentions in Source

- "wiki ingest the roleplay notes pulled from netprobe" — [[sources/usually_p|usually_p]]
- "roleplay notes from netprobe being ingested into wikillm sources" — [[sources/usually_p|usually_p]]
- "Run wiki lint after the ingest completes to merge overlapping pages" — [[sources/usually_p|usually_p]]
- "roleplay notes from netprobe" — [[sources/usually_p|usually_p]]