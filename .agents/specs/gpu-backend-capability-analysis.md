# GPU inference and model/backend capability analysis

**Status:** Architecture analysis only; no implementation or roadmap decision is made here.
**Scope:** GPU acceleration, model-specific backend support, Settings UI, future Vulkan/ONNX providers,
and the concurrency requirements shared with the planned parallel-transcription phase.

## Conclusion

The Settings dialog should expose acceleration choices, but not as a universal **GPU on/off** checkbox.
The application needs to distinguish three things:

1. **Model/engine:** Whisper GGML, Parakeet, or another model family and its required format/features.
2. **Execution backend:** Whisper.net CUDA/Vulkan, an ONNX Runtime execution provider, Const-me Whisper,
   or another engine-specific implementation.
3. **Physical device:** the GPU or CPU device that the selected runtime can actually use.

The recommended default is **Automatic**. Available choices should be computed for the selected model
and this machine, with the reason for any unavailable option. Persist the user's preference, not a
cached list of detected GPUs or providers. After initialization, report the backend that really loaded;
DLL presence or a GPU model name alone is not proof that inference is running on that GPU.

## Current implementation baseline

- `WhisperWriter.csproj` pins `Whisper.net`, `Whisper.net.Runtime`, and
  `Whisper.net.Runtime.Cuda` to 1.9.0. The current upstream release list includes 1.9.1, while its
  README documents Vulkan and a separate Parakeet runtime family. Check package/API availability
  against the project's pinned version before adopting those options.[1][8]
- In `WhisperWriter/Services/Whisper.cs`, `Whisper.DetectCudaVersion()` only attempts to load CUDA
  runtime DLLs. `Whisper.InitializeAsync()`
  logs that GPU will be used when those DLLs are found, then calls `WhisperFactory.FromPath()` without
  an explicit backend preference. `MainWindow` also derives its `GPU`/`CPU` status from that CUDA probe,
  not from a confirmed active runtime or selected device.
- `WhisperWriter/Models/AppSettings.cs` has a model path but no backend preference.
  `WhisperWriter/Views/SettingsWindow.xaml.cs` contains a hard-coded list
  of 12 model files and fixed disk/VRAM/CUDA descriptions; those values cannot describe the provider,
  quantization, available device, or concurrent memory use for a particular installation.
- `WhisperWriter/Services/ITranscription.cs` defines `ITranscription.InitializeAsync(string modelPath)`,
  which receives only a path. The `IWhisperRuntime`/`IWhisperFactory` seam in
  `WhisperWriter/Services/WhisperBackend.cs` is useful for tests, but it does not expose model identity,
  backend options, capability results, or the active runtime.
- `WhisperNetRuntime.DownloadModelAsync()` in `WhisperWriter/Services/WhisperBackend.cs` currently
  downloads `GgmlType.Medium` with no quantization
  for every missing model path. A path such as `ggml-large-v3.bin` therefore does not tell the downloader
  which model to fetch. A model catalog must carry the exact download asset/format instead of inferring
  identity from the filename.
- The `Whisper` transcription service is a singleton with one shared factory. A processor is constructed for each
  call, but the code has no explicit concurrency contract or per-request state identifier. The settings
  The settings dialog persists a changed model path; `WhisperWriter/Services/TrayMenu.cs` saves settings
  and refreshes history/hotkey, while `WhisperWriter/App.xaml.cs` initializes Whisper at startup.
  Applying a new model/backend therefore needs an explicit restart or
  reinitialization policy.

## Recommended architecture

Keep model facts, provider facts, hardware discovery, and the user's preference separate. A resolver
should produce the options shown by the UI and the exact backend configuration passed to the service.

### 1. Model catalog

Replace the UI-owned `_models` array with model descriptors containing at least:

- stable model ID, display name, engine/family, file format, quantization, and expected local path;
- exact download source/type and expected files, including directory-based model packages;
- model features such as language selection, prompts, translation, and VAD;
- known platform/runtime constraints and advisory storage/memory estimates.

Do not make a model descriptor claim that a GPU is usable. Compatibility depends on the provider and
runtime discovered on the current machine. Treat memory estimates as advisory and provider-specific,
not as a guarantee based on one static VRAM number.

### 2. Backend registry and capability probes

