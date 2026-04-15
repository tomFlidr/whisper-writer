# WhisperWriter – Copilot Instructions

This file describes the current state of the **WhisperWriter** project and serves as context for GitHub Copilot / AI chat to seamlessly continue previous work.

> **Language rule**: Always respond and reason in **English**, regardless of the language the user writes in. This applies to all explanations, plans, code comments, commit messages, and file edits.

---

## 1. What the app does

WhisperWriter is a minimalist WPF application for Windows – **local push-to-talk voice transcription**. Inspired by Wispr Flow.

1. The user holds a keyboard shortcut (default: Left Alt + Left Win).
2. The app records audio from the microphone.
3. After releasing the keys, the recording is transcribed by a local Whisper model (whisper.cpp via Whisper.net).
4. The transcribed text is injected (`SendInput` Unicode) into the window that had focus before recording started.

No data leaves the computer. CUDA GPU acceleration is automatic.

---

## 2. Environment and stack

| Item | Value |
|---|---|
| Platform | Windows 10/11, .NET 8, WPF + WinForms (NotifyIcon) |
| Project | `D:\tec\cs\whisper-writer\WhisperWriter.csproj` |
| GPU | NVIDIA Quadro T2000, 4 GB VRAM, CUDA 13.2, driver 595.71 |
| Whisper.net | 1.9.0 (+ Runtime + Runtime.Cuda) |
| NAudio | 2.2.1 |
| Microsoft.Data.Sqlite | 8.0.0 |
| System.Management | 8.0.0 |
| System.Text.Json | 8.0.5 |
| Serilog | 4.3.0 (+ Serilog.Sinks.File 6.0.0) |
| Code language | C# 12, nullable enable, implicit usings, file-scoped namespaces |
| Indentation | TAB (not spaces) |
| Comments | English |

---

## 3. Project structure

```
D:\tec\cs\whisper-writer\
├── .github\
│   ├── copilot-instructions.md       ← this file
│   └── workflows\
│       └── release.yml               ← GitHub Actions: build & publish release ZIPs on tag push
├── README.md                          ← user-facing documentation (EN)
├── WhisperWriter.csproj
├── WhisperWriter.pfx                  ← Authenticode certificate (self-signed, password 1234, in .gitignore)
├── WhisperWriter.snk                  ← Strong Name key (in .gitignore)
├── setup-dev.ps1                      ← one-time developer setup: checks .NET SDK + CUDA, restores NuGet, copies CUDA DLLs, optional model download
├── setup-dev.bat                      ← launcher for setup-dev.ps1, bypasses execution policy
├── download-models.ps1                ← interactive PowerShell script: download GGML models to models\
├── download-models.bat                ← launcher for download-models.ps1, bypasses execution policy
├── App.xaml                           ← global WPF resources, styles
├── App.xaml.cs                        ← startup, tray icon, static services
├── AssemblyInfo.cs
├── settings.json                      ← default configuration (copied to bin on build)
├── Services\
│   ├── AudioRecorder.cs               ← NAudio, 16 kHz mono WAV, RMS amplitude event
│   ├── EtaStatsService.cs             ← SQLite-backed ETA statistics (per-model timing history)
│   ├── HotkeyService.cs               ← polling GetAsyncKeyState, 20 ms interval
│   ├── LogService.cs                  ← Serilog facade, daily rolling log to logs/
│   ├── SettingsService.cs             ← JSON load/save to BaseDirectory/settings.json
│   ├── TextInjector.cs                ← SaveFocus() + SendInput Unicode
│   └── WhisperService.cs              ← WhisperFactory, TranscribeAsync, CUDA
├── Util\
│   ├── AppSettings.cs                 ← POCO configuration (serialized to settings.json)
│   ├── HotkeyModifiers.cs             ← [Flags] enum: None, Alt, Control, Shift, Win
│   ├── TranscriptionEntry.cs          ← data record: Timestamp, Text, Duration
│   ├── TranscriptionHistory.cs        ← ObservableCollection<TranscriptionEntry>, thread-safe Add()
│   └── VkCodeHelper.cs                ← static helper: VK code → display name, FormatCombo()
├── Views\
│   ├── MainWindow.xaml/.cs            ← floating pill widget, PTT logic, VU meter, ETA
│   ├── HistoryWindow.xaml/.cs         ← transcription list, copy to clipboard
│   ├── SettingsWindow.xaml/.cs        ← settings form
│   └── AboutWindow.xaml/.cs           ← about window
└── models\
    ├── eta-time-stats.db          ← SQLite database with per-model ETA timing statistics
    ├── ggml-large-v2.bin          ← 2.95 GB, default model
    └── ggml-medium.bin            ← 1.46 GB, fallback model
```

---

## 4. Key files – overview

### `App.xaml`
- Defines global `ResourceDictionary`: colors (`BgBrush`, `AccentBrush`, `AccentRecordingBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`) and `IconButton` style.
- Color palette: dark nearly-opaque background `#F71C1C1E` (97% opacity), accent `#8B8BFF`, red for recording `#FF3B30`.
- `ShutdownMode="OnExplicitShutdown"` – the app does not close when the window is closed.

### `App.xaml.cs`
- Four static services: `App.SettingsService`, `App.History`, `App.WhisperService`, `App.EtaStats`.
- `EtaStats.Initialize()` is called on startup (opens/creates `models/eta-time-stats.db`).
- `EtaStats.Dispose()` is called in `OnExit` to cleanly close the SQLite connection.
- Tray icon (`NotifyIcon` from WinForms) – **right-click** opens menu:
  ```
  About WhisperWriter
  ─────────────────────
  Transcriptions
  Settings
  ─────────────────────
  Exit
  ```
