# GPU inference and model/backend capability analysis

**Status:** Supporting architecture analysis for Roadmap Phase 3; no Phase 3 runtime implementation has started.
**Scope:** GPU acceleration, model-specific backend support, Settings UI, Vulkan/ONNX providers,
and concurrency requirements shared with parallel transcription.
**Phase 3 package/version gates:** `.agents/specs/phase-3-gpu-and-onnx-backends.md`.

## Conclusion

Do not model acceleration as a universal **GPU on/off** switch. The application needs to distinguish:

1. **Model/engine:** Whisper GGML, Parakeet, or another model family and its required format/features.
2. **Execution backend:** Whisper.net CUDA/Vulkan, an ONNX Runtime execution provider, Const-me Whisper,
   or another engine-specific implementation.
3. **Physical device:** the GPU or CPU device that the selected runtime can actually use.

For Phase 3.1, Whisper.net's ordered runtime loader is the default; report the library that actually
loaded after initialization. An explicitly selected engine/library is authoritative and must not be
silently replaced by another engine. `Auto` may choose only among model/engine/provider combinations
that are actually compatible. Within a selected engine, provider `Auto` uses that runtime's order;
an explicit provider must either load or be reported unavailable. For ONNX Runtime in Phases 3.2–3.3,
keep the separate `OrtBackend` preference planned in the Roadmap. DLL presence or a GPU model name alone
is not proof that inference is running on that provider.

## Current implementation baseline

- `WhisperWriter.csproj` pins `Whisper.net`, `Whisper.net.Runtime`, and
  `Whisper.net.Runtime.Cuda` to 1.9.0. The package/runtime inventory and newer stable/preview versions
  are recorded in the Phase 3 spec; keep related package versions aligned when upgrading.
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
  dialog persists a changed model path; `WhisperWriter/Services/TrayMenu.cs` saves settings
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
provider. The installed Whisper.net 1.9.0 XML documentation confirms `RuntimeOptions.RuntimeLibraryOrder`
and `RuntimeOptions.LoadedLibrary`; its default order is CUDA, Vulkan, CoreML, OpenVINO, CPU, then
CPU-NoAVX. `LoadedLibrary` reports the selected runtime, not a physical device name. Do not force a
provider without checking the selected model and device; retain a tested fallback.[7]

## Settings dialog proposal and scope

The current Roadmap requires truthful runtime status for Whisper in Phase 3.1; it does not yet commit
to a user-facing Whisper provider/device picker. Keep those controls optional unless auto-selection
fails the compatibility goal. The user's confirmed rule is that an explicit engine/library choice is
honored; only `Auto` may select another compatible option. Phases 3.2–3.3 separately propose an
`OrtBackend` setting. If selectors are approved, keep engine and provider as separate controls:

- **Backend:** `Automatic (recommended)` plus only the backends compatible with this model and detected
  runtime. Show unavailable choices disabled with a short reason rather than presenting an unexplained
  global list.
- **Device:** show a GPU/device picker only when the selected provider has a stable device-selection API
  and a way to map the selected value to a device name. Whisper.net 1.9.0 exposes `GpuDevice` as an
  index (default 0); the Vulkan smoke did not return the selected device name, so verify index mapping
  before showing a user-facing picker. Backend choice and physical-device choice are separate.
- **Status:** show the active provider/device after initialization, plus a visible fallback warning if
  the requested preference could not be honored.
- **Model-specific controls:** enable, hide, or explain unsupported language/prompt/translation controls
  based on the model engine's feature descriptor.

Persist stable engine/library and provider IDs, not the detected hardware list. Prefer one provider
preference per engine family (for example Whisper.net vs. ONNX/Parakeet), with a per-model override only
if actual model combinations require it. In `Auto`, the application may fall back within a compatible
engine/provider set. For an explicit engine/library, never cross-fallback to another engine; for an
explicit provider, report unavailable if that provider did not load rather than claiming it is active.

Changing a model or backend must either recreate the inference service safely or clearly require a
restart. For the first implementation, a restart-required setting is simpler and matches the current
startup initialization behavior. Live switching can be added later when per-job runtime ownership is
implemented for parallel transcriptions.

## Backend/model compatibility observations

- **Whisper GGML:** The current app has Whisper.net 1.9.0 CPU/CUDA packages; the pinned runtime's
  automatic selection order already includes Vulkan after CUDA. The app currently does not include the
  Vulkan runtime package or report `RuntimeOptions.LoadedLibrary`; see the Phase 3 spec for package
  versions, OS gates, and the exact status correction.[1]
- **Const-me Whisper:** Phase 1 completed a same-machine 10-recording comparison. CUDA totaled 144.040 s,
  versus 148.296 s for Const-me D3D11 Media Foundation and 147.110 s for direct PCM. Neither route was
  faster overall; the full metrics are in `.agents/specs/gpu-backend-benchmark-handoff.md`. The user
  clarified that the Intel UHD used for the separate D3D11 smoke is a weak integrated GPU; its timing is
  device-specific and must not be treated as a general D3D11/Vulkan/AMD performance result.
- **Parakeet:** The current upstream `main` documents a separate native runtime family and feature gaps
  such as prompt/translation/VAD support. Published stable package availability must be verified for
  the version selected by this app. The native family is not automatically compatible with the ONNX
  int8 model artifacts currently planned for Phase 7.[1][8]
