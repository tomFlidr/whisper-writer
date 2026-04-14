# WhisperWriter – Copilot Instructions

This file describes the current state of the **WhisperWriter** project and serves as context for GitHub Copilot / AI chat to seamlessly continue previous work.

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
| Whisper.net | 1.7.2 (+ Runtime + Runtime.Cuda) |
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
├── App.xaml                           ← global WPF resources, styles
├── App.xaml.cs                        ← startup, tray icon, static services
├── AssemblyInfo.cs
├── settings.json                      ← default configuration (copied to bin on build)
├── Models\
│   ├── AppSettings.cs                 ← POCO configuration (serialized to settings.json)
│   └── TranscriptionHistory.cs        ← ObservableCollection<TranscriptionEntry>
├── Services\
│   ├── AudioRecorder.cs               ← NAudio, 16 kHz mono WAV, RMS amplitude event
│   ├── HotkeyService.cs               ← polling GetAsyncKeyState, 20 ms interval
│   ├── LogService.cs                  ← Serilog facade, daily rolling log to logs/
│   ├── SettingsService.cs             ← JSON load/save to BaseDirectory/settings.json
│   ├── TextInjector.cs                ← SaveFocus() + SendInput Unicode
│   └── WhisperService.cs              ← WhisperFactory, TranscribeAsync, CUDA
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
- Color palette: dark semi-transparent background `#CC1C1C1E`, accent `#8B8BFF`, red for recording `#FF3B30`.
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

### `Models/AppSettings.cs`
```csharp
public class AppSettings
{
    public string ModelPath { get; set; } = "models/ggml-large-v2.bin";
    public string Language  { get; set; } = "auto";   // "auto", "cs", "en", ...
    public string Prompt    { get; set; } = "";
    public int    HotkeyModifiers { get; set; } = 0x0002 | 0x0008; // Ctrl + Win
    public int    HistorySize     { get; set; } = 30;
    public double WindowLeft      { get; set; } = -1;  // -1 = default bottom-center
    public double WindowTop       { get; set; } = -1;
}

[Flags]
public enum HotkeyModifiers { None=0, Alt=1, Control=2, Shift=4, Win=8 }
```

### `Models/TranscriptionHistory.cs`
- `TranscriptionEntry`: `Timestamp`, `Text`, `Duration` (TimeSpan – transcription time).
- `TranscriptionHistory.Add()`: thread-safe, inserts at index 0 (newest on top), trims to `MaxSize`.

### `Services/LogService.cs`
- Static facade over `Serilog.Log`.
- `Initialize()` – configures rolling file sink to `logs/whisperwriter-YYYYMMDD.log` next to exe, 14-day retention.
- Methods: `Info(string)`, `Warning(string, Exception?)`, `Error(string, Exception?)`.
- `CloseAndFlush()` – called from `App.OnExit`.
- `LogDirectory` – public property with path to the log folder.
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
- `InjectText(string)` – `SetForegroundWindow` + 100 ms sleep + `SendInput` Unicode (each char as keydown+keyup with `KEYEVENTF_UNICODE`).

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
  - `EtaFactor = 0.35` (empirical coefficient for large-v2 + Quadro T2000 CUDA) – **may need calibration**.
  - `DispatcherTimer` 100 ms counts down and displays `~Xs` next to "Transcribing…".
- PTT flow: `SaveFocus` → `StartRecording` → (release) → `StopRecording` → `StartEtaCountdown` → `TranscribeAsync` → `StopEtaCountdown` → `InjectText`.

### `Views/HistoryWindow.xaml`
- `ListView` with `ObservableCollection` binding to `App.History.Entries`.
- `DataTemplate`: main text as **`TextBox` IsReadOnly** (mouse-selectable!), below it timestamp + duration.
- Footer: "Copy to clipboard" button (active only when an entry is selected).

