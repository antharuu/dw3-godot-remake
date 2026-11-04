#!/bin/sh
# Bisect helper: returns 0 (good), 1 (bad), 125 (skip/build-failure).
# Usage: git bisect run tools/bisect-probe.sh
#
# Checks that DW3_DIALOGUE_PROBE exits cleanly (code 0, no "Leaked unsafe
# reference" lines).  Commits where the game project does not build are
# skipped so git-bisect keeps searching.
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT"

# ---- Build the game project (skip this commit if it cannot compile) --------
if ! dotnet build src/Dw3.Game/Dw3.Game.csproj --configuration Debug --no-restore \
        --nologo -clp:ErrorsOnly 2>/dev/null; then
    if ! dotnet restore 2>/dev/null; then exit 125; fi
    if ! dotnet build src/Dw3.Game/Dw3.Game.csproj --configuration Debug \
            --nologo -clp:ErrorsOnly 2>/dev/null; then
        exit 125   # build broken at this commit — skip
    fi
fi

# ---- Run the dialogue probe headlessly ------------------------------------
TMPDIR_PROBE=$(mktemp -d)
LOGFILE=$(mktemp)
PROBE_EXIT=0
env -u WAYLAND_DISPLAY -u XDG_SESSION_TYPE \
    DW3_DIALOGUE_PROBE=1 \
    DW3_CAPTURE_DIR="$TMPDIR_PROBE" \
    timeout 400 xvfb-run -a "$ROOT/run.sh" >"$LOGFILE" 2>&1 || PROBE_EXIT=$?
rm -rf "$TMPDIR_PROBE"

# ---- Verdict ---------------------------------------------------------------
if grep -q "Leaked unsafe reference" "$LOGFILE"; then
    rm -f "$LOGFILE"
    exit 1   # bad: has leaked references
fi
rm -f "$LOGFILE"

if [ "$PROBE_EXIT" -ne 0 ]; then
    exit 1   # bad: non-zero exit code
fi

exit 0   # good: clean exit, no leaks
