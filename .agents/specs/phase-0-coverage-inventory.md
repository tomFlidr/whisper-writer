# Phase 0 — Coverage Inventory

**Status:** Phase 0 baseline verified locally; current Phase 3 behavior is recorded in the appended section.
`AGENTS.md` Sections 2–4 were synchronized under explicit user approval; Section 1 and Sections 5 onward
were left unchanged.
Last verified November 12, 2026.
**Scope:** Existing behavior in the current Windows/WPF source tree. This is a characterization
baseline, not a line-coverage target and not authorization to begin later roadmap phases.
Counts in the Phase 0 baseline section below describe the repository as verified on November 4, 2026;
later source/test additions are tracked in their phase-specific sections.

**Path bases:** Production source paths below are relative to `WhisperWriter/`; test source files
are in the sibling `WhisperWriter.Tests/` project. Solution and documentation paths are relative
to the repository root.

## Verified repository and test-run counts

- Production source: 52 `.cs` files and 5 `.xaml` files.
- Test project: 30 `.cs` files (25 test sources and 5 support files).
- xUnit v3/VSTest execution: 185/185 passed in Debug and 185/185 passed in Release.
- Application builds: Debug and Release each completed with 0 warnings and 0 errors.
- Current behavior inventory: 95 behavior-bearing public/protected contracts; all 95 map to
  one or more asserted tests. Higher-level tests may cover multiple contracts.
- XAML: all 15 routed-event assignments across four view XAML files map to XAML-level tests.
- A temporary failing assertion was previously used to verify nonzero `dotnet test` failure
  signaling; the probe was removed. At this 2026-11-04 checkpoint, no remote GitHub Actions run had been made;
  Tests workflow run #4 later passed on the final `main` commit.

Run from the repository root on Windows:

```powershell
dotnet test WhisperWriter.sln -c Debug --no-restore -p:SignAssembly=false --logger "console;verbosity=normal"
dotnet test WhisperWriter.sln -c Release --no-restore -p:SignAssembly=false --logger "console;verbosity=normal"
dotnet build WhisperWriter/WhisperWriter.csproj -c Debug --no-restore -p:SignAssembly=false
dotnet build WhisperWriter/WhisperWriter.csproj -c Release --no-restore -p:SignAssembly=false
```

`--no-restore` assumes the solution has already been restored. On a clean checkout, run
`dotnet restore WhisperWriter.sln` first. The tests target `net8.0-windows7.0`; the test host
uses one WPF `Application` and xUnit parallelization is disabled.

## Behavior-bearing contract map

These 95 contracts exclude constructors, interface-only declarations, simple data accessors,
and forwarding members with no independent behavior. Tests assert externally observable
behavior; they are not required to call every method in isolation.

### `App.xaml.cs` — 7 contracts

`OnStartup(StartupEventArgs)`, `ExitApp()`, `OnExit(ExitEventArgs)`,
`initSystemPath4GfxLibs()`, `initGlobalErrorHandlers()`,
`handleDispatcherUnhandledException(object?, DispatcherUnhandledExceptionEventArgs)`, and
`handleAppDomainUnhandledException(object?, UnhandledExceptionEventArgs)`.

Covered by `AppLifecycleTests`: `Startup_LoadsSettingsShowsTheWindowAndInitializesWhisperInOrder`,
`ExitApp_DisposesTrayBeforeClosingTheWindowAndShuttingDown`,
`OnExit_DisposesEtaBeforeClosingTheLog`, `DispatcherUnhandledException_IsLoggedAndMarkedHandled`,
`GlobalErrorHandlerInitialization_RegistersAppDomainHandlerThroughRuntime`,
`AppDomainUnhandledException_LogsOnlyExceptionObjects`, and
`GraphicsPathInitialization_PrependsTheRuntimeFolderOnlyOnce`.

### `Program.cs` — 2 contracts

`Main(string[])` and `StartApplication(string[], IProgramRuntime)`.

Covered by `ProgramStartupTests.Main_UsesTheStartupRuntimeAndRunsTheContainerAppInTheRequiredOrder`
and `StartApplication_RejectsANullRuntimeBeforeChangingGlobalApplicationState`.

