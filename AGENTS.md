# Repository Guidelines

## Project Structure & Module Organization
- `src/Snap`, `src/Snapx`, `src/Snap.Installer`: primary .NET projects (library, CLI, and installer).
- Native C++ runtime: `src/Snap.CoreRun` (+ tests in `src/Snap.CoreRun.Tests`).
- Managed tests: `src/Snap.Tests`, `src/Snapx.Tests`, `src/Snap.Installer.Tests`, shared fixtures in `src/Snap.Shared.Tests`.
- Solution: `src/Snapx.sln`. Build outputs land under `build/` (e.g., `build/dotnet`, `build/native`).
- Docs and tooling: `docs/`, `docker/`, PowerShell scripts in repo root.

## Build, Test, and Development Commands
- Bootstrap (first run): `pwsh ./init.ps1`.
- Full bootstrap build: `pwsh ./build.ps1 Bootstrap` (builds native + .NET for host OS/RIDs).
- Build and pack core packages: `pwsh ./build.ps1 Snap` or `pwsh ./build.ps1 Snapx`.
- Build installer: `pwsh ./build.ps1 Snap-Installer -Rid win-x64 -NetCoreAppVersion net9.0`.
- .NET tests: `pwsh ./build.ps1 Run-Dotnet-UnitTests -Rid win-x64`.
- Native tests: `pwsh ./build.ps1 Run-Native-UnitTests -Rid linux-x64`.
Notes: Scripts default to `Debug`, `net9.0`, and sensible RIDs; see `build.ps1 -?` for options. Requires .NET SDK 6/8/9 and PowerShell 7 (see README).

## Coding Style & Naming Conventions
- `.editorconfig` enforced: spaces, 4-space indent for C#; 2 for `*.csproj`/`*.props`/YAML; LF line endings in `src/`.
- C#: PascalCase for types/methods/properties, camelCase for locals/parameters, private fields `_camelCase` or `camelCase` consistently; files match main type name.
- Tests: classes end with `Tests`; filenames `*Tests.cs`.
- CMake/C++: 4-space indent; keep headers in `src/.../include` and sources under `src/.../src` as existing patterns show.

## Testing Guidelines
- Frameworks: xUnit (+ Moq) for .NET; Google Test for native.
- Run via scripts above or `dotnet test` inside each test project. Keep tests deterministic and cross-platform where applicable.
- Naming: one test class per unit/feature; method names should describe behavior, e.g., `MethodName_WhenCondition_ShouldResult`.

## Commit & Pull Request Guidelines
- Commits: short, imperative subject (e.g., "Update nuget packages"), optional scope, reference issues/PRs with `(#123)` when relevant.
- PRs: clear description, linked issues, reproduction steps, and output logs; include tests for new behavior and update docs (README/docs/) when user-facing changes occur.
- CI: target `develop` unless instructed; ensure `build.ps1 Run-*-UnitTests` passes locally for your platform/RID.

