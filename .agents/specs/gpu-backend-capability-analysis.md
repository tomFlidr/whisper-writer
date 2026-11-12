# GPU inference and model/backend capability analysis

**Status:** Supporting architecture analysis for Roadmap Phase 3. Phase 3.0 is verified; Vulkan 3.1
packaging, CPU fallback, full-corpus Vulkan/CUDA comparison, and fresh x86 CPU launch are locally verified.
ONNX DirectML/CUDA provider packs run through the WPF app's production loader on synthetic Parakeet inputs.
Phase 3.4 CUDA/cuDNN provisioning, Phase 3.5 Parakeet engine implementation, and Phase 3.6 transactional
engine selection are required but not implemented; Phase 3 remains open. Hosted release packaging, known-audio
Parakeet transcription, and representative AMD validation remain unverified.
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
For ONNX Runtime in Phases 3.2–3.3, use the separate persisted `OrtBackend` preference implemented for
Parakeet. `Auto` selects DirectML if its pack exists and otherwise CPU; CUDA is an explicit choice. An
explicit provider failure does not cross-switch cores. DLL presence or a GPU model name alone is not proof
that inference is running on that provider.

## Current implementation baseline

- `WhisperWriter.csproj` pins `Whisper.net`, `Whisper.net.Runtime`, and
  `Whisper.net.Runtime.Cuda` to 1.9.0. The package/runtime inventory and newer stable/preview versions
  are recorded in the Phase 3 spec; keep related package versions aligned when upgrading.
- `Whisper.DetectCudaVersion()` and its P/Invoke helper are removed. Whisper initialization and the
  MainWindow no longer infer support or active provider from CUDA DLL presence. `App.xaml.cs` still adds
  the local CUDA directory to the process loader path; this is dependency resolution, not detection.
- `NetRuntime.ActiveLibrary` exposes `RuntimeOptions.LoadedLibrary`; after successful factory
  creation, `Whisper.ActiveBackend` maps recognized library IDs into the immutable `ActiveBackendInfo`.
  The UI and each `TranscriptionJob` use that descriptor; unknown library IDs stay unclassified.
- `EtaCalc` requires the job's `ActiveBackendInfo` for estimate/record operations and serializes the nested
  `backend` object. Missing identity creates no ETA environment sample; the old flat CUDA-derived schema is
  not reused.
- `Services/Inference/Capacity/Manager` fingerprints the full engine/runtime/provider/device/CUDA-major descriptor.
  `TranscriptionQueue` and the capacity manager pass the descriptor's primary provider ID to the memory
  monitor. The project no longer exposes the parallel string-only `ActiveProvider` interface contract.
- `WhisperWriter/Models/AppSettings.cs` now stores `OrtBackend` (default `Auto`) and
  `OrtCudaRuntimePath`. `SettingsWindow` exposes the Parakeet provider preference and CUDA/cuDNN folder;
  these controls do not change the current Whisper runtime.
- ORT runtime dependencies are isolated in `WhisperWriter.OrtRuntime.Cpu`, `.DirectML`, and `.Cuda`.
  The main WPF project references the app-owned `WhisperWriter.OrtRuntime.Contracts` only, avoiding
  competing native ORT cores in its root output.
- `ProviderCatalog`, `PluginLoader`, and `Services/OnnxRuntime/BackendFactory` resolve one
  provider pack, load it through ALC, configure process-local native search paths, create sessions, and
  derive verified `ActiveBackendInfo` from ORT execution-profile events after a session run.
- The process sets `ORT_DISABLE_TELEMETRY=1` before loading a provider; each plugin also calls
  `OrtEnv.Instance().DisableTelemetryEvents()` before creating a session. Active backend status still
  comes only from the ORT profile after a successful run.
- `WhisperWriter/Contracts/ITranscription.cs` remains the current contract with
  `InitializeAsync(string modelPath)` and `ActiveBackendInfo? ActiveBackend`. The current Whisper service
  fills the descriptor only after successful factory creation. No `ParakeetService` or audio-to-text ONNX
  pipeline is implemented; Phase 3.5 must consume the ORT factory without exposing ORT session types across
  plugin contexts.
NetRuntime.DownloadModelAsync() in `WhisperWriter/Services/Whispers/NetRuntime.cs`
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
then create the right engine/runtime from the resolved selection. Whisper returns `ActiveBackendInfo`
after initialization; `Services/OnnxRuntime/BackendFactory` now returns the same identity only after an ORT session
has run and its execution profile confirms provider assignment. Phase 3.5 must pass that identity
through the Parakeet transcription job and ETA lifecycle.

Prefer ordered runtime selection or a validated backend preference over blindly forcing a native
provider. The installed Whisper.net 1.9.0 XML documentation confirms `RuntimeOptions.RuntimeLibraryOrder`
and `RuntimeOptions.LoadedLibrary`; its default order is CUDA, Vulkan, CoreML, OpenVINO, CPU, then
CPU-NoAVX. `LoadedLibrary` reports the selected runtime, not a physical device name. Do not force a
provider without checking the selected model and device; retain a tested fallback.[7]

## Settings dialog scope and current implementation