### Dependency injection — 3 contracts

`AutofacExtensions.InjectProtectedProperties<TLimit,TActivatorData,TRegistrationStyle>(...)`,
`Container.GetInstance()`, and `Container()`.

Covered by `AutofacExtensionsTests.InjectProtectedProperties_ResolvesMarkedPropertiesFromTheScope`,
`ContainerTests.GetInstance_ReturnsTheSameContainerEachTime`, and
`ContainerTests.Constructor_RegistersSettingsAsASingletonWithProtectedInjection`.

### `Utils/VirtualKeyCodeHelper.cs` — 2 contracts

`GetName(int)` and `FormatCombo(IReadOnlyList<int>)`.

Covered by `VirtualKeyCodeHelperTests`: configured-key theory cases, left-Alt naming, unknown-key
fallback, empty formatting, and input-order formatting.

### `Services/Recording/AudioRecorder.cs` — 4 contracts

`AmplitudeChanged`, `StartRecording()`, `StopRecording()`, and `Dispose()`.

Covered by `AudioRecorderTests.StartRecording_CapturesWaveSamplesAndReportsRmsAmplitude`,
`StopRecording_ReturnsNullWhenNoRecordingIsActive`, and
`Dispose_StopsAndReleasesAnActiveCapture`, using a fake `IAudioCapture`.

### `Services/Hotkey.cs` — 7 contracts

`Push2TalkStarted`, `Push2TalkStopped`, `SetVirtualKeyCodes(IReadOnlyList<int>)`,
`UpdateKeys(IReadOnlyList<int>)`, `Start()`, `Stop()`, and `Dispose()`.

Covered by `HotkeyTests.SetVirtualKeyCodes_ReturnsTheHotkeyInstance`,
`Polling_RaisesTransitionsAndUsesCopiedKeysThenUpdatedKeys`,
`UpdateKeys_StopsAnActiveHoldImmediately`, `Stop_PreventsFurtherPollingTransitions`, and
`Dispose_StopsPollingAndCanBeCalledRepeatedly`.

### `Services/Log.cs` — 5 contracts

`Info(string)`, `Warning(string, Exception?)`, `Error(string, Exception?)`,
`Transcription(string, TimeSpan)`, and `CloseAndFlush()`.

Covered by `LogTests.LoggingMethods_WriteToExpectedIsolatedSinks_AndFlushesOnClose` in Debug and
`LoggingMethods_AreNoOpsWithoutCreatingOutputDirectory` in Release.

### `Services/Settings.cs` — 2 contracts

`Load()` and `Save()`.

Covered by `SettingsTests.Load_MissingFileCreatesDefaultSettings`,
`SaveAndLoad_RoundTripConfiguredSettings`, `Load_InvalidJsonResetsToDefaults`, and
`Save_WhenPathIsDirectoryDoesNotThrow`, using isolated temporary paths.

### `Services/Persistence/TranscriptionHistory.cs` — 2 contracts

`MaxSize { get; set; }` and `Add(TranscriptionEntry)`.

Covered by `TranscriptionHistoryTests.Add_InsertsNewestFirstAndTrimsOldestEntries` and
`Add_RaisesCollectionChangeOnTheWpfDispatcherThread`.

### `Services/TrayMenu.cs` — 6 contracts

`Dispose()`, `HandleAppExit()`, `closeSecondaryWindow()`, `showAbout()`, `showHistory()`, and
`showSettings()`.

Covered by `TrayMenuTests.DisposeAndAppExit_DisposeTrayIconOnlyOnce`,
`AboutAndHistoryRequests_ResolveAndShowWindowsClosingThePreviousWindow`,
`SettingsRequest_WhenAccepted_SavesUpdatesHistoryAndReloadsHotkey`,
`SettingsRequest_WhenCancelled_DoesNotApplySettingsEffects`, and
`ExitAndDoubleClickRequests_RouteToInjectedActions`.

### `Services/Whisper.cs` — 6 current contracts

