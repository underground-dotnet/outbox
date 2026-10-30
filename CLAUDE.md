# CLAUDE.md

## Commands

```bash
dotnet restore                                       # then pass --no-restore below
dotnet build --no-restore -warnaserror -v minimal    # CI treats warnings as errors; -v minimal cuts output-path noise

# Tests run on Microsoft.Testing.Platform (via global.json), not VSTest, so these are MTP options.
dotnet test --no-restore
dotnet test --no-restore --project test/Underground.Outbox.SourceGeneratorTest/Underground.Outbox.SourceGeneratorTest.csproj
dotnet test --no-restore --project <project> --filter-class "*OutboxGeneratorTest" # or --filter-method
```

## Tests

- `test/Underground.Outbox.SourceGeneratorTest` — Meziantou.Framework.SnapshotTesting tests of generator
  output (~4s). Snapshots live in `__snapshots__/` as `<Class>_<Test>.verified.cs`, numbered `_0`, `_1`, …
  when a run yields several files (generated sources by hint name, then a `.txt` of diagnostics). A diff
  fails the test and writes an `.actual.*` next to it; accept by renaming it over the `.verified.*`.
- `test/Underground.OutboxTest` — integration tests on Testcontainers Postgres. **Requires Docker**
- Structure every test as Arrange/Act/Assert, marked with `// Arrange`, `// Act`, `// Assert` comments.
  Merge into `// Act & Assert` when the act is wrapped in the assertion (`Assert.Throws`); omit
  `// Arrange` when there is nothing to set up. A short *why* may follow the marker (`// Act: …`).

## Conventions

- Central package management: add or bump versions in `Directory.Packages.props`, and reference
  packages without a `Version` attribute in the csproj.
- `Directory.Build.props` enables `EnforceCodeStyleInBuild` plus the Meziantou, Sonar and Roslynator
  analyzers, and `GenerateDocumentationFile` — public members need XML docs. This is what `-warnaserror`
  usually trips on, so build before handing work back.
- The source generator project must stay `netstandard2.0`.
- Keep comments short. Prefer readable code — naming, small methods, types that make the invariant
  obvious — over a comment explaining unclear code. Comment the *why* that the code cannot express
  (a non-obvious SQL predicate, an analyzer suppression, a subtle ordering), never the *what*.
  XML `<summary>` on a public member is one or two lines; `<remarks>` is for rationale that would
  otherwise be lost, at a few lines at most. Drop comments that restate the line below them and
  empty doc tags (`<param></param>`, `<returns></returns>`).

## Agent skills

### Issue tracker

Issues and specs live as markdown files under `.scratch/<feature>/` in this repo. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical roles, used verbatim as `Status:` values. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.