- Double-click on tray icon → `Show()` + `Activate()` on `MainWindow`.
- `WhisperService.InitializeAsync(modelPath)` starts asynchronously in the background on startup.
- Global exception handling: `DispatcherUnhandledException` (UI thread) + `AppDomain.CurrentDomain.UnhandledException` (background threads) – both logged via `LogService`.

### `Util/AppSettings.cs`
- namespace `WhisperWriter.Util`
```csharp
public class AppSettings
{
    public string ModelPath { get; set; } = "models/ggml-large-v2.bin";
    public string Language  { get; set; } = "auto";   // "auto", "cs", "en", ...
    public string Prompt    { get; set; } = "";
    public int    HotkeyModifiers { get; set; } = 0x0001 | 0x0008; // Alt + Win (kept for reference)
    public List<int> HotkeyVkCodes { get; set; } = [0xA4, 0x5B]; // VK_LMENU + VK_LWIN (default: L Alt + L Win)
    public int    HistorySize     { get; set; } = 30;
    public bool   CopyToClipboard { get; set; } = true;  // copy result to clipboard after each transcription
    public bool   RunAtStartup     { get; set; } = false; // register/unregister HKCU Run key
    public double WindowLeft      { get; set; } = -1;  // distance from left edge of primary working area (DIP); -1 = default
    public double WindowBottom    { get; set; } = -1;  // distance from bottom edge of primary working area (DIP); -1 = default
}
```
- `WindowLeft` and `WindowBottom` store the **centre** of the widget relative to the primary screen's working area (device-independent pixels). This means the widget always expands symmetrically from the anchor point when its size changes (e.g. status text grows/shrinks). `WindowBottom` = distance from the widget's **centre** to the bottom of the working area.

### `Util/HotkeyModifiers.cs`
- namespace `WhisperWriter.Util`
- `[Flags]` enum: `None=0`, `Alt=0x0001`, `Control=0x0002`, `Shift=0x0004`, `Win=0x0008`.

### `Util/TranscriptionEntry.cs`
- namespace `WhisperWriter.Util`
- Record-like class: `Timestamp` (DateTime), `Text` (string), `Duration` (TimeSpan – transcription time).

### `Util/TranscriptionHistory.cs`
- namespace `WhisperWriter.Util`
- `TranscriptionHistory.Add()`: thread-safe, inserts at index 0 (newest on top), trims to `MaxSize`.

### `Services/LogService.cs`
- Static facade over `Serilog.Log`.
- **All logging is active in DEBUG builds only** – in Release all methods are no-ops and Serilog is never initialized.
- `Initialize()` – configures rolling file sink to `logs/whisperwriter-YYYYMMDD.log` next to exe, 14-day retention. No-op in Release.
- Methods: `Info(string)`, `Warning(string, Exception?)`, `Error(string, Exception?)`, `Transcription(string, TimeSpan)` – all wrapped in `#if DEBUG`.
- `CloseAndFlush()` – called from `App.OnExit`. No-op in Release.
- `LogDirectory` – public property with path to the log folder (available in both configurations).
- `using Serilog` / `using Serilog.Core` are inside `#if DEBUG` so Serilog is not loaded at all in Release.
- Logged in all `catch` blocks in the app + global `DispatcherUnhandledException` and `AppDomain.UnhandledException`.

### `Services/SettingsService.cs`
- Saves to `AppContext.BaseDirectory + "settings.json"` (i.e. next to exe).
- On deserialization error silently returns `new AppSettings()`.

### `Services/EtaStatsService.cs`
- Manages a SQLite database at `<BaseDirectory>/models/eta-time-stats.db` via `Microsoft.Data.Sqlite`.
- **Schema**: two tables:
  - `Versions (value TEXT)` – single row seeded from the current assembly version (`typeof(App).Assembly.GetName().Version`).
  - `Environments (id INTEGER PK AUTOINCREMENT, fingerprint TEXT UNIQUE, value TEXT UNIQUE)` – one row per detected runtime environment.
  - `Models (id INTEGER PK AUTOINCREMENT, model_key TEXT UNIQUE)` – auto-populated on first use of each model.
  - `Stats (id INTEGER PK AUTOINCREMENT, model_id INTEGER FK, environment_id INTEGER FK, audio_seconds REAL, processing_seconds REAL)` – one row per completed transcription.
  - Indexes: `idx_stats_model_env_id ON Stats(model_id, environment_id, id DESC)` and `idx_stats_model_env_audio ON Stats(model_id, environment_id, audio_seconds)`.