Give each backend a stable ID and a provider-specific implementation for:

- supported engine/model formats and platform/process architecture;
- runtime/package/driver prerequisites and a meaningful availability probe;
- device enumeration or device selection, if that provider actually supports it;
- compatible model/operator constraints and the reason for an incompatibility;
- runtime initialization, fallback behavior, and the active backend/device information to report;
- concurrency/session constraints and approximate per-session memory cost.

A probe result should distinguish at least **available**, **runtime missing**, **device unavailable**,
**unsupported model**, **insufficient memory estimate**, and **probe/initialization failed**. The selected
model and detected GPU name are only inputs; the resolver must also consider runtime version, model
format/quantization, provider support, and any relevant driver/architecture constraint.

For ONNX Runtime, provider support is not necessarily all-or-nothing: an execution provider can claim
supported graph subgraphs while other nodes remain on another provider. Therefore a provider listed as
available must not automatically be described as accelerating every operation in the model.[2]

### 3. Resolver and engine factory

A `BackendCapabilityResolver` should combine the model descriptor, provider probes, platform, and saved
preference into filtered UI options with an explanatory status. A transcription-engine factory should
then create the right engine/runtime from the resolved selection. Initialization should return an
`ActiveBackendInfo` record (engine, runtime/provider, device, runtime version, and whether fallback was
used) so the main window reports what is actually running.

Prefer ordered runtime selection or a validated backend preference over blindly forcing a native
provider. Current Whisper.net upstream source exposes runtime ordering and a forced-runtime option; the
forced option bypasses compatibility checks and can cause failures in native code. Verify that behavior
against 1.9.0 before using it; do not make an unchecked force option the normal UI path.[7]

## Settings dialog proposal

Add an **Acceleration** section next to the selected model/engine:

- **Backend:** `Automatic (recommended)` plus only the backends compatible with this model and detected
  runtime. Show unavailable choices disabled with a short reason rather than presenting an unexplained
  global list.
- **Device:** show a GPU/device picker only when the selected provider exposes a stable device-selection
  API and more than one suitable device exists. Backend choice and physical-device choice are separate.
- **Status:** show the active provider/device after initialization, plus a visible fallback warning if
  the requested preference could not be honored.
- **Model-specific controls:** enable, hide, or explain unsupported language/prompt/translation controls
  based on the model engine's feature descriptor.

Persist a stable preference such as `auto` or a provider ID, initially defaulting to `auto`; do not
persist the detected hardware list. Prefer one preference per engine family (for example Whisper vs.
Parakeet/ONNX), with a per-model override only if actual model combinations require it. In `Auto`,
the application may fall back automatically. If the user selected a specific provider, do not silently
claim it is active when another provider was selected; show the actual provider and the fallback reason.

Changing a model or backend must either recreate the inference service safely or clearly require a
restart. For the first implementation, a restart-required setting is simpler and matches the current
startup initialization behavior. Live switching can be added later when per-job runtime ownership is
implemented for parallel transcriptions.

## Backend/model compatibility observations

- **Whisper GGML:** The current application uses Whisper.net CPU/CUDA packages. Current upstream
  documentation also lists a Vulkan runtime; this is a Whisper.net-specific runtime option, not a
  universal Vulkan switch for every engine.[1]
- **Parakeet:** Current Whisper.net documentation lists a separate Parakeet runtime family, including
  CPU, CUDA, and Vulkan variants. It also documents feature gaps including language detection, prompt,
  translation, and VAD support. Before building the roadmap's custom ONNX service, compare this option
  with direct ONNX Runtime on the exact model, package version, features, performance, and licensing.[1]
- **ONNX Runtime on Windows:** Revisit the roadmap's DirectML-first direction before implementation.
  Microsoft describes DirectML as being in sustained engineering and recommends WinML for new Windows
  applications; provider choice and availability remain specific to the model/runtime.[3][4]
- **ONNX Runtime WebGPU:** This is a separate candidate provider, not the same backend as Whisper.net
  Vulkan. The current ONNX Runtime documentation describes its Windows WebGPU path as D3D12-backed, so
  the label shown to users should describe the actual provider/API rather than imply Vulkan.[5]
