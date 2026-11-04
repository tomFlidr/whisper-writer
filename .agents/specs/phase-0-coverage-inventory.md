# Phase 0 — Coverage Inventory

**Status:** Test baseline, inventory, and current `AGENTS.md` sections reconciled locally.
Last verified November 4, 2026.
**Scope:** Existing behavior in the current Windows/WPF source tree. This is a characterization
baseline, not a line-coverage target and not authorization to begin later roadmap phases.

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
  signaling; the probe was removed. No remote GitHub Actions run has been made.

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

### `Services/AudioRecorder.cs` — 4 contracts

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

### `Services/TranscriptionHistory.cs` — 2 contracts

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

### `Services/Whisper.cs` — 6 contracts

`StateChanged`, `DetectCudaVersion()`, `GetInferenceThreadCount()`, `InitializeAsync(string)`,
`TranscribeAsync(byte[], string, string)`, and `DisposeAsync()`.

Covered by `WhisperTests`: `DetectCudaVersion_ReturnsOnlySupportedMajorVersions`,
`GetInferenceThreadCount_LeavesTwoProcessorsFreeAndAtLeastOneActive`, the initialization tests
for missing, adequate, undersized, and corrupt model files and factory failures, transcription
language/prompt/segment/error cases, and `DisposeAsync_DisposesTheInitializedFactory`.

### `Services/EtaCalc.cs` — 19 contracts

`EstimateProcessingSeconds(string,double)`, `Record(string,double,double)`, `Dispose()`,
`initConnection()`, `ensureSchema()`, `estimateForWindow(long,long,double,double,double)`,
`estimateWithoutWindow(long,long,double)`, `findOrCreateEnvironmentId()`,
`buildEnvironmentJson()`, `readGpuModels()`, `readWmiString(string,string,bool)`,
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
`BuildEnvironmentJson_WhenCudaAndWmiAreUnavailableUsesCpuAndSafeDefaults`, and
`ComputeFingerprint_ReturnsSha256OfTheUtf8EnvironmentJson`.

### `Services/MainWindows/Style.cs` — 4 contracts

`WindowDisplayChange`, `SetWindow(Window)`, `InitializeSystemEvents(object,EventArgs)`, and
`handleWin32SystemHook(IntPtr,int,IntPtr,IntPtr,ref bool)`.

Covered by `StyleTests.SetWindow_ReturnsTheSameStyleInstance`,
`InitializeSystemEvents_AddsToolWindowStyleAndClearsAppWindowStyle`, and
`NativeHook_ForwardsDisplayChangeWithoutMarkingTheMessageHandled`.

### `Services/MainWindows/Position.cs` — 7 contracts

`SetWindow(Window)`, `InitialPosition(Action)`, `ApplyStoredPosition()`,
`PlaceAtDefaultPosition()`, `ClampWindowToScreen()`, `SaveWindowPosition()`, and
`getPrimaryScreenScale()`.

Covered by `PositionTests` for stored/default placement, missing-primary-monitor fallbacks,
monitor-overlap clamping, persistence, callback ordering, DPI scaling, and
`SetWindow_ReturnsTheSamePositionInstance`.

### `Views/MainWindow.xaml.cs` — 19 contracts

`ReloadHotkey()`, `ForceClose()`, `OnActivated(EventArgs)`, `OnClosing(CancelEventArgs)`,
`handleWindowDisplayChange()`, `handleWindowSizeChanged(object,SizeChangedEventArgs)`,
`handleMouseEnter(object,MouseEventArgs)`, `handleMouseLeave(object,MouseEventArgs)`,
`handleMouseLeftButtonDown(object,MouseButtonEventArgs)`, `handlePush2TalkStarted()`,
`handlePush2TalkStopped()`, `handleRecordingAmplitudeChanged(float)`,
`handleWhisperStateChanged(TranscriptionState,string)`, `handleEtaTick(object?,EventArgs)`,
`setRecordingState(bool)`, `setStatus(string,bool)`, `updateEtaLabel()`,
`startEtaCountdown(double)`, and `stopEtaCountdown()`.

Covered by `MainWindowTests`: activation/reload, close and disposal paths, PTT success/blank/error
paths and call order, Whisper state changes, ETA countdown, amplitude/recording UI, display/size
handling, XAML mouse behavior, status brushes, and background-injection error logging. The
production scheduler is independently covered by
`MainWindowRuntimeTests.RunBackground_ExecutesWorkOffTheCallingThread`.

## XAML event-binding map

