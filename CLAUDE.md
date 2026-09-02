# CLAUDE.md

## Layout

`Underground.slnx` — `src/Underground.Outbox` (library) + `src/Underground.Outbox.SourceGenerator`
(Roslyn generator, netstandard2.0), `test/`, `example/` (consumers used as generator fixtures).

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

- `test/Underground.Outbox.SourceGeneratorTest` — Verify snapshot tests of generator output (~4s).
  Snapshots live in `Snapshots/` as `<Class>.<Test>#<GeneratedFile>.g.verified.cs`. A diff fails the
  test and writes a `.received.cs` next to it; accept by replacing the `.verified.cs` with it.
- `test/Underground.OutboxTest` — integration tests on Testcontainers Postgres. **Requires Docker**
  and takes ~3 min. Use `--project` on the generator tests for a fast inner loop.

## Conventions

- Central package management: add or bump versions in `Directory.Packages.props`, and reference
  packages without a `Version` attribute in the csproj.
- `Directory.Build.props` enables `EnforceCodeStyleInBuild` plus the Meziantou, Sonar and Roslynator
  analyzers, and `GenerateDocumentationFile` — public members need XML docs. This is what `-warnaserror`
  usually trips on, so build before handing work back.
- `net10.0`, nullable and implicit usings enabled. The source generator project must stay
  `netstandard2.0`.

## Additional Tools

@.claude/RTK.md
