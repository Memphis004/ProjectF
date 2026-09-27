---
type: concept
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/netprobe-lessons_598136]]"
tags:
aliases:
  - "Multi-pass preload"
  - "Incremental preload"
  - "Preload sync loop"
  - "Repeated preload passes"
generation_complete: true
---

# Repeated Swarm preload passes

## Definition

**Repeated Swarm preload passes** is the synchronization technique the netprobe uses to bring a lagging chain back up to the seed tip when a single `Swarm.PreloadAsync` call stops short of the latest block (misses the tip). A single pass was observed to return at block #1497 while the seed tip was still at #3960, so with the tail missing neither gossip between peers nor a local state read can observe fresh transactions, and downstream verification fails until the tip is actually reached. The fix runs preload passes repeatedly until the tip stops advancing, and adds a single recovery pass when the **delta-based ping counter** stays flat while waiting for a mined transaction. Unlike a one-shot preload, this approach tolerates the partial-progress states that occur under the slow Unity Mono runtime and long CI timeouts, and it depends on a stable genesis so that chain identity does not shift between passes — tying it back to deterministic genesis derivation.

## Key Characteristics

- Runs preload passes repeatedly until the replayed block tip stops advancing past the seed tip.
- Adds one recovery pass when the delta-based ping counter remains flat while awaiting a mined transaction, so a stalled peer is not mistaken for a terminal state.
- Tolerates the partial-progress/pre-mature-terminal states produced by the slow Unity Mono runtime and long CI timeouts.
- Requires a stable genesis so chain identity (and therefore swarm/account address) does not shift between passes.
- Distinguished from a single-shot `Swarm.PreloadAsync` that applies once and stops.

## Applications

- Bootstrapping a node from a `SeedNode` where the local chain is far behind the seed tip and a single preload call cannot reach it.
- CI/CD and long-running environments (notably the slow Unity Mono runtime) in which preload can only make partial progress within a single timeout window.
- Any downstream verification pipeline that fails while the tail of the chain is still missing.

## Related Concepts

- [[concepts/swarm-preloadasync|Swarm.PreloadAsync]]
- [[concepts/delta-based-ping-counter-baseline|Delta-based ping counter baseline]]

## Related Entities

- [[entities/seednode|SeedNode]]
- [[entities/libplanet|Libplanet]]

## Mentions in Source

- "a single Swarm.PreloadAsync pass returned at block #1497 while the seed tip was #3960. With the tail missing, neither gossip nor a state read can see fresh transactions." — [[sources/netprobe-lessons|netprobe-lessons]]
- "The probe now runs repeated preload passes until the tip stops advancing, plus one recovery pass if the ping counter stays flat while waiting for the mined transaction." — [[sources/netprobe-lessons|netprobe-lessons]]