`StateChanged`, `ActiveBackend`, `GetInferenceThreadCount()`, `InitializeAsync(string)`,
`TranscribeAsync(byte[], string, string)`, and `DisposeAsync()`.

Covered by `TranscriberTests`: `GetInferenceThreadCount_LeavesTwoProcessorsFreeAndAtLeastOneActive`,
`InitializeAsync_ReportsTheBackendFromTheLoadedWhisperLibrary`, the initialization tests for missing,
adequate, undersized, and corrupt model files and factory failures, transcription language/prompt/segment/error
cases, and `DisposeAsync_DisposesTheInitializedFactory`. CUDA DLL probing was removed from the runtime contract.

### `Services/EtaCalc.cs` — 19 contracts

`EstimateProcessingSeconds(string,double,ActiveBackendInfo?)`,
`Record(string,double,double,ActiveBackendInfo?)`, `Dispose()`,
`initConnection()`, `ensureSchema()`, `estimateForWindow(long,long,double,double,double)`,
`estimateWithoutWindow(long,long,double)`, `findOrCreateEnvironmentId(ActiveBackendInfo)`,
`buildEnvironmentJson(ActiveBackendInfo)`, `readGpuModels()`, `readWmiString(string,string,bool)`,
`readWmiStrings(string,string,bool)`, `readWmiInt(string,string)`, `readWmiLong(string,string)`,
`computeFingerprint(string)`, `readSystemPowerStatus()`, `findModelId(string)`,
`findOrCreateModelId(string)`, and `execute(string)`.

Covered by `EtaCalcTests.RecordAndEstimate_UsesWindowedAndFallbackProcessingRatios`,
`Dispose_ClosesAndClearsAnOpenDatabaseConnection`,
`Record_IgnoresSamplesWithNonPositiveAudioOrProcessingDurations`,
`EstimateAndRecord_WhenDatabaseCannotOpenReturnSafely`,
`Record_TrimsSamplesToTheNewestThousandForEachModelAndEnvironment`,
`BuildEnvironmentJson_UsesInjectedEnvironmentAndSerializesTheFingerprintContract`,
`WmiHelpers_TrimTextAndConvertNumericValuesInvariantly`,
`WmiHelpers_ReturnNullOrEmptyWhenTheProviderThrows`,
`ReadGpuModels_FiltersSortsDeduplicatesAndUsesBothFallbacks`,
`ReadSystemPowerStatus_UsesNativeStatusOrSafeFallback`,
`BuildEnvironmentJson_UsesVerifiedCpuBackendWithSafeEnvironmentDefaults`, and
`ComputeFingerprint_ReturnsSha256OfTheUtf8EnvironmentJson`.

### `Services/MainWindows/Styles/Style.cs` — 4 contracts

`WindowDisplayChange`, `SetWindow(Window)`, `InitializeSystemEvents(object,EventArgs)`, and
`handleWin32SystemHook(IntPtr,int,IntPtr,IntPtr,ref bool)`.

Covered by `StyleTests.SetWindow_ReturnsTheSameStyleInstance`,
`InitializeSystemEvents_AddsToolWindowStyleAndClearsAppWindowStyle`, and
`NativeHook_ForwardsDisplayChangeWithoutMarkingTheMessageHandled`.

### `Services/MainWindows/Positions/Position.cs` — 7 contracts

`SetWindow(Window)`, `InitialPosition(Action)`, `ApplyStoredPosition()`,
`PlaceAtDefaultPosition()`, `ClampWindowToScreen()`, `SaveWindowPosition()`, and
`getPrimaryScreenScale()`.

Covered by `PositionTests` for stored/default placement, missing-primary-monitor fallbacks,
monitor-overlap clamping, persistence, callback ordering, DPI scaling, and
`SetWindow_ReturnsTheSamePositionInstance`.

### `Views/Screens/MainWindow.xaml.cs` — 19 contracts

