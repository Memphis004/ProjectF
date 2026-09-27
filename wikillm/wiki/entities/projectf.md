---
type: entity
created: 2026-09-27
updated: 2026-09-28
generation_complete: true
sources:
  - "[[sources/auto-watch-test_65d124]]"
  - "[[sources/netprobe-lessons_598136]]"
tags:
  - "project"
aliases:
  - "libplanet ProjectF"
  - "Project F"
  - "PIXELART GAME"
---

# ProjectF

## Description

ProjectF is a blockchain-based pixel-art fishing and farming game developed under the libplanet project ecosystem, using the libplanet framework — a networked game engine for multiplayer blockchain games — to support its cooperative farming and fishing gameplay. Within its blockchain infrastructure, ProjectF acts as the validator system that maintains a genesis block derived deterministically from each validator's key, which makes the validator infrastructure sensitive to key identity at restart. This deterministic chain-derivation contract defines a genesis-stability requirement: a SeedNode restart must reuse the original key (persisted in privkey.txt), because a fresh ephemeral key produces a different genesis that Libplanet rejects with InvalidGenesisBlockException against the stored chain. The deterministic genesis-derivation contract and its genesis-stability requirement are what the ProjectF netprobe must preserve across restarts; see [[entities/run-netprobe-ps1|run-netprobe.ps1]] and [[entities/probe-cs|Probe.cs]]. ProjectF is described in the extraction pipeline as "pixel-art fishing and farming game" and is also referred to as "libplanet ProjectF."

## Related Entities

- [[entities/pingaction|PingAction]]
- [[entities/seednode|SeedNode]]
- [[entities/libplanet|Libplanet]]
- [[entities/scriptsrun-netprobe-ps1|scripts/run-netprobe.ps1]]
- [[entities/probe-cs|Probe.cs]]

## Related Concepts

- [[concepts/deterministic-genesis-block-derivation|Deterministic genesis block derivation]]
- [[concepts/genesis-block|Genesis block]]

## Mentions in Source

- "[object Object]" — [[sources/auto-watch-test|auto-watch-test]]
- "Automating the ProjectF Stage 2.5 vertical spike into scripts/run-netprobe.ps1 surfaced a series of real failure modes." — [[sources/netprobe-lessons|netprobe-lessons]]
- "the ProjectF genesis block is derived deterministically from the validator key. A SeedNode restart MUST reuse the original key (persisted in privkey.txt)." — [[sources/netprobe-lessons|netprobe-lessons]]