- `Initialize()` – opens/creates the DB from scratch, creates all tables/indexes, and seeds `Versions` when empty. No migration logic is present; the DB is assumed to be created fresh.
- `BuildEnvironmentJson()` – creates a canonical JSON description of the current runtime environment. It includes CPU model, logical/physical core counts, `gpus` (all detected GPU names sorted alphabetically), backend (`CPU`/`GPU`), CUDA version, total RAM, OS version/build, process architecture, Whisper thread count, `onAcPower`, and `powerSaverEnabled`.
- `ComputeFingerprint(json)` – stores a SHA-256 hex fingerprint alongside the JSON (`fingerprint TEXT UNIQUE`), while also keeping the full JSON unique in `value`.
- `EstimateProcessingSeconds(modelKey, audioSeconds)` – returns `double?`. It uses only rows from the same `model_id + environment_id`, first with similar recording lengths (±30%), then ±50%, then all rows for the same model/environment. ETA is shown from the **second matching transcription onward**, i.e. once at least one previous matching row exists.
- `Record(modelKey, audioSeconds, processingSeconds)` – inserts a new `Stats` row and trims oldest rows to keep at most `MaxRecordsPerModelEnvironment` (= 1000) rows per `model_id + environment_id`.
- Environment discovery uses `System.Management` (WMI) for CPU/GPU/RAM details and `GetSystemPowerStatus` from `kernel32.dll` for AC power and battery-saver state.
- All public methods are silently no-ops (with error logging) when the DB is unavailable.

### `Services/AudioRecorder.cs`
- `WaveInEvent`, 16 000 Hz, 16 bit, mono, 50 ms buffer.
- `StartRecording()` / `StopRecording()` – returns `byte[]` WAV.
- `AmplitudeChanged` event (float 0–1 RMS) for VU meter.

### `Services/HotkeyService.cs`
- **Polling** (not `RegisterHotKey`) – `GetAsyncKeyState` every 20 ms on a background thread.
- Reason: `RegisterHotKey` with the Win key behaves unreliably on Win 10/11.
- Events: `PushToTalkStarted`, `PushToTalkStopped`.
- Constructor `HotkeyService(IReadOnlyList<int> vkCodes)` – primary constructor, drives polling off an explicit VK list.
- Constructor `HotkeyService(HotkeyModifiers)` – legacy constructor, converts bitmask to VK list via `ModifiersToVkCodes()`.
- `UpdateKeys(IReadOnlyList<int>)` – atomically replaces the active key combination at runtime (no restart needed). If recording was in progress, fires `PushToTalkStopped` immediately.
- `IsComboHeld()` – all listed VKs must be simultaneously pressed; lock-protected read of `_vkCodes`.

### `Services/TextInjector.cs`
- `SaveFocus()` – saves the `GetForegroundWindow()` handle before PTT steals focus.
- `RestoreFocus()` – must be called from the **UI thread** (owns message pump). Uses `AttachThreadInput` + `SetForegroundWindow` + `BringWindowToTop`. `AttachThreadInput` requires a thread with a message queue – it fails silently on threadpool threads.
- `InjectText(string)` – must be called from a **background thread** (via `Task.Run`), NOT from the UI/Dispatcher thread, so that `Thread.Sleep` in `WaitForPhysicalRelease` does not block the message pump:
  1. `WaitForPhysicalRelease()` – polls `GetAsyncKeyState` up to 2 s until all currently configured PTT VK codes (read from `App.SettingsService.Settings.HotkeyVkCodes` at call time) plus both Win keys are physically released. **This is the critical step** – sending a synthetic keyup while the key is still physically held is silently ignored by Windows, leaving the Win shell hook active and permanently breaking mouse input.
  2. `ReleaseModifierKeys()` – sends synthetic `KEYEVENTF_KEYUP` for all 8 modifier VKs (LWin, RWin, LCtrl, RCtrl, LAlt, RAlt, LShift, RShift). This is **critical**: without it, the physically-held Win+Ctrl keys interfere with `SendInput` (text arrives as shortcuts) and Win key permanently breaks mouse buttons until reboot. **Win keys (`VK_LWIN`, `VK_RWIN`) additionally require `KEYEVENTF_EXTENDEDKEY` flag** – without it the keyup event is silently ignored by Windows and the Win shell hook stays active.
  3. `Thread.Sleep(80)` – gives the target window time to process the focus change from `RestoreFocus()`.
  4. `SendInput` Unicode (each char as keydown+keyup with `KEYEVENTF_UNICODE`, `wVk=0`).
- **`INPUT` struct layout**: On 64-bit Windows `sizeof(INPUT)` = 40 bytes. The union field (`ki`/`mi`) must be at **`[FieldOffset(8)]`**, NOT `[FieldOffset(4)]`. With `FieldOffset(4)`, `SendInput` receives misaligned data and silently processes 0 events – text is never injected and modifier keyups are never sent, permanently breaking mouse input. `SaveFocus()` logs an error at startup if the struct size does not match the expected 40 bytes.
- Call order in `MainWindow.OnPttStopped`: `TextInjector.RestoreFocus()` on UI thread → `Task.Run(() => TextInjector.InjectText(text))` on background thread.

### `Services/WhisperService.cs`
- `DetectCudaVersion()` – static method that probes CUDA runtime DLLs (`cudart64_N.dll` + `cublas64_N.dll`) for major versions **13, 12, 11** in order using `LoadLibraryEx`. Returns the first available major version as `int?`, or `null` if no CUDA runtime is found. Replaces the old `IsCudaAvailable()` bool method.
- `GetInferenceThreadCount()` – static helper returning `Math.Max(1, Environment.ProcessorCount - 2)`. Used both by Whisper inference and by the ETA environment fingerprint.
- `InitializeAsync(modelPath)` – loads model from disk (`WhisperFactory.FromPath`). Before loading, validates file size against `MinModelFileSizeBytes` (70 MB): if the file is smaller it is deleted and re-downloaded (guards against truncated/corrupted files). If the model is missing, downloads medium via `WhisperGgmlDownloader`.
- `TranscribeAsync(byte[] wavBytes, string language, string prompt)`:
  - Always sets an explicit language (`"auto"` → fallback `"cs"`) to **prevent translation**.
  - **Never calls `WithTranslate()`** – whisper.cpp translates only if explicitly enabled.
  - Builder: `.WithLanguage(effectiveLanguage).WithThreads(processorCount - 2)`.
  - Optionally `.WithPrompt(prompt)`.