`ReloadHotkey()`, `ForceClose()`, `OnActivated(EventArgs)`, `OnClosing(CancelEventArgs)`,
`handleWindowDisplayChange()`, `handleWindowSizeChanged(object,SizeChangedEventArgs)`,
`handleMouseEnter(object,MouseEventArgs)`, `handleMouseLeave(object,MouseEventArgs)`,
`handleMouseLeftButtonDown(object,MouseButtonEventArgs)`, `handlePush2TalkStarted()`,
`handlePush2TalkStopped()`, `handleRecordingAmplitudeChanged(float)`,
`handleWhisperStateChanged(TranscriptionState,string)`, `handleEtaTick(object?,EventArgs)`,
`setRecordingState(bool)`, `setStatus(string,bool)`, `updateEtaLabel()`,
`startEtaCountdown(double)`, and `stopEtaCountdown()`.

Covered by `MainWindowTest`: activation/reload, close and disposal paths, PTT success/blank/error
paths and call order, Whisper state changes, ETA countdown, amplitude/recording UI, display/size
handling, XAML mouse behavior, status brushes, and background-injection error logging. The
production scheduler is independently covered by
`RuntimeTests.RunBackground_ExecutesWorkOffTheDispatcherThread`.

## XAML event-binding map

The Phase 0 baseline had 15 routed-event assignments. Phase 2 adds two numeric `PreviewTextInput`
bindings in Settings, for 17 current assignments across the four view XAML files. The repeated
Cancel and Close bindings are separate assignments and are each exercised through their XAML
controls.

- `Views/Screens/MainWindow.xaml`: `MouseLeftButtonDown`, `MouseEnter`, `MouseLeave` —
  `MainWindowTest.XamlMouseBindingsDelegateDragAndHoverEventsWithoutPhysicalInput`.
- `Views/Screens/SettingsWindow.xaml`: title-bar `MouseLeftButtonDown`, hotkey-capture `Click`,
  `PreviewTextInput` on history size and both queue-limit fields, two Cancel `Click` bindings,
  and Save `Click` —
  `SettingsWindowTests.TitleBarMouseDown_DelegatesOnlyPressedDragsToTheInjectedPlatform`,
  `HotkeyCapture_AcceptsTheSimultaneouslyHeldKeysAndEscapeRestoresTheSavedCombo`,
  `NumericSettingsPreviewTextInput_BlocksNonDigitsAndAllowsDigits`,
  `CancelButton_DoesNotApplyEditedSettingsAndReturnsFalseThroughTheXamlBinding`,
  `CloseGlyph_CancelsThroughItsXamlClickBinding`, and
  `SaveButton_PersistsFormValuesAndRegistryChoiceThroughTheXamlBinding`.
- `Views/Screens/HistoryWindow.xaml`: title-bar drag, Copy `Click`, and Close `Click` —
  `HistoryWindowTests.TitleBarMouseDown_UsesTheInjectedPressedDecisionAndDragBoundary`,
  `CopyButton_UsesItsBoundEntryAndWritesTheTextThroughTheXamlBinding`, and
  `CloseButton_ClosesHistoryThroughItsXamlClickBinding`.
- `Views/Screens/AboutWindow.xaml`: title-bar drag and the same close handler on two buttons —
  `AboutWindowTests.TitleBarMouseDown_DelegatesDragToTheInjectedPlatform`,
  `CloseGlyph_ClosesTheAboutWindowThroughItsXamlClickBinding`, and
  `CloseButton_ClosesTheAboutWindowThroughItsXamlClickBinding`.
- `App.xaml` has no event-handler assignments. History item-template copy behavior and rendered
  text/metadata bindings are asserted by the History tests.

## Phase 2 additions (November 10, 2026)

These behavior contracts extend the Phase 0 baseline; they do not change its historical 95-contract
and 185-test counts.

- `Services/SpeechPipeline/TranscriptionQueue.cs`: active-slot scheduling, FIFO waiting, per-target result order,
  queue status/ETA, recording reservations, configurable outstanding-job admission, the soft audio
  budget and its one-recording overshoot behavior. Covered by `TranscriptionQueueTests`.
