#!/usr/bin/env bash
# =============================================================================
# Chain E2E CI (Stage 11) — boots the real seed node + hub, replays the full
# in-editor chain E2E harness (.freebuff/e2e/test_chain.cs) through the Unity
# MCP bridge, and tears everything down.
#
#   bash tools/ci-chain-e2e.sh              # full run
#   SKIP_BOOTSTRAP=1 bash tools/ci-chain-e2e.sh   # reuse a running seed+hub
#                                                 # and an already-synced client
#
# Exit codes: 0 = ALL PASS, 1 = any failure.
#
# Prerequisites (mirrors the session that produced Stage 11):
#   - dotnet SDK 8 (src/ builds), Node (npx unity-mcp-cli)
#   - Unity 2022.3.62f2 at the default Hub path WITH the project open and the
#     MCP plugin listening on localhost:20817
#   - .freebuff/seed_key.hex present (seed identity; store persists beside it)
#   - NetworkSettings.asset pointed at the seed (SeedPeers/ApvToken/GenesisPath)
#
# Session-proven gotchas baked in:
#   - nohup for background procs (run_terminal_command BACKGROUND unimplemented)
#   - seed REBUILDS first: tables are embedded in ProjectF.Lib at build time —
#     --no-build after a data change runs stale tables and rejects valid txs
#   - Editor.log resets when the editor restarts → grep stale-tag discipline
#   - MCP dispatches race domain reloads → retry loop, not a single shot
# =============================================================================
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

LOGDIR="$ROOT/.freebuff"
EDITOR_LOG="$USERPROFILE/AppData/Local/Unity/Editor/Editor.log"
MCP="npx unity-mcp-cli run-tool"
PROJECT="UnityProject"
SEED_PORT=31236
HUB_PORT=5170
PAYLOAD_DIR="$LOGDIR/e2e"

say()  { printf '[ci] %s\n' "$*"; }
fail() { printf '[ci] FAIL: %s\n' "$*" >&2; exit 1; }

# ---------------------------------------------------------------- sanity ----
[ -f "$PAYLOAD_DIR/p_test_chain.json" ] || fail "missing $PAYLOAD_DIR/p_test_chain.json (run: node .freebuff/e2e/build_payloads.js test_chain)"
[ -f "$LOGDIR/seed_key.hex" ] || fail "missing $LOGDIR/seed_key.hex"
command -v dotnet >/dev/null || fail "dotnet not on PATH"
command -v node   >/dev/null || fail "node not on PATH"

# ------------------------------------------------- dispatch helper ----------
# script-execute is fire-and-forget (60s CLI cap vs long editor jobs); the
# harness logs [ce2e] lines into Editor.log which we poll instead.
dispatch() {
  local payload="$1" tries="${2:-3}" n=1
  while [ "$n" -le "$tries" ]; do
    if $MCP script-execute --input-file "$payload" $PROJECT 2>&1 | grep -q SUCCESS; then
      return 0
    fi
    say "dispatch attempt $n failed (domain reload race?) — retrying"
    n=$((n + 1)); sleep 10
  done
  return 1
}

# ------------------------------------------------------- build seed+hub -----
if [ "${SKIP_BOOTSTRAP:-0}" != "1" ]; then
  say "building SeedNode + HubServer (embeds current tables into the lib)"
  dotnet build src/ProjectF.SeedNode  -c Debug --nologo -v q >/dev/null || fail "seed build"
  dotnet build src/ProjectF.HubServer -c Debug --nologo -v q >/dev/null || fail "hub build"

  say "starting seed (31236) + hub (5170)"
  nohup dotnet run --project src/ProjectF.SeedNode --no-build -c Debug -- \
    --SeedNode:PrivateKeyHex="$(cat "$LOGDIR/seed_key.hex")" \
    --SeedNode:StorePath="$ROOT/.freebuff/chain" \
    --SeedNode:IsMiner=true > "$LOGDIR/seed.log" 2>&1 &
  sleep 1
  nohup dotnet run --project src/ProjectF.HubServer --no-build -c Debug \
    > "$LOGDIR/hub.log" 2>&1 &

  for _ in $(seq 1 20); do
    sleep 3
    if netstat -ano | grep -q ":$SEED_PORT .*LISTEN" \
       && netstat -ano | grep -q ":$HUB_PORT .*LISTEN"; then
      break
    fi
  done
  netstat -ano | grep -q ":$SEED_PORT .*LISTEN" || fail "seed did not start (see .freebuff/seed.log)"
  netstat -ano | grep -q ":$HUB_PORT .*LISTEN" || fail "hub did not start (see .freebuff/hub.log)"
  say "seed tip: $(grep -a '\[status\]' "$LOGDIR/seed.log" | tail -1)"
