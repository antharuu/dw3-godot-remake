# Roadmap

## Public roadmap

DW3 Remake is being prepared as a source-available C# and Godot project that works from a user-supplied supported disc image. The extractor writes a private SQLite and PNG data pack. The game reads the pack and never reads the disc.

### Available foundations

- .NET solution with data, disc, format, extractor, game, and xUnit projects.
- BIN/CUE validation and supported-edition identification.
- Atomic data-pack creation and validation.
- Disc format decoders for field, text, dialogue, characters, and battle domains.
- Field exploration with collision, warps, diagnostics, climbing, ledge drops, and dialogue presentation.
- Battle simulation and presentation under active refinement.

### Next priorities

1. Complete the remaining conversion domains and verify each against synthetic tests and local extraction output.
2. Make the Godot runtime consume the SQLite and PNG pack for every supported domain.
3. Remove remaining legacy conversion code after C# parity is established.
4. Finish first-launch extraction and cross-platform packaging checks.
5. Add public contribution, format, and release documentation.
6. Complete broader gameplay systems including saves, story progression, services, cinematics, and audio.

## Quality gates

Every change must keep the solution buildable and testable without game content:

```text
dotnet build Dw3.sln --nologo
dotnet test Dw3.sln --nologo
```

Format work uses fabricated fixtures in Git. Disc-derived output remains private. A decoder that cannot interpret an input must fail with an explicit error.

## Release criteria

A public release requires a completed extraction path, a runtime that reads only the private pack, no proprietary content in the repository or release manifest, a reviewed allowlist, passing provenance checks, and a separate history audit.

See [design/ARCHITECTURE.md](design/ARCHITECTURE.md) and [design/DISTRIBUTION.md](design/DISTRIBUTION.md) for the current contracts.