# Architecture

## Overview

DW3 Remake is a C# and Godot project that converts a user-supplied disc into a private data pack. The game reads only that pack. Disc access is isolated in the extractor process.

```
user disc image
  -> Dw3.Disc validates and reads sectors
  -> Dw3.Formats decodes supported structures
  -> Dw3.Extractor writes a data pack atomically
  -> Dw3.Data exposes the pack contract
  -> Dw3.Game reads the pack and presents gameplay
```

## Projects

- `src/Dw3.Data` owns the SQLite pack schema, atomic writer, reader, validation errors, and per-user data paths.
- `src/Dw3.Disc` parses supported CUE/BIN input, ISO9660 records, and edition identity.
- `src/Dw3.Formats` contains pure binary and format decoders. It has no Godot dependency.
- `src/Dw3.Extractor` is the console application that coordinates conversion and reports progress.
- `src/Dw3.Game` is the Godot application. It references `Dw3.Data` only.
- `tests/` contains xUnit projects using fabricated input only.

## Data pack

A pack is stored in the platform user-data directory, or in the directory selected by `DW3_DATA_DIR`. It contains `dw3.sqlite` plus PNG files under `images/`. The extractor writes a staging directory, validates it, then publishes it as `data/`. The reader rejects a missing, incomplete, corrupt, or incompatible pack with an explicit error.

The game never opens a disc image. When no valid pack is available, its first-launch UI starts the extractor as a separate process.

## Boundaries

Gameplay simulation, format decoding, data persistence, and Godot presentation have separate responsibilities. A format decoder returns validated data or an explicit error. A renderer does not invent missing data. Game rules do not read disc files, and the extractor does not require Godot scene state.

## Extension points

Add a new decoded domain in three narrow steps:

1. Add a format decoder and fabricated xUnit cases in `Dw3.Formats`.
2. Add an extraction step and pack writer in `Dw3.Extractor`.
3. Add a reader and runtime binding in `Dw3.Game` when the feature needs presentation or simulation.

Schema changes belong in `Dw3.Data` and must make an older pack fail validation cleanly. Keep disc-derived output out of the repository and keep all committed tests synthetic.

## Build and test

```text
dotnet build Dw3.sln --nologo
dotnet test Dw3.sln --nologo
```

See [DISTRIBUTION.md](DISTRIBUTION.md) for the content and provenance contract.