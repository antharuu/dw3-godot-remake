# Code rules

Every C# project in `Dw3.sln` builds with the .NET analyzers in `Recommended` mode, the
Sonar analyzer and StyleCop. Any diagnostic is a build error (`TreatWarningsAsErrors`).
The only exception is a temporary list of rules that the existing code still breaks,
tracked by the quality ratchet described below. New code must follow every rule.

## The rules

| Rule | Limit | Why |
| --- | --- | --- |
| S3776 cognitive complexity | 13 per method | A method has to be understood in one reading. Branches, loops and nesting add up fast. |
| S138 method length | 60 lines | A method that does not fit on a screen usually does more than one job. |
| S107 parameter count | 7 per method | Long parameter lists make call sites unreadable. Group related values in a type. |
| S134 nesting depth | 3 levels of `if`, `for`, `while`, `switch`, `try` | Deep nesting hides the main path. Return early or extract a method. |
| SA1402 one type per file | 1 | A type is found from its name. |
| SA1649 file name | the name of the first type | Same reason. |
| CS1591, SA1600 and the SA16xx family | every element, private members included, private fields excepted | Disc formats are full of offsets and conventions that are not obvious from the code. The comment says what the code relies on. |
| .NET analyzers (CAxxxx) | `AnalysisMode=Recommended` | Correctness, globalization and performance defaults of the .NET team. |
| Line coverage | 80 % for `Dw3.Data`, `Dw3.Disc`, `Dw3.Formats`, `Dw3.Extractor` | The libraries that read the disc and write the data pack are tested without a disc, with fabricated input. |
| Formatting | `dotnet format whitespace` and `dotnet format style` | Diffs show behaviour changes, not layout. |

The thresholds live in `SonarLint.xml`, the documentation scope and the `using` placement
(outside the namespace, `System` first) in `stylecop.json`, the severities in `.editorconfig`.
Test projects follow every rule except the documentation completeness rules
(`CS1591`, `SA1600`, `SA1601`, `SA1602`, `SA1611`, `SA1615`, `SA1618`): a test documents
its intent through its name. Comments written in tests must still be well formed.

Three StyleCop defaults are replaced by the conventions of this repository, in `.editorconfig`:
members are not prefixed with `this.` (SA1101), private fields use `_camelCase` (SA1309),
and files carry no copyright header because `LICENSE.md` covers the repository (SA1633).

## No suppressions

Do not silence a diagnostic: no `#pragma warning disable`, no `[SuppressMessage]`,
no `GlobalSuppressions.cs`, no `NoWarn`, no `.editorconfig` severity lowered to make a
build pass. Fix the code. If a rule is wrong for this repository, open an issue to change
the rule for everyone, with the reason written next to the setting.

## The quality ratchet

The analyzers were introduced on an existing code base, so some rules still have violations.
These rules are listed in `QualityRatchetedRules` in `Directory.Build.props` and passed to
`WarningsNotAsErrors`: they stay warnings while their count goes down. The list is temporary.
When a rule has no violation left, its identifier is removed from the list and the rule
becomes a build error like the others. The list is never extended.

Each compilation writes its diagnostics to `artifacts/quality/sarif/<project>.sarif`.
`tools/Dw3.QualityRatchet` counts the violations per rule, project and top-level folder of
the project (for example `S3776 Dw3.Game/Simulation`) and compares them with
`tools/quality-baseline.json`. It fails when a count goes up, when a rule appears in a
project folder where the baseline does not list it, or when the line coverage of a library
drops below its recorded floor. CI runs it on Linux after the tests, so a pull request that
adds a violation fails.

### Run it locally

```sh
dotnet build Dw3.sln
dotnet run --project tools/Dw3.QualityRatchet -- check
```

To include coverage, delete `artifacts/quality/coverage` first (old runs would be merged
in), then:

```sh
dotnet test Dw3.sln --collect:"XPlat Code Coverage" --results-directory artifacts/quality/coverage
dotnet run --project tools/Dw3.QualityRatchet -- check --require-coverage
```

The ratchet also writes the merged Cobertura report to `artifacts/quality/coverage.cobertura.xml`.

### Lower the baseline

When you fix violations or add tests, lock the improvement in:

```sh
dotnet run --project tools/Dw3.QualityRatchet -- update
```

`update` only lowers counts and raises coverage floors. If any count went up or any coverage
went down, it refuses, prints what regressed and leaves the baseline untouched. When a rule
reaches zero everywhere, `update` says so: remove that identifier from `QualityRatchetedRules`
in the same pull request. Commit `tools/quality-baseline.json` with the fix.

Never edit the baseline by hand to make room for new violations.

Exit codes: `0` the ratchet holds, `1` a regression or a refused update, `2` a missing or
unreadable input (no SARIF log for a project, no coverage report with `--require-coverage`,
an invalid baseline).