- **ONNX Runtime on Windows:** DirectML is an ONNX execution provider, not a Whisper.net runtime.
  Revisit DirectML versus WinML before implementation; provider support can be model/operator-specific,
  and official Windows guidance should be checked against the target Windows versions.[3][4]
- **ONNX Runtime WebGPU:** This is a separate provider, not the same backend as Whisper.net Vulkan.
  The current ONNX Runtime documentation describes its Windows WebGPU path as D3D12-backed; report the
  actual provider/API instead of implying Vulkan.[5]
- **OpenVINO / CoreML / Metal:** Whisper.net documents an Intel-oriented OpenVINO runtime and
  Apple-specific CoreML/Metal packages. They are not substitutes for a cross-vendor Windows Vulkan
  path; detailed package boundaries are in the Phase 3 spec.[1]

## Parallel transcription constraints

The user clarified that recording must continue while inference slots are full. Each released audio batch
becomes an independent immutable job; when no active inference slot is available, the completed job waits
in an explicit FIFO inference queue. This queue must never delay microphone capture or merge/overwrite
recordings. Each job also needs its own result/error/state and target focus context. A single global
`TranscriptionState` event is not enough once requests overlap; events need a request/job ID so one job
cannot overwrite another job's UI state.

Backend metadata must state whether concurrent calls on one session are supported, whether separate
sessions are required, and the memory cost/limit. In particular, ONNX Runtime's DirectML documentation
requires session configuration constraints and does not permit concurrent `Run` calls on the same
session; separate sessions and their memory cost must be evaluated rather than assuming one shared
session can serve parallel jobs.[4] The active-session semaphore and pending FIFO queue are separate
limits. Bound queue memory/length and define visible behavior at capacity; never discard a captured
recording or silently change engine/provider selection.

## Suggested implementation sequence

1. **Phase 3 provider validation:** Phase 1's Const-me evaluation is closed; do not repeat it as a
   speed-up candidate. Validate Whisper.net Vulkan and ONNX Runtime providers separately, using the
   exact available model/provider combination, identical inputs/settings, and confirmed active
   backends. Record quality, cold/warm latency, memory, package size, and fallback. The weak integrated
   Intel UHD D3D11 smoke is compatibility evidence for one adapter only, not a Vulkan performance
   baseline. No generic 3× target is carried forward from Phase 1.
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
   isolation, per-job UI state, and memory limits. Use Phase 2's explicit FIFO queue for completed jobs
   waiting on the active-inference semaphore; never block new audio capture or silently drop jobs.

## Phase 3 implementation gates

Phase 3 now contains Whisper.net Vulkan (3.1), ONNX Runtime DirectML (3.2), and ONNX Runtime CUDA (3.3).
The detailed version matrix and acceptance criteria are in
`.agents/specs/phase-3-gpu-and-onnx-backends.md`.

- For 3.1, replace the old `vulkan-1.dll`-presence probe with actual runtime initialization and
  `RuntimeOptions.LoadedLibrary` reporting. Vulkan DLL presence alone cannot prove the selected model
  loaded or that inference used Vulkan.
- The user confirms Windows 11 or newer as the support floor; Windows 10 is not a target. `AGENTS.md` and
  `README.md` now reflect this. Validate the pinned runtime on Windows 11 before release.
- The current `Whisper` and `MainWindow` code derive `GPU`/`CPU` status from CUDA DLL presence. Update
  them to show the selected runtime; do not add a duplicate `ITranscriptionService` interface solely
  because the current contract is named `ITranscription`.
- For 3.2, compare WinML and DirectML and verify provider coverage on the actual Parakeet graph.
  DirectML session concurrency constraints must fit the parallel-transcription behavior.[3][4]
- For 3.3, verify the exact ONNX Runtime CUDA/cuDNN package requirements and active provider; do not
  assume the existing Whisper CUDA DLL-copy target is sufficient.
- Before Phase 7, compare any published Whisper.net Parakeet package/model family against the planned
  ONNX int8 files and feature requirements; the upstream `main` branch alone does not prove a matching
  stable package is available.[1][8]
- Treat Phase 1's Const-me timings on the weak integrated Intel UHD as device-specific curiosity, not
  a general D3D11/Vulkan/AMD performance conclusion. Keep backend speed as a measured result, not an
  assumption from an API or upstream benchmark.[6]

## Sources

[1] https://github.com/sandrohanea/whisper.net/blob/main/readme.md — Whisper.net README
[2] https://onnxruntime.ai/docs/execution-providers — ONNX Runtime Execution Providers
[3] https://onnxruntime.ai/docs/get-started/with-windows.html — ONNX Runtime on Windows / WinML
[4] https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html — ONNX Runtime DirectML EP
[5] https://onnxruntime.ai/docs/execution-providers/WebGPU-ExecutionProvider.html — ONNX Runtime WebGPU EP
[6] https://github.com/Const-me/Whisper/blob/master/Readme.md — Const-me Whisper README
[7] https://github.com/sandrohanea/whisper.net/blob/main/Whisper.net/LibraryLoader/RuntimeOptions.cs — RuntimeOptions
[8] https://github.com/sandrohanea/whisper.net/releases — Whisper.net releases
