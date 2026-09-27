---
type: concept
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/netprobe-lessons_598136]]"
tags:
aliases:
  - "deterministic genesis derivation"
  - "genesis block derivation from key"
  - "validator-key genesis"
generation_complete: true
---

# Deterministic genesis block derivation

## Definition

Deterministic genesis block derivation is the mechanism by which ProjectF computes its genesis block **solely from a validator's private key**, making the genesis a pure, reproducible function of that key. Because the derivation depends only on the private key and no other volatile input (network time, clock, ephemeral state), any process that possesses the original private key can deterministically reconstruct the identical genesis block — for example, after a process restart.

## Key Characteristics

- **Pure function of the private key** — the genesis block is derived deterministically from the key alone; the same key always yields the same genesis.
- **Restart-stability requirement** — a SeedNode must reuse the original key persisted in `privkey.txt` on restart. Using any other key (a refactured or regenerated one) produces a different genesis.
- **Invariant key identity across process restarts** — the netprobe must treat the private key as an invariant of the node identity, so that repeated launches yield an identical genesis.
- **Triggered by fabricated keys** — the property breaks whenever a key is fabricated at launch time, including a CI or tool timeout that spawns a fresh ephemeral key instead of resuming the persisted one.
- **Coupled to genesis validation and hashing** — the derived genesis participates in Libplanet's genesis validation and in the genesis-hash namespacing adopted by the disk store and result files.

## Applications

- **Chain alignment** — since the genesis is the source of truth for chain alignment, deterministic derivation from a fixed key guarantees that independent nodes and restarts converge on the same chain.
- **Stable chain identity** — enables reproducible, verifiable chain identity that can be checked via libplanet's `validGenesis` / genesis-hash helpers embedded in the disk store and result file naming.
- **Failure diagnosis for probes** — because key fabrication is a root cause of many probe failures, deterministic derivation is used to reason about why a node deviates from its expected chain. `Probe.cs` relies on treating key identity as invariant to reduce such failures.

## Related Concepts

- [[concepts/genesis-block|Genesis block]]
- [[concepts/invalidgenesisblockexception|InvalidGenesisBlockException]]

## Related Entities

- [[entities/projectf|ProjectF]]
- [[entities/libplanet|Libplanet]]
- [[entities/seednode|SeedNode]]
- [[entities/probe-cs|Probe.cs]]

## Mentions in Source

- "the ProjectF genesis block is derived deterministically from the validator key." — [[sources/netprobe-lessons|netprobe-lessons]]
- "a fresh ephemeral key produces a different genesis and Libplanet throws InvalidGenesisBlockException against the stored chain." — [[sources/netprobe-lessons|netprobe-lessons]]