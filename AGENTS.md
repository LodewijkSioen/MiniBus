# AGENTS

## Purpose
This repository contains MiniBus runtime code plus a Roslyn source generator and tests.
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
dotnet test MiniBus.Tests --verbosity normal
dotnet test MiniBus.Generator.Tests --verbosity normal
```

Reference CI workflow: [.github/workflows/build.yml](.github/workflows/build.yml).

## Project Boundaries
- Runtime library: [MiniBus/MiniBus.csproj](MiniBus/MiniBus.csproj)
- Source generator: [MiniBus.Generator/MiniBus.Generator.csproj](MiniBus.Generator/MiniBus.Generator.csproj)
- Integration tests: [MiniBus.Tests/MiniBus.Tests.csproj](MiniBus.Tests/MiniBus.Tests.csproj)
- Generator tests: [MiniBus.Generator.Tests/MiniBus.Generator.Tests.csproj](MiniBus.Generator.Tests/MiniBus.Generator.Tests.csproj)

Keep runtime behavior changes in `MiniBus/` and generator logic changes in `MiniBus.Generator/`.
When changing generation behavior, update or add tests in `MiniBus.Generator.Tests/` and integration coverage in `MiniBus.Tests/` when relevant.

## Handler Model And Pipeline
Canonical behavior is described in [README.md](README.md).

## Generator Conventions
- Generator target framework is `netstandard2.0`; keep it analyzer-compatible.
- Runtime and tests target `net10.0`; do not move generator to `net10.0`.
- Diagnostic IDs and semantics live in [MiniBus.Generator/Diagnostics.cs](MiniBus.Generator/Diagnostics.cs). Reuse existing IDs when extending diagnostics.
- If you change generated output shape, verify both source output and diagnostics in tests.
- Any output of the `HandlerModelFactory` must be fully equatable.

## Testing Conventions
- Test framework: NUnit.
- Generator tests use Verify snapshots (see files ending in `.verified.txt` under `MiniBus.Generator.Tests/`).
- Generator helper entry points are in [MiniBus.Generator.Tests/GeneratorTestHelper.cs](MiniBus.Generator.Tests/GeneratorTestHelper.cs).
- Integration test service setup is in [MiniBus.Tests/AppUnderTest.cs](MiniBus.Tests/AppUnderTest.cs).

When snapshots intentionally change, review `.received.` output and promote to `.verified.`.

## Change Safety Checklist
- Run targeted tests for touched area before full suite.
- Avoid unrelated refactors in generated-code-sensitive files.
- For public behavior changes, update [README.md](README.md) examples or behavior notes if needed.