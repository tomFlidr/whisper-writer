# WhisperWriter

![Release](https://img.shields.io/github/v/release/tomFlidr/whisper-writer)
![Build](https://img.shields.io/github/actions/workflow/status/tomFlidr/whisper-writer/.github/workflows/release.yml)
![Framework](https://img.shields.io/badge/.NET-8-blue)
![UI](https://img.shields.io/badge/UI-WPF-512BD4)
![OS](https://img.shields.io/badge/OS-Windows-0078D6)
![License](https://img.shields.io/github/license/tomFlidr/whisper-writer)

A minimalist Windows desktop app for **local, privacy-first push-to-talk voice transcription**. Inspired by Wispr Flow.

Hold a keyboard shortcut, speak, release — the transcribed text is instantly typed into whatever window you were using. Audio and transcripts stay local; model downloads are an intentional network action.

---

## Table of Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [ONNX Runtime provider packs](#onnx-runtime-provider-packs)
- [NVIDIA CUDA setup (temporary)](#nvidia-cuda-setup-temporary)
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

- 🎙️ **Push-to-talk** recording with a **configurable hotkey** — default is `Left Alt + Left Win`
- 🤖 **Local AI transcription** via [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp backend) — no cloud, no API key
- 🎧 **Overlapping recordings** — record another clip while earlier jobs are transcribed or wait in a FIFO queue
- ⚡ **Optional GPU acceleration** — Whisper.net tries its configured runtimes; after initialization the widget
  reports the runtime that actually loaded. CPU fallback remains available; CUDA/Vulkan require compatible
  x64 GPU, driver, and runtime packages.
- 🌍 **57 languages** supported, including auto-detection
- 🪟 **Floating pill widget** — stays on top, draggable, remembers position
- ⏱️ **Adaptive ETA countdown** — estimated from real previous transcription timings stored locally per model and runtime environment
- 📋 **Transcription history** — browse, select and copy past transcriptions
- 🔕 **System tray** — runs silently in the background
- 🔒 **Local, privacy-first inference** — audio and transcripts stay on-device; no cloud transcription service

---

## Requirements

| Component | Minimum |
|---|---|
| OS | Windows 11 or newer (64-bit) |
| Runtime | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) |
| RAM | 4 GB (8 GB+ recommended for large models) |
| Disk | 78 MB – 3 GB depending on the chosen model |
| GPU (optional) | Supported x64 GPU/driver/runtime for CUDA or Vulkan; `win-x86` uses the CPU runtime only |
| Microphone | Any Windows-recognized input device |

> **Without a supported GPU runtime** the app still works through the CPU runtime. Performance depends on
> the model and device; use a smaller model if CPU transcription is too slow.

---

## Installation

1. **Download** the latest release from the [Releases](https://github.com/tomFlidr/whisper-writer/releases) page.
2. **Extract** the ZIP archive to a folder of your choice (e.g. `C:\Apps\WhisperWriter`).
3. **Download a Whisper model** using the included script (see [Downloading Models](#downloading-models)) or manually (see [Whisper Models](#whisper-models)).
4. **Run** `WhisperWriter.exe`.

> If Windows shows a SmartScreen warning, click **More info → Run anyway**. The executable is signed with a self-signed certificate.

---

## ONNX Runtime provider packs

The x64 application package includes the CPU ONNX Runtime pack under `runtimes/onnx/cpu`.
The DirectML provider pack is a separate optional ZIP attached to the release. The ONNX CUDA
provider ZIP is intentionally not attached to v1.4.0; CI builds it as a short-lived artifact for
verification only.

These are provider libraries for the planned Parakeet ONNX engine, **not a separate plugin application**.
Whisper continues using Whisper.net and is unaffected. The Parakeet transcription pipeline is not yet
included; until it is added, this setting does not change Whisper transcription.

- `Auto` uses DirectML when its pack is present, otherwise CPU. CUDA is an explicit choice.
- The app loads only the selected provider pack into its process. Other pack DLLs remain on disk.
  Changing provider requires restarting the app.
- ONNX Runtime CUDA requires compatible CUDA 13 and cuDNN 9. Installing those runtime DLLs alone is
  insufficient: the ONNX CUDA provider pack and Parakeet transcription engine are also required. The
  app searches the configured runtime folder, its existing `runtimes/cuda/win-x64` folder (if present),
  and the current process `PATH`.
- The `win-x86` application remains CPU-only for Whisper and does not include ONNX provider packs.

---

## NVIDIA CUDA setup (temporary)

WhisperWriter has two separate CUDA paths. Follow the instructions for the engine you intend to use;
the Whisper.net and ONNX Runtime requirements are not interchangeable.

### CUDA for the current Whisper engine (Whisper.net)

1. Use the `win-x64` application ZIP. The `win-x86` package is CPU-only.
2. Install a supported NVIDIA driver for your GPU.
3. Install the [CUDA Toolkit](https://developer.nvidia.com/cuda-downloads) for Windows. The pinned
   `Whisper.net.Runtime.Cuda` 1.9.0 runtime documents CUDA Toolkit 13.0.1 or newer; see the
   [Whisper.net CUDA prerequisites](https://github.com/sandrohanea/whisper.net/blob/main/readme.md)
   and NVIDIA's [Windows installation guide](https://docs.nvidia.com/cuda/cuda-installation-guide-microsoft-windows/index.html).
4. Ensure the Toolkit's `bin` directory is on the Windows `PATH`, then start a new WhisperWriter
   process. Install the required Visual C++ 2022 x64 Redistributable from the upstream prerequisites
   above if it is missing.
5. Verify the driver with `nvidia-smi` and the Toolkit with `nvcc --version`.
6. Start WhisperWriter and wait for model initialization. `Ready (Cuda)` confirms CUDA actually
   loaded. `Ready (Vulkan)` or `Ready (Cpu)` means another runtime loaded instead; check the app log
   and verify the driver, Toolkit, and `PATH`.

Whisper.net selects its runtime automatically; there is no Whisper CUDA selector in Settings. The
`Parakeet provider` setting below does not affect Whisper. Whisper.net's documented CUDA prerequisites
list the CUDA Toolkit; the cuDNN requirement belongs to the separate ONNX Runtime path.

### CUDA for the future Parakeet ONNX engine

This is a different provider and does not accelerate current Whisper transcription. The v1.4.0 release
does not include its ONNX CUDA provider pack, and Parakeet audio-to-text is not implemented yet.
Installing CUDA/cuDNN alone therefore **cannot enable Parakeet inference in v1.4.0**.

When a Parakeet release includes the provider pack, the manual runtime setup will require compatible
CUDA 13.x and cuDNN 9.x. Use the [ONNX Runtime CUDA requirements](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html)
and the [NVIDIA cuDNN Windows installation guide](https://docs.nvidia.com/cudnn/installation/backend/windows/).
The app's **ONNX CUDA/cuDNN runtime directory** setting is only for this future Parakeet path.

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
5. Files are saved directly into the `llms\` folder next to the script.

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

1. The app checks for `llms\ggml-large-v2.bin` by default.
2. If the model file is **not found**, it automatically downloads `ggml-medium` (~1.5 GB) from the official Hugging Face repository. This may take a few minutes depending on your connection.
3. The app also creates a local SQLite database `llms\eta-time-stats.db` for ETA timing statistics.
4. The floating pill widget appears at the bottom-center of your screen with **"Loading model…"** while
   Whisper checks or downloads the model. It does not guess GPU support from CUDA DLL presence. After the
   model initializes, the ready status reports the runtime actually loaded when it is recognized.
5. Once it shows **"Ready"** (optionally with the loaded provider), you can start transcribing.

---

## How to Use

### Basic workflow

1. **Click into any text field** — a browser address bar, Notepad, Word, chat app, anywhere.
2. **Hold your push-to-talk hotkey** — by default `Left Alt + Left Win`. The widget turns red and shows "Recording…". Speak clearly into your microphone.
3. **Release the keys** — that clip starts transcribing or waits in the FIFO queue. You can begin
   another recording while earlier clips are processing; when a configured limit blocks it, the
   widget shows **Busy**.
4. The widget shows ETA estimates for the next result-ready transcriptions when matching timing data is
   available. With multiple jobs it shows up to three times in recording order, e.g.
   `~8s; ~17s; ~25s; +N`; active and queued counts appear separately.
5. The transcribed text is **automatically typed** into the window that had focus before you started recording.

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
| **Parakeet provider** | `Auto` / CPU / DirectML / CUDA for Parakeet only; no effect on Whisper. |
| **ONNX CUDA/cuDNN path** | Runtime folder for the future Parakeet ONNX CUDA provider; it does not change Whisper inference. |
| **Language** | Transcription language. `auto` attempts to detect the language automatically. Explicitly selecting a language is faster and more reliable. |
| **Prompt** | Optional hint text for Whisper. Use it to teach the model uncommon words, names or formatting. Example: `Whisper, GPT-4, OpenAI, CUDA`. |
| **Push-to-talk hotkey** | Configurable directly in Settings. Click **Change…**, hold the desired key combination, then release it to save. Default is `Left Alt + Left Win`. |
| **History size** | Maximum number of transcriptions kept in memory during a session (1 to 2,147,483,647). |
| **Maximum jobs** | Maximum active, waiting and currently-recording jobs. Default: 10. |
| **Audio budget (MiB)** | RAM threshold for audio batches. Default: 128 MiB. See the note below. |

Click **Save** to apply changes. The model is reloaded automatically if you change it.

The audio budget is a soft admission threshold: audio remains in RAM until its transcription completes,
and a recording already admitted is not cut off if it exceeds the threshold. A PTT attempt that cannot
be admitted is declined with **Busy**; try again when the indicator returns to blue. Accepted
recordings stay queued and are not discarded or repeated. If an admitted recording pushes the audio
buffer above the budget, the gray pause remains until all outstanding jobs drain; a pause caused only
by the job count clears when a slot opens. Low system RAM may reduce the effective budget; the audio
budget excludes the Whisper model and its inference memory. Finalizing a recording may briefly require
extra memory. The indicator returns to blue automatically when recording can resume.

---

## Whisper Models

Models must be placed in the `llms\` folder next to `WhisperWriter.exe`. File names must match exactly.

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

Download the `.bin` file for your chosen model and place it in the `llms\` folder.

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

## ETA Statistics Database

WhisperWriter stores ETA timing data in a local SQLite database:

- File: `llms\eta-time-stats.db`
- The database is used only to improve the ETA countdown.
- It stores timing samples per **model** and per detected **runtime environment**.
- The environment fingerprint keeps CPU/GPU inventory separate from the verified active engine/runtime/provider
  descriptor. CUDA major version is included only when the loaded runtime identifies it; thread count, AC/battery
  power, and power-saver state are stored independently.
- For a new ETA estimate, WhisperWriter prefers past transcriptions with a **similar audio length** (roughly ±30%, then ±50%, then fallback to all samples for the same model/environment).
- ETA is shown only after at least **one previous matching sample** exists, so it starts appearing from the **second matching transcription** onward.

---

## Privacy

- **No cloud transcription:** recorded audio and transcript text are processed locally and are not sent to
  a cloud transcription service.
- **ONNX Runtime telemetry is disabled** by setting `ORT_DISABLE_TELEMETRY=1` in the app process before
  loading an ORT provider.
- Internet is used only for model downloads and optional provider-pack downloads; inference itself is local.
- Audio is recorded **only while the hotkey is held** and is discarded immediately after transcription.

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

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (or any newer .NET SDK — 9, 10, … all work)
- Windows 11 or newer
- Visual Studio 2022 (optional, for IDE support)
- CUDA Toolkit 11, 12 or 13 (optional, for GPU acceleration — not needed to compile)

### Quick setup (recommended)

After cloning, double-click **`WhisperWriter\setup-dev.bat`** in the app project folder.

The script performs all first-time setup steps automatically:

1. Verifies a compatible .NET SDK (>= 8) is installed.
2. Detects the locally installed CUDA Toolkit (versions 11, 12, 13) and extracts the runtime version.
3. Runs `dotnet restore`.
4. Copies the CUDA runtime DLLs (`cudart64_N.dll`, `cublas64_N.dll`, `cublasLt64_N.dll`) to the application output under `WhisperWriter\bin\Debug\net8.0-windows7.0\runtimes\cuda\win-x64`.
5. If the detected CUDA path differs from the one stored in `WhisperWriter\WhisperWriter.csproj`, offers to update it.
6. Checks for a Whisper model in `WhisperWriter\llms\` and offers to launch `WhisperWriter/download-models.ps1` if none is found.

If CUDA is not installed the script skips steps 4–5 and continues — the app will run on CPU.

### Manual steps

```powershell
# Clone the repository
git clone https://github.com/tomFlidr/whisper-writer.git
cd whisper-writer

# One-time setup (NuGet restore, CUDA DLL copy, model download prompt)
.\WhisperWriter\setup-dev.bat

# Restore and build the app plus the isolated ORT provider projects
dotnet restore WhisperWriter.sln
dotnet build WhisperWriter.sln -c Debug

# Run
.\WhisperWriter\bin\Debug\net8.0-windows7.0\WhisperWriter.exe
```

> Before building, make sure `WhisperWriter.exe` is not running — the output file will be locked by the running process.

### CUDA runtime packaging (x64)

`CopyCudaRuntimeDllsToBuild` and `CopyCudaRuntimeDllsToPublish` copy the configured CUDA runtime DLLs
under `runtimes/cuda/win-x64`. They derive the CUDA major version from `CudaBinDir`; they are loader
packaging steps, not GPU-support detection. `WhisperWriter/setup-dev.ps1` can update `CudaBinDir` to the
locally installed toolkit. The `win-x86` release excludes CUDA and Vulkan packages and remains CPU-only.

ONNX Runtime packages are in separate `WhisperWriter.OrtRuntime.*` provider projects, not the main WPF
project. Build `WhisperWriter.sln` to compile them. The release workflow stages the CPU provider pack
inside the x64 app ZIP and publishes the DirectML pack as a separate optional ZIP. The CUDA provider
pack is built as a short-lived CI artifact but is not attached to the v1.4.0 release. It omits CUDA/cuDNN
runtime DLLs and Parakeet transcription is not implemented; see the temporary CUDA guide above.

### Key dependencies

| Package | Version | Purpose |
|---|---|---|
| Whisper.net | 1.9.0 | Whisper model inference |
| Whisper.net.Runtime | 1.9.0 | Native whisper.cpp runtime |
| Whisper.net.Runtime.Cuda | 1.9.0 | CUDA GPU acceleration |
| Whisper.net.Runtime.Vulkan | 1.9.0 | Vulkan GPU runtime (x64) |
| Microsoft.ML.OnnxRuntime (CPU provider project) | 1.31.1 | Parakeet CPU runtime pack |
| Microsoft.ML.OnnxRuntime.DirectML | 1.24.4 | Optional Parakeet DirectML provider pack |
| Microsoft.ML.OnnxRuntime.Gpu.Windows | 1.31.1 | Optional Parakeet CUDA provider pack |
| NAudio | 2.2.1 | Microphone capture |
| Microsoft.Data.Sqlite | 8.0.0 | Local ETA timing statistics database |
| System.Management | 8.0.0 | Windows hardware discovery for ETA environment fingerprinting |
| Serilog | 4.3.0 | Logging |
| System.Text.Json | 8.0.5 | Settings serialization |

---

## Creating a Release

Releases are built and published automatically via GitHub Actions. No manual steps are needed.

### How to publish a new release

```powershell
# Tag the current commit and push the tag
git tag v1.2.3
git push origin v1.2.3
```

For the approved v1.4.0 release, GitHub Actions will automatically:
1. Build the app for **Windows x64** and **Windows x86**. The x64 ZIP includes the Parakeet CPU ORT pack;
   x86 remains CPU-only for Whisper and contains no ONNX provider pack.
2. Build the optional DirectML ZIP for release. The CUDA provider pack is built for CI verification but
   is not attached to v1.4.0; CUDA/cuDNN runtime provisioning is still pending.
3. Create a **GitHub Release** with auto-generated release notes and attach the two app ZIPs and the
   DirectML provider ZIP.

| Archive | Contents |
|---|---|
| `WhisperWriter-v1.x.x-win-x64.zip` | x64 app with Parakeet CPU pack; optional Whisper GPU runtimes |
| `WhisperWriter-v1.x.x-win-x86.zip` | 32-bit app, CPU-only Whisper; no ONNX provider pack |
| `WhisperWriter-ORT-directml-1.24.4-win-x64.zip` | Optional DirectML pack; extract into the x64 app folder |
| ONNX CUDA provider pack | Not attached to v1.4.0; see the temporary CUDA guide and Phase 3.4 status |

> **Pre-releases**: if the tag name contains a hyphen (e.g. `v1.0.0-beta`), the GitHub Release is automatically marked as a pre-release.

> **Signing**: Authenticode signing and strong-name signing are skipped in CI (`.pfx` / `.snk` are not committed to the repository). Release builds are unsigned; local Debug builds are signed if the key files are present.

---

## For Contributors & AI Assistants

> **After every successful build that changes user-facing behavior, update this README** if the changes affect installation steps, settings, model list, hotkeys, UI navigation or any other section described here.

The internal developer/AI context is maintained separately in [`AGENTS.md`](AGENTS.md).
