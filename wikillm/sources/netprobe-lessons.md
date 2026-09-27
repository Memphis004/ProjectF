# Netprobe Lessons (Stage 2.5)

Automating the ProjectF Stage 2.5 vertical spike into scripts/run-netprobe.ps1
surfaced a series of real failure modes. Each fix is now encoded either in the
script or in UnityProject/Assets/Main/Scripts/Editor/Probe.cs.

## Facts for extraction

- **Restart determinism**: the ProjectF genesis block is derived deterministically
  from the validator key. A **SeedNode** restart MUST reuse the original key
  (persisted in privkey.txt) — a fresh ephemeral key produces a different genesis
  and Libplanet throws InvalidGenesisBlockException against the stored chain.
- **Detached seed process**: a tool/CI timeout kills the whole child process tree.
  A seed node launched as a direct child died mid-probe and looked exactly like
  "tx not mined". scripts/run-netprobe.ps1 now spawns it via Win32_Process.Create
  (WMI) with the environment baked into a generated .cmd file, so it survives the
  caller. The kill path locates the PID by listening port (netstat -ano parse).
- **Preload completes early**: a single Swarm.PreloadAsync pass returned at block
  #1497 while the seed tip was #3960. With the tail missing, neither gossip nor a
  state read can see fresh transactions. The probe now runs repeated preload
  passes until the tip stops advancing, plus one recovery pass if the ping
  counter stays flat while waiting for the mined transaction.
- **Persistent probe store**: a MemoryStore re-downloads the WHOLE chain on every
  run (~10 minutes at 4,000 blocks under Unity's Mono). The probe now keeps a
  disk store namespaced by genesis hash (pf-seed-probe-<hash8>) and mirrors
  SeedNode.SwarmRunner.BootChain: adopt an existing canonical chain id, create
  only for a fresh store. Warm runs sync only the delta, in seconds.
- **Stale probe isolation**: a probe thread left running from a timed-out CLI
  call kept mutating shared state. Two mitigations: every run writes its own
  result file (net-probe-result-<HHmmss>.json) and probe stores are separated
  per genesis, so an old run can never pollute a new chain.
- **Compile gate before probing**: when Probe.cs fails to compile, the Unity
  editor silently keeps running the LAST GOOD assembly. The script refreshes
  assets first and aborts when the refresh output contains
  "compilation errors exist", instead of probing stale code.
- **Counter baseline criterion**: the chain accumulates pings from all prior
  runs (counter was already 39), so "counter >= 3" proves nothing. The pass
  condition is counter strictly greater than the baseline read right before
  staging this run's transaction. Final measured run: 0 -> 3 (delta +3) in a
  63-second total wall clock using -FreshSeed.
- **JSON escaping of exception messages**: exception text containing Windows
  paths (backslash + U) broke ConvertFrom-Json ("Bad JSON escape sequence").
  The C# launcher escapes backslashes before quotes when serializing errors.
- **npx on Windows**: Start-Process "npx" resolves to npm's sh wrapper and fails
  with "%1 is not a valid Win32 application"; the script resolves npx.cmd
  explicitly.
- **Unity MCP session fragility**: right after a domain reload the MCP plugin
  answers 503; the script retries the CLI up to 3 times with 45s backoff.

## Environment snapshot

- Libplanet 5.5.3 (all sub-packages), Unity 2022.3.62f2, MagicOnion 7.0.0.
- Seed genesis 7c4dd642… after -FreshSeed (previous chain c45702a2… retired).
- Evidence: artifacts/run-netprobe.out, artifacts/seed-netprobe.log,
  artifacts/net-probe-result-*.json.


## Postscript

Added later to retrigger ingestion: the watch deduplicates by content hash,
so a content change (not just mtime) is required to re-notify the watcher.