- `StateChanged` event: `(TranscriptionState, string)` → `Loading`, `Transcribing`, `Done`, `Error`.
- `InitializeAsync` is fully wrapped in try/catch – on model load failure fires `StateChanged(Error, "Model load failed: …")` instead of silently losing the exception (fire-and-forget in `App.xaml.cs`).
- **Note**: `WithGreedySamplingStrategy()` returns `IWhisperSamplingStrategyBuilder` (a different interface than the main builder) – cannot chain `WithPrompt` or `Build()` after it. Do not add to the fluent chain.

### `Views/MainWindow.xaml`
- Floating pill widget, `SizeToContent="WidthAndHeight"`, `Topmost="True"`, no taskbar entry.
- **No buttons** – all navigation via tray menu.
- Layout: `[dot] [StatusLabel] [EtaLabel]` + below `[AmplitudeRow]` (3 px bar, hidden outside recording).
- Drag: `MouseLeftButtonDown` → `DragMove()`, position saved to settings.
- Animations: `PulseAnim` (RecDot blinking during recording), `FadeIn` on show, `FadeToHover`/`FadeToIdle` (mouse enter/leave opacity).
- **`AmplitudeRow`**: uses `Visibility="Collapsed"` (no `MinWidth`) – fully hidden outside recording so the window size adapts freely to the status text. Set to `Visibility.Visible` only during active recording, back to `Visibility.Collapsed` in `SetRecordingState(false)`.
- **`WidgetBorder_MouseEnter` / `WidgetBorder_MouseLeave`**: event handlers that trigger the `FadeToHover` / `FadeToIdle` storyboards (opacity 0.6 ↔ 1.0). Defined in `MainWindow.xaml.cs`.
- `ReloadHotkey()` – public method called by `App.ShowSettings()` after settings are saved; calls `_hotkey.UpdateKeys()` with the new VK list from settings for instant live reload without restart.

### `Views/MainWindow.xaml.cs`
- **ETA countdown**: statistics-based and environment-aware. After releasing PTT, calls `App.EtaStats.EstimateProcessingSeconds(modelKey, audioSeconds)`. The query uses only the same model and the same runtime environment, preferring similar audio lengths (±30%, then ±50%, then all rows for the same model/environment). ETA appears from the **second matching transcription onward**; until then the UI shows only "Transcribing…".
- After `InjectText` completes on the background thread, calls `App.EtaStats.Record(modelKey, audioSeconds, sw.Elapsed.TotalSeconds)` to persist the actual processing time for the current environment. Over time, estimates converge to real values and remain separated across different hardware/power states.
- `GetModelKey()` – static helper, returns `Path.GetFileNameWithoutExtension(Settings.ModelPath)` for use as the DB model key.
- PTT flow: `SaveFocus` → `StartRecording` → (release) → `StopRecording` → (if stats available) `StartEtaCountdown` → `TranscribeAsync` → `StopEtaCountdown` → (if `CopyToClipboard`) `Clipboard.SetText` → `RestoreFocus` → `Task.Run(InjectText + Record)`.
- **Window positioning architecture**:
- Position is stored as `(WindowLeft, WindowBottom)` relative to the primary screen's working area (device-independent pixels). Both values represent the **centre** of the widget so that it expands symmetrically from the anchor point regardless of size changes.
- `PositionWindow()` (called from constructor): registers a `Loaded` handler that calls either `ApplyStoredPosition()` or `PlaceAtDefaultPosition()` once `ActualHeight` is known.
- `ApplyStoredPosition()`: reconstructs `Left = waLeft + WindowLeft − ActualWidth/2`, `Top = waBottom − WindowBottom − ActualHeight/2`, then calls `ClampWindowToScreen()`.
- `PlaceAtDefaultPosition()`: centres the widget horizontally, places its centre 20 px above the taskbar on the primary screen.
- `SaveWindowPosition()`: stores `WindowLeft = Left + ActualWidth/2 − waLeft` and `WindowBottom = waBottom − (Top + ActualHeight/2)`.
- `GetPrimaryScreenScale()`: returns WPF DIP scale factors from `PresentationSource`; before the window is shown falls back to `SystemParameters / Screen.Bounds` ratio.
- **Display change handling**: `OnSourceInitialized` registers a `WndProc` hook via `HwndSource.AddHook` and a `SizeChanged` handler. On `WM_DISPLAYCHANGE` (0x007E), `OnDisplayChange()` is called: it calls `ApplyStoredPosition()` (or `PlaceAtDefaultPosition()` if no position is stored) to re-anchor the widget to the new primary monitor, then calls `ClampWindowToScreen()` as a safety clamp. On every size change, `ClampWindowToScreen()` is called directly.
- **`ClampWindowToScreen()`**: guards against `ActualWidth/Height == 0`. Finds the monitor with the maximum overlap with the window (using `Screen.AllScreens` + `Rectangle.Intersect`). Converts pixel coordinates to WPF DIP units. Clamps `Left`/`Top` to fit entirely within the working area of that monitor. If the result is still fully off every screen (e.g. monitor disconnected), falls back to bottom-centre of the primary screen. Persists the new position via `SaveWindowPosition()`.

