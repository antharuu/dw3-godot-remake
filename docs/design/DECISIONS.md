# Design decisions

## Product direction

DW3 Remake is an unofficial, from-scratch recreation of Digimon World 2003 for the supported European disc edition. Original game rules are the reference. Improvements are deliberate, isolated, and documented rather than treated as accidental compatibility changes.

## Technical decisions

### Separate extraction from gameplay

The extractor is a standalone C# process. It reads and validates a user-supplied disc image, then writes a SQLite and PNG data pack. The Godot game process reads the pack only and never opens the disc.

### Keep generated game data private

Game-derived assets, text, tables, captures, and converted data are local output. They are not committed, distributed, or used as test fixtures. Committed tests use fabricated input.

### Preserve clear boundaries

`Dw3.Disc` owns disc input, `Dw3.Formats` owns binary decoding, `Dw3.Data` owns pack persistence, and `Dw3.Game` owns runtime behavior and presentation. The game references `Dw3.Data`, not disc or format projects.

### Explicit failure over invented fallback

Malformed input, unsupported structures, invalid records, and missing pack resources fail with actionable errors. The runtime does not create replacement geometry, artwork, text, or rules for unavailable data.

## Player-facing goals

The project targets a widescreen presentation, responsive controls, gamepad support, useful diagnostics, and a stable simulation cadence. These are intentional modernization goals. They must not silently change gameplay behavior or obscure missing source data.

The current playable work includes field exploration, dialogue presentation, and an in-progress battle system. Save handling, broader story progression, service menus, cinematics, and audio remain incomplete.

## Compatibility scope

The supported input is Digimon World 2003 (Europe), SLES-03936, supplied by the user as a BIN/CUE pair. Other editions and ISO images are rejected by the disc reader.

## Publication rule

No release is made until the repository is free of game content, tests are synthetic, the extraction path is complete, and the provenance guard passes. See [DISTRIBUTION.md](DISTRIBUTION.md).