- **Const-me Whisper:** Treat this as another engine adapter with its own model compatibility and
  runtime lifecycle. Its published performance example is not a same-build, same-input comparison
  against the current WhisperWriter path, so it cannot establish the project's ~3× target.[6]

## Parallel transcription constraints

The parallel-hotkey requirement rules out silently placing recordings in a FIFO work queue. Each
released recording must become an independent request with immutable audio, its own result/error/state,
and its own context for inserting the result. A single global `TranscriptionState` event is not enough
once requests overlap; events need a request/job ID so one job cannot overwrite another job's UI state.

Backend metadata must state whether concurrent calls on one session are supported, whether separate
sessions are required, and the memory cost/limit. In particular, ONNX Runtime's DirectML documentation
requires session configuration constraints and does not permit concurrent `Run` calls on the same
session; separate sessions and their memory cost must be evaluated rather than assuming one shared
session can serve parallel jobs.[4] Any hard concurrency limit must be explicit: it is a resource limit,
not permission to queue jobs. If capacity is exceeded, define a visible failure or a verified fallback
rather than silently changing the user's no-queue behavior.

## Suggested implementation sequence

1. **Backend spike and benchmark:** compare the current Whisper.net path, the candidate GPU engine, and
   any Vulkan/ONNX candidate on the same machine, model, audio samples, and settings. Measure cold and
   warm latency, real-time factor, quality, RAM/VRAM, startup/package size, fallback behavior, and at
   least two simultaneous requests. Keep ~3× as an acceptance target to measure, not a promise. The
   Const-me example is useful as a candidate signal, not as the project's baseline.[6]
2. **Model catalog and downloads:** move model metadata out of `SettingsWindow`; fix model-specific
   download mapping before adding additional model families or formats.
3. **Capability resolver and runtime identity:** add provider probes, compatibility reasons, and actual
   active-backend reporting; avoid treating DLL presence as proof of usable GPU inference.
4. **Settings and persistence:** add backward-compatible `Auto` defaults and filtered model/provider
   options. Test old settings JSON, unknown provider IDs, save/load round trips, and UI states with fake
   capability results.
5. **Engine adapters and packaging:** integrate one measured backend at a time, preserve CPU fallback,
   verify native-library loading/architecture/licensing, and add opt-in hardware smoke tests. Keep the
   deterministic required test suite independent of a physical GPU.
6. **Parallel integration:** verify the selected engine's actual concurrent inference behavior, request
   isolation, per-job UI state, and memory limits without introducing a FIFO transcription queue.

## Roadmap points to reconcile before implementation

These are observations for a later roadmap edit; this analysis does not change `.agents/ROADMAP.md`.

- Phase 1 refers to an `ITranscriptionService` from Phase 3.1, while the current source defines
  `ITranscription` and the roadmap does not currently provide that Phase 3.1 item. Either pull the
  minimal required abstraction into Phase 1 or correct the dependency.
- Phase 6.1's proposed `vulkan-1.dll` presence check is not enough to establish that a selected model
  can initialize on a usable Vulkan device. Resolve and report the actual runtime/device result.
- Phase 6.2 should compare WinML with DirectML before choosing a new Windows ONNX path.[3][4]
- Phase 7 should compare Whisper.net's Parakeet runtime with custom ONNX Runtime, including the
  documented feature gaps and the project's pinned package version.[1][8]
- Keep the ~3× performance figure as a benchmark criterion against the current application, not as a
  value inferred from an upstream benchmark using another baseline.[6]

## Sources

[1] https://github.com/sandrohanea/whisper.net/blob/main/readme.md — Whisper.net README
[2] https://onnxruntime.ai/docs/execution-providers — ONNX Runtime Execution Providers
[3] https://onnxruntime.ai/docs/get-started/with-windows.html — ONNX Runtime on Windows / WinML
[4] https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html — ONNX Runtime DirectML EP
[5] https://onnxruntime.ai/docs/execution-providers/WebGPU-ExecutionProvider.html — ONNX Runtime WebGPU EP
[6] https://github.com/Const-me/Whisper/blob/master/Readme.md — Const-me Whisper README
[7] https://github.com/sandrohanea/whisper.net/blob/main/Whisper.net/LibraryLoader/RuntimeOptions.cs — RuntimeOptions
[8] https://github.com/sandrohanea/whisper.net/releases — Whisper.net releases
