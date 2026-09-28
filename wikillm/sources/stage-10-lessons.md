# Stage 10 — Gameplay systems end to end (lessons)

Stage 10 wires the three remaining gameplay systems — Shop (buy/sell),
Taskboard (daily tasks + reroll countdown), Kitchen (craft + eat) — plus the
[E] interaction framework and the ErrorMapper. Same contract as previous
stage docs: what the spec asked, what shipped, and the traps worth keeping.

## Shipped (spec → code)

- **Interaction framework (10.1)**: `Infrastructure/Interaction/` —
  `IInteractable` (Kind/Anchor/Prompt/CanInteract/InteractAsync),
  `InteractionDetector` on the Player prefab (Physics2D.OverlapCircleAll,
  NEAREST interactable wins, radius 1.2), `InteractionPromptView` (one shared
  "[E] {verb}" label on the UIRoot World layer) + `InteractionPromptDriver`
  (E key → opens the matching window through IWindowService, so gate/blocker
  semantics match the I-key windows). Concrete markers: `NpcInteractable`
  (shopkeeper), `TaskBoardInteractable`, `KitchenInteractable` — shipped
  inside their OWNER PREFABS (TaskBoard/NpcShopkeeper/Kitchen) and instanced
  by the scene generator (one class per file — see the E2E postscript).
- **Shop (10.2)**: `ShopWindow`/`ShopPresenter` + pure `ShopLogic`.
  Buy list = TbShop ⋈ TbItem (price, daily stock "∞/N", lock icon with the
  required level when fishing AND cooking are below it), quantity stepper
  clamped 1..99, running total, Buy disabled when gold is short. Buy →
  pending toast → `BuyItemAction` → success toast / mapped error →
  StateWatcher re-entry refreshes rows. Sell tab sells Fish/Crop/Food at the
  60% rate via the NEW `SellItemAction` (sell_item_v1).
- **Taskboard (10.3)**: `TaskBoardWindow`/`TaskBoardPresenter` +
  `TaskBoardLogic`. Today's 3 tasks from the CONFIRMED TaskBoardState (the
  StateWatcher now reads Addresses.TaskBoard — see the snapshot change
  below), progress "2/3" computed against the live inventory, Submit enabled
  only when fully met, rewards previewed per row; Submit → `SubmitTaskAction`
  → reward popup (ConfirmDialog listing gold/exp/items). Countdown to the
  next reroll in blocks AND estimated minutes
  (600 − (tip − LastRerolledAt), ×2s, ceil to minutes).
