#!/bin/sh
# Development launcher for Dw3 Remake.
# Run `dotnet build Dw3.sln` first. On first launch the game detects a missing or
# outdated data pack and runs the extractor itself; no disc access happens here.
#
# Optional environment variables:
#   DW3_GODOT_BIN       — path to the Godot 4.7.2 mono binary (default: ~/Applications/Godot/...)
#   DW3_DATA_DIR        — extracted data-pack directory (default: ~/.local/share/dw3-remake)
#   DW3_EXTRACTOR       — path to the Dw3.Extractor binary (default: src/Dw3.Extractor/bin/Debug/...)
#   DW3_PSX_REFERENCE_DIR — root directory containing PSX oracle captures (battle/...); required
#                            only for battle PSX comparison probes (DW3_BATTLE_HUD_PROBE=1, etc.);
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
GODOT_BIN=${DW3_GODOT_BIN:-"$HOME/Applications/Godot/Godot_v4.7.2-stable_mono_linux.x86_64"}
DATA_DIR=${DW3_DATA_DIR:-"${XDG_DATA_HOME:-$HOME/.local/share}/dw3-remake"}

if [ ! -x "$GODOT_BIN" ]; then
  printf '%s\n' "GODOT_REQUIRED: expected Godot 4.7.2 mono at $GODOT_BIN (override DW3_GODOT_BIN)" >&2
  exit 2
fi

# Expose the extractor binary so the game can spawn it on first launch.
# DW3_EXTRACTOR can be overridden (e.g. for a production binary placed next to the game).
export DW3_EXTRACTOR="${DW3_EXTRACTOR:-$ROOT/src/Dw3.Extractor/bin/Debug/net10.0/Dw3.Extractor}"

exec env DW3_DATA_DIR="$DATA_DIR" "$GODOT_BIN" --path "$ROOT/src/Dw3.Game" --rendering-driver opengl3
