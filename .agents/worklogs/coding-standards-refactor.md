# Coding Standards Refactor Worklog

## Scope and invariants

- Apply the approved rules in `.agents/skills/coding-standards.md`, especially Section 20: singular type names where
  practical, owner-specific helper folders, shared types at the nearest common directory, one top-level type per file,
  folder-aligned namespaces, no redundant enclosing-folder prefixes, and source-folder capacity limits.
- Preserve behavior. Include authored production and test C# sources; do not reorganize generated outputs or the
  cohesive audio fixture corpus merely to meet folder counts.
- Preserve all worktree content. The original Phase 3 index had 69 paths; the user later explicitly approved
  restaging the complete change set into feature commits. Do not use `git reset`/`git clean` or overwrite source.
  Push/tag only after the approved commit split and v1.4.0 release checks.
- Do not edit protected `AGENTS.md` Sections 1–4 without explicit permission. Ask before updating those sections if the
  new file layout makes their map stale.
- Do not claim the refactor is complete until Debug and Release tests/builds and a mechanical structure/namespace audit
  pass.

## Baseline

Verified on 2026-11-12 before structural changes:

- Debug tests: 257/257 passed.
- Release tests: 257/257 passed.
- Debug solution build: 0 warnings, 0 errors.
- Release solution build: 0 warnings, 0 errors.
- Existing staged index: 69 paths; preserve it unchanged. Initial unstaged changes were only the requested
  coding-standards addition.

## Approved coding-standard change

- Added `## 20. Component and Directory Organization` to `.agents/skills/coding-standards.md`.
- Included the clarification that “subclasses” means both class-owned helper/support types and inheritance-derived
  types.
- Scoped the size guidance to authored source-code folders; cohesive fixture datasets and generated/project metadata are
  not split without a structural reason.

## Initial audit

- 132 authored C# files across the app, tests, and ORT projects before splitting helper types.
- Initial folder-cap warnings: app root (mostly project metadata/assets), `WhisperWriter/Services` (43 direct source
  files), `WhisperWriter/Views` (12 files), `WhisperWriter.Tests/Services` (20 test files), and
  `WhisperWriter.Tests/Audio` (11 cohesive fixture files; retain as a documented structural exception).
- Namespace mismatch: `WhisperWriter/Utils/Enums/HotkeyModifiers.cs` declared `WhisperWriter.Utils`.
- Multiple top-level types: `WhisperWriter/Services/WhisperBackend.cs` contained four.
- Assembly-attribute-only files have no namespace by design.

## Completed and verified changes

- Moved `HotkeyModifiersTests.cs` to `WhisperWriter.Tests/Utils/Enums/` and aligned its namespace/import with
  `HotkeyModifiers.cs`.
- Corrected `HotkeyModifiers.cs` namespace to `WhisperWriter.Utils.Enums`.
- Corrected the swapped declarations in `Utils/Interfaces/ISingleton.cs` and `ITransient.cs` so file names match their
  interfaces.
- Focused Debug build passed with 0 warnings/errors; `HotkeyModifiersTests` passed 1/1.
- Added and read this worklog.

## Completed: ONNX Runtime source organization

- Moved the nine provider orchestration types to `WhisperWriter/Services/OnnxRuntime/` and aligned their namespaces.
- Moved five focused tests to `WhisperWriter.Tests/Services/OnnxRuntime/`, aligned namespaces, and shortened test class
  names.
- Shortened production type names to avoid repeating the `OnnxRuntime` folder name.
- Renamed CPU, CUDA, and DirectML implementation types/files to `ProviderPlugin.cs` / `ProviderPlugin`; updated the
  reflection type names in `ProviderCatalog`.
- Debug solution build passed with 0 warnings/errors; focused ONNX Runtime tests passed 17/17.
- A bulk patch had one no-match for `OnnxRuntimeProviderPack` in `ProviderCatalogTests.cs`; inspection confirmed that
  test did not reference that type, so no correction was needed.
- Historical intermediate checkpoint: 69 paths were staged before the approved commit split. This was superseded
  by the later nine-commit split; use live `git status`, not this old count.

