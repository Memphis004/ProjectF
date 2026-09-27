---
type: source
created: 2026-09-28
updated: 2026-09-28
source_file: "[[sources/netprobe-lessons.md]]"
tags: [other]
aliases: ["run-netprobe Lessons", "Stage 2.5 failure modes", "Netprobe postmortem"]
contentHash: e26-2b529a20
generation_complete: true
---

# Netprobe Lessons (Stage 2.5) - Summary

## Source

- Original file: [[sources/netprobe-lessons.md]]
- Ingested: 2026-09-28

## Core Content

This technical postmortem documents the real-world failure modes discovered while automating [[entities/projectf|projectf]]'s Stage 2.5 vertical spike into a PowerShell harness ([[entities/scriptsrun-netprobe-ps1|scriptsrun-netprobe-ps1]]) and its companion editor code ([[Probe.cs]]). Rather than a conceptual overview, the text is an engineering lesson catalogue: it records ten hard-won fixes that turned a flaky, manual probe into a deterministic, restart-safe pipeline. The work runs on a pinned stack of [[entities/libplanet|libplanet]] 5.5.3, Unity 2022.3.62f2, and MagicOnion 7.0.0, having migrated the chain from genesis `c45702a2` to `7c4dd642` after a `-FreshSeed` operation. Each lesson traces a concrete bug (process lifetime, chain sync, key identity) to the exact line of code that now encodes its fix.

## Key Entities

- [[entities/projectf|projectf]] — the blockchain validator whose genesis is derived deterministically from the validator key.
- [[entities/libplanet|libplanet]] — the C# blockchain engine that validates genesis stability and throws `InvalidGenesisBlockException` when a stored chain does not match its expected genesis.
- [[entities/seednode|seednode]] — the validator node hosting the ProjectF chain; both the runtime under test and a template for the probe's own chain plumbing.
- [[entities/scriptsrun-netprobe-ps1|scriptsrun-netprobe-ps1]] — the orchestration script that spawns the seed, manages the store, and applies every fix captured in this postmortem.
- [[entities/memorystore|memorystore]] — the Libplanet in-memory store the probe initially relied on, replaced by a per-genesis disk-backed store.

## Key Concepts

- [[concepts/deterministic-genesis-block-derivation|deterministic-genesis-block-derivation]] — the source-of-truth contract that ties chain identity to the validator key and makes key reuse invariant across restarts.
- [[concepts/repeated-swarm-preload-passes|repeated-swarm-preload-passes]] — multi-pass `Swarm.PreloadAsync` to sync a chain lagging far behind the seed tip.
- [[concepts/per-genesis-disk-store|per-genesis-disk-store]] — a `pf-seed-probe-<hash8>`-namespaced disk store that syncs only the delta on warm runs.
- [[concepts/delta-based-ping-counter-baseline|delta-based-ping-counter-baseline]] — a relative ping-counter criterion that isolates fresh activity from cumulative counters.
- [[concepts/netprobe-lessons|netprobe-lessons]] — the enclosing postmortem taxonomy.

## Main Points

- The `ProjectF` genesis block is derived deterministically from the validator key, so a `SeedNode` restart **MUST** reuse the key persisted in `privkey.txt`; a fresh ephemeral key changes the genesis and triggers `InvalidGenesisBlockException` (`[[entities/libplanet|libplanet]]`).
- The script detaches the seed via `Win32_Process.Create` with the environment baked into a generated `.cmd`, so the node survives CI/timeout kills and is located by listening port via `netstat`.
- A single `Swarm.PreloadAsync` can stop far behind the tip (observed #1497 vs seed tip #3960), so the probe runs **repeated preload passes** (plus a recovery pass on a flat ping counter) until the tip advances.
- A `MemoryStore` re-downloads the whole chain every run (~10 min at 4,000 blocks); the fix is a **per-genesis disk store** (`pf-seed-probe-<hash8>`) that adopts a canonical chain id and syncs only the delta in seconds.
- Each run writes its own `net-probe-result-<HHmmss>.json` and uses per-genesis stores, so a timed-out run cannot pollute a new chain.
- A **compile gate** refreshes Unity assets and aborts if output contains "compilation errors exist", preventing runtime use of stale assemblies.
- The ping pass compares the counter strictly against the baseline read just before staging this run's transaction (the absolute threshold `>= 3` was bogus— the chain was already at 39).
- Reliability fixes: escape backslashes in C#-serialized exceptions, resolve `npx.cmd` explicitly on Windows (plain `npx` hits npm's sh wrapper), and retire MCP CLI calls by retrying through an initial 503.
- Environment: [[entities/libplanet|libplanet]] 5.5.3, Unity 2022.3.62f2, MagicOnion 7.0.0; migrated `c45702a2` → `7c4dd642` after `-FreshSeed`; supporting evidence in `run-netprobe.out` and `seed-netprobe.log`.

## Mentions in Source

- "Automating the ProjectF Stage 2.5 vertical spike into scripts/run-netprobe.ps1 surfaced a series of real failure modes." — [[sources/netprobe-lessons|netprobe-lessons]]
- "the ProjectF genesis block is derived deterministically from the validator key. A SeedNode restart MUST reuse the original key (persisted in privkey.txt)." — [[sources/netprobe-lessons|netprobe-lessons]]
- "a fresh ephemeral key produces a different genesis and Libplanet throws InvalidGenesisBlockException against the stored chain." — [[sources/netprobe-lessons|netprobe-lessons]]
- "Libplanet 5.5.3 (all sub-packages), Unity 2022.3.62f2, MagicOnion 7.0.0." — [[sources/netprobe-lessons|netprobe-lessons]]
- "a SeedNode restart MUST reuse the original key (persisted in privkey.txt) - a fresh ephemeral key produces a different genesis and Libplanet throws InvalidGenesisBlockException against the stored chain." — [[sources/netprobe-lessons|netprobe-lessons]]
- "Mirror the SeedNode.SwarmRunner.BootChain approach: adopt an existing canonical chain id, create only for a fresh store." — [[sources/netprobe-lessons|netprobe-lessons]]
- "a single Swarm.PreloadAsync pass returned at block #1497 while the seed tip was #3960. With the tail missing, neither gossip nor a state read can see fresh transactions." — [[sources/netprobe-lessons|netprobe-lessons]]
- "Automating the ProjectF Stage 2.5 vertical spike into scripts/run-netprobe.ps1 surfaced a series of real failure modes. Each fix is now encoded either in the script or in UnityProject/Assets/Main/Scripts/Editor/Probe.cs." — [[sources/netprobe-lessons|netprobe-lessons]]
- "scripts/run-netprobe.ps1 now spawns it via Win32_Process.Create (WMI) with the environment baked into a generated .cmd file, so it survives the caller. The kill path locates the PID by listening port (netstat -ano parse)." — [[sources/netprobe-lessons|netprobe-lessons]]