# WhisperWriter – Copilot Instructions

This file describes the current state of the **WhisperWriter** project and serves as context for GitHub Copilot / AI chat to seamlessly continue previous work.

> **Language rule**: Always respond and reason in **English**, regardless of the language the user writes in. This applies to all explanations, plans, code comments, commit messages, and file edits.

---

## 1. What the app does

WhisperWriter is a minimalist WPF application for Windows – **local push-to-talk voice transcription**. Inspired by Wispr Flow.

1. The user holds a keyboard shortcut (default: Left Ctrl + Left Win).
2. The app records audio from the microphone.
3. After releasing the keys, the recording is transcribed by a local Whisper model (whisper.cpp via Whisper.net).
4. The transcribed text is injected (`SendInput` Unicode) into the window that had focus before recording started.

No data leaves the computer. CUDA GPU acceleration is automatic.

---

## 2. Environment and stack

| Item | Value |
|---|---|
| Platform | Windows 10/11, .NET 8, WPF + WinForms (NotifyIcon) |
| Project | `D:\llms\whisper-writer\WhisperWriter.csproj` |
| GPU | NVIDIA Quadro T2000, 4 GB VRAM, CUDA 13.2, driver 595.71 |
| Whisper.net | 1.9.0 (+ Runtime + Runtime.Cuda) |
| NAudio | 2.2.1 |
| System.Text.Json | 8.0.5 |
| Serilog | 4.3.0 (+ Serilog.Sinks.File 6.0.0) |
| Code language | C# 12, nullable enable, implicit usings, file-scoped namespaces |
| Indentation | TAB (not spaces) |
| Comments | English |

---

## 3. Project structure

```
D:\llms\whisper-writer\
├── .github\
│   └── copilot-instructions.md       ← this file
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
│   ├── HotkeyService.cs               ← polling GetAsyncKeyState, 20 ms interval
│   ├── LogService.cs                  ← Serilog facade, daily rolling log to logs/
│   ├── SettingsService.cs             ← JSON load/save to BaseDirectory/settings.json
│   ├── TextInjector.cs                ← SaveFocus() + SendInput Unicode
│   └── WhisperService.cs              ← WhisperFactory, TranscribeAsync, CUDA
├── Util\
│   ├── AppSettings.cs                 ← POCO configuration (serialized to settings.json)
│   ├── HotkeyModifiers.cs             ← [Flags] enum: None, Alt, Control, Shift, Win
│   ├── TranscriptionEntry.cs          ← data record: Timestamp, Text, Duration
│   └── TranscriptionHistory.cs        ← ObservableCollection<TranscriptionEntry>, thread-safe Add()
├── Views\
│   ├── MainWindow.xaml/.cs            ← floating pill widget, PTT logic, VU meter, ETA
│   ├── HistoryWindow.xaml/.cs         ← transcription list, copy to clipboard
│   ├── SettingsWindow.xaml/.cs        ← settings form
│   └── AboutWindow.xaml/.cs           ← about window
└── models\
    ├── ggml-large-v2.bin              ← 2.95 GB, default model
    └── ggml-medium.bin                ← 1.46 GB, fallback model
```

---

## 4. Key files – overview

### `App.xaml`
- Defines global `ResourceDictionary`: colors (`BgBrush`, `AccentBrush`, `AccentRecordingBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`) and `IconButton` style.
- Color palette: dark nearly-opaque background `#F71C1C1E` (97% opacity), accent `#8B8BFF`, red for recording `#FF3B30`.
- `ShutdownMode="OnExplicitShutdown"` – the app does not close when the window is closed.