### `Views/SettingsWindow.xaml`
- Form: `ModelPath` (ComboBox: 12 models), `Language` (ComboBox: 57 languages + `auto`), `Prompt` (multiline TextBox), hotkey info (read-only), `HistorySize` (numeric TextBox, min 1, max Int32.MaxValue).
- `CmbModelPath`: each item has `Tag` = file path, `Content` = two-line `StackPanel` (name bold + HW requirements and size in smaller text).
- Available models (largest to smallest): `large-v3-turbo`, `large-v3`, `large-v2`, `large-v1`, `medium`, `medium.en`, `small`, `small.en`, `base`, `base.en`, `tiny`, `tiny.en`.
- `CmbLanguage`: each item has `Tag` = ISO language code (e.g. `"cs"`) and `Content` = `"cs – Čeština"` (code + native name).
- `TxtHistorySize`: numeric TextBox, `PreviewTextInput` handler blocks non-numeric characters, fallback to `1` on save if value is invalid or < 1.
- `ShowDialog()` returns `true` on Save, `false` on Cancel/X.

### `Views/AboutWindow.xaml`
- Info window, `ScrollViewer` with `TextBlock` (inline `Run` elements).
- Draggable, closeable via the close button or "Close" in the footer.

### `WhisperWriter.csproj` – signing
- **Strong name**: `<SignAssembly>true</SignAssembly>` + `WhisperWriter.snk` (RSA key pair, no password).
- **Authenticode**: post-build target `AuthenticodeSigning` calls `signtool.exe` from Windows SDK `10.0.26100.0`.
  - Signs `$(OutputPath)WhisperWriter.exe` using `WhisperWriter.pfx` (password `1234`).
  - Timestamp server: `http://timestamp.digicert.com`, SHA256 algorithm.
  - `ContinueOnError="true"` – build does not fail if signtool is unavailable.
- **Certificate** (`WhisperWriter.pfx`): self-signed, CN=Tomáš Flídr, valid 10 years (until 2036), stored in `Cert:\CurrentUser\My`.
- Both files (`.pfx`, `.snk`) are in `.gitignore` – must not be committed.

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
| Medium | **EtaFactor calibration** – currently `0.35`, measure the real ratio `transcription_time / recording_length` and update in `MainWindow.xaml.cs:33` if needed. Ideally the factor would be computed adaptively from the last N transcriptions. |
| Medium | **HotkeyService – configurable keys** – VK codes `VK_LCONTROL` and `VK_LWIN` are currently hardcoded. The `AppSettings.HotkeyModifiers` bitmask is saved, but `IsComboHeld()` ignores the Alt and Shift branches. A VK lookup table for Alt (`VK_LMENU = 0xA4`) and Shift (`VK_LSHIFT = 0xA0`) needs to be added. |
| Low | **settings.json overwrite on build** – `PreserveNewest` copies the source `settings.json` to bin if it is newer, overwriting user settings. Consider `CopyToOutputDirectory=Never` and manual initialization on first run (SettingsService already handles this via `Save()` when the file does not exist). |
| Low | **Whisper model download fallback** – if the default model (`ggml-large-v2.bin`) is missing, `InitializeAsync` downloads `GgmlType.Medium` (not Large). A "Downloading model…" message is shown. |

### Resolved issues (for context)

- **Translation instead of transcription**: `WithTranslate()` must not be called. Without an explicit language, whisper.cpp may translate. Fixed: always use `WithLanguage(effectiveLanguage)`, `"auto"` → `"cs"`.
- **`WithGreedySamplingStrategy()` API**: this method returns a different interface (`IWhisperSamplingStrategyBuilder`) without `WithPrompt`/`Build`. Cannot be chained. Not used currently.
- **Ambiguous reference `System.Windows` vs `System.Windows.Forms`**: resolved with alias `using WpfApp = System.Windows.Application`.
- **"Failed to load the whisper model" error on PTT press**: `InitializeAsync` lacked try/catch, the exception from `WhisperFactory.FromPath` was silently lost, `_initialized` stayed `false`. Fixed: entire `InitializeAsync` wrapped in try/catch, error surfaced via `StateChanged(Error, "Model load failed: …")`. `RecDot` also correctly turns red on error state.
- **CRLF in C# files**: `LogService.cs`, `WhisperService.cs` and `AssemblyInfo.cs` had `\r\n` line endings. Fixed to `\n` via Node.js. `.editorconfig` extended with full C# K&R style rules (`csharp_new_line_before_open_brace = none` etc.). All project code reformatted to K&R style (opening `{` at end of line).
- **Wrong indentation of `case TranscriptionState.Error:` in MainWindow.xaml.cs**: one tab was missing. Fixed.

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
