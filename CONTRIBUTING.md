# Contributing to DW3 Remake

## Ground rules (read first)

1. **Never commit game content.** No disc files, extracted or converted assets, screenshots of game assets,
   text, tables, audio. Everything game-derived is produced at runtime from the user's disc.
   Full contract: [docs/design/DISTRIBUTION.md](docs/design/DISTRIBUTION.md).
2. **The project needs nothing but the user's disc.** Never read, vendor or require an external decompilation,
   dataset or download. Whatever the game needs is derived from the disc by this project's own analyzers at
   conversion time. Never copy or line-by-line translate decompiled code, and describe structures in comments
   by disc file and offset.
3. Offsets, addresses, structure layouts and format constants are fine to commit. Content values are not.
4. Tests use synthetic data only. They must run without a disc.

The CI runs the provenance guard (`tests/Dw3.Repository.Tests`) on every PR.
It catches known patterns, not everything: reviewers will ask about provenance.

## Provenance checklist

Before opening a PR, verify each item:

- [ ] No game content (assets, text, tables, captures) is added in any form
- [ ] No code copied or line-by-line translated from a decompilation
- [ ] Disc structures are described by file path and byte offset, not by symbol or function name from a decompilation
- [ ] Tests use only fabricated data and pass without a disc present
- [ ] `dotnet format whitespace Dw3.sln` and `dotnet format style Dw3.sln` have been run before committing
- [ ] `dotnet test Dw3.sln` passes locally
- [ ] `dotnet build Dw3.sln` has zero errors: every analyzer rule is a build error, and no diagnostic is suppressed
- [ ] The coverage check passes (80 % of lines in `Dw3.Data`, `Dw3.Disc`, `Dw3.Formats`, `Dw3.Extractor`)

## Setup

See the Quick start in [README.md](README.md). Local overrides go in `.env.local` (copy `.env.example`).

## Running the checks

```sh
dotnet build Dw3.sln
dotnet format whitespace Dw3.sln   # the CI verifies both format passes on Linux
dotnet format style Dw3.sln
dotnet test Dw3.sln                # all synthetic tests, including the provenance guard
```

Coverage (the CI fails when a library listed in `tools/quality-baseline.json` is under 80 %):

```sh
rm -rf artifacts/quality/coverage
dotnet test Dw3.sln --collect:"XPlat Code Coverage" --results-directory artifacts/quality/coverage
dotnet run --project tools/Dw3.QualityRatchet -- check
```

The build fails on any rule violation. The rules and their limits, in short:
cognitive complexity 13 per method (S3776) and 50 per class (DW3001), 60 lines (S138),
7 parameters (S107) and 3 nesting levels (S134) per method, one type per file named after it
(SA1402, SA1649), XML documentation on every element outside `tests/` (CS1591, SA16xx),
the .NET analyzers in `Recommended` mode and the full StyleCop set.
No suppression is accepted: no `#pragma warning disable`, `[SuppressMessage]`,
`[ExcludeFromCodeCoverage]`, `NoWarn` or lowered severity. Fix the code instead.
Details and reasons: [docs/CODE_RULES.md](docs/CODE_RULES.md).

### Optional pre-push guard

Maintainers can install the tracked guard for this checkout with:

```sh
git config core.hooksPath tools/hooks
```

It refuses branches other than `main` and `v*` tags, then runs the repository provenance guard.
To remove it, run `git config --unset core.hooksPath`.

## Where things live

- `src/Dw3.Disc` — reads BIN/CUE images and identifies the disc edition
- `src/Dw3.Formats` — decoders for each disc format
- `src/Dw3.Extractor` — the extraction pipeline and its command line
- `src/Dw3.Data` — the game-data schema, reader and writer shared by extractor and game
- `src/Dw3.Game` — the Godot project: `Simulation` game rules, `Presentation` rendering
- `docs/research/` — reverse-engineering notes per format (internal, not in the public repo)

## Workflow

- Open an issue or comment on one before large changes.
- One topic per PR. Conventional commit messages (`feat(battle): ...`, `fix(field): ...`).
- PRs need a green CI and one maintainer review.
- Without a disc you can still work on synthetic tests, tooling, docs and pure simulation code.

## Research contributions

Format findings are valuable even without code. Use the "Research finding" issue template.
Describe structures and offsets, never paste dumped content.

## License of contributions

By submitting a contribution you agree that it is licensed under the project license
(PolyForm Noncommercial 1.0.0) and that you have the right to submit it.
Sign your commits with `git commit -s`
(Developer Certificate of Origin, https://developercertificate.org).

## For AI coding assistants

Follow the same ground rules as human contributors: no game content, no decompilation references,
synthetic test data only. Run `dotnet build Dw3.sln && dotnet test Dw3.sln` before finalizing any change.
Never write game-derived data (decoded values, extracted bytes, game text) into the repository.