### `App.xaml.cs`
- Three static services: `App.SettingsService`, `App.History`, `App.WhisperService`.
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
    public int    HotkeyModifiers { get; set; } = 0x0002 | 0x0008; // Ctrl + Win
    public int    HistorySize     { get; set; } = 30;
    public bool   CopyToClipboard { get; set; } = true;  // copy result to clipboard after each transcription
    public bool   RunAtStartup     { get; set; } = false; // register/unregister HKCU Run key
    public double WindowLeft      { get; set; } = -1;  // -1 = default bottom-center
    public double WindowTop       { get; set; } = -1;
}
```

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

### `Services/AudioRecorder.cs`
- `WaveInEvent`, 16 000 Hz, 16 bit, mono, 50 ms buffer.
- `StartRecording()` / `StopRecording()` – returns `byte[]` WAV.
- `AmplitudeChanged` event (float 0–1 RMS) for VU meter.

### `Services/HotkeyService.cs`
- **Polling** (not `RegisterHotKey`) – `GetAsyncKeyState` every 20 ms on a background thread.
- Reason: `RegisterHotKey` with the Win key behaves unreliably on Win 10/11.
- Events: `PushToTalkStarted`, `PushToTalkStopped`.
- Currently hardcoded VK codes: `VK_LCONTROL (0xA2)` + `VK_LWIN (0x5B)`.
- `HotkeyModifiers` bitmask from `AppSettings` controls what is checked.

### `Services/TextInjector.cs`
- `SaveFocus()` – saves the `GetForegroundWindow()` handle before PTT steals focus.
- `RestoreFocus()` – must be called from the **UI thread** (owns message pump). Uses `AttachThreadInput` + `SetForegroundWindow` + `BringWindowToTop`. `AttachThreadInput` requires a thread with a message queue – it fails silently on threadpool threads.
- `InjectText(string)` – must be called from a **background thread** (via `Task.Run`), NOT from the UI/Dispatcher thread, so that `Thread.Sleep` in `WaitForPhysicalRelease` does not block the message pump:
  1. `WaitForPhysicalRelease()` – polls `GetAsyncKeyState` up to 2 s until `VK_LWIN`, `VK_RWIN`, `VK_LCONTROL`, `VK_RCONTROL` are all physically released. **This is the critical step** – sending a synthetic keyup while the key is still physically held is silently ignored by Windows, leaving the Win shell hook active and permanently breaking mouse input.
  2. `ReleaseModifierKeys()` – sends synthetic `KEYEVENTF_KEYUP` for all 8 modifier VKs (LWin, RWin, LCtrl, RCtrl, LAlt, RAlt, LShift, RShift). This is **critical**: without it, the physically-held Win+Ctrl keys interfere with `SendInput` (text arrives as shortcuts) and Win key permanently breaks mouse buttons until reboot. **Win keys (`VK_LWIN`, `VK_RWIN`) additionally require `KEYEVENTF_EXTENDEDKEY` flag** – without it the keyup event is silently ignored by Windows and the Win shell hook stays active.
  3. `Thread.Sleep(80)` – gives the target window time to process the focus change from `RestoreFocus()`.
  4. `SendInput` Unicode (each char as keydown+keyup with `KEYEVENTF_UNICODE`, `wVk=0`).
- **`INPUT` struct layout**: On 64-bit Windows `sizeof(INPUT)` = 40 bytes. The union field (`ki`/`mi`) must be at **`[FieldOffset(8)]`**, NOT `[FieldOffset(4)]`. With `FieldOffset(4)`, `SendInput` receives misaligned data and silently processes 0 events – text is never injected and modifier keyups are never sent, permanently breaking mouse input. `SaveFocus()` logs an error at startup if the struct size does not match the expected 40 bytes.
- Call order in `MainWindow.OnPttStopped`: `TextInjector.RestoreFocus()` on UI thread → `Task.Run(() => TextInjector.InjectText(text))` on background thread.

### `Services/WhisperService.cs`
- `InitializeAsync(modelPath)` – loads model from disk (`WhisperFactory.FromPath`). If the model is missing, downloads medium via `WhisperGgmlDownloader`.
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
- Animations: `PulseAnim` (RecDot blinking during recording), `FadeIn` on show.

### `Views/MainWindow.xaml.cs`
- **ETA countdown**: after releasing PTT calculates `estimatedSeconds = wavBytes.Length / 32000.0 * EtaFactor`.
- `EtaFactor = 0.90` (empirical coefficient for large-v2 + Quadro T2000 CUDA) – **may need calibration**.
  - `DispatcherTimer` 100 ms counts down and displays `~Xs` next to "Transcribing…".
- PTT flow: `SaveFocus` → `StartRecording` → (release) → `StopRecording` → `StartEtaCountdown` → `TranscribeAsync` → `StopEtaCountdown` → (if `CopyToClipboard`) `Clipboard.SetText` → `InjectText`.
- **Display change handling**: `OnSourceInitialized` registers a `WndProc` hook via `HwndSource.AddHook`. On `WM_DISPLAYCHANGE` (0x007E – fired when resolution/monitor layout changes, e.g. docking/undocking), `ClampWindowToScreen()` is called via `Dispatcher.BeginInvoke`.
- **`ClampWindowToScreen()`**: uses `System.Windows.Forms.Screen` to find the monitor containing the current window position. Converts its working area to WPF device-independent units using `PresentationSource.TransformFromDevice`. Clamps `Left`/`Top` so the window fits entirely within the working area. Falls back to bottom-centre of the primary screen if the clamped position is still outside any screen (e.g. the monitor it was on no longer exists). Saves the new position to `settings.json`.

### `Views/HistoryWindow.xaml`
- `ListView` with `ObservableCollection` binding to `App.History.Entries`.
- `DataTemplate`: two-column `Grid` – left column contains the main text (`TextBox` IsReadOnly, mouse-selectable) + timestamp/duration row, right column has a per-item **"Copy"** button (`BtnCopyEntry_Click`). Left column has `Margin="0,0,8,0"` so text never flows under the button.
- No footer – the global "Select an entry…" hint and shared "Copy to clipboard" button have been removed.

### `Views/SettingsWindow.xaml`
- Form: `ModelPath` (ComboBox: 12 models, items built at runtime in code-behind), `Language` (ComboBox: 57 languages + `auto`), `Prompt` (multiline TextBox), hotkey info (read-only), `HistorySize` (numeric TextBox, min 1, max Int32.MaxValue), `CopyToClipboard` (CheckBox, default checked), `RunAtStartup` (CheckBox – reads/writes `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`, value name `WhisperWriter`, default unchecked).
- `CmbModelPath`: items are built dynamically in `BuildModelItems()` in code-behind. Each item has `Tag` = file path, `Content` = two-line `StackPanel` (name bold + HW requirements and size in smaller text). Models that are not downloaded (`File.Exists` check against `AppContext.BaseDirectory`) are shown with `Opacity=0.45`, `IsEnabled=false`, and ` · not downloaded` appended to the detail line.
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
- `ContinueOnError="true"` – build does not fail if CUDA is not installed.

### `WhisperWriter.csproj` – signing
- **Strong name**: `<SignAssembly>true</SignAssembly>` + `WhisperWriter.snk` (RSA key pair, no password).
- **Authenticode**: post-build target `AuthenticodeSigning` calls `signtool.exe` from Windows SDK `10.0.26100.0`.
  - Signs `$(OutputPath)WhisperWriter.exe` using `WhisperWriter.pfx` (password `1234`).
  - Timestamp server: `http://timestamp.digicert.com`, SHA256 algorithm.
  - `ContinueOnError="true"` – build does not fail if signtool is unavailable.
