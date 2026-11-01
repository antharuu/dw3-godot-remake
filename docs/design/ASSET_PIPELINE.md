# Asset pipeline

## Purpose

The asset pipeline turns a user-owned disc image into a private data pack. It never writes to the disc and never places disc-derived files in the repository.

## Input and output

The extractor accepts the supported BIN/CUE disc layout. It validates the source before decoding. Its output is a data pack in the user-data directory:

```text
data/
  dw3.sqlite
  images/<domain>/<identifier>.png
```

`DW3_DATA_DIR` overrides the default data location for development and testing.

## Pipeline stages

1. `Dw3.Disc` validates the CUE, BIN, file system, and supported edition.
2. `Dw3.Formats` decodes binary structures with bounds checks and explicit unsupported-format errors.
3. `Dw3.Extractor` writes structured records to SQLite and decoded images to PNG.
4. `Dw3.Data` validates the completed pack before it is published.
5. `Dw3.Game` reads the published pack without opening the disc.

The extraction process writes to `data.staging-<id>` first. A failed extraction never marks a partial pack as ready. A successful extraction publishes the validated staging directory atomically and removes abandoned staging directories on a later run.

## Decoder contract

Every decoder must:

- accept only documented binary structure and bounds;
- reject malformed, truncated, or unsupported input explicitly;
- return data that can be validated before storage;
- use fabricated input in committed tests;
- keep game-derived output outside Git.

A missing image, unknown record, or invalid reference is an error. It is not permission to use a plausible substitute.

## Runtime contract

The game reads a complete, schema-compatible SQLite database and its registered images. It uses `Dw3.Data` for pack validation and per-user paths. If the pack is absent or invalid, the first-launch flow requests extraction rather than attempting disc access in the game process.

See [ARCHITECTURE.md](ARCHITECTURE.md) for ownership boundaries and [DISTRIBUTION.md](DISTRIBUTION.md) for repository rules.