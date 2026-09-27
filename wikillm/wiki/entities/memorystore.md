---
type: entity
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/netprobe-lessons_598136]]"
tags:
aliases:
  - "MemoryStoreWithPreloadedChain"
  - "in-memory chain store"
  - "Libplanet MemoryStore"
generation_complete: true
---

# MemoryStore

## Description

MemoryStore is Libplanet's in-memory chain-storage backend, the option the ProjectF probe initially relied on for keeping current chain state available to miners and validators. [[concepts/per-genesis-disk-store|Per-genesis disk store]] replaced it as the probe matured.

## Related Entities

- [[entities/libplanet|Libplanet]]
- [[entities/seednode|SeedNode]]
- [[entities/scriptsrun-netprobe-ps1|scripts/run-netprobe.ps1]]

## Related Concepts

- [[concepts/per-genesis-disk-store|Per-genesis disk store]]
- [[concepts/repeated-swarm-preload-passes|Repeated Swarm preload passes]]
- [[concepts/deterministic-genesis-block-derivation|Deterministic genesis block derivation]]

## Mentions in Source

- "Persistent probe store: a MemoryStore re-downloads the WHOLE chain on every run (~10 minutes at 4,000 blocks under Unity's Mono)." — [[sources/netprobe-lessons|netprobe-lessons]]
- "The probe now keeps a disk store namespaced by genesis hash (pf-seed-probe-<hash8>) and mirrors SeedNode.SwarmRunner.BootChain: adopt an existing canonical chain id, create only for a fresh store." — [[sources/netprobe-lessons|netprobe-lessons]]