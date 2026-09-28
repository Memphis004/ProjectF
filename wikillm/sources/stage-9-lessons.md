# Stage 9 Lessons (UI Layer, VContainer Service Wiring, Prefab Self-Containment, MCP Loop)

Stage 9 delivered the whole uGUI stack — UIRoot with five canvas layers,
pooled WindowService (Escape/modal blocker), ToastService (queue + pending
spinner), LocalizationService (th/en CSVs), LoadingOverlay with a chain-sync
progress line, InventoryWindow (tabs/grid/tooltip), ConfirmDialog (returns
bool), the Hud upgrade (bars + regen ETA + exp) and the editor menu
`ProjectF/Setup/Generate UI Prefabs`. Every generator was executed in the
LIVE editor (MCP `script-execute`), ending with
`ProjectF validation: PASS (0 error(s), 0 warning(s))` and a play-mode smoke
test whose boot chain is clean: `[loc] loaded Th: 63 keys` →
`[tables] loaded … (items: 29, ponds: 1)` → `[boot] Stage 7 client up.` with
zero exceptions, and an in-play hierarchy dump proving
`UIRoot/Hud → HUD (HudView, staminaBar wired)`.

## Facts for extraction

- **VContainer `RegisterInstance` keys by IMPLEMENTATION type**: registering
  three prefab fields via `builder.RegisterInstance(toastPrefab)` +
  `(inventoryWindowPrefab)` + `(confirmDialogPrefab)` (all `RectTransform`)
  throws at container build: `VContainerException: Conflict implementation
  type : Registration RectTransform`. Fix: one typed bundle class
  (`UiPrefabSet` with three named fields) registered as a single instance.
- **`Func<T>` is NOT auto-registered in VContainer**: a service that wants
  lazy resolution (`Func<IObjectResolver>`) fails with
  `No such registration of type: System.Func'1[VContainer.IObjectResolver]`.
  Fix for the WindowService cycle (it must resolve WindowPrefab<T>/presenters
  only at FIRST OPEN, i.e. after the container is built): hold the
  `RootLifetimeScope` reference and read `scope.Container` lazily via a
  property; register with `builder.Register<IWindowService>(c => new
  WindowService(this, …), Singleton)` — `this` is the scope, captured.
- **Unity resolves MonoBehaviour/ScriptableObject scripts by CLASS NAME ==
  FILE NAME**: `SpriteRegistryAsset` (ScriptableObject) and
  `SpriteRegistrySource` (MonoBehaviour) both living in SpriteRegistry.cs
  made the generated .asset script-less: console warning `No script asset for
  SpriteRegistryAsset`, the asset's fields silently null, and the scene
  instance logged `The referenced script on this Behaviour (Game Object
  'UIRoot') is missing!`. Fix: one class per file (SpriteRegistryAsset.cs,
  SpriteRegistrySource.cs); delete the script-less asset and regenerate.
- **A UI prefab must be SELF-CONTAINED**: the first UIRoot prefab left the
  `[SerializeField] worldLayer…toastLayer` fields unassigned (the generator
  created the child layer GameObjects but never wrote them back into the
  component). At runtime `GetLayer` threw `The variable worldLayer of UIRoot
  has not been assigned`. Fix: after creating the layer children, the
  generator wires them with `SerializedObject.FindProperty(name)
  .objectReferenceValue = layerRect` BEFORE `SaveAsPrefabAsset`. Rule: every
  `[SerializeField]` the generator expects must be wired in the same pass.
- **Scene files cannot own canvases across scenes (Stage 8 lesson, applied)**:
  the Hud prefab is now a BARE GameObject tree (root has neither Canvas nor
  RectTransform children anchored center — rows anchor TOP-LEFT of the parent
  rect, y growing down in the 320x180 reference space). Each gameplay scene
  instantiates the tree; `UiSceneStartup.AttachHud` re-parents it under the
  live `UIRoot.GetLayer(UILayer.Hud)` with
  `SetParent(hudLayer, worldPositionStays: false)` + identity local TRS.