### `Views/HistoryWindow.xaml`
- `ListView` with `ObservableCollection` binding to `App.History.Entries`.
- `DataTemplate`: two-column `Grid` – left column contains the main text (`TextBox` IsReadOnly, mouse-selectable) + timestamp/duration row, right column has a per-item **"Copy"** button (`BtnCopyEntry_Click`). Left column has `Margin="0,0,8,0"` so text never flows under the button.
- No footer – the global "Select an entry…" hint and shared "Copy to clipboard" button have been removed.

### `Views/SettingsWindow.xaml`
- Form: `ModelPath` (ComboBox: 12 models, items built at runtime in code-behind), `Language` (ComboBox: 57 languages + `auto`), `Prompt` (multiline TextBox), **hotkey capture** (display field + "Change…" button), `HistorySize` (numeric TextBox, min 1, max Int32.MaxValue), `CopyToClipboard` (CheckBox, default checked), `RunAtStartup` (CheckBox – reads/writes `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`, value name `WhisperWriter`, default unchecked).
- **Hotkey capture UI**: `TxtHotkeyDisplay` (read-only `TextBlock` inside a styled `Border`) shows the current combo. `BtnCaptureHotkey` ("Change…" / "Cancel") toggles capture mode. `TxtCaptureHint` (collapsed outside capture) shows instructions. Default combo: Left Alt + Left Win (`VK_LMENU` + `VK_LWIN`).
- Capture mode: `PreviewKeyDown` accumulates VK codes into `_captureDown`; on `PreviewKeyUp` the full set is snapshotted as `_capturedVkCodes` and capture exits. Escape cancels without saving. The captured combo is previewed live while keys are held. WPF input types aliased as `WpfKeyEventArgs` / `WpfKey` / `WpfKeyInterop` to avoid `System.Windows.Forms.KeyEventArgs` ambiguity.
- `CmbModelPath`: items are built dynamically in `BuildModelItems()` in code-behind. Each item has `Tag` = file path, `Content` = two-line `StackPanel` (name bold + HW requirements and size in smaller text). Models that are not downloaded (`File.Exists` check against `AppContext.BaseDirectory`) are shown with `Opacity=0.45`, `IsEnabled=false`, and ` · not downloaded` appended to the detail line. Model availability is checked against the 12 known `.bin` paths only, so `eta-time-stats.db` in the same `models\` folder is ignored.
- `CmbLanguage`: each item has `Tag` = ISO language code (e.g. `"cs"`) and `Content` = `"cs – Čeština"` (code + native name).
- `TxtHistorySize`: numeric TextBox, `PreviewTextInput` handler blocks non-numeric characters, fallback to `1` on save if value is invalid or < 1.
- `ChkCopyToClipboard`: CheckBox bound to `AppSettings.CopyToClipboard`.
- `ShowDialog()` returns `true` on Save, `false` on Cancel/X.

### `Views/AboutWindow.xaml`
- Info window, `ScrollViewer` with `TextBlock` (inline `Run` elements).
- Draggable, closeable via the close button or "Close" in the footer.

### `WhisperWriter.csproj` – CUDA target
- `CopyCudaRuntimeDlls` MSBuild target copies `cublas64_N.dll`, `cublasLt64_N.dll`, `cudart64_N.dll` to `$(OutputPath)runtimes\cuda\win-x64` after each build.
- `<CudaBinDir>` holds the full path to the CUDA bin folder (e.g. `C:\...\CUDA\v13.2\bin\x64`). Updated automatically by `setup-dev.ps1`.
- `<CudaVersion>` is extracted at build time from the path suffix via MSBuild `Regex` property function (e.g. `v13.2` → `13`). DLL names are assembled as `cublas64_$(CudaVersion).dll` – no hardcoded version number. Supports CUDA 11, 12, 13 and any future major version without further changes.
- **`$(IsPublishing)` condition**: `CudaEffectiveOutputDir` uses `$(IsPublishing) == 'true'` to decide between `$(PublishDir)` and `$(OutputPath)`. Using `$(PublishDir) != ''` was incorrect because the .NET SDK sets `$(PublishDir)` during every regular build when `<PublishSingleFile>true</PublishSingleFile>` is present, causing the DLLs to be copied into `bin\Debug\...\publish\runtimes\...` instead of the actual run directory.
- `ContinueOnError="true"` – build does not fail if CUDA is not installed.

### `WhisperWriter.csproj` – signing
- **Strong name**: `<SignAssembly>true</SignAssembly>` + `WhisperWriter.snk` (RSA key pair, no password).
- **Authenticode**: post-build target `AuthenticodeSigning` calls `signtool.exe` from Windows SDK `10.0.26100.0`.
  - Signs `$(OutputPath)WhisperWriter.exe` using `WhisperWriter.pfx` (password `1234`).
  - Timestamp server: `http://timestamp.digicert.com`, SHA256 algorithm.
  - `ContinueOnError="true"` – build does not fail if signtool is unavailable.
- **Certificate** (`WhisperWriter.pfx`): self-signed, CN=Tomáš Flídr, valid 10 years (until 2036), stored in `Cert:\CurrentUser\My`.
- Both files (`.pfx`, `.snk`) are in `.gitignore` – must not be committed.

