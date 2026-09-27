---
type: entity
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/netprobe-lessons_598136]]"
tags:
aliases:
  - "Libplanet framework"
  - "Libplanet blockchain engine"
  - "Libplanet 5.5.3"
generation_complete: true
---

# Libplanet

## Description

Libplanet is a C# blockchain engine library (version 5.5.3, shipping all sub-packages) that underpins the chain-handling layer of [[entities/projectf|ProjectF]]. As an enforcer of the ProjectF determinism contract — the consistency requirement surfaced throughout the netprobe lessons — Libplanet validates genesis stability and rejects any chain whose genesis does not match the stored state. When a validator restarts with a fresh ephemeral key, the derived genesis differs from the canonical one, and Libplanet throws [[concepts/invalid-genesis-block-exception|InvalidGenesisBlockException]] against the stored chain. The engine is pinned alongside a specific runtime stack: Unity 2022.3.62f2 and MagicOnion 7.0.0. Together, these constraints guarantee that consensus-critical genesis derivation is reproducible across validator restarts. Libplanet is therefore the component that makes [[concepts/deterministic-genesis-block-derivation|deterministic genesis block derivation]] observable and enforced.

## Related Entities

- [[entities/projectf|ProjectF]]
- [[entities/seednode|SeedNode]]

## Related Concepts

- [[concepts/deterministic-genesis-block-derivation|Deterministic genesis block derivation]]
- [[concepts/genesis-block|Genesis block]]
- [[concepts/invalidgenesisblockexception|InvalidGenesisBlockException]]

## Mentions in Source

- "a fresh ephemeral key produces a different genesis and Libplanet throws InvalidGenesisBlockException against the stored chain." — [[sources/netprobe-lessons|netprobe-lessons]]
- "Libplanet 5.5.3 (all sub-packages), Unity 2022.3.62f2, MagicOnion 7.0.0." — [[sources/netprobe-lessons|netprobe-lessons]]