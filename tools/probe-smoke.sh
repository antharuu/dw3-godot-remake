#!/bin/sh
# tools/probe-smoke.sh — smoke-test all non-PSX probes after a code change.
#
# Launches each field probe headlessly and fails immediately on:
#   - a non-zero exit code (probe assertion failure or engine abort)
#   - any "Leaked unsafe reference" line (Godot C# object lifetime bug)
#
# Usage:
#   tools/probe-smoke.sh
#
# Prerequisites:
#   - dotnet build Dw3.sln completed successfully
#   - Extracted data pack present (~/.local/share/dw3-remake or DW3_DATA_DIR)
#   - xvfb-run available (xvfb package)
#
# Exit codes:
#   0  all probes passed
#   1  one or more probes failed (probe name and reason printed to stderr)
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT"

# Non-PSX probes: every field probe that runs without a private oracle capture
# directory (DW3_PSX_REFERENCE_DIR).  Battle probes that require PSX captures
# exit with code 5 (unavailable) rather than 0 or 3, so they are excluded here.
PROBES="
DW3_DIALOGUE_PROBE
DW3_ICON_PROBE
DW3_WARP_PROBE
DW3_LOCOMOTION_PROBE
DW3_CLIMB_PROBE
DW3_DROP_PROBE
DW3_SORTY_PROBE
DW3_DEPTH_PROBE
DW3_SQLITE_PROBE
DW3_INPUT_PROBE
"

FAIL=0

for PROBE in $PROBES; do
    TMPDIR_PROBE=$(mktemp -d)
    LOGFILE=$(mktemp)
    PROBE_EXIT=0
    env -u WAYLAND_DISPLAY -u XDG_SESSION_TYPE \
        "${PROBE}=1" \
        DW3_CAPTURE_DIR="$TMPDIR_PROBE" \
        timeout 400 xvfb-run -a "$ROOT/run.sh" >"$LOGFILE" 2>&1 || PROBE_EXIT=$?
    rm -rf "$TMPDIR_PROBE"

    LEAKS=$(grep -c "Leaked unsafe reference" "$LOGFILE" 2>/dev/null || printf '0')

    if [ "$PROBE_EXIT" -ne 0 ]; then
        printf 'FAIL %s exit=%s\n' "$PROBE" "$PROBE_EXIT" >&2
        FAIL=1
    elif [ "$LEAKS" -gt 0 ]; then
        printf 'FAIL %s leaks=%s\n' "$PROBE" "$LEAKS" >&2
        FAIL=1
    else
        printf 'PASS %s\n' "$PROBE"
    fi
    rm -f "$LOGFILE"
done

exit "$FAIL"