- **Never guess a Libplanet API — reflect first (Stage 7 rule, repeated)**:
  the spec's "syncing block 1234 / 1717" needs a target height; reflection
  against Libplanet.Net 5.5.3 shows `BlockSyncState` exposes ONLY
  `CurrentPhase` (no totals) and Swarm has no tip-estimate property. So
  LoadingPresenter.SetProgress accepts `(current, total?)`, renders
  "syncing block N" when total is null, and Stage 11's ChainConnectionMonitor
  will supply the peer-tip estimate. Honest UI beats invented numbers.
- **Regeneration while in PLAY mode corrupts the scene files**: running
  RunFullSetupSteps with the editor still playing made PrefabGenerator
  DELETE + recreate prefabs (new GUIDs) while SceneGenerator threw at
  `EditorSceneManager.NewScene` (illegal in play mode) — the on-disk scenes
  kept the dead GUIDs and validation failed with 11 "not assigned" errors
  (every prefab ref on every scope). FIXED postscript: `BatchSetup.RunGuarded`
  now wraps every generator/validator entry point (and the setup window's
  buttons route through it) — in play mode it logs a warning, exits play, and
  replays the action via `playModeStateChanged` + `delayCall` once edit mode
  is restored (EnteredPlayMode aborts/clears the queue, so a failed exit can
  never fire generation mid-play). Live-verified: RunValidation while playing
  → warn → auto-stop → replay → result line in edit mode.
- **Gate layering bug (UI smoke finding)**: UiInputDriver mirrored the window
  stack onto PlayerInputGate, so a modal opened as the FIRST window got
  `WindowOpen` pushed ABOVE `ModalOpen` — click-dismiss would have fired under
  a live modal. Fix: WindowService is the SOLE owner of both gate levels —
  pushes `WindowOpen` when a regular window claims the empty stack, reclaims
  it when the last modal closes above still-open regular windows
  (`PlayerInputGate.Contains` added for the check); the driver's mirror and
  `windowsOpenLastFrame` are gone.
- **ToastView MonoBehaviour must live in its own file**: ToastView was nested
  in ToastService.cs, so the generator's AddComponent serialized a script-less
  reference (`m_Script: {fileID: 0}` in Toast.prefab) and every ToastService
  call threw "Toast prefab has no ToastView component" — the MonoScript
  name rule extends to ANY MonoBehaviour referenced from generated prefabs,
  not just ScriptableObjects (SpriteRegistryAsset rule, repeated). ToastView
  now lives in ToastView.cs; broken prefab deleted + full setup regenerated.
- **Scripts compile against OLD assemblies while the editor plays**: fixes
  written to disk mid-play are invisible until stop + recompile — an in-play
  probe re-run keeps hitting the pre-fix bug. Loop: stop play → assets-refresh
  → verify 0 `error CS` in the Editor.log tail → re-enter play. Also:
  crashed probes leave state behind (ConfirmDialog still open, gate stuck in
  WindowOpen) — T0 of every probe now runs CloseAllAsync + a frame as cleanup.
- **MCP script-execute payload shape**: the tool takes
- **MCP script-execute payload shape**: the tool takes
  `{ "csharpCode": "<full class 'Script' with static 'Main'>", "className":
  "Script", "methodName": "Main", "isMethodBody": false }` — a missing
  csharpCode fails validation (empty calls). Complex payloads are best built
  from a file with node (`JSON.stringify({csharpCode, …})`) and sent via
  `npx unity-mcp-cli run-tool script-execute --input-file <json> <UnityProject>`.
  The CLI times out at ~60s — long generator passes complete in the editor
  AFTER the timeout; verify via the console log tail, not the call result.
- **Editor.log is the transcript of record**: MCP console-get-logs truncates;
  `$env:LOCALAPPDATA/Unity/Editor/Editor.log` (bash:
  `$USERPROFILE/AppData/Local/Unity/Editor/Editor.log`) carries every
  `[stage9-*]` marker with timestamps — grep it for verify/validate/smoke
  results and for the distinguishing `21:54` (stale) vs `22:10` (fresh)
  play-mode error triage.
- **VContainer TryResolve is non-generic in 1.19**:
  `resolver.TryResolve<T>()` does not compile (CS0308) — use
  `container.TryResolve(typeof(T), out object o) && o is T`. Same for
  `Enum.GetValues<T>()` under the project's C# level in Unity 2022.3
  (`(T[])Enum.GetValues(typeof(T))`), and `UniTask.Forget()` needs the
  `Cysharp.Threading.Tasks` using (Stage 7 files routinely drop it).
