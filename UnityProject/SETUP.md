# Unity client setup — Stage 8

Stage 8's editor automation generates **everything** that used to be hand-made
(placeholder art, NetworkSettings, prefabs, all 5 scenes, build settings), per
knowledge.md "Editor-generated content". The manual checklist is down to
**3 items**.

Generators live in `UnityProject/Assets/Main/Editor/` (asmdef
`ProjectF.Unity.Editor`, Editor-only). GUI: Unity menu **ProjectF → Setup**.
CLI (CI / headless):

```bash
Unity -batchmode -quit -projectPath UnityProject \
  -executeMethod ProjectF.Editor.BatchSetup.RunFullSetup -logFile -
```

`RunFullSetup` exits non-zero if generation or validation fails;
`ProjectF.Editor.BatchSetup.RunValidation` runs only the checks.

---

## Before you start (build the libraries)

```bash
pwsh tools/gen.ps1        # CSV → C# tables + Luban binaries (needs Luban.dll)
dotnet build ProjectF.sln
pwsh tools/sync-dlls.ps1  # netstandard2.1 + MagicOnion.Client → Assets/Plugins/ProjectF
```

`sync-dlls.ps1` must end with `ProjectF.Lib.dll`, `ProjectF.Shared.dll`,
`ProjectF.Tables.dll` and the Libplanet stack in the copied list.

The presence-client stack is owned elsewhere (no duplicates!):

| Assembly | Source |
|---|---|
| `MagicOnion.Client.dll`, `Grpc.Net.Client.dll`, `Grpc.Net.Common.dll`, `Grpc.Core.Api.dll` | **NuGetForUnity** (`Assets/packages.config` → restore on project open) |
| `YetAnotherHttpHandler` | **UPM** git package (`com.cysharp.yetanotherhttphandler`) |
| `MagicOnion.Client.Unity` | **UPM** git package (`com.cysharp.magiconion.client.unity`) |
| `MessagePack.dll` | **NuGetForUnity** (already pinned 3.1.10) |

If Unity's Console complains that `MagicOnion.Client.Unity` cannot find
`Grpc.Net.Client` / `IMagicOnionAwareGrpcChannel` / `GrpcChannelOptions`, the
NuGetForUnity restore has not run — open **NuGet → Manage NuGet Packages →**
**Restore** (or delete `Assets/Packages` and restore) so the four packages
from `packages.config` land in `Assets/Packages/`.

---

## Checklist (3 items)

### 1. Generate everything

Open the Unity project, then **ProjectF → Setup → Full Setup (all of the
above)** (or run the batch command above). This produces, in dependency order:

| Step | Output |
|---|---|
| Generate Placeholder Sprites | `Assets/Main/Art/Placeholder/` — tiles, 16×32 player sheet (4 dirs × 4 frames), 32×32 9-slice panel, one 16×16 icon per Luban item id (colours keyed by category). Point filter, no compression, PPU 16. |
| Generate Settings Assets | `Assets/Main/Settings/NetworkSettings.asset` with defaults; "Allow downloads over HTTP" = Always allowed. Existing settings are **never** overwritten unless **Force Regenerate** is ticked. |
| Generate Prefabs | `Assets/Main/Prefabs/` — Player (kinematic RB2D), RemotePlayer + name tag, SceneTransitionTrigger, FishingSpot (pondId 1), FarmTile, NpcShopkeeper, NpcAuntie, TaskBoard, **Hud (bare tree: bars, exp, dots — re-parented under UIRoot at runtime)**, FishingWindow, PlayerController. |
| Generate UI Prefabs (Stage 9) | `Assets/Main/Prefabs/UI/` — UIRoot (Screen Space - Camera 320×180 canvas, five layers World/HUD/Window/Modal/Toast with sorting 0/10/20/30/40, LoadingOverlay + binder, UiInputDriver), Toast row, InventoryWindow (tabs/grid/tooltip), ConfirmDialog (modal). Also bakes `Assets/Main/Settings/SpriteRegistry.asset` (white square, 9-slice panel, one icon per item id). |
| Generate Scenes | `Assets/Main/Scenes/` — Persistent (index 0: Pixel Perfect Camera PPU 16 / 320×180 / upscale RT off, EventSystem, **UIRoot instance wired to the camera**, RootLifetimeScope + settings + UI prefabs + sprite registry) + Village / Shop / AuntieHouse / FarmPlot, all wired and added to build settings in order. |
| Validate Project | Static checks: build-settings order, exactly one LifetimeScope per scene, every scope reference assigned, one icon per item id. |

### 2. Point NetworkSettings at a seed (optional for offline play)

Select `Assets/Main/Settings/NetworkSettings.asset` and fill in (values come
from a running `ProjectF.SeedNode` — it prints the peer string and writes
`apv.txt`):

- **Seed Peers**: `{pubkeyHex},{host},{port}` — leave empty to run a local
  solo chain.
- **Apv Token**: the FULL contents of the seed's `apv.txt` (a mismatched token
  = silent message drops).
- **Instance Id / Player Name**: unique per running build (`player1`,
  `player2`, …).

Defaults for `HubHost`/`HubPort` (127.0.0.1:5170), `NodePort` (0) and
`StorePath` (`{persistentDataPath}/chain-{instanceId}`) are already correct —
don't change `NodePort` or remove `{instanceId}` (two-instance gotcha below).

### 3. Smoke test (offline is fine)

1. Open `Assets/Main/Scenes/Persistent.unity`, press **Play**.
2. Console should show, in order: `[tables] loaded …`, `[chain] bootstrap
   done — tip #N …` (or the offline warning), `[boot] chain status: …`,
   `[boot] Stage 7 client up.`, then Village loading additively with the
   generated player.
3. With no seed configured the game runs a **local solo chain**; with a seed
   configured but down it runs read-only and the HUD chain dot goes red.
4. Two editors + `run-two-nodes.ps1` + HubServer = presence visible: move in
   one window, the other follows (10 Hz + interpolation).
5. Stage 9 UI smoke: the UIRoot shows the loading overlay during boot,
   `I` opens the InventoryWindow (grid + category tabs + tooltip from
   TbItem), `Escape` closes the top window, a modal (ConfirmDialog) dims and
   blocks the world below it. Toasts: `IToastService.Info/Success/Warning/
   Error` (3s, errors 6s) and `ShowPending(...)` for the confirming spinner.

To run the full stack:

```bash
# terminal 1
dotnet run --project src/ProjectF.SeedNode     # copy the peer string + apv.txt
# terminal 2
dotnet run --project src/ProjectF.HubServer    # http://127.0.0.1:5170 (h2c)
```

---

## Notes

> Two-instance gotcha: every build needs its own `InstanceId` — the StorePath
> template contains `{instanceId}` and `NodePort` must stay 0 (random), or two
> editors fight over one chain store / port.

- **Regenerating**: re-running any generator rebuilds its output from code.
  Prefabs are deleted and recreated; scenes are rebuilt from scratch. Only
  `NetworkSettings.asset` is protected by the **Force Regenerate** toggle.
- **Real art**: drop PNGs over the placeholder files (same paths, same sprite
  names, PPU 16) — no code changes needed.
- **Validation in CI**: `BatchSetup.RunFullSetup` / `RunValidation` exit
  non-zero with a readable report on failure.
- Scene ids MUST match `data/pond.csv` (village pond is scene 4 = FarmPlot);
  `SceneId.cs` is the contract.