- `Services/Inference/Capacity/Policy.cs`, `Services/Inference/Capacity/Manager.cs`,
  `Services/Inference/Capacity/ProfileStore.cs`, and `Services/Inference/Memory/WindowsMonitor.cs`: adaptive capacity,
  resource-failure retry feedback, persisted profiles, and memory-signal fallbacks. Covered by the
  corresponding `PolicyTests`, `ManagerTests`,
  `ProfileStoreTests`, and `WindowsMonitorTests`.
- `Views/Screens/MainWindow.xaml.cs`: queue admission, per-recording settings/focus snapshot, queue status/ETA,
  and ordered result injection. Covered by `MainWindowTest` and the queue service tests.
- `App.xaml.cs` / `Services/Whisper.cs`: startup initialization is dispatched to a background task;
  at the Phase 2 checkpoint the pill showed `GPU Detection…` before the CUDA probe. Phase 3.0 supersedes
  that status with neutral model-loading text and the successfully loaded runtime identity.
  The Dispatcher responsiveness is covered by
  `AppLifecycleTests.Startup_RunsWhisperInitializationOffDispatcherAndKeepsTheUiResponsive`; status
  ordering is covered by `TranscriberTests` and `MainWindowTest`.
- `Views/Screens/SettingsWindow.xaml` / `.xaml.cs`: configurable job count and audio-buffer budget, numeric
  input, persistence into `AppSettings`, and user-facing explanation. Covered by
  `SettingsWindowTests`, `SettingsTests`, and `AppSettingsTests`.
- `TranscriptionQueue.Enqueue` takes ownership of the WAV byte array; the caller must not mutate it
  after enqueue. This avoids an extra full-size queue copy and is tested by
  `TranscriptionQueueTests.Enqueue_TransfersTheOwnedAudioBufferWithoutCopyingIt`.
- Current local verification on November 10, 2026: **217/217 Debug tests** and **217/217 Release
  tests** passed; Debug and Release application builds each completed with 0 warnings/errors.

## Phase 3.0 additions (November 11, 2026)

- `Models/ActiveBackendInfo.cs`: maps only recognized, actually loaded Whisper.net library IDs to a stable
  engine/runtime/provider descriptor and `cpu`/`gpu` device class. Unknown library IDs remain unclassified.
  `ActiveBackendInfoTests` covers CUDA, CUDA 12, Vulkan, CPU, CPU-NoAVX, and unknown-library handling.
- `Services/Whisper.cs` / `Contracts/ITranscription`: removed the CUDA DLL probe API. The active descriptor remains null
  until factory creation succeeds; startup shows `Loading model…` and does not predict a backend. Covered by
  `TranscriberTests.InitializeAsync_UsesNeutralLoadingStatusWhileTheModelLoads` and
  `InitializeAsync_ReportsTheBackendFromTheLoadedWhisperLibrary`.
- `Views/Screens/MainWindow.xaml.cs`: readiness status uses only the verified descriptor. Each
  `TranscriptionJob` snapshots that descriptor and passes the same value to ETA lookup and result recording.
  Covered by `MainWindowTest.QueueStatus_UsesOnlyVerifiedBackendDescriptorForReadyStatus` and
  `PushToTalkStop_UsesTheSameBackendSnapshotForEtaLookupAndRecording`.
- `Services/EtaCalc.cs`: ETA lookup/recording require an `ActiveBackendInfo`; unknown identity creates no profile.
  Environment JSON now nests device, engine, runtime, deterministic active provider list, and optional
  authoritative CUDA version under `backend`; legacy rows are preserved but not reused by the new fingerprint.
  Covered by the Phase 3 ETA JSON, missing-identity, and legacy-row tests.
- `Services/Inference/Capacity/Manager` fingerprints include the full active backend descriptor, including CUDA runtime
  major version; the memory monitor accepts stable `cpu` and `cpu-no-avx` provider IDs. Covered by
  `ManagerTests` and `WindowsMonitorTests`.
- `Whisper.DetectCudaVersion()` and its P/Invoke helper were deleted. The process PATH setup and build-time CUDA
  DLL copy remain native-loader packaging steps only; neither is used to claim an active backend.
- Final local verification on November 11, 2026, after the Vulkan/RID/publish-target changes:
  **235/235 Debug tests** and **235/235 Release tests** passed; Debug and Release application builds each
  completed with **0 warnings and 0 errors**.

