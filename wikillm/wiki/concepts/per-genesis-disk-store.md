---
type: concept
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/netprobe-lessons_598136]]"
tags:
aliases:
  - "disk-backed chain store"
  - "chain id store"
  - "namespaced chain store"
  - "per-genesis store"
generation_complete: true
---

# Per-genesis disk store

## Definition

A per-genesis disk store is a persistence technique used to replace a `MemoryStore`, which re-downloads the entire chain on every run. Rather than re-fetching the whole chain on startup, it persists data on disk and is namespaced by genesis hash (e.g. `pf-seed-probe-<hash8>`), mirroring the `SeedNode.SwarmRunner.BootChain` convention of adopting an existing canonical chain id for a persistent store and creating one only for a fresh store. In a warm run it syncs only the delta — the tail newly added since the last run — so a chain retirement (e.g. `c45702a2` superseded by `7c4dd642`) does not pollute the new run. This makes the probe fast, idempotent, and restart-safe.

## Key Characteristics

- **Genesis-hashed namespace**: store sets are named per chain id via `pf-seed-probe-<hash8>`, so one store set is allocated per chain.
- **Delta-only resync on warm runs**: only the tail newly added since the last run is loaded, replacing a full-chain re-download.
- **Idempotent and restart-safe**: re-running against the same genesis hash does not double-populate or corrupt stored data.
- **Clean chain-retirement migration**: retiring a previous chain does not pollute stores belonging to the successor chain.
- **On-disk persistence**: substitutes a full in-memory re-download with a compact disk-backed mirror.
- **Seed-and-reuse pattern**: mirrors the `SeedNode.SwarmRunner.BootChain` convention for adopting canonical chain ids.

## Applications

- **Seeded probes** such as `Probe.cs`, which must start fast on warm runs.
- **Repeated or continuous blockchain probing / scanning workloads** that run many times against evolving chains.
- **Workloads requiring idempotent, restart-safe persistence** where a full re-sync on each start would be prohibitively slow (minutes per run at 4,000 blocks under Unity's Mono).

## Related Concepts

## Related Entities

- [[entities/memorystore|MemoryStore]]
- [[entities/seednode|SeedNode]]
- [[entities/probe-cs|Probe.cs]]

## Mentions in Source

- "a MemoryStore re-downloads the WHOLE chain on every run (~10 minutes at 4,000 blocks under Unity's Mono). The probe now keeps a disk store namespaced by genesis hash (pf-seed-probe-<hash8>) and mirrors SeedNode.SwarmRunner.BootChain: adopt an existing canonical chain id, create only for a fresh store." — [[sources/netprobe-lessons|netprobe-lessons]]