# ProjectF

A cozy pixel-art fishing & farming game (Stardew Valley look, Pixels.xyz
structure) built on **Libplanet 5.x** — every ownership/economy mutation lives
on-chain, while a MagicOnion presence layer handles cosmetics only. Unity
2022.3 LTS client; the on-chain core targets `netstandard2.1` so Unity loads
the same DLLs the server runs.

Hard rules live in the project knowledge base (`9-27-2026-1-30pm.md`):
deterministic `IAction.Execute()` only, no wall-clock/randomness on-chain, two
network layers never mixed, Bencodex/MessagePack serialization boundaries.

## Layout

```
src/ProjectF.Lib          On-chain actions + states (netstandard2.1)
src/ProjectF.Lib.Tests    xUnit (88 tests incl. determinism + round-trips)
src/ProjectF.Tables       Luban-generated game tables (netstandard2.1)
src/ProjectF.Shared       MagicOnion contracts (Stage 6)
src/ProjectF.SeedNode     Headless chain node + miner (net8.0)
src/ProjectF.HubServer    Presence server (Stage 6)
data/                     Luban CSV sources
tools/                    gen.ps1, sync-dlls.ps1, audit-determinism.ps1
scripts/                  run-two-nodes.ps1, run-netprobe.ps1
UnityProject/             Unity client (Stage 7)
artifacts/                Script outputs (logs, validator.key) — gitignored
```

## Prerequisites

- .NET SDK 8+ (repo builds on SDK 9)
- PowerShell 7 (`pwsh`) for the scripts
- Unity 2022.3 LTS with the MCP plugin (only for the netprobe / Stage 7)

## Build & test

```bash
dotnet build ProjectF.sln
dotnet test src/ProjectF.Lib.Tests/ProjectF.Lib.Tests.csproj
pwsh tools/gen.ps1                  # CSV -> C# tables + Unity binary tables (needs Luban.dll)
pwsh tools/audit-determinism.ps1    # greps ProjectF.Lib for on-chain determinism violations
```

## Run order

```bash
# 1. tables (once per data change)
pwsh tools/gen.ps1

# 2. build + tests (incl. Stage 6 presence integration tests)
dotnet build ProjectF.sln && dotnet test src/ProjectF.Lib.Tests

# 3. Stage 5 + 6 proof in ONE command: two chain nodes converge, then the
#    presence integration tests run against a live HubServer (skip with -SkipPresence)
pwsh scripts/run-two-nodes.ps1          # COMBINED: PASS = chain tip hashes match AND presence flows hold

# 4. Stage 2.5 Unity pipeline proof (Unity editor must be open with MCP running)
pwsh scripts/run-netprobe.ps1

# 5. Stage 6 presence server (cosmetic layer — chain keeps all ownership)
cd src/ProjectF.HubServer && dotnet run   # h2c on http://127.0.0.1:5170

# 6. Stage 7 Unity client — not wired yet
```

## The bootstrap files

When a seed node boots with a `StorePath`, it writes three files there. They
are the **entire interface** a joining node needs — no keys, no manual chain
setup:

| File         | Written by | Consumed by                    | Content |
|--------------|------------|--------------------------------|---------|
| `genesis.dat`| seed       | follower (`GenesisPath`), Unity probe | Bencodex-encoded genesis `Block` — the chain's identity. A follower cannot derive it locally (it is key-derived), it must load these bytes. |
| `peer.txt`   | seed       | follower (`StaticPeers`), Unity `NetworkSettings.SeedPeers` | `{pubkeyHex},{host},{port}` — Libplanet's standard `BoundPeer.ParsePeer` format. |
| `apv.txt`    | seed       | follower (`ApvToken`), Unity `NetworkSettings` | Pre-signed **AppProtocolVersion token** (`version/signerAddress/signature`). Libplanet silently drops any message whose signed APV differs, signer included — every node in a network must present the *same token*. |

**No file here contains a secret.** The old `privkey.txt` export was removed
in the Stage 5 rework: sharing the validator key made two nodes one peer
identity (Libplanet throws `Cannot receive ping from self`) and both would
mine competing forks. The validator key now lives in
`artifacts/validator.key` (gitignored), generated once and reused by
`scripts/*.ps1` for restart determinism.

## Two nodes on one machine (manual)

**Terminal 1 — seed (validator, miner):**

```bash
# first time: generate a stable validator key and keep it
openssl rand -hex 32 > mykey.hex

dotnet run --project src/ProjectF.SeedNode -- \
  --SeedNode:StorePath=storeA --SeedNode:Port=31236 \
  --SeedNode:PrivateKeyHex=$(cat mykey.hex)
```

**Terminal 2 — follower (its own key, does not mine):**

