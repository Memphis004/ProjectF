---
type: entity
created: 2026-09-27
updated: 2026-09-27
sources:
  - "[[sources/auto-watch-test_65d124]]"
tags:
aliases:
  - "Ping action"
  - "PingCounter"
generation_complete: true
---

# PingAction

## Description

[[PingAction]] is an on-chain action introduced as the first action within [[ProjectF|ProjectF]], functioning in connection with the [[Ping|Ping]] counter machine. As a product-level construct of the project, it encodes a specific, discrete on-chain operation that the extraction system identifies and tracks. Because it is the initiating on-chain action of [[ProjectF|ProjectF]], it serves as an anchor point for the project's interaction model. Related entities include [[ProjectF|ProjectF]] (which defines the surrounding action space) and [[Ping|Ping]] (whose counter the action references). [[PingAction]] should be linked back from [[ProjectF|ProjectF]] and [[Ping|Ping]] to preserve bidirectional context.

## Related Entities

- [[entities/projectf|ProjectF]]
- [[entities/ping|Ping]]

## Related Concepts

## Mentions in Source

- ""The **PingAction** is the first on-chain action of ProjectF; it increments a global ping counter stored in the Ping account space."," — [[sources/auto-watch-test|auto-watch-test]]