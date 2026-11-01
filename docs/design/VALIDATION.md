# Validation

## Build and test gate

All committed work must pass:

```text
dotnet build Dw3.sln --nologo
dotnet test Dw3.sln --nologo
```

The build must report zero warnings and zero errors. Tests use fabricated data and must run with no disc image or generated pack.

## Decoder validation

A format decoder is accepted only when it has synthetic coverage for valid input, malformed input, truncation, invalid offsets, and unsupported features where applicable. It must return an explicit error rather than create plausible output.

## Data-pack validation

The pack writer must preserve a previous valid pack when an extraction fails. The reader must reject a missing pack, incomplete database, schema mismatch, corrupt content, and missing registered image. Tests cover staging cleanup and atomic publication.

## Runtime validation

The game uses a validated pack through `Dw3.Data`. A missing or invalid pack enters the first-launch extraction flow. A runtime resource failure preserves the current valid state and reports the failed resource instead of loading a replacement.

Visual claims require deterministic headless captures when practical. Simulation claims require state assertions or traces. A screenshot alone does not prove movement, collision, timing, or transition behavior.

## Provenance validation

The public allowlist is the release manifest. The repository guard checks public tracked files for prohibited paths and known content-risk patterns. Each new public file must be reviewed and added to the allowlist deliberately.

Passing an automated guard does not prove that a release is legally safe. A separate history audit is required before publication.