Whisper still delegates provider order to its own runtime; Phase 3.1 does not expose a Whisper CUDA/Vulkan
picker. The Settings window now persists `OrtBackend` (`Auto`, `Cpu`, `DirectMl`, or `Cuda`) and an optional
`OrtCudaRuntimePath` for the future Parakeet ONNX engine. These controls do not affect the current Whisper
service, and the Parakeet transcription pipeline is not yet implemented. The provider list is not currently
filtered by installed packs; the factory reports a missing/failed explicit provider rather than silently
switching. Phase 3.6 must surface that result in the engine UI.

The user's confirmed rule remains: an explicit engine/library choice is honored; only `Auto` may select
another compatible provider within that engine. The capability resolver proposal below remains future work:

- **Backend:** eventually show `Automatic (recommended)` plus only the backends compatible with this model
  and detected runtime. Explain unavailable choices instead of presenting an unexplained global list.
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

The current runtime ownership model requires restart for some model/provider changes. Phase 3.6 is a
required transactional engine-selection workflow: pause new admission, drain active and queued jobs, preflight
the candidate, and enable Save only after success. The candidate check must not irreversibly pin the live
process to an uncommitted ORT core; use an isolated preflight or an explicit restart transaction. This is
planned behavior, not implemented behavior.

## Backend/model compatibility observations

- **Whisper GGML:** The app pins Whisper.net 1.9.0 CPU/CUDA/Vulkan packages; the pinned runtime's automatic
  order includes Vulkan after CUDA. Phase 3.0's neutral loading, `ActiveBackendInfo`, and ETA identity are
  implemented. Phase 3.1 local x64/x86 publish assets, CUDA-to-CPU fallback, x86 CPU launch, and the full-corpus
  Vulkan/CUDA comparison are verified. Representative AMD hardware and device-index mapping remain open.[1]
- **Const-me Whisper:** Phase 1 completed a same-machine 10-recording comparison. CUDA totaled 144.040 s,
  versus 148.296 s for Const-me D3D11 Media Foundation and 147.110 s for direct PCM. Neither route was
  faster overall; the full metrics are in `.agents/specs/gpu-backend-benchmark-handoff.md`. The user
  clarified that the Intel UHD used for the separate D3D11 smoke is a weak integrated GPU; its timing is
  device-specific and must not be treated as a general D3D11/Vulkan/AMD performance result.
- **Parakeet ONNX archive:** The project's pinned V3 int8 archive SHA was verified on November 11, 2026;
  its actual filenames use `encoder-model.int8.onnx` and `decoder_joint-model.int8.onnx`. Production
  x64 provider plugins for CPU 1.31.1, DirectML 1.24.4, and CUDA 1.31.1 ran all three graphs through
  the app's ALC factory with synthetic tensors. Profiles show CPU plus the selected GPU provider; this is
  graph bring-up, not audio-to-text, accuracy, or performance evidence. The upstream Whisper.net native
  Parakeet runtime remains a separate family and is not assumed compatible with these ONNX artifacts.[1][8]
- **ONNX Runtime on Windows:** DirectML is an ONNX execution provider, not a Whisper.net runtime. Microsoft
  prefers WinML for new Windows applications and puts DirectML in sustained engineering; provider support is
  model/operator-specific, and WinML's API/runtime version must be compared with the CUDA path.[3][4]
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

1. **Phase 3.0 — verified identity and ETA fingerprint:** implemented and locally verified. Provider
   labels and ETA fingerprints use successful runtime identity, preserve old SQLite rows without reusing
   provider-ambiguous fingerprints, and never treat DLL presence as proof of capability.
2. **Phase 3.1 — Whisper.net Vulkan:** package 1.9.0 is integrated with the other runtime references;
   x64/x86 publish assets and actual CPU fallback are locally verified. Continue only the broader Vulkan
   benchmark/vendor matrix and fresh-publish launch checks; report only the runtime that actually initialized.
3. **Phase 3.2/3.3 — ONNX provider packs:** implemented as separate CPU, DirectML, and CUDA plugin
   projects with one ORT core per process. The WPF host does not flatten provider cores into its root
   output; provider selection is restart-only and profiling reports CPU/GPU partitioning. v1.4.0 releases
   CPU-in-x64 plus the DirectML ZIP; the CUDA ZIP is built as a CI artifact but not attached until
   CUDA/cuDNN provisioning is resolved. Hosted CI and final artifact tests remain.
4. **Provider validation:** Phase 1's Const-me evaluation is closed; do not repeat it as a
   speed-up candidate. Validate Whisper.net Vulkan and ONNX Runtime providers separately, using the
   exact available model/provider combination, identical inputs/settings, and confirmed active
   backends. Record quality, cold/warm latency, memory, package size, and fallback. The weak integrated
   Intel UHD D3D11 smoke is compatibility evidence for one adapter only, not a Vulkan performance
   baseline. No generic 3× target is carried forward from Phase 1.
5. **Model catalog and downloads:** move model metadata out of `SettingsWindow`; fix model-specific
   download mapping before adding additional model families or formats.
