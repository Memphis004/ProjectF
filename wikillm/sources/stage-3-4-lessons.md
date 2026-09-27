# Stage 3/4 Lessons (States, Actions, Determinism Audit)

Implementing ProjectF Stage 3 (five Bencodex state classes + round-trip tests)
and Stage 4 (ten on-chain actions + determinism audit + test scaffolding)
surfaced real failure modes beyond the Stage 2.5 netprobe set. Each lesson is
now encoded in the code or scripts it concerns, so it is reproducible rather
than merely described.

## Facts for extraction

- **Libplanet.Mocks 5.5.3 has no MockActionContext**: the package ships only
  MockWorldState / MockBlockChainStates / MockUtil — the old mock context was
  removed with the legacy IContext style. ProjectF hand-rolls TestActionContext
  against the real 13-member IActionContext interface instead of guessing a
  name. Discovery method that worked: a throwaway console project referencing
  Libplanet.Mocks 5.5.3 that dumps GetExportedTypes() — PowerShell in-process
  reflection over Libplanet's dependency graph hangs, so script-based
  reflection was abandoned.
- **Legacy/modern world split bites in tests**: MockBlockChainStates
  .GetWorldState(null) returns a LEGACY IWorldState; new World(legacyState)
  then rejects non-legacy Account writes with "Cannot set a non-legacy account
  to a legacy IWorld". TestWorld must build on MockUtil.MockModernWorldState.
- **Mock account GetState returns C# null, not Bencodex Null**: a real trie
  returns the Bencodex Null value for a missing key; the mock returns a plain
  null reference. Existence checks written as `x is not Null` silently pass on
  mocks — the pattern must be `x is not null and not Null` (encoded in
  CreateAvatarAction's one-time check).
- **Bencodex Boolean ambiguity under ImplicitUsings**: after adding
  `using Bencodex.Types;` the name Boolean is ambiguous between
  Bencodex.Types.Boolean and the bool alias (CS0104). ProjectF states qualify
  it explicitly as Bencodex.Types.Boolean in pattern matches and casts.
- **Tables namespace/class collision**: the generated Luban manager class is
  ProjectF.Tables.Tables, and the namespace wins unqualified resolution, so
  every consumer needs `using TablesClass = ProjectF.Tables.Tables;`
  (CS0118 otherwise). This now appears in GameTables.cs, FishingAction.cs and
  the test suite.
- **Game tables must be embedded, not read**: on-chain action execution may
  not touch the filesystem (knowledge.md rule 2). The Luban *.bytes files are
  EmbeddedResource in ProjectF.Lib (LinkBase=Resources, resource name
  ProjectF.Lib.Resources.<table>.bytes) and loaded by GameTables via
  GetManifestResourceStream — genesis execution and Unity evaluation need no
  StreamingAssets round-trip.
- **Audit greps match their own comments**: a doc comment stating "no
  System.Random" is itself a grep hit for System.Random. The determinism
  audit (tools/audit-determinism.ps1 + DeterminismAuditTests.cs mirror)
  initially failed on ActionBase.cs's comment; the comment was reworded to
  "no process-seeded RNG" so the spec's expected-empty grep is truly empty
  with zero exemptions.
- **Naive DateTime grep has false positives**: `grep DateTime` matches
  DateTimeOffset type declarations. The genesis builder legitimately declares
  DateTimeOffset? parameters (explicit timestamps for deterministic genesis).
  The audited patterns match CALLS (DateTime.Now/UtcNow, DateTimeOffset.Now/
  UtcNow), not type names — and the distinction is documented in the script.
- **Audits must be executed, not assumed**: the trust-but-verify pass
  injected a file containing `DateTime.UtcNow` and confirmed both the script
  (exit 1, file:line table) and the xUnit mirror fail before removing it —
  proving the audit catches violations rather than silently passing.
- **Read the CSVs before asserting numbers**: three Stage-4 tests were first
  written against guessed balance values (grow 60 vs actual 180, recipe 2x
  fish vs actual 1x fish + 1x salt, task reward 20 gold vs actual 40). The
  live data/*.csv files are the single source of truth; test constants now
  cite them (ActionsTests.cs header comment).
- **Sort before you encode**: Bencodex bytes must not depend on mutation or
  insertion order (knowledge.md rule 2). All state maps are SortedDictionary
  (Inventory by itemId, PondOwnershipState by Address bytes, TaskBoardState
  by taskId) and dedicated order-independence tests pin this.
- **TaskBoardState reroll constants**: 3 tasks/day (TasksPerDay) and a 600-
  block reroll period (RerollPeriodBlocks ≈ 20 minutes at the 2s target block
  interval) live on TaskBoardState as balance knobs; reroll_taskboard_v1
  draws with a partial Fisher-Yates over the level-eligible task pool using
  context.GetRandom() only.

## Environment snapshot

- Libplanet 5.5.3 (all sub-packages), netstandard2.1 lib, xUnit on net8.0.
- Stage 3: 5 state classes, StatesRoundTripTests (16 cases). Stage 4: 11
  actions total (incl. reroll_taskboard_v1), 88/88 tests green, determinism
  audit PASS over 24 files.
- Evidence: tools/audit-determinism.ps1, src/ProjectF.Lib.Tests/
  DeterminismAuditTests.cs, src/ProjectF.Lib.Tests/ActionsTests.cs,
  src/ProjectF.Lib.Tests/TestHelpers.cs.

## Postscript

Added for the wiki ingest pipeline: the watcher deduplicates by content hash,
so this new file (not an edit to netprobe-lessons.md) is what guarantees a
fresh extraction pass.
