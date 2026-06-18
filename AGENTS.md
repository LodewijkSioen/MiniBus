# AGENTS

## Purpose
This repository contains Caravelle runtime code plus a Roslyn source generator and tests.
Use these instructions to make safe changes quickly.

## Build And Test
Run from repository root:

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build --verbosity normal
```

Useful targeted runs:

```powershell
dotnet test Caravelle.Tests --verbosity normal
dotnet test Caravelle.Generator.Tests --verbosity normal
```

Reference CI workflow: [.github/workflows/build.yml](.github/workflows/build.yml).

## Project Boundaries
- Runtime library: [Caravelle/Caravelle.csproj](Caravelle/Caravelle.csproj)
- Source generator: [Caravelle.Generator/Caravelle.Generator.csproj](Caravelle.Generator/Caravelle.Generator.csproj)
- Integration tests: [Caravelle.Tests/Caravelle.Tests.csproj](Caravelle.Tests/Caravelle.Tests.csproj)
- Generator tests: [Caravelle.Generator.Tests/Caravelle.Generator.Tests.csproj](Caravelle.Generator.Tests/Caravelle.Generator.Tests.csproj)

Keep runtime behavior changes in `Caravelle/` and generator logic changes in `Caravelle.Generator/`.
When changing generation behavior, update or add tests in `Caravelle.Generator.Tests/` and integration coverage in `Caravelle.Tests/` when relevant.

## Handler Model And Pipeline
Canonical behavior is described in [README.md](README.md).

## Generator Conventions
- Generator target framework is `netstandard2.0`; keep it analyzer-compatible.
- Runtime and tests target `net10.0`; do not move generator to `net10.0`.
- Diagnostic IDs and semantics live in [Caravelle.Generator/Diagnostics.cs](Caravelle.Generator/Diagnostics.cs). Reuse existing IDs when extending diagnostics.
- If you change generated output shape, verify both source output and diagnostics in tests.
- Any output of the `HandlerModelFactory` must be fully equatable.

## Testing Conventions
- Test framework: NUnit.
- Generator tests use Verify snapshots (see files ending in `.verified.txt` under `Caravelle.Generator.Tests/`).
- Generator helper entry points are in [Caravelle.Generator.Tests/GeneratorTestHelper.cs](Caravelle.Generator.Tests/GeneratorTestHelper.cs).
- Integration test service setup is in [Caravelle.Tests/AppUnderTest.cs](Caravelle.Tests/AppUnderTest.cs).

When snapshots intentionally change, review `.received.` output and promote to `.verified.`.

## Change Safety Checklist
- Run targeted tests for touched area before full suite.
- Avoid unrelated refactors in generated-code-sensitive files.
- For public behavior changes, update [README.md](README.md) examples or behavior notes if needed.