fi

# ------------------------------------------------- editor reachable? --------
# A dispatch against a dead editor fails 3x fast below; ping the MCP plugin
# with a harmless call first so the error message points at the right layer.
$MCP script-execute --input-file "$PAYLOAD_DIR/p_stop_play.json" $PROJECT >/dev/null 2>&1 \
  || fail "Unity MCP unreachable (open $PROJECT in the editor; MCP plugin listens on :20817)"

# ------------------------------------------- fresh catch-up (the HUD path) --
if [ "${SKIP_BOOTSTRAP:-0}" != "1" ]; then
  say "wiping client chain store for a full catch-up run"
  rm -rf "$USERPROFILE/AppData/LocalLow/DefaultCompany/UnityProject/chain-player1"
fi

say "entering play mode"
dispatch "$PAYLOAD_DIR/p_enter_play.json" || fail "could not enter play mode"

log_line() { grep -a "$1" "$EDITOR_LOG" 2>/dev/null | tail -1; }

say "waiting for chain bootstrap (fresh store may pull thousands of blocks)"
BOOT_DONE=0
for _ in $(seq 1 90); do
  sleep 20
  case "$(log_line 'bootstrap done')" in
    *"bootstrap done"*) BOOT_DONE=1; break ;;
  esac
done
[ "$BOOT_DONE" = "1" ] || fail "no 'bootstrap done' in Editor.log after 30 min"
say "$(log_line 'bootstrap done')"
say "$(log_line 'Stage 7 client up')"

# --------------------------------------------------------- run the harness --
say "dispatching chain E2E harness"
BEFORE_CE2E=$(grep -ac '\[ce2e\]' "$EDITOR_LOG" 2>/dev/null || echo 0)
dispatch "$PAYLOAD_DIR/p_test_chain.json" || fail "harness dispatch failed"

RESULT=""
for _ in $(seq 1 60); do
  sleep 10
  # Count only lines AFTER the dispatch marker — reruns append, never replace.
  TAIL_OUTPUT=$(grep -a '\[ce2e\]' "$EDITOR_LOG" 2>/dev/null | tail -n +"$((BEFORE_CE2E + 1))")
  if printf '%s' "$TAIL_OUTPUT" | grep -q 'ALL PASS'; then
    RESULT=PASS; break
  fi
  if printf '%s' "$TAIL_OUTPUT" | grep -q 'FAILED\|NOT PLAYING\|abort'; then
    RESULT=FAIL; break
  fi
done
[ "$RESULT" = "PASS" ] || { printf '%s\n' "$TAIL_OUTPUT" | tail -30 >&2; fail "harness did not report ALL PASS"; }

say "──────────────────────────────────────────────"
grep -a '\[ce2e\]' "$EDITOR_LOG" | tail -n +"$((BEFORE_CE2E + 1))" | tail -25
say "──────────────────────────────────────────────"
say "CHAIN E2E ALL PASS ✔"

# ------------------------------------------------------------- teardown -----
# TEARDOWN=0 keeps seed/hub + play mode alive for debugging; default cleans up.
if [ "${TEARDOWN:-1}" = "1" ]; then
  say "stopping play mode"
  dispatch "$PAYLOAD_DIR/p_stop_play.json" >/dev/null 2>&1 || true
  say "stopping seed + hub"
  for pid in $(netstat -ano | grep -E ":($SEED_PORT|$HUB_PORT)" | grep LISTEN | awk '{print $NF}' | sort -u); do
    taskkill //PID "$pid" //F >/dev/null 2>&1 || true
  done
fi

exit 0
