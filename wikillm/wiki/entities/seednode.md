---
type: entity
created: 2026-09-27
updated: 2026-09-28
generation_complete: true
sources:
  - "[[sources/auto-watch-test_65d124]]"
  - "[[sources/netprobe-lessons_598136]]"
tags:
  - "product"
aliases:
  - "Seed node"
  - "SeedNode bootstrap peer"
  - "bootstrap peer"
---

# SeedNode

## Description

SeedNode designates a bootstrap peer used to initialise and assemble a peer-to-peer development network, serving as the starting peer that other nodes connect to in order to bring up a local dev network. In the ProjectF context it is the validator node software that hosts the chain and exposes the chain's genesis and tip heights (for example block #1497 versus a seed tip of #3960), mining blocks every 2 seconds and serving genesis and peer info from its store folder. It restarts deterministically only when the original validator key persisted in privkey.txt is reused — a regenerated key yields a different genesis and Libplanet throws InvalidGenesisBlockException against the stored chain. Because it is both the runtime under test and a template for the probe's chain plumbing, SeedNode exposes a SwarmRunner whose BootChain sub-component already bootstraps a canonical chain and adopts an existing chain id, which the probe's persistent, per-genesis disk store mirrors. Within the netprobe it is launched as a detached Win32 child process (surviving the caller) so that a parent CLI timeout killing the direct child can't take down the node mid-probe; the launcher then locates the running process by its listening port via netstat.

## Related Entities

- [[entities/projectf|ProjectF]]
- [[entities/libplanet|Libplanet]]
- [[entities/scriptsrun-netprobe-ps1|scripts/run-netprobe.ps1]]
- [[entities/swarm-preloadasync|Swarm.PreloadAsync]]

## Related Concepts

- [[concepts/deterministic-genesis-block-derivation|Deterministic genesis block derivation]]
- [[concepts/repeated-swarm-preload-passes|Repeated Swarm preload passes]]
- [[concepts/per-genesis-disk-store|Per-genesis disk store]]
- [[concepts/genesis-block|Genesis block]]

## Mentions in Source

- ""The **SeedNode** is the bootstrap peer of the ProjectF dev network; it mines blocks every 2 seconds and serves genesis + peer info from its store folder."," — [[sources/auto-watch-test|auto-watch-test]]
- "a SeedNode restart MUST reuse the original key (persisted in privkey.txt) - a fresh ephemeral key produces a different genesis and Libplanet throws InvalidGenesisBlockException against the stored chain." — [[sources/netprobe-lessons|netprobe-lessons]]
- "Mirror the SeedNode.SwarmRunner.BootChain approach: adopt an existing canonical chain id, create only for a fresh store." — [[sources/netprobe-lessons|netprobe-lessons]]