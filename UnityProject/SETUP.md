# Unity client setup — Stage 7 manual checklist

Stage 7 delivers all **code** (Infrastructure + Presentation scripts), the
package manifest, the asmdef, and the DLL sync pipeline. Scenes, prefabs and
ScriptableObject assets **must be created in the Unity Editor** — that work is
not faked from the command line. Follow this checklist top-to-bottom once;
stage 8 replaces most of it with editor automation.

Before starting, from the repository root:

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

> Two-instance gotcha: every build needs its own `InstanceId` (NetworkSettings)
> — the StorePath template contains `{instanceId}` and `NodePort` must stay 0
> (random), or two editors fight over one chain store / port.

---

## Checklist

### 1. Enable HTTP/2 cleartext for the presence hub

The hub (MagicOnion 7) requires HTTP/2. Player settings:

1. Edit → Project Settings → Player → Other Settings → Configuration.
2. Under **Allow downloads over HTTP**, set **Always allowed**
   (local dev talks plain `http://` to 127.0.0.1:5170).

### 2. Create the NetworkSettings asset

1. In the Project window, right-click `Assets/Main/Settings` →
   Create → **ProjectF → Network Settings**. Name it `NetworkSettings`.
   (Create the `Settings` folder if it does not exist.)
2. Select the asset and fill in:
   - **Seed Peers**: paste the seed's peer string, format
     `{pubkeyHex},{host},{port}` — printed by `ProjectF.SeedNode` on boot
     (or the first line of its `store/peer.txt`). Leave empty to run a local
     solo chain.
   - **Apv Token**: paste the FULL contents of the seed's `apv.txt`
     (pre-signed AppProtocolVersion token). Different token = silent message
     drops.
   - **Store Path**: keep the default
     `{persistentDataPath}/chain-{instanceId}`.
   - **Node Port**: keep `0`.
   - **Hub Host / Hub Port**: `127.0.0.1` / `5170`.
   - **Instance Id**: unique per running build (`player1`, `player2`, …).
   - **Player Name**: your display name (cosmetic layer only).

### 3. Create the Persistent scene (index 0 — never unloaded)

1. File → New Scene → **Empty Scene**, save as `Assets/Main/Scenes/Persistent.unity`.
2. Add a **Camera** named `Main Camera` (Stage 8 adds Pixel Perfect Camera,
   reference res 320×180, PPU 16).
3. Add an **EventSystem** GameObject (GameObject → UI → Event System).
4. Create an empty GameObject named `RootLifetimeScope` and add the
   `RootLifetimeScope` component (script: Infrastructure/RootLifetimeScope.cs).
5. Assign the `NetworkSettings` asset from step 2 into its
   **Network Settings** field.
6. (HUD canvas comes with Stage 9 — nothing else is required to boot.)

### 4. Create the four gameplay scenes

Each scene: File → New Scene → Empty Scene, then the matching
LifetimeScope + one child named `Spawn` (empty GameObject positioned where
the player should appear on entry):

| Scene id | File                                        | Scope component            |
|----------|---------------------------------------------|----------------------------|
| 1        | `Assets/Main/Scenes/Village.unity`          | `VillageLifetimeScope`     |
| 2        | `Assets/Main/Scenes/Shop.unity`             | `ShopLifetimeScope`        |
| 3        | `Assets/Main/Scenes/AuntieHouse.unity`      | `AuntieHouseLifetimeScope` |
| 4        | `Assets/Main/Scenes/FarmPlot.unity`         | `FarmPlotLifetimeScope`    |

For each gameplay scene:

1. Create an empty GameObject named `<Scene>LifetimeScope` (e.g. `VillageLifetimeScope`)
   and add the matching scope script (Presentation/<Scene>/…).
2. Set its **Parent Reference** (Inspector, VContainer section):
   Type = `RootLifetimeScope` (project script), Object = the Persistent scene's
   `RootLifetimeScope` object. Open Persistent additively while editing if the
   picker cannot find it (right-click scene tab → Additive).
3. Create an empty child GameObject named `Spawn` under the scope object;
   drag it into the scope's **Spawn Point** field.
4. Assign the prefab fields (see section 5): **Player Prefab**,
   **Remote Player Prefab**, **Hud View** (an empty GameObject is fine — HUD
   visuals arrive in Stage 9), and for FarmPlot also **Fishing View**.
5. In `FarmPlot.unity`, the pond itself needs nothing manual yet — Stage 13
   finishes the mini-game; the presenter already targets pondId 1 (see
   `FarmPlotPresenter.VillagePondId`).

Scene ids MUST match `data/pond.csv`: the village pond is scene 4 (FarmPlot).

### 5. Create placeholder prefabs (until Stage 8 automates this)

Minimal throwaway versions so the scopes bind — Stage 8's generators replace
them without code changes:

**Player prefab** (`Assets/Main/Prefabs/Player.prefab`):

1. Empty GameObject `Player`, add components: `SpriteRenderer`
   (any 2D sprite), `Rigidbody2D` (**Body Type: Dynamic**, **Gravity Scale 0**,
   **Collision Detection: Continuous**), `BoxCollider2D`, `Animator`
   (empty controller), `PlayerView`, `PlayerInputController`,
   `PresenceBroadcaster`.
2. Drag into `Assets/Main/Prefabs/`, delete from the scene.

> The player is spawned ONCE by `ScenePlayerService` (app-lifetime) and kept
> alive with DontDestroyOnLoad — do NOT place a Player instance in any scene.

**RemotePlayer prefab** (`Assets/Main/Prefabs/RemotePlayer.prefab`):

1. Empty GameObject `RemotePlayer`: `SpriteRenderer`, `RemotePlayerView`.
2. Child GameObject `NameTag`: add `TextMesh` (character size ~0.1, anchor
   Middle Center) + `NameTagView`; drag it into the view's **Name Tag** field.
3. Save as prefab.

### 6. Build settings

File → Build Settings → Scenes In Build, in this exact order:

| Index | Scene          |
|-------|----------------|
| 0     | Persistent     |
| 1     | Village        |
| 2     | Shop           |
| 3     | AuntieHouse    |
| 4     | FarmPlot       |

(SceneRouter unloads only gameplay scenes; Persistent is never unloaded and
never left alone.)

### 7. Smoke test (offline is fine)

1. Open `Persistent.unity`, press **Play**.
2. Console should show, in order:
   `[tables] loaded …`, `[chain] bootstrap done — tip #N …` (or the offline
   warning), `[boot] chain status: …`, `[boot] Stage 7 client up.`,
   then Village loading additively.
3. With no seed configured the game runs a **local solo chain** — actions
   mine on this node only. With a seed configured but down, the game runs in
   read-only mode and the HUD chain dot goes red.
4. Two editors + `run-two-nodes.ps1` + HubServer = presence visible:
   move in one window, the other follows (10 Hz + interpolation).

### 8. Run the full stack (order from README)

```bash
# terminal 1
dotnet run --project src/ProjectF.SeedNode     # copy the peer string + apv.txt
# terminal 2
dotnet run --project src/ProjectF.HubServer    # http://127.0.0.1:5170 (h2c)
```

Paste the peer string + APV token into NetworkSettings, press Play.

---

## What Stage 8 removes from this checklist

Scene/prefab/asset generation moves into editor scripts
(`Assets/Main/Editor/`), leaving at most 3 manual items. Until then this page
is the source of truth for editor-side wiring.
