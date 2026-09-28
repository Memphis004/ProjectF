# Stage 7 Lessons (Unity Client, Manifest, DLL Ownership, Reflection Audit)

Wiring the ProjectF Stage 7 Unity client (37 runtime scripts, packages
manifest, asmdefs, tools/sync-dlls.ps1) surfaced five distinct failure
groups. Several errors visible in one Unity console pass shared a single
root cause; the only reliable way to separate them was to fix and recompile
per group. Each lesson is encoded in the file it concerns.

## Facts for extraction

- **A Unity-6 module in a 2022.3 manifest blocks ALL resolution**:
  `com.unity.modules.accessibility` does not exist in Unity 2022.3 (it ships
  with Unity 6). One invalid dependency makes the Package Manager refuse the
  whole resolve step, and every later console error is a shadow of it. Fix:
  validate module names against the target Unity version before adding.
- **MagicOnion 7 on Unity needs an explicit DLL-ownership split**: the
  MagicOnion.Client.Unity UPM package (asmdef name `MagicOnion.Unity`) lists
  `Grpc.Core.Api` and `Grpc.Net.Client` as precompiled references; if they
  are missing, the package itself fails with `GrpcChannelOptions` /
  `IMagicOnionAwareGrpcChannel` / `Grpc.Net` errors. The working split:
  NuGetForUnity (Assets/packages.config) owns MagicOnion.Client 7.0.0 +
  MagicOnion.Abstractions + MagicOnion.Shared +
  MagicOnion.Serialization.MessagePack + Grpc.Net.Client/Net.Common/
  Core.Api 2.66.0; UPM git owns YetAnotherHttpHandler 1.11.5 (asmdef
  `Cysharp.Net.Http.YetAnotherHttpHandler`) and MagicOnion.Client.Unity;
  tools/sync-dlls.ps1 owns only the Libplanet stack and ProjectF.*
  assemblies. One assembly, exactly one owner — never two.
- **slimRestore = true disables transitive deps**: Assets/NuGet.config sets
  slimRestore, so NuGetForUnity installs only what packages.config lists.
  MagicOnion.Client needs MagicOnion.Serialization.MessagePack +
  MagicOnion.Shared; Grpc.Net.Client needs
  Microsoft.Extensions.Logging.Abstractions + System.Diagnostics.DiagnosticSource.
  Every transitive dependency must be enumerated by hand, and version bumps
  require re-walking the nuspec chain.
- **Duplicate assembly identity from publish output**: ProjectF.Shared's
  publish output carries MagicOnion.Abstractions.dll and Grpc.Core.Api.dll,
  and sync-dlls.ps1's allowlist briefly copied them into
  Assets/Plugins/ProjectF while NuGetForUnity had already installed them —
  a second identity of the same assembly. The fix is a nuget-provided skip
  list keyed on the Assets/Packages directory; the allowlist now documents
  the whole MagicOnion/Grpc client stack as deliberately absent.
- **A truncated DLL silently disappears**: after an interrupted session,
  Assets/Plugins/ProjectF/ProjectF.Shared.dll was 4,096 bytes against the
  real 12,800. Unity neither errors nor warns on a corrupt assembly — it
  just fails to load it, and the console reports dozens of
  `type or namespace not found` errors for everything the DLL should have
  provided (`ProjectF.Shared.*`). The diagnostic that cracked it was
  comparing file sizes and timestamps between the project's bin/Release
  output and the synced plugin folder. Re-running tools/sync-dlls.ps1 (which
  wipes and repopulates the plugin folder) is self-healing.
- **Diagnostic hierarchy for `type not found`**: the same console message
  covers three root causes of increasing severity — a missing using
  directive, a missing assembly reference, and a corrupt/unloaded DLL.
  Check file size and timestamp of the providing assembly before editing
  code.
- **Unity-side code does not compile until the Editor opens it**: Stage 7
  scripts live outside ProjectF.sln, so the first real compile happened in
  Unity. The batch included missing usings (`System` for IDisposable/
  TimeSpan, `System.Threading` for CancellationToken, `Libplanet.Action`
  for IAction, `ProjectF.Lib.Genesis` for GenesisBuilder),
  the C# 9 `init` polyfill (`IsExternalInit` must be hand-declared under
  Unity 2022.3), and `(Direction)(-1)` constant conversions that need
  `unchecked`.
- **`UnityEngine.AnimationState` is an ambiguity trap**: the legacy Unity
  class shares a name with the ProjectF presence enum, so a plain
  `using ProjectF.Shared.Presence;` makes the name ambiguous (CS0104). The
  fix is a file-scoped alias
  `using AnimationState = ProjectF.Shared.Presence.AnimationState;`
  in exactly the files that name the enum. `Direction` has no UnityEngine
  counterpart and stays unqualified.
- **The Tables class collides with its own namespace**: ProjectF.Tables.Tables
  loses unqualified name resolution to the namespace (CS0118). Consumers
  alias it (`using GeneratedTables = ProjectF.Tables.Tables;`) — the same
  lesson Stage 3/4 recorded for GameTables.cs, now repeated in
  UnityTableService.cs.
- **netstandard2.1/Mono 2022.3 lacks .NET 5+ BCL additions**:
  Convert.ToHexString / Convert.FromHexString do not exist there; hex
  conversion must be hand-rolled (Convert.ToByte(s,16) parse, x2 format).
  Same class of trap: `ImmutableArray<byte>.ToArray()` — whether that
  extension resolves depends on which System.Collections.Immutable version
  the compile picks; the version-proof path is the Length + indexer copy.
- **Reflection over a real assembly beats guessing API shapes**: the
  Libplanet 5.5.3 facts confirmed by a throwaway dotnet scratch project
  (GetMethods/GetProperties dump): `chain.GetWorldState()` (no-arg method —
  the `World` property does not exist), no `BlockChain.GetBlock`, swarm
  `PreloadAsync(IProgress<BlockSyncState>, CancellationToken)`,
  `ValidatorSet(List<Validator>)`, BlockHash has no `operator!=` (use
  `.Equals`), `PrivateKey.ByteArray` is `ImmutableArray<byte>`. Guess-then-
  compile inside Unity costs minutes per cycle; a scratch-project dump costs
  one.
- **Tx-confirmation polling without GetBlock**: because BlockChain 5.5.3
  cannot walk blocks backwards, Stage 7's ActionQueue keeps a ring of the
  last 64 tip hashes and probes `GetTxExecution(hash, txId)` over them —
  the client syncs every mined block, so the winning block must pass through
  Tip within a few polls.
- **SETUP.md location**: the manual editor checklist lives at
  UnityProject/SETUP.md (moved from the old unity/ path; the unity/ folder
  no longer exists).

## Environment snapshot

- Unity 2022.3.62f2, MagicOnion 7.0.0 (server and client pins matched),
  Grpc.Net.Client 2.66.0 (matching HubServer's Grpc.AspNetCore 2.66.0),
  YetAnotherHttpHandler 1.11.5, Libplanet 5.5.3.
- Evidence: UnityProject/Packages/manifest.json,
  UnityProject/Assets/packages.config,
  UnityProject/Assets/Main/Scripts/ProjectF.Unity.asmdef,
  tools/sync-dlls.ps1, UnityProject/SETUP.md.

## Postscript

Written for the wiki ingest pipeline: the watcher deduplicates by content
hash, so this new file (not an edit to netprobe-lessons.md or
stage-3-4-lessons.md) guarantees a fresh extraction pass.
