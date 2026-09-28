# ProjectF — Project Knowledge

## What this is
A cozy pixel-art fishing/farming game (Stardew Valley visual style, Pixels.xyz structure)
built on **Libplanet** (C# blockchain library), with a separate real-time presence layer.

## Hard architectural rules — NEVER violate these

1. **Two-layer split is absolute.**
   - Consensus layer (Libplanet): anything affecting ownership or economy —
     inventory, gold, stamina, levels, fish roll results, craft results, pond slot ownership.
   - Presence layer (MagicOnion): cosmetic only — player position, facing, animation state, emotes.
   - NEVER put position/animation on-chain. NEVER let the presence layer decide ownership.

2. **All `IAction.Execute()` code MUST be 100% deterministic.**
   - FORBIDDEN inside Execute: `DateTime.Now`, `DateTime.UtcNow`, `System.Random`,
     `Guid.NewGuid()`, file I/O, network calls, `Environment.*`, unordered collection iteration.
   - Use `context.BlockIndex` instead of wall-clock time.
   - Use `context.GetRandom()` for ALL randomness.
   - Use `SortedDictionary`/ordered iteration when serializing, never `Dictionary` enumeration order.

3. **No cron/scheduler exists on a blockchain.**
   - Crop growth: store `PlantedAt` block index; evaluate `context.BlockIndex - PlantedAt >= GrowBlocks` at harvest.
   - Pond occupancy: store `ExpiresAtBlock`; lazily clean up expired slots when the next action touches that pond.
   - Stamina: store `StaminaUpdatedAt`; regenerate by elapsed block count on read.

4. **Target frameworks.**
   - `ProjectF.Lib`, `ProjectF.Shared`, `ProjectF.Tables` → `netstandard2.1` ONLY (Unity must load these DLLs).
   - `ProjectF.SeedNode`, `ProjectF.HubServer` → `net8.0`.
   - Unity cannot consume NuGet directly; DLLs are copied via `tools/sync-dlls.ps1`.

5. **Serialization boundaries.**
   - Unity ↔ Libplanet → **Bencodex** (mandated by Libplanet).
   - Unity ↔ MagicOnion → **MessagePack** (`[MessagePackObject]` + explicit `[Key(n)]`).
   - CSV → code → **Luban** (`cs-bin`), output shared by both server and Unity.
   - **Do NOT add Protobuf.** There is no cross-language boundary in this project.

6. **Signer validation.** Every action that mutates a player's state must verify
   `context.Signer == AvatarAddress` before doing anything else.

## Reference projects (patterns to follow)
- https://github.com/planetarium/planet-clicker — overall Unity + Libplanet wiring
- https://github.com/planetarium/lib9c — action/state structure at scale
- https://github.com/Cysharp/MagicOnion — StreamingHub patterns

## Tech stack (fixed — do not substitute)
Unity 2022.3 LTS (URP 2D) · VContainer (DI) · UniTask (async) · MessagePack ·
MagicOnion 7.x (presence) · Libplanet 5.x (chain) · Luban (CSV data tables) · xUnit (tests)

## Style conventions
- C# file-scoped namespaces, nullable enabled, `sealed` on concrete classes.
- Unity: MVP pattern — `XxxPresenter` (logic, plain C# + VContainer) / `XxxView` (MonoBehaviour, no logic).
- One `LifetimeScope` per scene, `RootLifetimeScope` for app-lifetime singletons.
- Presence sends at 10 Hz max, never per-frame; clients interpolate.
- Comments in code: English. Commit messages: English.
## Unity scene model
- `Persistent.unity` loads first and is NEVER unloaded. It hosts RootLifetimeScope,
  the main Camera, the HUD canvas, and all app-lifetime singletons.
- Gameplay scenes (Village=1, Shop=2, AuntieHouse=3, FarmPlot=4) are loaded ADDITIVELY,
  one at a time, and unloaded only after the replacement is live.
- Every gameplay scene has exactly one `XxxLifetimeScope` whose parent is RootLifetimeScope.
- Scene ids in the enum MUST match the scene_id column in data/pond.csv.

## UX contract for on-chain actions
- No user-facing action may block the UI thread waiting for a block.
- Every action shows: immediate local feedback -> "confirming" indicator -> confirmed or rolled back.
- If the chain is unreachable, the game stays playable in read-only mode and says so clearly.
- If the presence hub is unreachable, the game runs in single-player visual mode and says so.

## Editor-generated content
- Scenes, prefabs and ScriptableObject assets are produced by editor scripts under
  `Assets/Main/Editor/`, never by hand, so they can be regenerated and reviewed in git.
- Art is placeholder: solid-colour sprites generated at runtime/import time.
  Real pixel art is dropped in later and must not require code changes.
