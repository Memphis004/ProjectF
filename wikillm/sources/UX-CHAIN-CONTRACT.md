# UX-CHAIN-CONTRACT — Stage 11

How the client UI stays instant without ever lying about legality. The chain
is the sole authority; everything in this document is **display only**.

---

## 1. The three views of state

| View | Source | Who reads it | May decide legality? |
|---|---|---|---|
| **Confirmed** | `StateWatcher.AvatarUpdated` (on-chain, at tip) | `OptimisticState`, HUD fallback | YES (only source of truth) |
| **Merged (optimistic)** | confirmed + ordered `PendingMutation` list | `HudPresenter`, `ShopPresenter`, `TaskBoardPresenter`, `KitchenPresenter`, `InventoryPresenter`, `FishingPresenter` | **NEVER** |
| **Queue** | `ActionQueue.Pending` | HUD "…n" badge | NO |

Hard rule (spec stage 11): **nothing may read `OptimisticState.Current` to
decide legality.** Buttons use merged numbers as *affordances*; the chain
re-validates every action and throws on violation, and the failed submit
rolls the optimistic guess back.

## 2. Lifecycle of one optimistic guess

```
click → ApplyPending(mutation)  → merged view updates the SAME frame
      → queue drains FIFO (nonce-safe, one tx in flight per avatar)
      → confirm:  drop entry, adopt confirmed snapshot
                  drift beyond tolerance → reconciliation warning (both values logged)
      → fail/timeout/cancel: drop entry → OnRolledBack(reason)
                  → toast "ยกเลิกรายการ: <reason>" + display restored
```

- Retry policy: transport errors retry **2×** (500 ms, 1 s backoff) while
  staying `Staged`; an on-chain validation exception (`ActionFailedException`)
  **never** retries.
- Timeouts are terminal (`TimedOut`): the tx may still land later, but the
  display stops guessing.
- Nothing is persisted: on restart the queue starts empty **by design** —
  unconfirmed guesses die with the session; confirmed state re-reads from the
  chain.

## 3. Reorg handling

`StateWatcher` remembers recent tip hashes. A new tip whose **parent hash**
is neither the previous tip nor in recent history is not a descendant →
`OnReorg(depth)`, the per-tip debounce window is cleared, and **all watched
state is re-read from scratch**. Reads are debounced to at most one full read
per tip hash (stamina regen is stamped per block, so a moved tip still
re-reads; the same tip polled twice is free).

## 4. Connection monitor

`ChainConnectionMonitor` polls (250 ms) and reports
`Bootstrapping → Syncing (N/M while a peer target is known) → Synced`,
downgrading to **Stalled** after no new tip for 3× the target block interval
(2 s → 6 s), or **Offline** when the node reports no peers.

- Stalled/Offline: **submissions are refused with a clear message**
  (`TOAST_CHAIN_STALLED` / `TOAST_CHAIN_OFFLINE`) instead of silently timing
  out for 30 s.
- HUD dot: green Synced · yellow Syncing/Bootstrapping · amber Stalled ·
  red Offline. Pending actions show `…n` (staged) / `○n` (queued).

## 5. Sequence diagram — one fishing cast (click → confirmed catch)

