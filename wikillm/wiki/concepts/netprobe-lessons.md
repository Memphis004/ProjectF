---
type: concept
created: 2026-09-28
updated: 2026-09-28
sources:
  - "[[sources/netprobe-lessons_598136]]"
tags:
aliases:
  - "netprobe lesson"
  - "run-netprobe lessons"
  - "ProjectF netprobe postmortem"
generation_complete: true
---

# netprobe-lessons

## Definition

A catalog of ten hard-won engineering lessons from a technical postmortem in which a researcher automated ProjectF's Stage 2.5 vertical spike into a PowerShell script (**scripts/run-netprobe.ps1**), encoding each fix either in the script itself or in **UnityProject/Assets/Main/Scripts/Editor/Probe.cs**. In short, it is a documented collection of real failure modes encountered when automating and re-probing a Libplanet consensus chain as a repeatable local experiment.

## Key Characteristics

- Ten discrete engineering lessons, each derived from an actual runtime failure during CI and timeout-driven probing.
- Each fix is either committed into the automation script or the underlying C# editor asset, so the lesson is reproducible rather than merely described.
- Covers tooling, process lifecycle, state persistence, indexing, error serialization, and network retry behaviors specific to Windows/CLI/MCP execution.
- Tied to a concrete environment: **Libplanet 5.5.3**, **Unity 2022.3.62f2**, **MagicOnion 7.0.0**.
- Traces a genesis migration from **c45702a2** to **7c4dd642** performed after a `-FreshSeed` operation.
- Backed by an artifacts bundle containing evidence such as **run-netprobe.out** and **seed-netprobe.log**.

## Applications

- Reproducible local validation of consensus nodes in a CI/automation workflow.
- Referencing the correct lessons when re-running probes, migrating genesis state, or porting the spike to a new platform.
- Onboarding contributors to the run-netprobe automation by pointing them at the specific fixes and their reasoning.

## Related Concepts

## Related Entities

- [[entities/projectf|ProjectF]]
- [[entities/libplanet|Libplanet]]
- [[entities/seednode|SeedNode]]