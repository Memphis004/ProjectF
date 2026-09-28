# Stage 8 Lessons (Editor Automation, asmdef Topology, Scene Factories, MCP-Driven Verification)

Automating ProjectF Stage 8 (the editor scripts under
UnityProject/Assets/Main/Editor/ that generate placeholder art, prefabs,
scenes and settings) surfaced a distinct class of failure from Stage 7: here
the errors were not about the runtime stack but about Unity's asset/assembly
topology and about how serialized scene data actually resolves at runtime.
Every fix below is encoded in the file it concerns, and the whole pipeline
was verified by EXECUTING it inside the live editor (not by reading code):
two full `BatchSetup.RunFullSetupSteps()` passes, both ending with
`ProjectF validation: PASS (0 error(s), 0 warning(s))`, then a play-mode
smoke test that reached the Village scene additively.

## Facts for extraction

- **Editor-script folder vs asmdef location**: a `.cs` file under
  `Assets/Main/Editor/` while the only editor asmdef sits at
  `Assets/Main/Scripts/Editor/` compiles into Assembly-CSharp-Editor
  (unmanaged, no project references). The fix was `git mv` of the asmdef
  (and Probe.cs, keeping its .meta so GUIDs survive) to
  `Assets/Main/Editor/`, with generator scripts under
  `Assets/Main/Editor/ProjectF/`. Files moved WITH their .meta keep their
  identity; a bare rename is a delete+create to Unity.
- **asmdef references are NOT transitive**: `ProjectF.Unity` references
  `VContainer`, but the editor assembly still failed with CS0246
  'VContainer' until the editor asmdef listed `VContainer` itself. Same for
  `Unity.2D.PixelPerfect` after adding the package. Rule: every assembly
  whose code directly mentions a type must reference the defining asmdef by
  its exact name (VContainer's runtime asmdef is literally named `VContainer`;
  the pixel-perfect one is `Unity.2D.PixelPerfect`).
- **Two different PixelPerfectCameras**: `com.unity.render-pipelines.universal`
  was installed yet `PixelPerfectCamera` did not resolve, because the project
  runs the BUILTIN pipeline (`m_CustomRenderPipeline: 0` in
  GraphicsSettings/QualitySettings) and URP's PixelPerfectCamera is a
  DIFFERENT type that only functions under URP. The builtin-pipeline
  component ships in `com.unity.2d.pixel-perfect` (2.1.0 on Unity 2022.3).
  Lesson: a package being present does not mean its type is the one you
  want; check which render pipeline is active before picking the component.
- **Read the package source instead of guessing property names**: the
  builtin PixelPerfectCamera's property is `pixelSnapping`, not `pixelSnap`
  (one failed compile revealed it; a 30-second grep of
  Library/PackageCache/com.unity.2d.pixel-perfect@2.1.0/Runtime/PixelPerfectCamera.cs
  would have). Its serialized settings used here: assetsPPU 16,
  refResolution 320x180, upscaleRT false, pixelSnapping true, cropFrameX/Y true.
- **Duplicate scoped registries in manifest.json poison ALL package
  resolution**: a second entry `package.openupm.com` repeating the same
  scopes (`com.ivanmurzak`, `extensions.unity`) as the `OpenUPM` entry made
  every resolve fail with "Registry configuration is invalid … No packages
  loaded", and `PackageManager.Client.Add` returned HTTP 500. The scopes
  must be defined by exactly one registry.
- **AssetDatabase.Refresh does NOT re-resolve packages**: after fixing
  manifest.json, `Client.Add` (via the package-add tool) was required to
  materialize the package into Library/PackageCache and regenerate
  packages-lock.json. Refresh only imports assets.
- **PlayerSettings "Allow downloads over HTTP" is a serialized INT, not an
  enum**: `SerializedProperty.enumValueIndex` on `insecureHttpOption`
  throws "type is not a enum value" (both get and set). It must be written
  as `intValue` — 0 = not allowed, 1 = dev builds only, 2 = always allowed.
  The wrapper names suggested by older docs (`AndroidAllowHTTP`,
  `wsaAllowHTTPDownloads`) do not exist in Unity 2022.3; grep
  ProjectSettings.asset for the real key (`insecureHttpOption`).
  Access pattern: `new SerializedObject(
  Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"))`.
