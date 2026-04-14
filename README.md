# WhisperWriter

A minimalist Windows desktop app for **local, privacy-first push-to-talk voice transcription**. Inspired by Wispr Flow.

Hold a keyboard shortcut, speak, release — the transcribed text is instantly typed into whatever window you were using. Nothing ever leaves your computer.

---

## Table of Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Downloading Models](#downloading-models)
- [First Run](#first-run)
- [How to Use](#how-to-use)
- [Settings](#settings)
- [Whisper Models](#whisper-models)
- [Transcription History](#transcription-history)
- [Tray Icon & Navigation](#tray-icon--navigation)
- [Logs](#logs)
- [Privacy](#privacy)
- [Known Limitations](#known-limitations)
- [Building from Source](#building-from-source)

---

## Features

- 🎙️ **Push-to-talk** recording with `Left Ctrl + Left Win` (hold to record, release to transcribe)
- 🤖 **Local AI transcription** via [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp backend) — no cloud, no API key
- ⚡ **CUDA GPU acceleration** — automatic if an NVIDIA GPU is present
- 🌍 **57 languages** supported, including auto-detection
- 🪟 **Floating pill widget** — stays on top, draggable, remembers position
- 📋 **Transcription history** — browse, select and copy past transcriptions
- 🔕 **System tray** — runs silently in the background
- 🔒 **100% offline** — your voice data never leaves the machine

---

## Requirements

| Component | Minimum |
|---|---|
| OS | Windows 10 or Windows 11 (64-bit) |
| Runtime | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) |
| RAM | 4 GB (8 GB+ recommended for large models) |
| Disk | 78 MB – 3 GB depending on the chosen model |
| GPU (optional) | NVIDIA GPU with CUDA support (strongly recommended for large/medium models) |
| Microphone | Any Windows-recognized input device |

> **Without a CUDA-capable GPU** the app still works, but transcription will be significantly slower on large models. Use `small` or `tiny` models for CPU-only usage.

---

## Installation

1. **Download** the latest release from the [Releases](https://github.com/tomFlidr/whisper-writer/releases) page.
2. **Extract** the ZIP archive to a folder of your choice (e.g. `C:\Apps\WhisperWriter`).
3. **Download a Whisper model** using the included script (see [Downloading Models](#downloading-models)) or manually (see [Whisper Models](#whisper-models)).
4. **Run** `WhisperWriter.exe`.

> If Windows shows a SmartScreen warning, click **More info → Run anyway**. The executable is signed with a self-signed certificate.

---

## Downloading Models

The easiest way to get Whisper models is the included download script.

### Option A – double-click (recommended)

Simply double-click **`download-models.bat`** in the WhisperWriter folder. No setup required — it launches PowerShell automatically with the correct settings.

### Option B – run from PowerShell directly

```powershell
# Run from the WhisperWriter folder
.\download-models.ps1
```

> **If you get an "execution policy" error** in PowerShell or PowerShell ISE, use Option A (the `.bat` file) instead — it bypasses the policy automatically. Alternatively, run this once to allow local scripts permanently:
> ```powershell
> Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
> ```

### What the script does

1. Shows a numbered list of all available models with disk size, VRAM requirements and a short description.
2. Already-downloaded models are marked **[downloaded]** in green.
3. You type the numbers of the models you want (comma-separated, spaces, or ranges like `1-3,7`).
4. After confirming, missing models are downloaded one by one with a live progress indicator.
5. Files are saved directly into the `models\` folder next to the script.

**Example session:**

```
  #    Model                Disk       VRAM       Notes
  ---  -------------------  --------   --------   -----
  1    large-v3-turbo       1.6 GB     ~3 GB      Best speed/accuracy tradeoff
  2    large-v3             3.1 GB     ~10 GB     Most accurate, latest generation
  ...
  5    medium               1.5 GB     ~5 GB      Good balance, multilingual [downloaded]

  Your selection: 1,5
```

---

## First Run

On the very first launch:

1. The app checks for `models\ggml-large-v2.bin` by default.
2. If the model file is **not found**, it automatically downloads `ggml-medium` (~1.5 GB) from the official Hugging Face repository. This may take a few minutes depending on your connection.
3. The floating pill widget appears at the bottom-center of your screen showing **"Loading…"** while the model is being loaded into memory (or downloaded).
4. Once it shows **"Ready"**, you can start transcribing.

---

## How to Use

### Basic workflow

1. **Click into any text field** — a browser address bar, Notepad, Word, chat app, anywhere.
2. **Hold `Left Ctrl + Left Win`** — the widget turns red and shows "Recording…". Speak clearly into your microphone.
3. **Release both keys** — the widget shows "Transcribing… ~Xs" with a countdown.
4. The transcribed text is **automatically typed** into the window that had focus before you started recording.

> ⚠️ Make sure to click into the target text field *before* pressing the hotkey. The app saves the focus at the moment you press the keys.

### Tips

- Speak at a **natural pace** — Whisper handles natural speech well.
- Use the **Prompt** setting to pre-seed Whisper with domain-specific vocabulary (names, technical terms, abbreviations) for better accuracy.
- For **Czech or other non-English** languages, explicitly set the language instead of using auto-detect — it improves both speed and accuracy.
- The widget is **draggable** — click and drag it anywhere on screen. Its position is saved automatically.

---

## Settings

Open Settings via the **tray icon → Settings**.

| Setting | Description |
|---|---|
| **Model** | Which Whisper model to use. Affects accuracy, speed and VRAM usage. See [Whisper Models](#whisper-models). |
| **Language** | Transcription language. `auto` attempts to detect the language automatically. Explicitly selecting a language is faster and more reliable. |
| **Prompt** | Optional hint text for Whisper. Use it to teach the model uncommon words, names or formatting. Example: `Whisper, GPT-4, OpenAI, CUDA`. |
| **Push-to-talk hotkey** | Currently fixed to `Left Ctrl + Left Win` (display only, not configurable via UI yet). |
| **History size** | Maximum number of transcriptions kept in memory during a session (1 to 2,147,483,647). |

Click **Save** to apply changes. The model is reloaded automatically if you change it.

---

## Whisper Models

Models must be placed in the `models\` folder next to `WhisperWriter.exe`. File names must match exactly.

| Model | File name | Disk | VRAM | Notes |
|---|---|---|---|---|
| large-v3-turbo | `ggml-large-v3-turbo.bin` | 1.6 GB | ~3 GB | **Best speed/accuracy tradeoff** |
| large-v3 | `ggml-large-v3.bin` | 3 GB | ~10 GB | Most accurate, latest generation |
| large-v2 | `ggml-large-v2.bin` | 3 GB | ~10 GB | Most accurate, recommended (default) |
| large-v1 | `ggml-large-v1.bin` | 3 GB | ~10 GB | Accurate, older generation |
| medium | `ggml-medium.bin` | 1.5 GB | ~5 GB | Good balance, multilingual |
| medium.en | `ggml-medium.en.bin` | 1.5 GB | ~5 GB | Good balance, English only |
| small | `ggml-small.bin` | 488 MB | ~2 GB | Fast, multilingual |
| small.en | `ggml-small.en.bin` | 488 MB | ~2 GB | Fast, English only |
| base | `ggml-base.bin` | 148 MB | ~1 GB | Very fast, multilingual |
| base.en | `ggml-base.en.bin` | 148 MB | ~1 GB | Very fast, English only |
| tiny | `ggml-tiny.bin` | 78 MB | ~390 MB | Fastest, multilingual, least accurate |
| tiny.en | `ggml-tiny.en.bin` | 78 MB | ~390 MB | Fastest, English only, least accurate |

### Where to download models

**Option A – download script (recommended):**

Use the included `download-models.ps1` script — see [Downloading Models](#downloading-models).

**Option B – manual download:**

Download GGML model files from the official Whisper.net / whisper.cpp model repository:

👉 **https://huggingface.co/ggerganov/whisper.cpp**

Download the `.bin` file for your chosen model and place it in the `models\` folder.

### Which model should I choose?

- **NVIDIA GPU with 4+ GB VRAM** → `large-v3-turbo` (best balance) or `large-v2` (highest accuracy)
- **NVIDIA GPU with 2–4 GB VRAM** → `medium` or `small`
- **CPU only or weak GPU** → `small`, `base` or `tiny`
- **English only** → prefer `.en` variants (slightly faster and more accurate for English)

---

## Transcription History

Access via **tray icon → Transcriptions**.

- Lists all transcriptions from the current session, newest first.
- Each entry shows the **transcribed text**, **timestamp** and **transcription duration**.
- The text in each entry is **selectable** — click and drag to select, then use `Ctrl+C` or the **Copy to clipboard** button at the bottom.
- History is kept **in memory only** and is cleared when the app exits.
- The maximum number of stored entries is controlled by the **History size** setting.

---

## Tray Icon & Navigation

WhisperWriter lives in the **system tray** (notification area, bottom-right of taskbar).

| Action | Result |
|---|---|
| **Left double-click** on tray icon | Show / bring the floating widget to front |
| **Right-click** on tray icon | Open context menu |
| Context menu → **About WhisperWriter** | Show the About window |
| Context menu → **Transcriptions** | Open transcription history |
| Context menu → **Settings** | Open settings |
| Context menu → **Exit** | Quit the application |

> Closing the floating widget does **not** exit the app — it continues running in the tray.

---

## Logs

Application logs are written to the `logs\` folder next to `WhisperWriter.exe`.

- File pattern: `logs\whisperwriter-YYYYMMDD.log`
- Logs are retained for **14 days**, then automatically deleted.
- All errors, warnings and key events (model loading, transcription start/end, exceptions) are logged.

If the app behaves unexpectedly, check the latest log file for details.

---

## Privacy

- **No internet connection required** after the model is downloaded (first run only).
- **No telemetry, no analytics, no tracking** of any kind.
- Audio is recorded **only while the hotkey is held** and is discarded immediately after transcription.
- Audio data **never leaves your computer** — transcription runs entirely locally using whisper.cpp.

---

## Known Limitations

- **Hotkey is fixed** to `Left Ctrl + Left Win` and cannot be changed through the UI yet.
- **Only one microphone** is supported (Windows default input device). Device selection is not available yet.
- **History is session-only** — transcriptions are not persisted to disk.
- The **ETA countdown** during transcription is an estimate based on a fixed factor calibrated for NVIDIA Quadro T2000 + large-v2 model. It may be inaccurate on different hardware.
- On very short recordings (under ~1 second) Whisper may produce empty or noisy output.

---

## Building from Source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Windows 10/11
- Visual Studio 2022 (optional, for IDE support)
- CUDA Toolkit (optional, for GPU acceleration at runtime — not needed for build)

### Steps

```powershell
# Clone the repository
git clone https://github.com/tomFlidr/whisper-writer.git
cd whisper-writer

# Download a Whisper model — double-click download-models.bat, or run:
# .\download-models.ps1  (if execution policy allows it)

# Restore NuGet packages
dotnet restore

# Build
dotnet build WhisperWriter.csproj -c Release

# Run
.\bin\Release\net8.0-windows\WhisperWriter.exe
```

> Before building, make sure `WhisperWriter.exe` is not running — the output file will be locked by the running process.

### Key dependencies

| Package | Version | Purpose |
|---|---|---|
| Whisper.net | 1.7.2 | Whisper model inference |
| Whisper.net.Runtime | 1.7.2 | Native whisper.cpp runtime |
| Whisper.net.Runtime.Cuda | 1.7.2 | CUDA GPU acceleration |
| NAudio | 2.2.1 | Microphone capture |
| Serilog | 4.3.0 | Logging |
| System.Text.Json | 8.0.5 | Settings serialization |

---

## For Contributors & AI Assistants

> **After every successful build that changes user-facing behavior, update this README** if the changes affect installation steps, settings, model list, hotkeys, UI navigation or any other section described here.

The internal developer/AI context is maintained separately in [`.github/copilot-instructions.md`](.github/copilot-instructions.md).