## Phase 3.1 packaging additions (November 11, 2026)

- `WhisperWriter.csproj` references `Whisper.net.Runtime.Vulkan` 1.9.0, aligned with the pinned Whisper.net
  core, CPU, and CUDA packages. CUDA/Vulkan package references and the CUDA runtime copy target are omitted
  for `win-x86`.
- The Release workflow restores with `-p:RuntimeIdentifier=<RID>` so NuGet evaluates those package conditions
  for the actual target. It no longer edits the project to remove the CUDA copy target.
- The previous shared CUDA copy target ran after `Build` first and did not place the local runtime DLLs in a
  publish folder. Separate build/publish targets now put them in the correct output path.
- A fresh local `win-x64` publish contains 26 DLLs, including five Vulkan DLLs and the three configured
  CUDA toolkit DLLs. A fresh `win-x86` publish contains 13 DLLs, including only the four x86 CPU runtime
  DLLs; no CUDA/Vulkan asset is present.
- The fresh x86 application launch was verified under 64-bit Windows/WOW64 using `ggml-small.bin`; the
  widget reached `Ready (Cpu)`. This verifies the x86 app startup/model-load path, not a physical 32-bit
  Windows host or an x86 audio transcription.
- The 10-recording Vulkan/CUDA corpus comparison is complete; actual loaded runtimes and metrics-only output
  are recorded in `.agents/specs/phase-3-gpu-and-onnx-backends.md`. The run verifies this host/corpus only;
  no AMD GPU was tested and the Vulkan device index has no runtime-provided physical name.
- The isolated Whisper.net 1.9.0 Vulkan smoke from the Phase 3 spec completed transcription. No AMD GPU or
  older computer was tested; broad vendor/legacy coverage remains unverified.
- A separate real-runtime smoke forced CUDA-to-CPU runtime order with CUDA runtime directories removed from
  the child process PATH. Whisper.net reported `Cpu` and completed transcription (exit 0); transcript output
  was not saved. This does not verify automatic selection while a usable Vulkan runtime remains available.

## Phase 3.2/3.3 additions (November 11, 2026)

- `ProviderSelectionPolicy.Select` is covered for Auto ordering, unavailable explicit providers,
  and no cross-provider CUDA fallback by `ProviderSelectionPolicyTests`.
- `ProviderCatalog.GetInstalledProviderIds` is tested with a temporary pack directory; it
  requires both the provider assembly and `.deps.json` and does not report active backend support.
- `PluginLoader.Load` is tested through a real custom `AssemblyLoadContext` using a fake plugin
  assembly. The tests cover sharing the host-owned contract assembly and rejecting a provider-core switch
  after disposal.
- `NativeSearchPath.PrependDirectories` is covered for directory ordering, duplicate removal,
  and ignoring a missing provider path. `PrivacyPolicy.DisableTelemetry` is covered for setting
  the process-local `ORT_DISABLE_TELEMETRY` flag.
- `SettingsWindow` provider selection, CUDA/cuDNN path load/save, and pack-installation/restart explanation
  are covered by `SettingsWindowTests`; settings defaults/round-trip are covered by `AppSettingsTests`.
- `ActiveBackendInfo.FromOnnxRuntime` has deterministic mapping tests. The actual plugin/session path was
  also smoke-tested locally through `BackendFactory` against all three Parakeet graphs with
  synthetic inputs for CPU, DirectML, and CUDA. This hardware/runtime smoke is not part of deterministic CI
  and does not validate audio-to-text quality or performance.
- No automated known-audio Parakeet transcription test exists because the Phase 3.5 engine is not implemented.
- Current source inventory after the coding-standard organization refactor (November 12, 2026): 96 app C# files,
  5 XAML files, 47 test-project C# files (including 4 shared `TestSupport` files), and 10 C# files across the
  separate ORT provider/contract projects. The refactor leaves the 17 XAML event assignments unchanged.
- Phase 3 provider milestone verification on November 11, 2026: **257/257 Debug tests** and **257/257 Release
  tests** passed; full solution builds each completed with **0 warnings and 0 errors**.