```
 Player      FishingPresenter   ActionQueue        OptimisticState     StateWatcher        Chain (Libplanet)
   |                |                 |                   |                  |                    |
   |  click Cast    |                 |                   |                  |                    |
   |--------------->|                 |                   |                  |                    |
   |                | CanSubmit? (monitor)                |                  |                    |
   |                |--- stalled/offline --> refuse + toast (STOP)         |                    |
   |                |                 |                   |                  |                    |
   |                |  (1) SubmitWithGuessAsync(OccupyPondAction, guess: none)
   |                |---------------->|                   |                  |                    |
   |                |                 | state=Queued      |                  |                    |
   |                |                 | PendingChanged --> HUD "○1"          |                    |
   |                |                 |--- drain (FIFO) ->|                  |                    |
   |                |                 | state=Staged      |                  |                    |
   |                |                 |--- StageAndWait ->|----------------------------- stage tx -------->|
   |                |                 |                   |                  |     block N: occupy_pond_v1 ok   |
   |                |                 |<---- tip N -------|-----------------------------------------------|
   |                |                 | state=Confirmed   |                  |                    |
   |                |                 | OnConfirmed -------> handle.Confirm()   |                    |
   |                |                 |                   | (no display delta — occupy costs nothing)          |
   |                |                 |                   |                  |                    |
   |  "Casting…"    |                 |                   |                  |                    |
   |<---------------|                 |                   |                  |                    |
   |                |  (2) SubmitWithGuessAsync(FishingAction(pond,bait),
   |                |       guess: bait -1, stamina -5)                       |                    |
   |                |---------------->|                   |                  |                    |
   |                |                 |                   | ApplyPending ----> MergedUpdated            |
   |  HUD: bait 9→8, stamina 50→45  INSTANTLY (no block yet) |                  |                    |
   |                |                 | state=Staged      |                  |                    |
   |                |                 |--- StageAndWait ->|----------------------------- stage tx -------->|
   |                |                 |                   |                  |                    |
   |                |                 |                   |  .... 2s block ....                   |                    |
   |                |                 |                   |                  |                    |
```

### 5a. Happy path (catch)

```
   |                |                 |                   |                  |     block N+1: fishing_v1 rolls RNG:
   |                |                 |                   |                  |     bait-1, stamina-5, fish +1, exp+X
   |                |                 |<---- tip N+1 -----|-----------------------------------------------|
   |                |                 | state=Confirmed   |                  |                    |
   |                |                 | OnConfirmed -------> handle.Confirm()   |                    |
   |                |                 |                   | pending dropped; |                    |
   |                |                 |                   | confirmed snapshot adopted            |                    |
   |                |                 |                   | Reconcile: expected Δ(bait -1, stamina -5)
   |                |                 |                   |   == actual Δ → silent (within tolerance)
   |                |                 |                   |                  |                    |
   |                |                 |                   | AvatarUpdated(N+1) -> MergedUpdated           |
   |  HUD: fish +1, exp +X (the CHAIN's roll — client never rolled)          |                    |
   |<--------------- reveal panel "Caught something!"        |                  |                    |
```

### 5b. Failure branch A — on-chain validation (e.g. pond full)

```
   |                |                 |                   |                  |     block N+1: fishing_v1 THROWS
   |                |                 |                   |                  |     (PondFullException / NotEnoughStamina…)
   |                |                 |<-- exec.Fail -----|-----------------------------------------------|
   |                |                 | state=Failed (NEVER retried — validation)                 |
   |                |                 | OnFailed(reason)->|                  |                    |
   |                |                 |                   | handle.RollBack(Failed, rawReason)
   |                |                 |                   | pending dropped → display restores
   |                |                 |                   | OnRolledBack ----> AppBootstrapper:
   |  toast "ยกเลิกรายการ: หมดแรงแล้ว…" (ErrorMapper.Localize(rawReason))     |                    |
   |<--------------- "The fish got away" status line         |                  |                    |
```

### 5c. Failure branch B — transport error → retry, then timeout

```
   |                |                 |                   |                  |                    |
   |                |                 | state=Staged      |                  |                    |
   |                |                 |--- StageAndWait ->|     X transport error                |
   |                |                 |<-- Exception -----|                  |                    |
   |                |                 | retry 1/2 after 500ms (state stays Staged, badge "…1")    |
   |                |                 |--- StageAndWait ->|     X transport error again          |
   |                |                 | retry 2/2 after 1s |                 |                    |
   |                |                 |--- StageAndWait ->|     X ...                            |
   |                |                 | retries exhausted → TimeoutException                    |
   |                |                 | state=TimedOut (terminal — tx may STILL land later,        |
   |                |                 |   but display stops guessing)         |                    |
   |                |                 | OnFailed("timeout")                  |                    |
   |                |                 |                   | handle.RollBack(TimedOut, "timeout")
   |                |                 |                   | OnRolledBack ----> toast
   |  toast "ยกเลิกรายการ: เน็ตช้าเกินไป ลองใหม่อีกครั้ง"       |                  |                    |
   |<--------------- "The fish got away" status line         |                  |                    |
   |                |                 |                   |                  |                    |
   |                |                 |                   |  ... if the tx DOES land later, the next
   |                |                 |                   |  AvatarUpdated simply shows the true state —
   |                |                 |                   |  the confirmed snapshot always wins.        |
```