### `.github/workflows/release.yml`
- Triggered on any tag matching `v[0-9]+.[0-9]+.[0-9]+` (e.g. `v1.2.3`).
- **Build matrix**: two legs — `win-x64` (CUDA-capable) and `win-x86` (CPU only, no CUDA).
- Each leg:
  1. Patches the `.csproj` in-place to disable `SignAssembly` (no `.snk` in CI) and remove the `AuthenticodeSigning` target (no `.pfx` in CI).
  2. For the `win-x86` leg also removes the `CopyCudaRuntimeDlls` target (CUDA has no x86 package).
  3. Runs `dotnet publish -c Release -r <rid> --self-contained false`.
  4. Copies `download-models.ps1` + `download-models.bat` into the publish folder.
  5. Creates an empty `models\` folder with a `README.txt` placeholder.
  6. Strips `.pdb` files.
  7. Packs everything into `WhisperWriter-v1.x.x-<rid>.zip`.
- A separate `release` job downloads all ZIPs and creates a GitHub Release via `softprops/action-gh-release@v2`.
  - Pre-release flag is set automatically when the tag name contains `-` (e.g. `v1.0.0-beta`).
  - Release notes are auto-generated from commit messages (`generate_release_notes: true`).
- Whisper model `.bin` files are **not** included in the ZIP (too large); users download them with the bundled scripts.

### `setup-dev.ps1`
- One-time developer environment setup script. Run once after cloning the repository.
- **Steps performed**:
  1. Checks for a compatible .NET SDK (>= 8). Aborts with a download link if not found.
  2. Detects NVIDIA CUDA Toolkit (major versions 13, 12, 11) via env vars, standard install paths, and `nvcc` in PATH. Extracts version from `cudart64_NN.dll` filename.
  3. Runs `dotnet restore WhisperWriter.csproj --verbosity quiet`.
  4. Copies `cudart64_NN.dll`, `cublas64_NN.dll`, `cublasLt64_NN.dll` from CUDA bin to `bin\Debug\net8.0-windows\runtimes\cuda\win-x64` (skips Release if not built yet). If CUDA was not found, step is skipped.
  5. Compares the `<CudaBinDir>` value in `.csproj` with the detected path and offers to update it interactively.
  6. Checks whether the default Whisper model (`ggml-large-v2.bin`) exists in `models\`. If no model is present, offers to launch `download-models.ps1`.
- If CUDA is absent, the app still works via CPU inference – the script warns about it but does not abort.
- No external dependencies – built-in Windows PowerShell 5.1+ compatible.

### `setup-dev.bat`
- One-line `.bat` launcher: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-dev.ps1"`.
- Uses `%~dp0` so it works from any working directory.
- Ends with `pause` so the console window stays open after the script finishes.

### `download-models.ps1`
- Interactive PowerShell script (no external dependencies, runs on built-in Windows PowerShell 5.1+).
- **Location**: project root, next to `WhisperWriter.csproj`. Should also be present next to the release `WhisperWriter.exe` in the ZIP.
- **What it does**:
  1. Shows a numbered table of all 12 GGML models (name, disk size, VRAM, description). Already-downloaded models are highlighted in green with `[downloaded]`.
  2. User enters a selection: comma/space-separated numbers and/or ranges (e.g. `1-3,7`).
  3. After confirmation, downloads missing files one by one via `System.Net.WebClient.DownloadFileTaskAsync` with live progress (`%`, MB received / MB total).
  4. Downloads are written to a `.tmp` file first, then atomically renamed on success. Failed downloads clean up the `.tmp` file.
  5. The `models\` folder is created automatically if it does not exist.
- **Source URL base**: `https://huggingface.co/ggerganov/whisper.cpp/resolve/main`
- **Execution policy**: if blocked, user must run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once.

### `download-models.bat`
- One-line `.bat` launcher that runs `download-models.ps1` via `powershell.exe -NoProfile -ExecutionPolicy Bypass`.
- Solves the execution policy error users get when running `.ps1` directly in PowerShell ISE or a restricted environment.
- Uses `%~dp0` so it works from any working directory (always resolves to the folder where the `.bat` lives).
- Ends with `pause` so the window stays open after the script finishes.

---

## 5. Build and run

```powershell
# Build (without restore – models are large)
dotnet build "D:\tec\cs\whisper-writer\WhisperWriter.csproj" -c Debug --no-restore

# Run
Start-Process "D:\tec\cs\whisper-writer\bin\Debug\net8.0-windows\WhisperWriter.exe"
```

> **Important**: The app must be closed before building – the exe file is locked by the running instance.

---

## 6. Known issues / TODO

### Open tasks

| Priority | Description |
|---|---|
| Low | **settings.json overwrite on build** – `PreserveNewest` copies the source `settings.json` to bin if it is newer, overwriting user settings. Consider `CopyToOutputDirectory=Never` and manual initialization on first run (SettingsService already handles this via `Save()` when the file does not exist). |
| Low | **Whisper model download fallback** – if the default model (`ggml-large-v2.bin`) is missing, `InitializeAsync` downloads `GgmlType.Medium` (not Large). A "Downloading model…" message is shown. |

### Resolved issues (for context)