- Final verification after the organization/member-order refactor (November 12, 2026): **257/257 Debug tests** and
  **257/257 Release tests** passed; full solution builds each completed with **0 warnings and 0 errors**. At that
  checkpoint hosted Actions had not run; Tests workflow run #4 later passed on commit `46a0868`.

## Data-only members and shared contracts

These do not add separate behavior counts. `AppSettings` defaults are tested by
`AppSettingsTests.Defaults_MatchTheInitialApplicationConfiguration`; `TranscriptionEntry`
construction and initializer values by `TranscriptionEntryTests`; collection behavior by
`TranscriptionHistoryTests`; `HotkeyModifiers` bit values by `HotkeyModifiersTests`; and
`SystemPowerStatus` layout by `SystemPowerStatusTests.StructLayout_MatchesTheWin32SystemPowerStatusContract`.
Simple properties/fields such as `TranscriptionHistory.Entries`, `Settings.Data`,
`Container.Provider`, `Program.DI/App`, `PowerStatusSnapshot` properties, and native input
struct fields have no independent behavior beyond their consumer tests.

Public seams are exercised through consumer tests and fakes:
`Apps/IAppRuntime`, `Programs/IProgramRuntime`, `Views/Screens/MainWindows/IRuntime`,
`Views/Adapters/IPlatform`, `Contracts/ITranscription`,
`Services/Recording/AudioRecorders/IAudioCapture`, `Services/TextOutput/TextInjectors/IPlatform`,
`Services/TrayMenus/IPlatform`, `Services/TrayMenus/IWindow`,
`Services/MainWindows/Positions/IEnvironment`, `Services/MainWindows/Styles/INativeApi`,
`Services/IManager`, `Services/IMonitor`, and `Services/Inference/Capacity/IProfileStore`.
These interface members are not double-counted as separate production behavior.

## Platform boundaries excluded from deterministic CI

The required suite tests application decisions through fakes and does not alter external user or
device state. These remaining integrations are not claimed as direct adapter-level tests:

- `NAudioCapture` opening a real microphone/device; `AudioRecorder` is tested with fake PCM input.
- Real focus switching and `SendInput` into another application; `TextInjector` ordering,
  flags, waits, timeouts, Unicode payloads, and structure layout are tested through
  `Services/TextOutput/TextInjectors/IPlatform`.
- Whisper.net model download and inference, which require a model file and network and may use
  a GPU; lifecycle, model-file decisions, arguments, results, failures, and disposal use fakes.
- Real `NotifyIcon`/Windows shell interaction and the application message loop; tray decisions
  and window orchestration are tested with platform/window fakes.
- Actual registry Run-key writes and clipboard replacement; both change shared user state.
  Settings and History tests verify the exact values sent to the injected platform instead.
- Actual window dragging, HWND style hooks, monitor enumeration, and DPI behavior against a
  user's desktop configuration; event decisions and placement algorithms use fake boundaries.
- Live WMI and `GetSystemPowerStatus` provider behavior; filtering, normalization, fallback
  values, environment serialization, and native struct layout are tested deterministically.

Optional Windows smoke checks may be added separately if they can isolate/restore user state and
do not require committing secrets, downloading a model, or depending on physical hardware.

## Remaining verification boundary

- `.github/workflows/tests.yml` is configured for pull requests and pushes to `main`. The initial Phase 0
  checkpoint predated hosted runs; subsequently, Tests workflow run #4 passed on `main` at commit `46a0868`
  (Windows Debug build-and-test job). Release configuration remains locally verified.
- At the Phase 0 checkpoint, `AGENTS.md` matched the project. Sections 3–4 were refreshed on November 12, 2026,
  under explicit user approval; Sections 1–2 and 5 onward were left unchanged. Appendix A remains historical.
- Phase 0 itself introduced no product behavior. Later product changes must use RED → GREEN → REFACTOR
  and are documented separately; this inventory preserves the Phase 0 characterization baseline.
- At the Phase 0 checkpoint, no files had been staged or committed.
