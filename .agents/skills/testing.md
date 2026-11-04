# Skill: Automated Testing

Load this skill before writing or changing tests, and before implementing any behavior.

## Mandatory test-first workflow

- For a new behavior or bug fix, write the smallest behavior or regression test first.
- Run the targeted test and confirm it fails for the expected reason, not because of a setup or compilation error.
- Implement the smallest production change that makes the test pass.
- Run the targeted test, then the complete test suite and application build.
- For refactoring, add or extend characterization tests before changing production code;
  run them before and after the refactor.
- Phase 0's behavior inventory is complete in
  `.agents/specs/phase-0-coverage-inventory.md`. Maintain it when behavior-bearing
  `public`/`protected` members or XAML event bindings change. Higher-level tests may cover
  multiple members; do not create redundant tests for trivial accessors or forwarding wrappers.
- Work in vertical behavior slices: one test, one minimal implementation, then the next test.
- Phase 0 is complete. For later roadmap work, read its phase specification and continue
  test-first; do not treat the completed baseline as a substitute for acceptance tests for new behavior.
- Cover each changed requirement and behavior. Do not impose a blanket line-coverage
  percentage without an agreed baseline.

## .NET verification

Run from the repository root on Windows. Restore once after dependency changes or on a clean checkout:

```powershell
dotnet restore WhisperWriter.sln
dotnet test WhisperWriter.sln -c Debug --no-restore -p:SignAssembly=false `
  --logger "console;verbosity=normal"
dotnet test WhisperWriter.sln -c Release --no-restore -p:SignAssembly=false `
  --logger "console;verbosity=normal"
dotnet build WhisperWriter/WhisperWriter.csproj -c Debug --no-restore -p:SignAssembly=false
dotnet build WhisperWriter/WhisperWriter.csproj -c Release --no-restore -p:SignAssembly=false
```

A zero exit code with no tests discovered is not a passing test run. Record the number of
tests discovered and executed. If the test project or runner reports zero tests for a behavior
change, stop and fix test discovery.

The target is `net8.0-windows7.0` with WPF enabled. WPF allows only one `Application` per test
process, so use `WhisperWriter.Tests/TestSupport/WpfTestHost` and keep xUnit parallelization disabled.

## Determinism and platform boundaries

- The required automated suite must be deterministic and must not need a microphone, GPU,
  clipboard, foreground window, or model download.
- Keep operating-system and hardware effects behind testable boundaries; use fakes for
  deterministic unit and integration tests.
- A call into a hardware or Windows API is not, by itself, a reason to skip testing: test the
  surrounding logic through an injected boundary. Exclude only irreducible physical/device
  interactions from required CI, document the reason, and use separate opt-in Windows smoke tests.
- Put microphone, GPU, SendInput, or other hardware-dependent checks in separate opt-in Windows smoke tests.
- Test observable behavior rather than private implementation details. Use mocks only when a
  real dependency boundary is unavoidable.

## Dependencies

The xUnit v3 + VSTest package set in `WhisperWriter.Tests.csproj` is approved and already present.
Ask the user before adding unrelated NuGet packages or changing the framework/runner.
