# Development reference

This document describes the supported local build, test, diagnostic, and F3
workflows. Tests use synthetic inputs and run without a disc.

## Prerequisites

- .NET 10 SDK
- Godot 4.7.2 .NET for running the game
- A supported user-owned BIN/CUE disc only when extracting or exercising a
  disc-backed runtime probe
- Xvfb on Linux for headless game probes

## Build and test

Run these commands from the repository root:

```sh
dotnet build Dw3.sln --nologo
dotnet format Dw3.sln         # required before committing; verified by Linux CI
dotnet test Dw3.sln --nologo
```

The build gate is zero warnings and zero errors. The test suite includes format,
data-pack, extractor, game, and repository-provenance tests.

While the legacy Python parity pipeline remains in the tree, also run:

```sh
python3 production/tests/run_tests.py
```

That command is transitional. New code and tests belong in the .NET solution.

## Run the game

```sh
./run.sh
```

During the migration, `run.sh` remains the development entry point and can use
the existing development cache for domains not yet switched to the data pack.
The target production flow starts `Dw3.Extractor` separately, writes a private
SQLite plus PNG pack, and starts the game from that pack.

## Probes

Set exactly one `DW3_*_PROBE` variable to `1`. Probes exit with code `0` on
success and `3` on an assertion failure. A missing data pack exits with `2`.
A probe requiring unavailable PSX reference frames exits with `5`.

Run graphical probes headlessly so Godot cannot attach to a desktop session:

```sh
env -u WAYLAND_DISPLAY -u XDG_SESSION_TYPE \
  DW3_DIALOGUE_PROBE=1 \
  DW3_CAPTURE_DIR="$PWD/captures" \
  timeout 540 xvfb-run -a ./run.sh
```

Useful field probes include `DW3_WARP_PROBE`, `DW3_F3_PROBE`,
`DW3_WATER_PROBE`, `DW3_NPC_PROBE`, `DW3_CLIMB_PROBE`, `DW3_DROP_PROBE`, and
`DW3_DIALOGUE_PROBE`. Battle probes include `DW3_BATTLE_PLAY_PROBE`,
`DW3_BATTLE_MODEL_PROBE`, `DW3_BATTLE_HUD_PROBE`, and
`DW3_BATTLE_REPORT_PROBE`. See `HostStartupOptions` for the complete set of
recognized variables.

Configuration variables include `DW3_DATA_DIR` for a private pack location,
`DW3_CAPTURE_DIR` for probe images, `DW3_START_ZONE` and `DW3_START_POS` for
field diagnostics, and `DW3_RNG_SEED` for reproducible encounter and battle
sessions. Values documented as flags are active only when set to the exact
string `1`.

## F3 diagnostic overlay

Press **F3** during field exploration, or start with `DW3_START_F3=1`. F3 is
presentation-only: it reads the same immutable floor, occupancy, and exit maps
that simulation consumes. It shows admitted terrain, rejected floor cells,
unclassified cells, occupancy, supported exit cells, and the player collision
shape. It does not modify movement, collision, or warp decisions.

Use `DW3_F3_PROBE=1` for an automated on/off parity check and
`DW3_F3_PERF_PROBE=1` for its rendering measurement. Use F3 to locate a symptom,
then fix the decoder or simulation rule that owns the data rather than changing
the overlay.

## Adding and validating work

Read [Architecture overview](OVERVIEW.md) before adding a decoder or game rule.
Format structures are documented in [Format notes](formats/README.md). Keep all
new decoder tests synthetic, validate malformed inputs explicitly, and record
the exact build, test, and probe output with every change.
