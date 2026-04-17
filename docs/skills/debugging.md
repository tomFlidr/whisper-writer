# Skill: Debugging

Reference for diagnosing unexpected behaviour in WhisperWriter.

---

## 1. Logs

Logs are written only in **Debug builds**. In Release builds, `LogService` is a no-op.

**Log location:**
```
D:\tec\cs\whisper-writer\bin\Debug\net8.0-windows\logs\
```

Files are named `whisperwriter-YYYYMMDD.log`, rolling daily, retained for 14 days.

**Log levels used in the codebase:**

| Method | When |
|---|---|
| `LogService.Info(string)` | Normal lifecycle events (model loaded, transcription started) |
| `LogService.Warning(string, ex?)` | Recoverable issues (missing file, fallback used) |
| `LogService.Error(string, ex)` | Failures that affect functionality |
| `LogService.Transcription(string, TimeSpan)` | Completed transcription text + duration |

---

## 2. Run a debug build

```powershell
dotnet build "D:\tec\cs\whisper-writer\WhisperWriter.csproj" -c Debug --no-restore
Start-Process "D:\tec\cs\whisper-writer\bin\Debug\net8.0-windows\WhisperWriter.exe"
```

> The app must be closed before rebuilding – the exe is locked by the running process.

---

## 3. Common failure areas

### Text not injected / mouse broken after PTT
- Root cause: PTT keys still physically held when `SendInput` runs, or Win key shell hook active.
- Check: `TextInjector.WaitForPhysicalRelease()` and `ReleaseModifierKeys()` in `Services/TextInjector.cs`.
- Key gotcha: `INPUT` struct union field must be at `[FieldOffset(8)]` on 64-bit. Wrong offset = silent failure.
- Key gotcha: Win key keyup requires `KEYEVENTF_EXTENDEDKEY` flag, otherwise the shell hook stays active.

### Transcription never starts / stays on "Loading…"
- Check: model file exists and is > 70 MB (`MinModelFileSizeBytes` in `WhisperService.cs`).
- Check: `WhisperService.StateChanged` event is wired in `MainWindow.xaml.cs`.
- Check: CUDA DLLs present in `bin\Debug\net8.0-windows\runtimes\cuda\win-x64\`.

### CUDA not detected
- Check: `WhisperService.DetectCudaVersion()` probes `cudart64_N.dll` + `cublas64_N.dll` for major versions 13, 12, 11.
- Run `setup-dev.ps1` to copy CUDA DLLs to the correct location.

### ETA never appears
- ETA is shown only from the **second** matching transcription onward (same model + same environment).
- Database: `models\eta-time-stats.db` – open with any SQLite viewer to inspect `Stats` rows.
- Environment fingerprint is in `Environments.value` (JSON) – check if hardware/power state changed.

### Window off-screen after monitor change
- `ClampWindowToScreen()` in `MainWindow.xaml.cs` handles `WM_DISPLAYCHANGE`.
- If the window is invisible: delete `WindowLeft` and `WindowBottom` from `settings.json` (set to `-1`) to reset to default position.

### Settings not saved
- Settings file: `bin\Debug\net8.0-windows\settings.json`.
- On deserialization error, `SettingsService` silently returns `new AppSettings()` – check for a malformed JSON file.

---

## 4. Global exception handlers

Both are in `App.xaml.cs`:
- `DispatcherUnhandledException` – catches unhandled exceptions on the UI thread.
- `AppDomain.CurrentDomain.UnhandledException` – catches unhandled exceptions on background threads.

Both log via `LogService.Error` – check the log file first when the app crashes silently.