- **Environment-aware adaptive ETA**: replaced the old hardcoded per-model ETA factor approach with `EtaStatsService` backed by `models\eta-time-stats.db`. The DB now stores `Versions`, `Environments`, `Models`, and `Stats`. Each completed transcription records `(model_id, environment_id, audio_seconds, processing_seconds)`, where `environment_id` is derived from a fingerprinted JSON of CPU/GPUs/RAM/OS/backend/CUDA/thread-count/power state. ETA is estimated only from the same model + same environment, preferring similar recording lengths (±30%, then ±50%, then all rows). ETA appears from the second matching transcription onward; otherwise the UI shows only "Transcribing…".
- **`Models/` folder renamed to `Util/`, classes split into separate files**: C# source files (`AppSettings.cs`, `HotkeyModifiers.cs`, `TranscriptionEntry.cs`, `TranscriptionHistory.cs`) moved from `Models\` to `Util\`. Each class is now in its own file. All namespaces updated from `WhisperWriter.Models` to `WhisperWriter.Util`. All `using WhisperWriter.Models` directives updated accordingly in `SettingsService.cs`, `HotkeyService.cs`, and `MainWindow.xaml.cs`. The `models\` directory (lowercase) remains as the data folder for Whisper `.bin` weight files only.
- **`IsCudaAvailable()` replaced by `DetectCudaVersion()`**: the old boolean probe hardcoded CUDA 13. Replaced with `DetectCudaVersion()` (returns `int?`) that probes major versions 13, 12, 11 in order and returns the first that has both `cudart64_N.dll` and `cublas64_N.dll` loadable. `MainWindow.xaml.cs` updated at both call sites to use the new method (startup status message and `OnWhisperState` backend label).
- **`WithGreedySamplingStrategy()` API**: this method returns a different interface (`IWhisperSamplingStrategyBuilder`) without `WithPrompt`/`Build`. Cannot be chained. Not used currently.
- **Ambiguous reference `System.Windows` vs `System.Windows.Forms`**: resolved with alias `using WpfApp = System.Windows.Application`.
- **Text injection and broken mouse after PTT**: `SendInput` was called while `VK_LWIN` and `VK_LCONTROL` were still physically held, causing characters to arrive as shortcuts and Win key hook to permanently break mouse buttons. Also, plain `SetForegroundWindow` silently failed under UIPI. Also, `InjectText` was called from the UI/Dispatcher thread where `Thread.Sleep` blocks the message pump. Also, the **`INPUT` struct had `[FieldOffset(4)]` for the `ki`/`mi` union field** – on 64-bit Windows the correct offset is `[FieldOffset(8)]` (40-byte struct); with the wrong offset `SendInput` silently processed 0 events. Fixed: correct `FieldOffset(8)`, `ReleaseModifierKeys()` with `KEYEVENTF_EXTENDEDKEY` for Win keys, `WaitForPhysicalRelease()` polling on background thread, `RestoreFocus()` on UI thread.
- **CRLF in C# files**: `LogService.cs`, `WhisperService.cs` and `AssemblyInfo.cs` had `\r\n` line endings. Fixed to `\n` via Node.js. `.editorconfig` extended with full C# K&R style rules (`csharp_new_line_before_open_brace = none` etc.). All project code reformatted to K&R style (opening `{` at end of line).
- **Wrong indentation of `case TranscriptionState.Error:` in MainWindow.xaml.cs**: one tab was missing. Fixed.
- **`WM_DISPLAYCHANGE` handler and `ClampWindowToScreen()` missing**: the `OnSourceInitialized` handler only set the window style (toolwindow / no taskbar entry) but did not register a `WndProc` hook. Added `HwndSource.AddHook(WndProc)`, constant `WM_DISPLAYCHANGE = 0x007E`, method `WndProc` that triggers `Dispatcher.BeginInvoke(ClampWindowToScreen)`, and `ClampWindowToScreen()` that clamps the window to the nearest monitor's working area (with DPI-aware scaling), falls back to bottom-centre of the primary screen if the window ends up completely off-screen, and saves the new position to `settings.json`.
- **`WindowTop` replaced by `WindowBottom`, visibility clamping improved**: `AppSettings.WindowTop` was removed entirely and replaced by `WindowBottom` (distance from the bottom of the widget to the bottom of the primary screen's working area). This ensures the widget stays visually anchored to the bottom after resolution/DPI/dock changes. `ClampWindowToScreen()` uses `Screen.AllScreens` with maximum-overlap selection, checks all screens for visibility, and is also triggered by `SizeChanged`. `SaveWindowPosition()` stores only `WindowLeft` and `WindowBottom`.
- **Centre-based window anchor**: `WindowLeft`/`WindowBottom` now store the **centre** of the widget (not the bottom-left corner). `ApplyStoredPosition()`, `PlaceAtDefaultPosition()`, `SaveWindowPosition()` and the `ClampWindowToScreen()` fallback all updated accordingly. The widget now expands symmetrically from its anchor point when the status text changes length (e.g. "Ready [GPU]" ↔ "Transcribing…").
- **Release publish failing with `GenerateBundle` error**: `dotnet publish -c Release` crashed with `System.ArgumentException: Stream length minus starting position is too large to hold a PEImage` because the `GenerateBundle` task (single-file publish) tried to parse Whisper model `.bin` files in `models\` as PE assemblies. Fixed by adding `<ExcludeFromSingleFile>true</ExcludeFromSingleFile>` to both `<Content>` items in `.csproj` (`models\**\*` and `settings.json`). The files are still copied next to the exe – just not bundled into it.
- **Configurable push-to-talk hotkey**: the hotkey was previously hardcoded to Left Ctrl + Left Win. Now the full key combination is stored in `AppSettings.HotkeyVkCodes` (a `List<int>` of Windows VK codes). Default changed to Left Alt + Left Win (`[0xA4, 0x5B]`). Settings dialog has an interactive capture UI: clicking "Change…" enters capture mode, the user holds the desired keys and releases them – the snapshot of simultaneously held keys becomes the new combo. `HotkeyService.UpdateKeys()` applies the change immediately without restart. `VkCodeHelper` provides human-readable display names for VK codes. `TextInjector.WaitForPhysicalRelease()` now reads the active VK list from settings at call time.
- **CUDA DLLs copied to wrong directory on build**: `CopyCudaRuntimeDlls` MSBuild target used `'$(PublishDir)' != ''` to decide the output directory, but the .NET SDK sets `$(PublishDir)` during every regular `dotnet build` when `<PublishSingleFile>true</PublishSingleFile>` is set, so DLLs landed in `bin\Debug\...\publish\runtimes\...` instead of the actual run directory. Fixed by using `'$(IsPublishing)' == 'true'` as the condition.
- **Truncated model file causes `WhisperModelLoadException`**: `WhisperFactory.FromPath` succeeds on a truncated GGML file (only reads header metadata) but `CreateBuilder()` fails at transcription time. Two-layer defence: (1) `MinModelFileSizeBytes` (70 MB) size check in `InitializeAsync` deletes and re-downloads the file before `FromPath` is called; (2) `TranscribeAsync` catches `WhisperModelLoadException` from `CreateBuilder()` (for files that pass the size check but are still corrupted), disposes and nulls `_factory`, deletes the model file, and fires `StateChanged(Error, …)` with a user-facing message so the status widget shows the error and the file is removed for re-download on next startup.

---

## 7. Code conventions

- **OOP**, clean code, no unnecessary dependencies.
- Comments in **English**, indentation with **TAB**.
- File-scoped namespaces (`namespace WhisperWriter.Services;`).
- Async/await everywhere there is I/O (Whisper, disk).
- Win32 P/Invoke interop concentrated in the relevant class (`TextInjector`, `HotkeyService`).
- WPF: `Dispatcher.Invoke` for cross-thread UI updates.
- No MVVM framework – direct code-behind, the app is small.

### `.editorconfig`
The `.editorconfig` file is in the project root (`D:\tec\cs\whisper-writer\.editorconfig`) and defines mandatory formatting rules:

```ini
root = true

[*]
indent_style = tab
tab_width = 4
charset = utf-8
end_of_line = lf
insert_final_newline = false
trim_trailing_whitespace = false

[*.cs]
# K&R brace style - opening brace on the same line as the statement
csharp_new_line_before_open_brace = none
csharp_new_line_before_else = false
csharp_new_line_before_catch = false
csharp_new_line_before_finally = false
...
```

Key points:
- Indentation: **TAB**, width 4.
- Encoding: **UTF-8** (without BOM – `charset = utf-8`, not `-bom`).
- Line endings: **LF** (`\n`), not CRLF.
- No blank line at end of file (`insert_final_newline = false`).
- Trailing whitespace is not trimmed (`trim_trailing_whitespace = false`).
- C# style: **K&R** – opening `{` always at the **end of the line** (not Allman).
- **Note**: VS 2022 Ctrl+K+D ignores `csharp_new_line_before_open_brace = none` and always places `{` on a new line (Allman). Code formatting via Ctrl+K+D must therefore be avoided – K&R is maintained manually.

---

### File encoding
All files in this project use **UTF-8 without BOM**.
When using PowerShell to read or search files, always add `-Encoding UTF8`.

### Editing files
When editing files **always** use the `replace_string_in_file` or `multi_replace_string_in_file` tool.
Never use terminal scripts (PowerShell, Node.js) to **write or modify** C# files — prefer file-editing tools.
Exception: files outside the project (`.editorconfig`, `.md`) may be written via `node -e` with a single-line command using `fs.writeFileSync`.
If you need PowerShell only for **reading or searching**, use it, but add `-Encoding UTF8` and run the command as a single line, not as a block script.
Line endings in C# files are **LF** – do not fix them manually, `replace_string_in_file` will preserve them.

### Updating the instruction file after completing a task
**After every successfully completed task where `run_build` returns a successful build, you MUST update this file (`.github/copilot-instructions.md`) to reflect the current state of the application.**

Specifically, always check and update if needed:
- Section **3. Project structure** – add/remove files as appropriate.
- Section **4. Key files** – update descriptions of changed files, add descriptions of new files.
- Section **6. Known issues / TODO** – move resolved items to "Resolved issues", remove or update items that have changed, add new open tasks if they arose.
- Section **8. Ideas for future development** – remove ideas that have been implemented.
- Any other sections whose content has become outdated by the task change.

This file is the primary source of context for future AI sessions – keep it accurate and up to date.

At the same time, check whether the changes affect user-visible behavior (installation, settings, model list, keyboard shortcuts, tray navigation, etc.). If so, also update **`README.md`** to reflect the current state of the application.

---

## 8. Ideas for future development

Ideas mentioned during development, not yet implemented:

- **More languages in ComboBox** – add Slovak, German, etc. (the base in SettingsWindow.xaml is already there).
- **History export** – CSV or plain text.
- **Dark/light theme** – the color palette base is in App.xaml, just add a second ResourceDictionary.
- **Multiple microphones** – `WaveInEvent.DeviceNumber`, selection in Settings.
- **Silent recording discard** – RMS threshold below which the recording is discarded (anti-click).