6. **Capability resolver and runtime identity:** add provider probes, compatibility reasons, and actual
   active-backend reporting; avoid treating DLL presence as proof of usable GPU inference.
7. **Phase 3.4 — CUDA/cuDNN provisioning:** resolve legal end-user delivery without manual DLL hunting;
   verify the chosen bundle or consented official-source bootstrap on a clean x64 machine.
8. **Phase 3.5 — Parakeet V3 engine:** implement model download/discovery and `ParakeetService` through the
   existing ONNX provider factory; include known-audio transcription and truthful backend/ETA identity.
9. **Phase 3.6 — transactional engine selection:** pause new jobs, drain accepted work, preflight the candidate,
   enable Save only after success, and preserve the previous engine/settings on failure or cancel.
10. **Parallel integration:** verify the selected engine's actual concurrent inference behavior, request
    isolation, per-job UI state, and memory limits. Use Phase 2's explicit FIFO queue for completed jobs
    waiting on the active-inference semaphore; never block new audio capture or silently drop jobs.

## Phase 3 implementation gates

Phase 3 contains Whisper.net Vulkan (3.1), ONNX Runtime DirectML/CUDA packs (3.2/3.3), CUDA/cuDNN
runtime provisioning (3.4), the Parakeet V3 engine (3.5), and transactional engine selection (3.6). Items
3.4–3.6 are required and not implemented yet. Detailed version matrices and acceptance criteria are in
`.agents/specs/phase-3-gpu-and-onnx-backends.md`.

- Phase 3.0 is implemented and locally verified. There are no runtime CUDA DLL probes left; the local CUDA
  PATH and build-copy targets remain native-loader packaging only. ETA identity requires the typed
  `ActiveBackendInfo` descriptor.
- For 3.1, the aligned Vulkan package is added; use `RuntimeOptions.LoadedLibrary` after successful
  initialization. Vulkan DLL presence alone cannot prove the selected model loaded or that inference used Vulkan.
- For 3.2/3.3, DirectML and CUDA packages are isolated in their own x64 provider projects; the main WPF
  app references only the shared contracts. The app factory selects one pack before its first ORT call and
  requires restart to switch. Fresh x64 publish has no ORT core DLL at app root; x86 has no ORT pack.
- The production loader/factory smoke ran all three verified Parakeet graphs with synthetic inputs under
  CPU, DirectML, and CUDA. Profiles include CPU plus GPU for DirectML/CUDA; no full audio-to-text, accuracy,
  latency, or memory result is claimed. DLL presence is not an active-provider signal.
- `Auto` selects DirectML if its optional pack is present, otherwise CPU. DirectML session creation can
  fall back to CPU only within the same core when `Auto` allows it; explicit DirectML/CUDA selections fail
  rather than switching cores. DirectML `Run` calls are serialized on each session.[4]
- v1.4.0 release packaging includes the CPU pack in the x64 app ZIP and the optional DirectML pack ZIP.
  The CUDA pack is built for CI verification but is not attached to the release; it excludes CUDA/cuDNN
  runtime DLLs. License and third-party notice files are staged with all provider packs.
- Phase 3.4 is required: the current manual `OrtCudaRuntimePath` does not resolve end-user CUDA/cuDNN
  provisioning. Choose a legally supported bundle or consented official-source bootstrap and verify it on a
  clean x64 environment.
- Phase 3.5 is required: implement the Parakeet service through the existing ORT factory and current
  `ITranscription` contract. Validate a known-audio transcription; synthetic graphs alone do not pass.
- Phase 3.6 is required: Settings must drain accepted jobs and preflight a changed engine before enabling
  Save. Preserve the current engine/settings on failure or cancel and respect the one-ORT-core-per-process
  restriction; this transaction has not been implemented.
- Microsoft WinML guidance was considered; this implementation follows the user-approved separate ORT
  DirectML/CUDA provider path. Revisit WinML only if Phase 3.5 model compatibility or product requirements
  change.[3][4]
- The user confirms Windows 11 or newer as the support floor; Windows 10 is not a target. `AGENTS.md` and
  `README.md` now reflect this. Validate the pinned runtime on Windows 11 before release.
- `Whisper.ActiveBackend` and the `MainWindow` ready label now use the structured loaded-runtime descriptor;
  the old string-only `ActiveProvider` contract and DLL fallback were removed. Do not add a duplicate
  `ITranscriptionService` interface solely because the current contract is named `ITranscription`.
- For 3.2, WinML guidance was reviewed; this implementation uses the approved DirectML package. Actual graph
  profiles show mixed CPU/DirectML assignment. The Phase 3.5 session pool and its memory cost remain to be tested.[3][4]
- For 3.3, the CUDA 1.31.1 package requirements and active provider were verified locally on CUDA 13.2/cuDNN 9.19.
  The CUDA/cuDNN runtime is not bundled in the optional provider pack.
- For Phase 3.5, compare any published Whisper.net Parakeet package/model family against the planned
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
[9] https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support — .NET plugin load contexts
[10] https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/loading-unmanaged — native library resolution in .NET
[11] https://onnxruntime.ai/docs/reference/privacy.html — ONNX Runtime telemetry and privacy controls
