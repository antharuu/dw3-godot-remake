# `rom/` user-supplied disc directory

`rom/` contains instructions only. It is a convenient local location for your own disc image. No disc files or extracted output are tracked by Git.

## Supported input

DW3 Remake supports Digimon World 2003 (Europe), SLES-03936, volume `DMW3`, supplied as one CUE file that references one BIN file.

Other regional editions and ISO images are not supported.

Known supported BIN digests:

```text
MD5   da76a5685b875c353343102b7587fded
SHA1  457cb233349ba841e03b33d8060f8fbcadd45cb3
```

## Extraction

The separate `Dw3.Extractor` program validates the disc and writes a private data pack containing `dw3.sqlite` and PNG images. The game reads that pack only. It never opens the disc image.

The default data location is resolved by `Dw3.Data.UserPaths`. Set `DW3_DATA_DIR` to use a different local data directory during development.

The extractor reads the disc without modifying it. Missing, incomplete, malformed, and unsupported input is rejected with an actionable error.

## Keep private

Do not commit BIN, CUE, ISO, extracted files, converted images, dialogue, audio, tables, captures, or dumps. All game-derived output belongs in the private data directory outside the repository.

See [../docs/design/DISTRIBUTION.md](../docs/design/DISTRIBUTION.md) for the full repository and release contract.