### 5d. Edge branch — reorg during the wait

```
   |                |                 |                   |                  |  tip N+2 arrives whose parent
   |                |                 |                   |                  |  is NOT in our tip history
   |                |                 |                   |                  |  → OnReorg(depth) raised;
   |                |                 |                   |                  |  debounce cleared; ALL watched
   |                |                 |                   |                  |  state re-read from scratch
   |                |                 |                   | OnConfirmedUpdate(new snapshot)          |
   |                |                 |                   | pending guesses re-checked against truth   |
   |                |                 |                   | (drift beyond tolerance → warning with
   |                |                 |                   |  BOTH values; display rolls to truth)      |
```

### 5e. Edge branch — chain stalls mid-cast

```
   |                |                 |                   |                  |  no new block for 6s
   |                |   monitor.Tick(): Status=Stalled → HUD dot amber, toast "เชนหยุดนิ่ง…"
   |                |   CanSubmit=false → next click is REFUSED with a clear message
   |                |                     (never a silent 30s timeout)         |                    |
```

## 6. Tolerance & reconciliation

- Stamina: tolerance **10** — it legitimately regenerates while a tx waits.
- Gold / exp / inventory: tolerance **0** — the block delta must match the
  guess exactly.
- Beyond tolerance: `Debug.LogWarning("[optimistic] reconciliation drift on
  <field>: merged showed X but the confirmed block says Y")` — both values —
  and the display snaps to the confirmed numbers.
- The merge view clamps counts/stamina at ≥ 0 and stamina at ≤ max: the
  display never shows impossible negative numbers mid-flight.

## 7. Localization keys added

| Key | th | en |
|---|---|---|
| `TOAST_CHAIN_STALLED` | เชนหยุดนิ่ง ไม่มีบล็อกใหม่ — ส่งรายการไม่ได้ชั่วคราว | Chain stalled — no new blocks; actions paused |
| `ROLLBACK_TOAST` | ยกเลิกรายการ: {0} | Cancelled: {0} |
| `ROLLBACK_RECONCILED` | ค่าที่แสดงไม่ตรงกับเชน | display out of sync with the chain |

## 8. Files

- `Infrastructure/Blockchain/OptimisticState.cs` — merged view, apply/confirm/rollback, reconciliation (NEW)
- `Infrastructure/Blockchain/ActionQueue.cs` — FIFO per-avatar queue, state machine, retries, `Pending` + events (UPGRADED)
- `Infrastructure/Blockchain/StateWatcher.cs` — reorg detection (`OnReorg`), per-tip debounce (UPGRADED)
- `Infrastructure/Blockchain/ChainConnectionMonitor.cs` — 5-state monitor, submission gate (NEW)
- `Infrastructure/Blockchain/ChainStatus.cs` — `Stalled` added
- `Infrastructure/Blockchain/ILibplanetClient.cs` / `LibplanetClient.cs` — `TipPreviousHash` for reorg checks
- `Presentation/Common/FishingView.cs`, `InventoryWindow.cs`, `HudView.cs`,
  `Presentation/Shop/ShopWindow.cs`, `Presentation/Village/TaskBoardWindow.cs`,
  `Presentation/AuntieHouse/CraftWindow.cs` — presenters bound to the merged view + submission gate
- `Scripts/Tests/Stage11LogicTests.cs` — 12 EditMode tests (merge, rollback, debounce, reorg, stall, retry shape)