All 15 routed-event assignments across the four view XAML files map to tests. The repeated
Cancel and Close bindings are separate assignments and are each exercised through their XAML
controls.

- `MainWindow.xaml`: `MouseLeftButtonDown`, `MouseEnter`, `MouseLeave` —
  `MainWindowTests.XamlMouseBindingsDelegateDragAndHoverEventsWithoutPhysicalInput`.
- `SettingsWindow.xaml`: title-bar `MouseLeftButtonDown`, hotkey-capture `Click`,
  history-size `PreviewTextInput`, two Cancel `Click` bindings, and Save `Click` —
  `SettingsWindowTests.TitleBarMouseDown_DelegatesOnlyPressedDragsToTheInjectedPlatform`,
  `HotkeyCapture_AcceptsTheSimultaneouslyHeldKeysAndEscapeRestoresTheSavedCombo`,
  `HistorySizePreviewTextInput_BlocksNonDigitsAndAllowsDigits`,
  `CancelButton_DoesNotApplyEditedSettingsAndReturnsFalseThroughTheXamlBinding`,
  `CloseGlyph_CancelsThroughItsXamlClickBinding`, and
  `SaveButton_PersistsFormValuesAndRegistryChoiceThroughTheXamlBinding`.
- `HistoryWindow.xaml`: title-bar drag, Copy `Click`, and Close `Click` —
  `HistoryWindowTests.TitleBarMouseDown_UsesTheInjectedPressedDecisionAndDragBoundary`,
  `CopyButton_UsesItsBoundEntryAndWritesTheTextThroughTheXamlBinding`, and
  `CloseButton_ClosesHistoryThroughItsXamlClickBinding`.
- `AboutWindow.xaml`: title-bar drag and the same close handler on two buttons —
  `AboutWindowTests.TitleBarMouseDown_DelegatesDragToTheInjectedPlatform`,
  `CloseGlyph_ClosesTheAboutWindowThroughItsXamlClickBinding`, and
  `CloseButton_ClosesTheAboutWindowThroughItsXamlClickBinding`.
- `App.xaml` has no event-handler assignments. History item-template copy behavior and rendered
  text/metadata bindings are asserted by the History tests.

## Data-only members and shared contracts

These do not add separate behavior counts. `AppSettings` defaults are tested by
`AppSettingsTests.Defaults_MatchTheInitialApplicationConfiguration`; `TranscriptionEntry`
construction and initializer values by `TranscriptionEntryTests`; collection behavior by
`TranscriptionHistoryTests`; `HotkeyModifiers` bit values by `HotkeyModifiersTests`; and
`SystemPowerStatus` layout by `SystemPowerStatusTests.StructLayout_MatchesTheWin32SystemPowerStatusContract`.
Simple properties/fields such as `TranscriptionHistory.Entries`, `Settings.Data`,
`Container.Provider`, `Program.DI/App`, `PowerStatusSnapshot` properties, and native input
struct fields have no independent behavior beyond their consumer tests.

Public seams are exercised through consumer tests and fakes: `IAppRuntime`, `IProgramRuntime`,
`IMainWindowRuntime`, `IWindowPlatform`, `ITranscription`, `IAudioCapture`,
`ITextInjectorPlatform`, `IPositionEnvironment`, `IWindowStyleNativeApi`, and the protected
`TrayMenu` platform/window contracts. These interface members are not double-counted as separate
production behavior.

## Platform boundaries excluded from deterministic CI

The required suite tests application decisions through fakes and does not alter external user or
device state. These remaining integrations are not claimed as direct adapter-level tests:

- `NAudioCapture` opening a real microphone/device; `AudioRecorder` is tested with fake PCM input.
- Real focus switching and `SendInput` into another application; `TextInjector` ordering,
  flags, waits, timeouts, Unicode payloads, and structure layout are tested through
  `ITextInjectorPlatform`.
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

- `.github/workflows/tests.yml` is configured for pull requests and pushes to `main`, but a
  hosted GitHub Actions run has not been triggered or verified. Do not claim remote CI passed.
  The workflow can run when the user later publishes or opens a PR for these changes.
- `AGENTS.md` Sections 1–9 now reflect the current project and test setup. Appendix A is explicitly
  historical; use the source tree, this inventory, and updated `.agents` guidance for current truth.
- No new product behavior was implemented as part of Phase 0. The first later behavior slice
  must independently demonstrate RED → GREEN → REFACTOR; that requirement is deferred until
  that slice begins so Phase 0 can stop without starting another roadmap phase.
- No files were staged or committed.
