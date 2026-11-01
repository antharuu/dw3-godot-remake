# Distribution and provenance

## Scope

DW3 Remake distributes source code and tooling only. It does not include disc images, extracted files, decoded images, dialogue, audio, tables, captures, or other game-derived material.

## Disc and extraction

The extractor is a separate C# executable. It reads a user-supplied supported disc image, validates it, and writes a local data pack containing SQLite data and PNG images. The game process reads that pack only. It never opens the disc image.

The disc is required for extraction. Once extraction succeeds, the game can use the validated local pack without the disc.

## Repository rules

The repository and every distribution must contain only:

- original project code and documentation;
- fabricated test input and expected output;
- tooling required to build, test, extract, and run the project;
- permitted third-party dependencies and their required notices.

Do not commit disc files, extracted assets, converted output, game text, screenshots, recordings, or binary dumps. Keep all generated game data in the user-data directory outside the repository.

## Provenance rules

Implement behavior from independently understood format and gameplay contracts. Do not copy source code from external implementations or translate it line by line. Comments describe disc files, offsets, and structure where needed, not private research environments.

Every decoder must reject malformed or unsupported input. Every committed test must run without a disc and use fabricated bytes and identifiers.

## Data pack

The default per-user data location is resolved by `Dw3.Data.UserPaths`. `DW3_DATA_DIR` can override it for development. A valid pack contains `dw3.sqlite` and registered PNG files. The pack writer uses a staging directory and atomic publication. The reader rejects missing, incomplete, corrupt, or schema-incompatible data.

## Pre-release checks

Before publication:

1. Run `dotnet build Dw3.sln --nologo` with no warnings or errors.
2. Run `dotnet test Dw3.sln --nologo` successfully.
3. Verify the reviewed public allowlist contains every tracked public file.
4. Verify no proprietary or disc-derived content is present in the release manifest.
5. Verify that the game does not access a disc after a valid pack is available.
6. Audit the reachable history separately before publication.

The automated guard detects known risks. It is a control, not a legal guarantee.