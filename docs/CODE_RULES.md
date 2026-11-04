# Code rules

Every C# project in `Dw3.sln` builds with the .NET analyzers in `Recommended` mode, the
Sonar analyzer, StyleCop and the repository analyzer `DW3001`. Any diagnostic is a build
error (`TreatWarningsAsErrors`), with no warning-only rule and no baseline: the whole
solution has zero violations, and `dotnet build Dw3.sln` fails on the first new one.
Line coverage is the one rule the build cannot see. It is checked after the tests by
`tools/Dw3.QualityRatchet`, described below.

## The rules

| Rule | Limit | Why |
| --- | --- | --- |
| S3776 cognitive complexity | 13 per method | A method has to be understood in one reading. Branches, loops and nesting add up fast. |
| DW3001 class cognitive complexity | 50 per class | The sum of cognitive complexity across all methods in a class. Large classes that are hard to navigate break this limit. Configurable in `.editorconfig` via `dw3_class_cognitive_complexity_max`. |
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
no `GlobalSuppressions.cs`, no `NoWarn`, no `WarningsNotAsErrors`, no `.editorconfig` or
`.globalconfig` severity lowered to make a build pass, and no `[ExcludeFromCodeCoverage]`
to hide untested code from the coverage check. `tests/Dw3.Repository.Tests` fails when one
of these appears in `src/`, `tests/`, `tools/` or a root build file. Fix the code. If a rule is wrong for this repository, open an issue to change
the rule for everyone, with the reason written next to the setting.

## The coverage check

`dotnet build` enforces every analyzer rule. Coverage needs a test run, so it has its own
command. `tools/quality-baseline.json` holds only the coverage section: an absolute minimum
(`minimumPercent`, 80) and the libraries it applies to (`Dw3.Data`, `Dw3.Disc`,
`Dw3.Extractor`, `Dw3.Formats`). `tools/Dw3.QualityRatchet` merges the Cobertura reports of
every test project (a line counts as covered when any test project ran it) and fails when a
listed library is below the minimum or absent from the reports. The minimum is never
derived from a measurement: adding braces or splitting a line changes the number of
measured lines, and a recorded floor would turn that into a false regression. Raising the
minimum is a decision written by hand in the baseline, lowering it is not allowed.

CI runs it on Linux after the tests, so a pull request that drops a library under 80 % fails.

### Run it locally

Delete `artifacts/quality/coverage` first (old runs would be merged in), then:

```sh
dotnet test Dw3.sln --collect:"XPlat Code Coverage" --results-directory artifacts/quality/coverage
dotnet run --project tools/Dw3.QualityRatchet -- check
```

The command also writes the merged Cobertura report to `artifacts/quality/coverage.cobertura.xml`.

Exit codes: `0` every library reaches the minimum, `1` a library is below it or missing from
the reports, `2` a missing or unreadable input (no Cobertura report, a missing or invalid
baseline, an unknown option).

## SARIF logs

Each compilation also writes its diagnostics to `artifacts/quality/sarif/<project>.sarif`.
CI uploads them with the merged coverage report as the `quality-reports` artifact, so a red
build can be read without rerunning it.

## DW3001 — Class cognitive complexity

The custom Roslyn analyser `tools/Dw3.Analyzers` reports DW3001 when the total cognitive
complexity of a class (sum across all methods, constructors and property accessors, including
all `partial` declarations) exceeds the configured limit.

The algorithm follows G. Ann Campbell's public specification (SonarSource, "Cognitive
Complexity, a new way of measuring understandability"): increments for `if`/`else if`/`else`,
ternary, `switch`, loops, `catch`, logical operator sequences and recursion, plus a nesting
increment for each level of nesting.

The limit defaults to 50 and is configurable per file in `.editorconfig`:

```editorconfig
dw3_class_cognitive_complexity_max = 50
```

Nested types are counted for themselves, not for their enclosing type. When a class exceeds
the limit, the right fix is to extract a smaller, focused type or break a complex method into
helpers.
