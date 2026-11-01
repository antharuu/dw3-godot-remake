# Compatibility reference

## Supported disc

The supported input is Digimon World 2003 (Europe), release identifier SLES-03936, volume label `DMW3`.

The disc reader accepts one CUE file that references one BIN file. It identifies the supported release before extraction. ISO images and other regional editions are not supported.

Known supported BIN digests:

```text
MD5   da76a5685b875c353343102b7587fded
SHA1  457cb233349ba841e03b33d8060f8fbcadd45cb3
```

## Required software

- .NET 10 SDK
- Godot 4.7.2 .NET for running the game project

## Data location

The extractor writes a private data pack outside the repository. `Dw3.Data.UserPaths` resolves the platform user-data location. `DW3_DATA_DIR` overrides it for development.

The pack contains `dw3.sqlite` and PNG images. The game validates the pack before use and never opens the disc image.

## Repository contract

`rom/` contains instructions only. Disc images and all game-derived output are ignored by Git. Committed tests use fabricated data and run without a disc.

See [design/DISTRIBUTION.md](design/DISTRIBUTION.md) for the full content and release contract.