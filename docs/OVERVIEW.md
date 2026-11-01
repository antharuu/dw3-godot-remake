# Architecture overview

DW3 Remake turns a user-supplied disc into a private data pack, then runs the
Godot game entirely from that pack. Source code and tests contain format
structure and synthetic fixtures only. They contain no game assets, extracted
text, or disc dumps.

## Code map

| Area | Responsibility | May reference |
|---|---|---|
| `src/Dw3.Disc` | BIN/CUE, ISO9660, edition identification, and actionable source errors | no game or Godot project |
| `src/Dw3.Formats` | Pure binary decoders and format validation | `Dw3.Disc` data supplied by the extractor |
| `src/Dw3.Data` | SQLite schema, pack validation, staging writer, read-only reader, and user paths | no Godot or disc reader |
| `src/Dw3.Extractor` | Console pipeline that reads a disc once and writes a pack atomically | Disc, formats, data |
| `src/Dw3.Game` | Godot simulation, presentation, input, and diagnostics | data only |
| `tests/*` | xUnit tests with fabricated byte sequences and repository guards | source projects only |

`Dw3.Game` must never reference `Dw3.Disc` or `Dw3.Formats`. That boundary
keeps disc access out of the running game and makes format work testable without
Godot.

## Data-pack lifecycle

```text
user disc image
  -> Dw3.Extractor validates edition and reads disc members
  -> Dw3.Formats decodes structures
  -> Dw3.Data writes data.staging-<guid>/dw3.sqlite and images/*.png
  -> validation completes the pack and atomically promotes it to data/
  -> Dw3.Game opens data/dw3.sqlite read-only and loads PNG files
```

A failed extraction never promotes its staging directory. At startup the pack
reader rejects a missing, incomplete, corrupt, or schema-incompatible pack.
The first-launch flow then asks for a disc and starts the extractor as a
separate process. The game itself does not open the selected disc.

During the migration, the existing development cache remains the live parity
reference for domains that have not yet switched to the data pack. New runtime
code must preserve the same boundary when its domain is migrated.

## Adding a decoder

1. Put a small, deterministic decoder in the appropriate `Dw3.Formats` folder.
   Its public input is raw bytes or a bounded disc member. Validate every offset,
   count, terminator, and enum before consuming it, then throw `FormatException`
   for malformed input.
2. Write xUnit tests first in `tests/Dw3.Formats.Tests` using invented bytes.
   Cover the valid structure and each meaningful rejected boundary. Do not add
   bytes, names, or tables copied from a disc.
3. Add an extractor step that selects the required disc member through
   `Dw3.Disc`, invokes the decoder, and persists semantic records through
   `Dw3.Data`. Images belong under the pack image root, data belongs in SQLite.
4. Add pack-reader and game-loader support only when the domain is ready to
   switch. Keep the loader in `Dw3.Game` dependent on `Dw3.Data` records, not
   on format types or disc paths.
5. Run the build, full xUnit suite, and the domain's parity or diagnostic probe.
   Record the commands and real output in the change.

## Adding a game rule

Put deterministic game decisions in `src/Dw3.Game/Simulation` and rendering in
`src/Dw3.Game/Presentation`. A rule receives already decoded data and exposes
observable state. A renderer observes that state and does not decide movement,
collisions, warps, or battle outcomes. Add a synthetic simulation test first,
then use an existing probe or add a narrowly scoped one when runtime evidence is
needed.

## Documentation map

- [Development reference](DEVELOPMENT.md) covers build, tests, probes, and F3.
- [Format notes](formats/README.md) describe the on-disc structures implemented
  by `Dw3.Formats` without including game content.
- [Controls](CONTROLS.md) lists player and diagnostic controls.
- [Distribution contract](design/DISTRIBUTION.md) defines what may be committed
  and where extracted data may live.
