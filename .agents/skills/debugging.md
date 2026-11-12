# Skill: Debugging

Reference for diagnosing unexpected behaviour in WhisperWriter.

Before changing production code, follow `.agents/skills/testing.md` and add a regression test
that reproduces the bug. Verify that it fails for the expected reason before applying a fix.
If the failure depends on hardware or Windows UI state, isolate that boundary behind a testable
seam; document any check that must remain an opt-in smoke test.

Production source paths are relative to `WhisperWriter/`; build and output paths below are
relative to the repository root unless an absolute path is shown.

---

## 1. Logs

Logs are written only in **Debug builds**. In Release builds, `Log` is a no-op.

**Log location:**
```
WhisperWriter\bin\Debug\net8.0-windows7.0\logs\
```

Files are named `whisperwriter-YYYYMMDD.log`, rolling daily, retained for 14 days.

**Log levels used in the codebase:**

| Method | When |
|---|---|
| `Log.Info(string)` | Normal lifecycle events (model loaded, transcription started) |
| `Log.Warning(string, ex?)` | Recoverable issues (missing file, fallback used) |
| `Log.Error(string, ex)` | Failures that affect functionality |
| `Log.Transcription(string, TimeSpan)` | Completed transcription text + duration |

---

## 2. Run a debug build

```powershell
dotnet build "WhisperWriter/WhisperWriter.csproj" -c Debug --no-restore
Start-Process ".\WhisperWriter\bin\Debug\net8.0-windows7.0\WhisperWriter.exe"
```

> The app must be closed before rebuilding – the exe is locked by the running process.

---

## 3. Common failure areas

### Text not injected / mouse broken after PTT
- Root cause: PTT keys still physically held when `SendInput` runs, or Win key shell hook active.
- Check `TextInjector.WaitForPhysicalRelease()` and `ReleaseModifierKeys()` in
  `WhisperWriter/Services/TextOutput/TextInjector.cs`.
- Key gotcha: `INPUT` struct union field must be at `[FieldOffset(8)]` on 64-bit. Wrong offset = silent failure.
- Key gotcha: Win key keyup requires `KEYEVENTF_EXTENDEDKEY` flag, otherwise the shell hook stays active.

### Transcription never starts / stays on "Loading…"
- Check: model file exists and is > 70 MB (`MinModelFileSizeBytes` in `WhisperWriter/Services/Whisper.cs`).
- Check: `Whisper.StateChanged` is wired in `WhisperWriter/Views/Screens/MainWindow.xaml.cs`.
- Check: CUDA DLLs are present in `WhisperWriter\bin\Debug\net8.0-windows7.0\runtimes\cuda\win-x64\`.

### CUDA is not the active Whisper runtime
- Check the verified runtime after model initialization through `Whisper.ActiveBackend` or
  `RuntimeOptions.LoadedLibrary`; DLL presence alone does not prove CUDA loaded.
- Confirm runtime DLLs are available in the app-local CUDA folder or configured native search path.
- Run `WhisperWriter/setup-dev.ps1` to copy installed CUDA DLLs to the app runtime folder, then restart.

### ETA never appears
- ETA is shown only from the **second** matching transcription onward (same model + same environment).
- Database: `WhisperWriter\bin\Debug\net8.0-windows7.0\llms\etaCalc-time-stats.db` – inspect `Stats` rows with a SQLite viewer.
- Environment fingerprint is in `Environments.value` (JSON) – check if hardware/power state changed.

### Window off-screen after monitor change
- `WhisperWriter/Services/MainWindows/Positions/Position.cs` handles position clamping;
  `WhisperWriter/Services/MainWindows/Styles/Style.cs` forwards display-change events.
- If the window is invisible: delete `WindowLeft` and `WindowBottom` from `settings.json` (set to `-1`) to reset to default position.

### Settings not saved
- Settings file: `WhisperWriter\bin\Debug\net8.0-windows7.0\settings.json`.
- On deserialization error, `Settings` returns default `AppSettings` – check for malformed JSON.

---

## 4. Global exception handlers

Both are in `WhisperWriter/App.xaml.cs`:
- `DispatcherUnhandledException` – catches unhandled exceptions on the UI thread.
- `AppDomain.CurrentDomain.UnhandledException` – catches unhandled exceptions on background threads.

Both log via `Log.Error` – check the log file first when the app crashes silently.