```bash
dotnet run --project src/ProjectF.SeedNode -- \
  --SeedNode:StorePath=storeB --SeedNode:Port=31237 --SeedNode:IsMiner=false \
  --SeedNode:GenesisPath=storeA/genesis.dat \
  --SeedNode:ApvToken=$(cat storeA/apv.txt) \
  --SeedNode:StaticPeers=$(cat storeA/peer.txt)
```

Within ~10 seconds both print a `[status] Peers: 1, Tip: #N` heartbeat with
climbing tips. Proof of convergence: the `Appended the block #N <hash>` lines
show the **same block hashes** on both nodes (the follower appends the seed's
blocks; it produces none). `pwsh scripts/run-two-nodes.ps1` automates exactly
this and exits PASS/FAIL on it. The same script then starts ProjectF.HubServer
on 127.0.0.1:5170 and runs the presence integration tests against the live
server (`PF_HUB_URL=http://127.0.0.1:5170`) — one command proves both stages.
The tests also run standalone in-process by default; set `PF_HUB_URL` to point
them at any already-running hub.

Every `--SeedNode:X` flag also works as `PF_SeedNode__X` environment variables
or as `X` keys in `src/ProjectF.SeedNode/appsettings.json`.

## How nodes join a network — the Stage 5 rules

1. **One node = one key.** The key *is* the peer identity. Never share it.
2. **The validator derives the chain from its key**; everyone else must load
   `genesis.dat`. A follower's own state store is populated by deterministically
   re-executing the genesis transaction on first boot (checked against the
   header's state root).
3. **Same APV token everywhere**, or messages are dropped with no error.
4. **Only the seed mines** (`IsMiner=true`) until PBFT consensus is wired
   (`ConsensusReactorOption`) — two independent miners on a single-validator
   chain just fork.
5. **Restarts:** keep the validator key (`artifacts/validator.key` or your own
   hex file) or the store rejects the new genesis with
   `InvalidGenesisBlockException`. A fresh key requires a fresh store
   (`-FreshSeed` in the scripts).

## Presence server (Stage 6)

`ProjectF.Shared` holds the MessagePack models + `IPlayerHub`/`IPlayerHubReceiver`
contracts (netstandard2.1 — the same DLLs Unity loads). `ProjectF.HubServer`
implements them with MagicOnion 7: one hub instance per connection, grouped by
`scene-{id}`, backed by an in-memory `PresenceRegistry`.

- Server → clients: `OnSceneSnapshot`, `OnJoin`, `OnLeave`, `OnMove`,
  `OnEmote`, `OnPondHint` (all cosmetic only).
- Client → server: `JoinAsync` (returns the scene roster), `MoveAsync` (≤10 Hz),
  `EmoteAsync`, `ChangeSceneAsync` (leaves old group + broadcasts, joins new),
  `LeaveAsync`.
- **No ghost players:** killing a connection (no clean leave) still removes the
  player and fires `OnLeave` for the survivors — covered by integration tests
  (`PresenceHubIntegrationTests`: two fake clients on scene 4 over real gRPC,
  move + emote + scene-change + kill-connection flows).
- StreamingHub method returns must be `Task`/`ValueTask` forms — MagicOnion 7
  rejects `UnaryResult` on hubs at mapping time (it is for plain unary services).

## Unity client (Stage 7)

The client reads the same bootstrap artifacts (already proven by
`UnityProject/Assets/Main/Scripts/Editor/Probe.cs`):

- `NetworkSettings.SeedPeers[]` ← `peer.txt`
- `NetworkSettings` APV token field ← `apv.txt`
- genesis block ← `genesis.dat` (ship via `StreamingAssets/`)
- `StorePath` template **must** contain `{instanceId}` and `NodePort: 0` so two
  builds on one machine never lock the same chain store.

DLLs flow via `pwsh tools/sync-dlls.ps1` (`netstandard2.1` only — never
reference the SeedNode from Unity).

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `BootstrapAsync failed: All seeds are unreachable` | Seed not running, wrong port in `peer.txt`, or APV mismatch (pass `apv.txt`). |
| `Peers: 0` forever on both nodes | APV token differs between nodes — the seed's token must be passed via `ApvToken`. |
| `Genesis state root mismatch: genesis.dat evaluates to …` | `genesis.dat` was produced by a different action set or key — recopy it from the seed (or `-FreshSeed` both stores). |
| `InvalidGenesisBlockException` on seed restart | The validator key changed — restore `PrivateKeyHex` or wipe the store. |
| Follower tip never climbs | By design it does not mine; if it is also not syncing, see the two rows above. |
| `Could not parse NetMQMessage properly` on the seed | Classic shared-key symptom — a node is presenting the seed's identity. Give the follower its own key. |
