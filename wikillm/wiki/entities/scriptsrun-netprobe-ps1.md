---
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/netprobe-lessons_598136]]"

generation_complete: true
---

type: entity # MUST be exactly "entity" - do not change this value
created: 2026-09-28
updated: 2026-09-28
sources: ["[[sources/netprobe-lessons_598136|netprobe lessons]]", "[[sources/probe.cs|Probe.cs]]"]
tags: [product] # Copy the Tag value from Entity Information verbatim
aliases: ["run-netprobe PowerShell script", "Netprobe orchestration script", "run-netprobe.ps1"]
---

# scripts/run-netprobe.ps1

## Description

**scripts/run-netprobe.ps1** is the PowerShell artifact that orchestrates the [[entities/projectf|ProjectF]] Stage 2.5 probe; it is the integration point where every fix surfaced during the spike is encoded. The runtime-discovered failure modes all funnel into this script or into its companion .NET source, [[entities/probe.cs|Probe.cs]] at

## Related Entities

- [[entities/projectf|ProjectF]]
- [[entities/probe-cs|Probe.cs]]
- [[entities/libplanet|Libplanet]]
- [[entities/seednode|SeedNode]]

## Related Concepts

- [[concepts/repeated-swarm-preload-passes|Repeated Swarm preload passes]]
- [[concepts/per-genesis-disk-store|Per-genesis disk store]]
- [[concepts/deterministic-genesis-block-derivation|Deterministic genesis block derivation]]
- [[concepts/stale-probe-isolation|Stale probe isolation]]
- [[concepts/compile-gate-before-probing|Compile gate before probing]]

## Mentions in Source

- "Automating the ProjectF Stage 2.5 vertical spike into scripts/run-netprobe.ps1 surfaced a series of real failure modes. Each fix is now encoded either in the script or in UnityProject/Assets/Main/Scripts/Editor/Probe.cs." — [[sources/netprobe-lessons|netprobe-lessons]]