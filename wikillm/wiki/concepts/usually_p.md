---
type: concept
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/usually_p_d62559]]"
tags:
aliases:
  - "wiki source refreshing"
  - "ingest-to-lint workflow"
generation_complete: true
---

# usually_p

## Definition

usually_p is the first extraction round of a process that refreshes the wikillm source collection from roleplay notes harvested by an upstream tool called netprobe. It formalizes a procedural, pipeline-driven workflow — netprobe → ingest → wikillm → lint — whose primary function is to establish the initial seed set of entities and concepts for subsequent rounds, so that downstream pages can be generated from grounded source material.

## Key Characteristics

- **Procedural rather than analytical**: instructs how to operate the pipeline (update sources, ingest, then lint) rather than explaining *what* each component is or *how* it is implemented.
- **Establishes the seed layer**: supplies the initial grounded entities and concepts that later rounds build upon.
- **Ingest-to-consolidate workflow**: after ingesting roleplay notes, run lint to merge overlapping pages and prevent the collection from accumulating duplicated content.
- **Named-level coverage with modest substance**: because the note is written as a task instruction rather than a description, most items are introduced at the named level.
- **Fixed-order linkage of components**: netprobe harvests → wiki ingest loads → wikillm stores → wiki lint consolidates.
- **Shorthand terminology**: the source text uses บทเรีย as shorthand for the roleplay content type.

## Applications

- **Bootstrapping a wiki collection**: uses harvested roleplay notes as the initial grounding for a first-time build of the source set.
- **Maintaining collection health**: uses the lint step to merge overlapping pages and curb duplicated content across rounds.
- **Reproducible refresh cycles**: standardizes the ingest-to-consolidate pipeline so later extractions start from the same grounded base.
- **Linking upstream tooling to downstream documentation**: connects netprobe's harvested notes into wikillm and keeps them curated through lint.

## Related Concepts

- [[concepts/wiki-ingest|wiki ingest]]
- [[concepts/wiki-lint|wiki lint]]

## Related Entities