- **A freshly-written PNG has no importer until imported**: writing bytes
  with File.WriteAllBytes and immediately calling
  `AssetImporter.GetAtPath` returns null. Write →
  `AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport)`
  → then configure the TextureImporter (Point filter, no compression,
  PPU 16, alphaIsTransparency) and SaveAndReimport.
- **Sprite-sheet geometry: texture row 0 is the BOTTOM**: the player sheet
  (64x128, 4 rows x 4 frames of 16x32) draws rows Down/Up/Left/Right
  visually top-to-bottom, so the pixel lambda must invert
  (`row = (Rows-1) - y / 32`) while SpriteMetaData rect.y (measured from
  the bottom) uses `(Rows-1-r) * 32`. Slice naming: `Player_{Dir}_{frame}`,
  index = direction*4 + frame, matching ProjectF.Shared's
  `Direction` enum (Down=0, Up=1, Left=2, Right=3) and
  `AnimationState` (Idle=0, Walk=1) so animator ints map 1:1.
- **Sprite keyframes have no binding factory**: float curves have
  `EditorCurveBinding.PPS`, but object-reference curves must construct the
  binding directly — `new EditorCurveBinding { path = "", type =
  typeof(SpriteRenderer), propertyName = "m_Sprite" }` — and pass it to
  `AnimationUtility.SetObjectReferenceCurve`. Looping requires the clip
  settings (`AnimationUtility.GetAnimationClipSettings` → loopTime = true →
  SetAnimationClipSettings) plus a loop-tail keyframe repeating frame 0.
  The generated controller has 8 states (Idle_/Walk_ x 4 directions) driven
  by the runtime's `State` and `Facing` int parameters, fully
  inter-connected (each state's transitions carry both Equals conditions),
  so a facing change mid-walk swaps rows immediately.
- **VContainer parentReference is a TYPE NAME string, not an object
  reference**: `ParentReference` serializes only `TypeName`
  (`[NonSerialized] public LifetimeScope Object`), resolved by type at
  runtime in OnAfterDeserialize. Therefore gameplay scenes NEVER need a
  cross-scene object reference to the Persistent scene's RootLifetimeScope
  — set `parentReference.TypeName = "ProjectF.Infrastructure.RootLifetimeScope"`
  and every scene file can be built and saved independently in Single mode
  (no multi-scene editing pass, no additively-open dance). Read
  Library/PackageCache/jp.hadashikick.vcontainer@…/Runtime/Unity/ParentReference.cs
  before writing any scope-wiring tool.
- **Scene files cannot reference objects in other scene files**: the
  HudView/FishingView INSTANCES must be instantiated inside each gameplay
  scene (the scopes' `[SerializeField]` fields point at in-scene objects);
  the Persistent scene's HUD canvas root stays a separate, empty Stage 9
  canvas. Spawns are parented under the scope GameObject.
- **Wiring [SerializeField] fields requires exact names**: every generator
  sets fields via `SerializedObject.FindProperty("...")` with the runtime
  names — playerPrefab/remotePlayerPrefab/hudView/spawnPoint/fishingView on
  the four scopes, networkSettings on RootLifetimeScope,
  targetScene/spawnAt on SceneTransitionTrigger (sceneId stored as
  `intValue` = the SceneId enum number 1..4), animator/spriteRenderer on
  PlayerView, spriteRenderer/nameTag on RemotePlayerView,
  nameLabel/staminaLabel/goldLabel/levelLabel/sceneLabel/tipLabel/
  chainStatusDot/presenceStatusDot on HudView, statusLabel/castButton/
  revealPanel on FishingView, plotIndex on FarmTileView. Renaming any
  runtime field breaks the generator loudly (FindProperty returns null) —
  that is by design.
- **The validator mirrors VContainer's contract**: because scopes
  `RegisterInstance` every serialized field, a null objectReference anywhere
  on a LifetimeScope is a boot failure. ProjectValidator iterates the
  SerializedObject and fails on ANY null object reference, in addition to
  checking exactly-one-scope-per-scene, expected scope type names,
  parentReference.TypeName, Persistent-at-build-index-0, and one placeholder
  icon per data/item.csv row (29 items). Batch exit codes are guarded with
  `Application.isBatchMode` — an interactive menu/button calling
  `EditorApplication.Exit` would kill the editor.
- **The Luban item table's source of truth is data/item.csv**: the editor
  assembly cannot see the generated C# tables (they live in
  ProjectF.Tables, synced only into the runtime plugin DLLs), so the sprite
  generator and validator parse data/item.csv directly (strip BOM, skip
  `##` header lines, columns: id / name_key / category / base_price /
  stackable). Categories Bait/Rod/Fish/Seed/Crop/Material/Food each get a
  fixed hue; unknown categories fall back to grey.
- **MCP-driven verification loop (unity-mcp-cli)**: `script-execute`
  requires the compiled class to be named `Script` with a method `Main`
  (errors "does not contain class 'Script'" / "does not contain method
  'Main'" otherwise); the CLI needs the Unity project path as a positional
  argument (`npx unity-mcp-cli run-tool <tool> --input-file args.json
  UnityProject`) or it probes the wrong cwd; console logs are read back via
  console-get-logs. The definitive Stage 8 verification was executing
  `BatchSetup.RunFullSetupSteps()` + `ProjectValidator.Validate()` in the
  LIVE editor twice (idempotent second pass), not reading the code.
- **Generated content verified by play mode**: smoke test on the generated
  Persistent scene showed the full Stage 7 boot chain —
  `[tables] loaded … (items: 29, ponds: 1)`,
  `[boot] chain status: Offline (tip #0)`,
  `[boot] Stage 7 client up.` — and during play BOTH scenes were loaded
  (Persistent buildIndex 0 + Village buildIndex 1), proving
  SceneRouter's additive load and the parentReference wiring work from
  generated data alone.
- **Pre-existing main-thread bug surfaced by the smoke test**:
  `LibplanetClient.BootstrapAsync` (~line 85) touches
  `Application.persistentDataPath` from a threadpool task, logging
  "get_persistentDataPath can only be called from the main thread" and
  degrading to offline read-only mode. The UX contract absorbed it (boot
  continued, read-only mode per knowledge.md), but the queued fix is to
  resolve the store path BEFORE hopping off the main thread — same class of
  Unity 2022.3 Mono trap as the Stage 2.5 Probe.cs lessons.

## Environment snapshot

- Unity 2022.3.62f2, builtin render pipeline + com.unity.2d.pixel-perfect
  2.1.0 (newly added), VContainer 1.19.0 (git), UniTask, uGUI Text (legacy,
  project has TMP installed but the Stage 7 views use `UnityEngine.UI.Text`).
- Editor assembly `ProjectF.Unity.Editor` (Editor-only) at
  `Assets/Main/Editor/`, references: ProjectF.Unity, VContainer,
  Unity.2D.PixelPerfect.
- Generated outputs: Assets/Main/Art/Placeholder/ (Player.png 64x128,
  Tiles/*.png+*.asset, Items/Item_*.png x29, UI/Panel.png 9-slice,
  UI/WhiteSquare.png, PlayerController.controller 8 states),
  Assets/Main/Prefabs/ (Player, RemotePlayer, SceneTransitionTrigger,
  FishingSpot, FarmTile, NpcShopkeeper, NpcAuntie, TaskBoard, Hud,
  FishingWindow), Assets/Main/Scenes/ (Persistent, Village, Shop,
  AuntieHouse, FarmPlot — build order 0..4), Assets/Main/Settings/
  NetworkSettings.asset, EditorBuildSettings ordered with Persistent at
  index 0, ProjectSettings insecureHttpOption=2.
- New runtime component: FishingSpot.cs (pondId, RequireComponent
  BoxCollider2D) — the spec's FishingSpot prefab had no backing type before
  Stage 8.
- Evidence: UnityProject/Assets/Main/Editor/**,
  UnityProject/Packages/manifest.json (duplicate scoped registry removed),
  UnityProject/Packages/packages-lock.json, UnityProject/SETUP.md
  (rewritten to a 3-item checklist), data/item.csv,
  UnityProject/Assets/Main/Scripts/Presentation/Common/FishingSpot.cs.

## Postscript

Written for the wiki ingest pipeline: the watcher deduplicates by content
hash, so this new file (not an edit to stage-7-lessons.md or
netprobe-lessons.md) guarantees a fresh extraction pass. Suggested entity
seeds for extraction: BatchSetup, ProjectFSetupWindow, PlaceholderSpriteGenerator,
PrefabGenerator, SceneGenerator, SettingsAssetGenerator, ProjectValidator,
FishingSpot; suggested concepts: asmdef non-transitive references,
VContainer parentReference TypeName resolution, builtin-vs-URP
PixelPerfectCamera, duplicate-scoped-registry resolution poisoning,
MCP-driven editor verification (script-execute Script.Main).
