# Phase 0 — Automated Testing Foundation

**Status:** Tests, coverage inventory, and current `AGENTS.md` sections are reconciled locally
(November 4, 2026). Hosted GitHub Actions has not run.

## Goal

Establish a repeatable automated test workflow before implementing the behavior planned in
later roadmap phases. The active target remains Windows/WPF; cross-platform migration is
deferred to Phase 7.

## Initial baseline (before Phase 0)

- At the start of Phase 0, `WhisperWriter.sln` contained only the application project. No test project
  or tracked test files were found.
- `dotnet test WhisperWriter.sln` returned exit code 0 without discovering or reporting tests.
  This is not evidence that application behavior is tested.
- The existing GitHub Actions workflow is release-oriented and runs for version tags; it does
  not provide a pull-request test gate.
- The application build returned exit code 0 after restoring the existing project dependencies.

## Delivered and locally verified

- Added the sibling project `WhisperWriter.Tests/`, targeting `net8.0-windows7.0`, with xUnit v3 + VSTest.
- Registered the test project in `WhisperWriter.sln` and added a Windows workflow at
  `.github/workflows/tests.yml` for pull requests and pushes to `main`.
- Added the repository-wide map at `.agents/specs/phase-0-coverage-inventory.md`: 52 production
  `.cs` files, 5 XAML files, 95 behavior-bearing public/protected contracts mapped to tests, and
  all 15 view XAML routed-event assignments mapped to UI tests.
- Final local verification on November 4, 2026: **185/185 tests passed in Debug** and
  **185/185 passed in Release**; Debug and Release application builds completed with 0 warnings
  and 0 errors.
- A temporary failing assertion previously verified that `dotnet test` returns a nonzero result
  on failure; the probe was removed.
- Test seams and fakes preserve production defaults and isolate Windows, hardware, timing, and
  service orchestration boundaries. Exact integration exclusions are listed in the inventory.

## Scope

1. Add a dedicated test project targeting the app's exact Windows framework:
   `net8.0-windows7.0`.
2. Inventory the existing application's behavior-bearing `public` and `protected` members.
3. Add automated characterization tests for existing behavior, then ensure
   `dotnet test WhisperWriter.sln` discovers tests and fails when an assertion fails.
4. Add a Windows CI check that builds the application and runs the tests for pull requests and changes to `main`.
5. Make test-first development the project rule for every feature, bug fix, and behavior-preserving refactor.
6. Add one phase-specific specification before implementing each later phase. Do not guess
   unresolved behavior or distribution details.

## Existing application coverage

- Phase 0 covers behavior already present in the application; it is not only infrastructure for future features.
- Map every existing behavior-bearing `public` or `protected` method to one or more tests that
  verify its contract, either directly or through a higher-level behavior test. A separate test
  method for every production method is not required when that would duplicate the same behavior.
- Record members with no independent behavior (for example, simple data accessors or forwarding
  wrappers) in the inventory without adding artificial tests for them.
- Do not exempt code merely because it calls Windows, audio, clipboard, GPU, or other system APIs.
  Isolate those effects behind testable boundaries and cover the application logic with fakes.
  Only irreducible physical/device interaction may be excluded from required CI; document why
  and keep a separate opt-in smoke test where practical.
- Phase 0's baseline inventory is complete. Maintain it when public/protected behavior or XAML
  event bindings change; do not impose a one-test-per-method rule for data-only/forwarding members.

## Test approach

- Cover existing behavior as well as each changed acceptance behavior with deterministic
  automated tests; do not require 100% line coverage.
- Keep the required CI suite independent of a physical microphone, GPU, clipboard, foreground
  window, downloaded models, or external network services.
- Isolate Windows and hardware APIs behind testable boundaries. Keep hardware-dependent checks separate and opt-in.
- New behavior and fixes follow RED → GREEN → REFACTOR; behavior-preserving refactors start from
  a passing characterization baseline. Continue this test-first cycle for every later behavior phase.

## Completion criteria — verified locally

- The solution contains a test project; final runs reported and executed 185 tests in both Debug
  and Release.
- A failing assertion was previously verified to produce a nonzero `dotnet test` exit code.
- The inventory maps all 95 audited behavior-bearing public/protected contracts and all 15 view
  XAML event assignments to tests. Documented integration exclusions and their reasons are in the
  inventory; there is no mechanical one-test-per-method requirement.
- The Windows workflow is configured to build and test pull requests and pushes to `main`.
- Project instructions require test-first changes and describe deterministic boundaries.

## Explicitly deferred

- Hosted GitHub Actions execution is pending the next user-approved push or pull request; remote
  CI has not been claimed as passing.
- `AGENTS.md` Sections 1–9 now reflect the current project path, source map, test workflow, and
  settings. Appendix A is explicitly historical and is not current implementation guidance.
- Phase 0 itself introduced no product behavior. The first subsequent behavior phase, Phase 2,
  is documented in `.agents/specs/phase-2-parallel-transcription.md` and is complete. Later changes
  must continue to follow RED → GREEN → REFACTOR.
- No files were staged or committed as part of Phase 0.

## Approved framework and runner

- Framework: xUnit.net v3.
- Runner: VSTest, so the project uses the standard `dotnet test` and Visual Studio Test Explorer path.
- Use `xunit.v3.mtp-off` to select xUnit v3 without its MTP runner, plus
  `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` for VSTest integration.
- MTP is not selected; do not add MTP-specific `global.json` configuration.
- Package references are approved. Keep versions pinned in the test project and verify restore,
  discovery, execution, and failure exit code before considering the setup complete.

See the [xUnit v3 VSTest setup][xunit-v3-vstest], [xUnit v3 runner guidance][xunit-v3-runners],
and [test platform overview][platform-overview].

[platform-overview]: https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-platform-vs-vstest
[xunit-v3-vstest]: https://xunit.net/docs/getting-started/v3/getting-started
[xunit-v3-runners]: https://xunit.net/docs/getting-started/v3/microsoft-testing-platform
