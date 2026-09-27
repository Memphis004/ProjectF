---
type: concept
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/netprobe-lessons_598136]]"
tags:
aliases:
  - "relative ping baseline"
  - "delta ping criterion"
  - "ping delta check"
generation_complete: true
---

# Delta-based ping counter baseline

## Definition

The delta-based ping counter baseline is the transaction-confirmation criterion introduced after observing that the chain accumulates ping counters across repeated runs, rendering an absolute threshold meaningless. Rather than requiring an absolute counter value, the corrected criterion compares the current ping counter strictly against the baseline read immediately before staging this run's transaction, isolating only the incremental (fresh) pings produced by the current run.

## Key Characteristics

- **Relative, not absolute:** The pass condition measures activity since a preceding baseline (e.g. `counter >= baseline + N`) instead of an absolute threshold like `counter >= 3`, which previously falsely reported success because the counter was already sitting at 39 from prior runs.
- **Baseline is read just before staging:** The reference point is captured immediately before the current run's transaction is staged, so the delta isolates the pings genuinely produced by the current run.
- **Delphi isolates fresh activity:** Only the incremental contribution of the current run is evaluated; prior accumulated state is subtracted out.
- **Genetically bound:** The metric is only meaningful within a single chain identity, so it depends on a stable genesis block.
- **Verified empirically:** A measured run produced a transition of 0 → 3 (delta +3) in a 63-second total wall clock using the `-FreshSeed` flag, confirming the delta check correctly captures fresh activity.

## Applications

- Determining whether the current run generated new ping activity above the previously accumulated counter.
- Validating chain-probe progress across repeated Swarm preload passes, where counters otherwise grow monotonically and mask real progress.
- Confirming transaction confirmation under a stabilized genesis identity.

## Related Concepts

- [[concepts/repeated-swarm-preload-passes|Repeated Swarm preload passes]]
- [[concepts/deterministic-genesis-block-derivation|Deterministic genesis block derivation]]
- [[concepts/swarm-preloadasync|Swarm.PreloadAsync]]

## Related Entities

- [[entities/seednode|SeedNode]]
- [[entities/probe-cs|Probe.cs]]

## Mentions in Source

- "The counter baseline criterion: the chain accumulates pings from all prior runs (counter was already 39), so 'counter >= 3' proves nothing. The pass condition is counter strictly greater than the baseline read right before staging this run's transaction." — [[sources/netprobe-lessons|netprobe-lessons]]
- "Final measured run: 0 -> 3 (delta +3) in a 63-second total wall clock using -FreshSeed." — [[sources/netprobe-lessons|netprobe-lessons]]