- **Certificate** (`WhisperWriter.pfx`): self-signed, CN=Tomáš Flídr, valid 10 years (until 2036), stored in `Cert:\CurrentUser\My`.
- Both files (`.pfx`, `.snk`) are in `.gitignore` – must not be committed.

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
dotnet build "D:\llms\whisper-writer\WhisperWriter.csproj" -c Debug --no-restore

# Run
Start-Process "D:\llms\whisper-writer\bin\Debug\net8.0-windows\WhisperWriter.exe"
```

> **Important**: The app must be closed before building – the exe file is locked by the running instance.

---

## 6. Known issues / TODO

### Open tasks

| Priority | Description |
|---|---|
| Medium | **EtaFactor calibration** – currently `0.90`, measure the real ratio `transcription_time / recording_length` and update in `MainWindow.xaml.cs` if needed. Ideally the factor would be computed adaptively from the last N transcriptions. |
| Medium | **HotkeyService – configurable keys** – VK codes `VK_LCONTROL` and `VK_LWIN` are currently hardcoded. The `AppSettings.HotkeyModifiers` bitmask is saved, but `IsComboHeld()` ignores the Alt and Shift branches. A VK lookup table for Alt (`VK_LMENU = 0xA4`) and Shift (`VK_LSHIFT = 0xA0`) needs to be added. |
| Low | **settings.json overwrite on build** – `PreserveNewest` copies the source `settings.json` to bin if it is newer, overwriting user settings. Consider `CopyToOutputDirectory=Never` and manual initialization on first run (SettingsService already handles this via `Save()` when the file does not exist). |
| Low | **Whisper model download fallback** – if the default model (`ggml-large-v2.bin`) is missing, `InitializeAsync` downloads `GgmlType.Medium` (not Large). A "Downloading model…" message is shown. |

### Resolved issues (for context)

- **`Models/` folder renamed to `Util/`, classes split into separate files**: C# source files (`AppSettings.cs`, `HotkeyModifiers.cs`, `TranscriptionEntry.cs`, `TranscriptionHistory.cs`) moved from `Models\` to `Util\`. Each class is now in its own file. All namespaces updated from `WhisperWriter.Models` to `WhisperWriter.Util`. All `using WhisperWriter.Models` directives updated accordingly in `SettingsService.cs`, `HotkeyService.cs`, and `MainWindow.xaml.cs`. The `models\` directory (lowercase) remains as the data folder for Whisper `.bin` weight files only.
- **Translation instead of transcription**: `WithTranslate()` must not be called. Without an explicit language, whisper.cpp may translate. Fixed: always use `WithLanguage(effectiveLanguage)`, `"auto"` → `"cs"`.
- **`WithGreedySamplingStrategy()` API**: this method returns a different interface (`IWhisperSamplingStrategyBuilder`) without `WithPrompt`/`Build`. Cannot be chained. Not used currently.
- **Ambiguous reference `System.Windows` vs `System.Windows.Forms`**: resolved with alias `using WpfApp = System.Windows.Application`.
- **Text injection and broken mouse after PTT**: `SendInput` was called while `VK_LWIN` and `VK_LCONTROL` were still physically held, causing characters to arrive as shortcuts and Win key hook to permanently break mouse buttons. Also, plain `SetForegroundWindow` silently failed under UIPI. Also, `InjectText` was called from the UI/Dispatcher thread where `Thread.Sleep` blocks the message pump. Also, the **`INPUT` struct had `[FieldOffset(4)]` for the `ki`/`mi` union field** – on 64-bit Windows the correct offset is `[FieldOffset(8)]` (40-byte struct); with the wrong offset `SendInput` silently processed 0 events. Fixed: correct `FieldOffset(8)`, `ReleaseModifierKeys()` with `KEYEVENTF_EXTENDEDKEY` for Win keys, `WaitForPhysicalRelease()` polling on background thread, `RestoreFocus()` on UI thread.
- **CRLF in C# files**: `LogService.cs`, `WhisperService.cs` and `AssemblyInfo.cs` had `\r\n` line endings. Fixed to `\n` via Node.js. `.editorconfig` extended with full C# K&R style rules (`csharp_new_line_before_open_brace = none` etc.). All project code reformatted to K&R style (opening `{` at end of line).
- **Wrong indentation of `case TranscriptionState.Error:` in MainWindow.xaml.cs**: one tab was missing. Fixed.
- **`WM_DISPLAYCHANGE` handler and `ClampWindowToScreen()` missing**: the `OnSourceInitialized` handler only set the window style (toolwindow / no taskbar entry) but did not register a `WndProc` hook. Added `HwndSource.AddHook(WndProc)`, constant `WM_DISPLAYCHANGE = 0x007E`, method `WndProc` that triggers `Dispatcher.BeginInvoke(ClampWindowToScreen)`, and `ClampWindowToScreen()` that clamps the window to the nearest monitor's working area (with DPI-aware scaling), falls back to bottom-centre of the primary screen if the window ends up completely off-screen, and saves the new position to `settings.json`.

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
The `.editorconfig` file is in the project root (`D:\llms\whisper-writer\.editorconfig`) and defines mandatory formatting rules:

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

- **Adaptive EtaFactor** – average from the last N transcriptions, saved to settings.
- **Configurable hotkey via UI** – "press a key" dialog in SettingsWindow.
- **More languages in ComboBox** – add Slovak, German, etc. (the base in SettingsWindow.xaml is already there).
- **History export** – CSV or plain text.
- **Dark/light theme** – the color palette base is in App.xaml, just add a second ResourceDictionary.
- **Multiple microphones** – `WaveInEvent.DeviceNumber`, selection in Settings.
- **Silent recording discard** – RMS threshold below which the recording is discarded (anti-click).