## Completed: Whisper runtime boundary

- Split the four top-level types from `WhisperBackend.cs` into `IRuntime.cs`, `IRuntimeFactory.cs`, `NetRuntime.cs`, and
  `NetFactory.cs` under `Services/Whispers/`.
- Renamed the test suite and private fakes to avoid repeating the `Whispers` folder name; aligned namespaces and
  method-declaration spacing.
- Updated `Whisper` to use the new contracts and moved the characterization suite to `Tests/Services/Whispers/`.
- Debug solution build passed with 0 warnings/errors; `TranscriberTests` passed 19/19.
- A C# search finds no remaining references to the removed `IWhisperRuntime`, `IWhisperFactory`, `WhisperNetRuntime`, or
  `WhisperNetFactory` names.

## Completed: inference capacity and memory organization

- Moved the capacity family to `Services/Inference/Capacity/` and memory-budget family to `Services/Inference/Memory/`;
  renamed each type/file to avoid repeating its folder context.
- Moved and renamed the four corresponding test classes into the matching `Tests/Services/Inference/` folders.
- Updated `TranscriptionQueue` and test references/imports for the new capacity and memory namespaces.
- Removed `sealed` from the touched inference production/test types to comply with Section 9.
- Debug solution build passed with 0 warnings/errors; focused inference and queue tests passed 24/24.

## Completed: shared contracts, models, and core service groups

- Moved `ITranscription` to the project-level `Contracts/` folder and shared backend/job/queue/focus data to `Models/`;
  aligned namespaces and consumers.
- Grouped queue, history, audio capture, and text injection under `SpeechPipeline/`, `Persistence/`,
  `Recording/AudioRecorders/`, and `TextOutput/TextInjectors/`.
- Extracted `NativePlatform` and `InputEvent` into separate helper files; the interop-only `GuiThreadInfo` and `Rect`
  remain private nested structs beside their native boundary.
- Renamed the TextInjector test suite to `InjectionTests`; updated reflection-based struct checks for the new namespace.
- Updated `WhisperWriter.csproj`'s explicit compile metadata for the moved history service.
- Debug solution build passed with 0 warnings/errors; after the shared-model/service moves, the full Debug suite passed
  257/257. Full Release verification is still pending the final view migration.

## Completed: WPF view and owner-helper organization

- Moved the four XAML/code-behind pairs into `Views/Screens/`; updated every `x:Class` and code-behind namespace.
- Moved MainWindow-owned runtime types to `Views/Screens/MainWindows/` as `IRuntime` and `Runtime`; moved shared window
  adapters to `Views/Adapters/` as `IPlatform` and `WindowsPlatform`.
- Moved `Position` and `Style` implementation helpers into `MainWindows/Positions/` and `MainWindows/Styles/`; shortened
  their support type names.
- Extracted MainWindow test helpers into `Tests/Views/Screens/MainWindowTests/` and renamed the suite type to singular
  `MainWindowTest` to avoid a namespace/type name collision while retaining the plural helper folder.
- At the view migration milestone, full Debug/Release suites passed 257/257 and builds passed with 0 warnings/errors.
  After the latest test-helper split, Debug build passed with 0 warnings/errors and focused view/position/style/runtime
  tests passed 35/35; final full-suite verification is still pending.

## Completed: remaining owner-specific support types

- Moved the app and program runtime contracts into `Apps/` and `Programs/`; extracted their production runtime adapters
  into matching owner folders and updated namespaces/consumers.
- Extracted `RecordingContext`, transcription-queue `ActiveJob`, and provider-catalog `ProviderDefinition` into their
  plural owner folders, each in its own source file.
- Split the tray-menu platform/window contracts and Windows/WPF adapters into `Services/TrayMenus/`; kept the `TrayMenu`
  coordinator in `Services/TrayMenu.cs`.
- Retained native interop structs beside their owning Windows boundaries; private test fakes remain co-located with
  their single test suites.
- Refactored overlong methods in Whisper transcription, MainWindow recording/result handling, SettingsWindow selection,
  queue ETA calculation, and DXGI adapter enumeration into behavior-preserving helpers; the mechanical method-length
  scan now finds 0 of 571 parsed method bodies over 50 lines.