- **VContainer RegisterEntryPoint order is dispatch order**: UiBootstrapper
  registered BEFORE AppBootstrapper means localization + the loading overlay
  are live before tables load / chain bootstrap runs — required by the UX
  contract (overlay must cover the boot, toasts must work from frame one).
- **Validator mirrors spec, cheaply**: Stage 9 checks added — UIRoot present
  in Persistent with ScreenSpaceCamera + 320x180 ScaleWithScreenSize scaler;
  five child canvases with overrideSorting and sorting orders 0/10/20/30/40;
  UiInputDriver + LoadingOverlayBinder in the UIRoot hierarchy; the four UI
  prefabs exist; SpriteRegistry.asset baked (white/panel/icons vs
  data/item.csv); HudPresenter.LevelExpThresholds matches
  data/level_exp.csv (drift = exp bars lie); th.csv/en.csv cover every
  required key (UI chrome + item/pond/recipe name_keys, parsed from
  data/item.csv as the single source of truth).

## Environment snapshot

- Unity 2022.3.62f2, builtin pipeline + com.unity.2d.pixel-perfect 2.1.0,
  VContainer 1.19.0 (git), UniTask, uGUI Text (LegacyRuntime.ttf).
- New runtime files: Infrastructure/UI/{UILayer, UIRoot, IWindowService,
  WindowService, WindowPrefab, UiPrefabSet, IToastService, ToastService,
  LocalizationService, LoadingOverlay, RootLocalization}.cs;
  Presentation/Common/{PlayerInputGate, UiInputDriver, UiSceneStartup,
  ChainSyncPresenter, LoadingOverlayBinder, InventoryWindow, ConfirmDialog,
  SpriteRegistry, SpriteRegistryAsset, SpriteRegistrySource}.cs;
  Infrastructure/UiBootstrapper.cs.
- New editor file: Assets/Main/Editor/ProjectF/UiPrefabGenerator.cs
  (menu ProjectF/Setup/Generate UI Prefabs; also collects the required
  localization key set for the validator).
- Generated: Assets/Main/Prefabs/UI/{UIRoot, Toast, InventoryWindow,
  ConfirmDialog}.prefab, Assets/Main/Settings/SpriteRegistry.asset,
  Assets/Main/Resources/Localization/{th,en}.csv (63 keys each, Thai first).
- Modified runtime: RootLifetimeScope (UI registrations + UiPrefabSet +
  SceneRouter-with-overlay), SceneRouter (loading overlay per hop),
  HudView (bars/exp/ETA + loc), PlayerInputController (PlayerInputGate),
  ScenePlayerService.PushInputGate, the four scene scopes (gate + AttachHud).
- Modified editor: PrefabGenerator (bare-tree Hud with TOP-LEFT anchored
  rows + status dots), SceneGenerator (UIRoot instance in Persistent with
  ScreenSpaceCamera on the main camera; scope wiring for uiRoot/prefabs/
  registry), BatchSetup (UiPrefabGenerator step), ProjectFSetupWindow
  (Generate UI Prefabs button), ProjectValidator (Stage 9 checks),
  EditorPaths (ResourcesRoot/LocalizationRoot).

## Postscript

Written for the wiki ingest pipeline: new file (not an edit of stage-8-
lessons.md) guarantees a fresh extraction pass. Suggested entity seeds:
UIRoot, WindowService, ToastService, LocalizationService, LoadingOverlay,
InventoryWindow, ConfirmDialog, UiPrefabGenerator, UiPrefabSet,
SpriteRegistryAsset, UiBootstrapper; suggested concepts: VContainer
RegisterInstance implementation-type conflict, VContainer lazy Func
resolution, Unity script-name-file-name rule for .asset/MonoScript,
single-owner input-gate layering (modal vs window), play-mode generation
guard with deferred replay,
prefab self-containment (generator-wired SerializeFields), play-mode
regeneration GUID corruption, MCP script-execute payload shape and
Editor.log verification loop.