- **Kitchen (10.4)**: `CraftWindow`/`KitchenPresenter` + `CraftLogic`.
  Recipe list from TbRecipe, per-recipe material checklist from
  TbRecipeMaterial with owned/required counts, portions stepper 1..10
  (mirroring the chain's guard), total stamina cost, great-dish chance
  readout = min(40, 5 + cookingLevel×2) — the exact chain formula. Locked
  state when KitchenUnlocked is false: overlay + "Talk to Auntie first".
  Craft → `CraftFoodAction`; the result popup distinguishes normal vs great
  dishes by diffing the confirmed inventory before/after (the client never
  rolls outcomes — knowledge.md rule 1). Eat affordance lives in the
  inventory tooltip (Food items only): `EatFoodAction`, refused client-side
  when stamina is already full.
- **ErrorMapper (10.5)**: `Infrastructure/ErrorMapper.cs` maps the exception
  TYPE NAMES recorded on-chain (NotEnoughStaminaException →
  "หมดแรงแล้ว พักกินข้าวก่อน", NotEnoughGold, ItemNotFound, PondFull,
  PermissionDenied, FailedLoadState, timeout, cancelled) to localized
  messages; unknown reasons fall back to ERR_UNKNOWN and the RAW text goes
  to the debug log only.
- **StateWatcher snapshot extension**: `AvatarSnapshot` now carries
  `Tasks` (taskId → completed) + `TasksLastRerolledAt`; `ReadSnapshot` reads
  `Addresses.TaskBoard` and `PollOnce` diffs it (TasksEquals) so the
  taskboard window refreshes on confirmations.

## Traps worth keeping

- **sell_item_v1 lives in ProjectF.Lib → REBUILD THE PLUGIN DLL**: Unity
  consumes `ProjectF.Lib` as a prebuilt DLL (Assets/Plugins/ProjectF). After
  adding an action: `dotnet test src/ProjectF.Lib.Tests` (98/98) then
  `pwsh tools/sync-dlls.ps1` — Unity never sees source-side changes
  otherwise. Verify with `grep sell_item_v1 ProjectF.Lib.dll` (binary match).
- **Roslyn-compiled MCP probes need fully-qualified names AND static-safe
  bodies**: `Presentation.Shop.ShopWindow` does not resolve from the global
  namespace (CS0246 'Presentation'), generic arguments must be qualified too
  (`UniTask<ProjectF.Infrastructure.UI.NoWindowResult>`, NOT
  `UniTask<Presentation.Shop.NoWindowResult>`), and `this` is invalid in the
  static Main body (CS0026) — pass `CancellationToken.None` instead of
  `this.GetCancellationTokenOnDestroy()`.
- **Pooled windows need an initial Rebuild at Attach**: WindowService only
  invokes `OnOpenAsync` — without a first `Rebuild()` in
  `Attach`, a window opened before the first snapshot keeps stale/blank
  content, and the CraftWindow locked overlay stayed False in the first
  smoke even though KitchenUnlocked was false. (Found by the probe:
  `lockedActive=False` → fix → `True`.)
- **Presenter attach hooks are explicit**: `WindowService.AcquireAsync` has
  an if/else-if chain wiring each window TYPE to its presenter on first
  acquisition (pooled windows are NOT constructor-injected). Forgetting the
  hook = a window that never repaints. Stage 10 added Shop/TaskBoard/Craft
  hooks next to the InventoryWindow one.
- **UiBootstrapper fallback AddComponent is a footgun**: adding a bare
  `InteractionPromptDriver` to the Root GO (no serialized view) NRE'd every
  frame in `Update`. The fix is two-sided: the driver null-guards
  (`view is null → return`, `Configure` resolves
  `GetComponentInChildren<InteractionPromptView>(true)`), and the
  bootstrapper only configures a driver that already exists on the prefab —
  it never creates one.
- **Class-name==file-name (MonoScript rule, repeated)**: `GeneratedTables.Task`
  collides with `System.Threading.Tasks.Task` semantics — the presenter uses
  `using TaskRow = ProjectF.Tables.Task;`. And a row TEMPLATE per window kind
  (Shop/Task/Craft children differ) beat one fat shared template.
- **TaskBoardState.Tasks is a SortedDictionary<int,bool>** — iterate
  `board.Tasks` (ascending taskId, deterministic), and the CLIENT mirrors
  reroll timing constants (`RerollPeriodBlocks = 600`, 2s/block) as display
  logic only; the chain stays the sole authority.

## E2E interactive-test postscript (T1/T2/T3) — three real bugs, one rule

The first true play-mode E2E (teleport next to the board/NPC/kitchen →
assert `[E]` prompt → open the window through the DRIVER → assert rows →
close) surfaced three stacked defects that every compile-time check and the
earlier probe smoke missed:

- **THE MonoScript rule is stricter than thought: a MonoBehaviour that must
  survive serialization needs its class name == its file name — one class
  per file, no exceptions.** `Interactables.cs` held three marker classes
  under the BASE class's file name, and the binder returned a nameless,
  path-less script (`MonoScript.FromMonoBehaviour` nameLen=0,
  `FindAssets t:MonoScript <Class>` = 0, `TryGetGUIDAndLocalFileIdentifier`
  → no guid). Everything the file touched serialized as
  `m_Script: {fileID: 0}` — the components silently vanished on reload.
  Namespace is NOT the cause (FishingSpot resolves fine inside a namespace;
  the file content moved to global ns and stayed broken). The PrefabGenerator
  "ForceBindScript via MonoScript.FromMonoBehaviour" workaround was a no-op —
  you cannot write a reference to a script that has no asset. Splitting into
  `InteractableBase/NpcInteractable/TaskBoardInteractable/KitchenInteractable.cs`
  (names kept) fixed it instantly (probe: nameLen=21, real GUID). The
  validation gate never catches this — add a generator-time assertion:
  every AddComponent'd MonoBehaviour must yield a non-empty
  `MonoScript.GetScriptPathFromMonoBehaviour`.
- **A generator must INSTANTIATE the prefab, not just load it.** During the
  refactor, `LoadPrefab("TaskBoard")` stayed in the PrefabSet while the
  `Object.Instantiate(prefabs.TaskBoard, …)` line was dropped — Village had
  no board at all, and the detector failure looked like the serialization
  bug it had just replaced. Assert placement, not intent: after generating,
  FindObjectOfType the marker in the saved scene.
- **A driver that toggles its own GameObject must live on an always-active
  parent.** `promptGo.SetActive(false)` hid the shared prompt between uses —
  but `InteractionPromptDriver` sat ON that GO, so its `Update` never ran
  again after the first hide (and `FindObjectOfType` without `true` couldn't
  even see it). Fixed by adding the driver to the UIRoot root GO (always
  active) while only the prompt child toggles.
- Test-harness notes: `UniTaskVoid` methods invoked via reflection must not
  be cast to `UniTask` (InvalidCastException) — invoke and POLL state with a
  plain frame-count loop; combinator timeouts misbehaved under the MCP
  Roslyn host. E2E evidence lives in `.freebuff/e2e/` (payloads +
  `t3_kitchen.png`); T1 (TaskBoard, ดูบอร์ดภารกิจ), T2 (Shop NPC,
  คุยกับร้านค้า, 9 rows), T3 (Kitchen, ใช้ครัว) all PASS in play mode.

## Environment snapshot (delta from stage 9)

- New lib files: `src/ProjectF.Lib/Actions/SellItemAction.cs`,
  `src/ProjectF.Lib.Tests/SellItemActionTests.cs` (7 tests).
- New Unity runtime: `Infrastructure/Interaction/` (5 files),
  `Infrastructure/ErrorMapper.cs`, `Presentation/Shop/ShopWindow.cs`
  (view+presenter+logic), `Presentation/Village/TaskBoardWindow.cs`,
  `Presentation/AuntieHouse/CraftWindow.cs`,
  `Scripts/Tests/` (EditMode asmdef + Stage10LogicTests, 14 tests).
- Modified: `UiPrefabGenerator` (3 window prefabs + prompt view+driver on
  the UIRoot root GO + Eat button + ~35 new loc keys), `SceneGenerator`
  (3 prefab fields, interactables via OWNER PREFABS, taskboard instanced at
  (4,2)), `PrefabGenerator` (Player carries InteractionDetector;
  interactables baked into TaskBoard/NpcShopkeeper/Kitchen prefabs),
  `RootLifetimeScope` (UiPrefabSet×6, WindowPrefab×3,
  ShopPresenter/TaskBoardPresenter/KitchenPresenter at app scope),
  `WindowService` (attach hooks), `StateWatcher`/`AvatarSnapshot`
  (taskboard), `UiBootstrapper` (prompt driver), `ShopLifetimeScope`
  (ShopPresenter renamed to ShopScenePresenter — the economy presenter is
  app-scope), localization CSVs (63 → 104 keys).
- Interaction split: `Interactables.cs` → `InteractableBase.cs`,
  `NpcInteractable.cs`, `TaskBoardInteractable.cs`, `KitchenInteractable.cs`
  (MonoScript rule — see the E2E postscript).
- Verification: lib tests 98/98, EditMode 14/14, ProjectValidator PASS
  (0/0), play smoke T1/T2/T3 green (Shop opens, TaskBoard opens, Craft opens
  with locked overlay), 0 NREs.

## Postscript

Suggested entity seeds: SellItemAction, InteractionDetector,
InteractionPromptDriver, ShopPresenter, TaskBoardPresenter,
KitchenPresenter, ErrorMapper, ShopLogic, TaskBoardLogic, CraftLogic;
concepts: plugin-DLL rebuild after lib changes, pooled-window initial
rebuild at Attach, presenter attach hooks in WindowService, snapshot
extension for new account spaces, nearest-interactable-wins detection.