- Debug solution builds passed with 0 warnings/errors. Focused tests passed 77/77 after the method extractions and 51/51
  after the final helper-name cleanup.

## Completed: repository-wide structural and formatting audit

- Current source counts: 96 app C# files, 5 XAML files, 47 test-project C# files (including 4 shared `TestSupport`
  files), and 10 ORT provider/contract C# files.
- The mechanical audit found 0 namespace/path mismatches, 0 block-scoped namespaces, 0 multi-top-level-type files, 0
  file/type-name mismatches, 0 direct-folder-name redundancies, and 0 source-folder-capacity violations.
  `AssemblyInfo.cs` and `TestAssembly.cs` contain only assembly attributes and intentionally have no namespace/type.
- The class-size audit found no class over 600 lines; three types are within the documented 500–600 range. The method
  audit found no method over 50 lines.
- `dotnet format whitespace` was applied to authored C#; `--verify-no-changes` now passes. A byte-level check found no
  BOM, CRLF, final newline, or space-indented C# lines across the 153 C# files.
- `dotnet format style --verify-no-changes` passed for IDE0003, IDE1006, IDE0005, IDE0160, and IDE0011.
  Method-declaration spacing is configured in `.editorconfig` and verified by the formatter.
- Active specs, handoff/debugging notes, roadmap references, and `AGENTS.md` Sections 3–4 were refreshed; the user
  explicitly approved Sections 3–4 only. Historical run names and Appendix A were retained.
- Final verification after all Section 10 corrections (November 12, 2026): **257/257 Debug** and
  **257/257 Release** tests passed; Debug and Release solution builds each had 0 warnings/errors.
  Whitespace and style verification passed.

## Completed: member-order audit and final verification

Structural migrations, file/path/format audits, and active-doc reference refreshes are complete. The stale-name
search found only dated historical references. `AGENTS.md` Sections 3–4 were updated under explicit approval;
other protected sections remain unchanged. The initial Section 10 audit reported 193 inversions; the follow-up found
one remaining property-after-method inversion in `MainWindowTests/FakeEnvironment.cs`.
The live file was re-read after the fix: properties at lines 8–10, then the field at line 12 and methods at
lines 14–22. A delayed audit notification repeated the pre-fix positions. The audit reported no other
class/record or interface ordering violations. The written standard does not rank 13 `internal`, 6 `protected internal`,
8 explicit-interface, or 3 static-constructor members; their placement was not guessed.
The full Debug/Release suites and builds passed after the final correction.

## Final checkpoint

- No defined Section 10 ordering violations remain; access-order cases omitted from the written rule remain unspecified.
- Active source/documentation paths are current; remaining old names are historical notes.
- `AGENTS.md` Sections 3–4 were updated under explicit approval; Section 2 now records v1.4.0 under the
  later release authorization. Sections 1 and 5 onward remain unchanged.
- Debug and Release each passed 257/257 tests; both solution builds completed with 0 warnings and 0 errors.
- `git diff --check` passed with CRLF treated as line endings. The nine approved commits were pushed to `main`;
  tag `v1.4.0` was pushed to release commit `46a0868`.
- Hosted Tests [run #4](https://github.com/tomFlidr/whisper-writer/actions/runs/43338796222) passed the Windows
  Debug build-and-test job on `main`; Release [run #9](https://github.com/tomFlidr/whisper-writer/actions/runs/43341305308)
  published the approved v1.4.0 assets. GitHub API readback verified the non-draft release and its three uploaded
  assets: x64 app ZIP, x86 app ZIP, and DirectML ZIP. The CUDA provider-pack job also passed, but its ZIP was
  intentionally not attached.

## Resume procedure

On every context compaction/resume, read this file first, then reread `.agents/SKILLS.md`,
`.agents/skills/coding-standards.md`, `.agents/skills/refactoring.md`, `.agents/skills/testing.md`, `AGENTS.md`, the
current `git status`, and the exact affected source/test files. Treat this worklog as a navigation aid, not proof;
verify every listed state against the live repository before continuing.
