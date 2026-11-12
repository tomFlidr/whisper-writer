# Phase 3 Progress — GPU Backends and ONNX Runtime

**Status:** Phase 3.0 and the 3.2/3.3 provider implementations are locally verified, but Phase 3 is not complete.
The required 3.4 CUDA/cuDNN provisioning, 3.5 Parakeet V3 engine, and 3.6 transactional engine selection are
now ordered in the Roadmap and specified, but not implemented. The fresh x86 CPU launch and full-corpus
Vulkan/CUDA comparison have now passed locally. Hosted Actions, representative AMD validation, and the
Parakeet transcription pipeline remain open.
**Last updated:** 2026-11-12
**Project:** `D:/tec/cs/whisper-writer-wpf`
**Current observed HEAD:** `6ca61f5d4458` (`main`, unchanged; observed 2026-11-11)

## Resume and handoff rule

Read this file and the linked Phase 3 specification before resuming Phase 3 work. Re-read and update this
checkpoint before summarizing or handing off a conversation. Record only verified changes and explicit
user decisions; do not present planned work or package approval as completed implementation. Re-check Git
status and current source because this file can become stale between sessions.

## Current checkpoint

- The user authorized autonomous work on Phase 3.0–3.3 on 2026-11-10.
- On 2026-11-11, the user required three ordered Phase 3 items: 3.4 resolves end-user CUDA/cuDNN provisioning
  without manual DLL hunting; the former Phase 7 Parakeet V3 engine is now Phase 3.5; transactional engine
  switching after queued/active jobs drain is Phase 3.6. These roadmap changes are specified but not implemented.
- The user accepted the shared Phase 3.0 design and approved adding `Whisper.net.Runtime.Vulkan`.
- The approval was for the package, not a version. Version 1.9.0 was selected to match the existing
  Whisper.net package set and the already-tested scratch probe; the core/runtime/CUDA/Vulkan references
  now all resolve to 1.9.0.
- The project pins Whisper.net core/runtime/CUDA 1.9.0. The approved `Whisper.net.Runtime.Vulkan` 1.9.0
  package is now referenced alongside them; `dotnet restore WhisperWriter.sln` succeeded.
- Pre-change baseline rerun with .NET SDK 10.0.303: Debug and Release each passed **217/217 tests**; app
  builds in both configurations completed with **0 warnings and 0 errors**.
- Phase 3.0 implementation is locally verified: startup status is neutral, the app no longer probes CUDA DLLs for
  backend identity, and `ActiveBackendInfo` is captured after successful Whisper factory creation. Each
  transcription job carries the same immutable identity into ETA lookup and result recording. ETA JSON now
  nests device/engine/runtime/providers/optional CUDA version, and unidentified jobs do not create samples.
- The old `Whisper.DetectCudaVersion` API and its P/Invoke helper were removed; local CUDA PATH setup remains
  only as native-loader packaging, not capability detection. Full Debug and Release suites each passed
  **235/235 tests**, and Debug/Release application builds completed with **0 warnings and 0 errors**.
- Preflight confirmed the stock ONNX Runtime DirectML and CUDA NuGet distributions contain distinct
  provider-specific native `onnxruntime.dll` builds; a normal single-process package reference cannot
  safely provide both. WinML is the current Microsoft-preferred Windows direction, but its bundled ORT
  version and execution-provider catalog must be checked against this app and the planned CUDA path.
- Separate scratch provider probes on `mul_1.onnx` succeeded: DirectML 1.24.4 and CUDA 1.31.1 each created
  and ran an ONNX session; ORT profiling identified `DmlExecutionProvider` and `CUDAExecutionProvider`.
  CUDA used the host's CUDA 13.2 toolkit and cuDNN 9.19 from an existing PyTorch install. These probes do
  not prove the two native cores can be co-hosted or that the Parakeet graph is supported.
- The SHA-verified Parakeet archive was extracted only under scratch. Each of its three ONNX graphs was then
  loaded and run once with synthetic tensors in separate DML 1.24.4 and CUDA 1.31.1 processes; profiles show
  CPU plus the selected GPU provider in every graph. This is not audio-to-text validation.
- CUDA Toolkit 13.2 and the CUDA 13 runtime are installed under the standard toolkit directory. No CUDA 12 runtime
  files are present under `D:/llms`. A cuDNN 9 DLL was found in the existing ComfyUI PyTorch package at
  `D:/llms/StabilityMatrix/Data/Packages/ComfyUI/venv/Lib/site-packages/torch/lib/cudnn64_9.dll`; its
  matching `torch.version.cuda` is 13.0 and cuDNN version is 9.19. This is a Python-package dependency,
  not part of the ORT CUDA provider pack. The separate Whisper.net CUDA runtime may be present in the app,
  but it does not prove cuDNN or every ORT dependency is available.
- Source/test/docs changes remain uncommitted. The user approved ORT packages in isolated projects: CPU `Microsoft.ML.OnnxRuntime` 1.31.1, DirectML `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4, and CUDA `Microsoft.ML.OnnxRuntime.Gpu.Windows` 1.31.1. None is referenced by the main WPF project; only the app-owned contracts project is referenced there.
- The Vulkan NuGet `.targets` declares five x64 native DLLs under `runtimes/vulkan/win-x64` for Windows
  target frameworks without an RID guard. A first x86 publish reproduced the leak; CUDA and Vulkan package
  references and CUDA copy targets are now excluded for `win-x86`. The release workflow passes
  `-p:RuntimeIdentifier` during restore because `dotnet restore -r` alone did not evaluate those conditions.
- A clean `win-x64` publish into scratch contains all five Vulkan runtime DLLs and the three configured CUDA
  runtime DLLs (`cublas64_13.dll`, `cublasLt64_13.dll`, `cudart64_13.dll`). A clean `win-x86` publish contains
  only the four `runtimes/win-x86` CPU DLLs; it has no CUDA/Vulkan directories or DLLs. The local output
  directories are under `D:/llms/hermes/cache/scratch/phase3-vulkan-x64-cuda-20261111` and
  `phase3-vulkan-x86-cpu-20261111`.
- After the Vulkan reference was added, the Debug solution build passed with **0 warnings/errors** and the
  focused `ActiveBackendInfoTests`, `TranscriberTests`, and `MainWindowTest` selection passed **50/50**.
  After the RID-specific packaging and CUDA publish-target fixes, the full Debug and Release suites each
  passed **235/235 tests**; both application builds completed with **0 warnings and 0 errors**.

## Accepted Phase 3.0 design

- DLL presence, CUDA version probes, and GPU inventory are prerequisites/diagnostics only. They must not
  be treated as proof that a model/provider is supported, initialized, or used for inference.
- Show neutral `Loading model…` until model initialization succeeds. Thereafter report the actual loaded
  engine/runtime/provider; report CPU when the runtime actually falls back to CPU. Do not claim a physical
  device name unless the runtime supplies a stable identity for the selected device.
- Complete the partial `ActiveProvider` path with one immutable active-backend descriptor. Use the same
  descriptor for UI status and a job's ETA estimate and result recording; `EtaCalc` must not independently
  infer the backend from DLLs.
- Replace the flat ETA JSON values (`backend: GPU/CPU` and top-level `cudaVersion`) with a nested object:

  ```json
  {
    "backend": {
      "device": "gpu",
      "engine": "whisper",
      "runtime": "whisper.net",
      "providers": ["cuda"],
      "cudaVersion": 13
    }
  }
  ```

- `device` is `cpu` or `gpu`; `engine` and `runtime` use stable IDs. `providers` is a deterministic list
  of execution providers confirmed to perform work. For partitioned ONNX graphs, include CPU if it executes
  nodes; classify `device` as `gpu` only when a GPU provider actually performs work.
- Put `cudaVersion` inside `backend`; populate it only from an authoritative active-runtime value, otherwise
  use `null`. Never infer it from versioned DLL presence.
- Keep `gpus` as hardware inventory, separate from the selected execution backend. Preserve existing SQLite
  rows, but do not reuse their provider-ambiguous fingerprints under the new schema; expect new ETA samples
  to accumulate for the new environment fingerprint.
- If an active engine/runtime/provider cannot be reliably classified, do not create a falsely labeled CPU/GPU
  ETA profile.

## Subphase status

### 3.0 — Shared runtime identity and ETA fingerprint

- **Implemented and locally verified.** Phase 3 roadmap/spec/inventory/README are updated; `AGENTS.md` Sections 3–4
  were refreshed for the organization refactor on November 12 under explicit user approval. Sections 1–2 and 5+
  were left unchanged.
- The production startup/UI/ETA call sites no longer infer backend from CUDA DLL presence. The descriptor
  is derived after `WhisperFactory` creation from the actual loaded runtime and flows with each job.
- `Services/Inference/Capacity/Manager` fingerprints the full descriptor (including CUDA runtime major); the memory
  monitor receives its primary provider ID. Capacity metadata is tested for separate CUDA 12 and CUDA 13 profiles.
- Deterministic tests cover active identity, neutral loading, fallback, provider-to-device mapping, ETA JSON,
  fingerprint separation, unknown identity, and mixed CPU/GPU provider sets. DLL-only presence cannot pass a
  support test.

### 3.1 — Whisper.net Vulkan

- Package `Whisper.net.Runtime.Vulkan` 1.9.0 is restored and referenced. Clean x64/x86 publish asset gates
  pass; a fresh x86 app launch with `ggml-small.bin` reached `Ready (Cpu)`. This confirms x86 startup/model
  load, not x86 speech transcription. CPU fallback also passed an isolated smoke.
- On 2026-11-11, the same 10 user-provided Czech WAV files were run through the x64 CUDA `Auto` baseline and
  forced Vulkan (requested `GpuDevice=0`). CUDA loaded `Cuda` and Vulkan loaded `Vulkan` on all 10 pairs.
  Request totals: CUDA **198.134 s**, Vulkan **125.208 s**; Vulkan was faster on 10/10, saving **72.926 s**
  (**36.81%**). Weighted WER matched at **73/650 (11.231%)**; weighted CER was **121/4093 (2.956%)** for
  CUDA and **122/4093 (2.981%)** for Vulkan. Model-load sums were CUDA **17.994 s** and Vulkan **21.999 s**.
- The benchmark used `ggml-medium.bin`, language `cs`, and the local Settings prompt. Raw transcripts were
  compared in memory only; the scratch results file stores metrics only at
  `D:/llms/hermes/cache/scratch/phase3-vulkan-full-corpus-results.json`.
- This is evidence for this host/corpus, not broad vendor support. The host has Intel UHD and NVIDIA Quadro
  T2000, no AMD GPU. Vulkan used requested device index 0, but Whisper.net did not return a stable physical
  device name; index-to-adapter mapping is not claimed as confirmed.
- The Release workflow remains unrun in GitHub Actions; an authorized tag push is needed to trigger it.

### 3.2 — ONNX Runtime DirectML for Parakeet

- **Provider implementation and local smoke verified through the application's production loader.**
  `WhisperWriter.OrtRuntime.DirectML` pins `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4 in its own
  x64 project. The WPF app references only the app-owned contracts; it does not flatten the DirectML
  native core into its root output.
- `Services/OnnxRuntime/PluginLoader` uses a provider-specific `AssemblyLoadContext` and
  `AssemblyDependencyResolver`, sharing only the app-owned contract assembly. The loader locks one
  selected ORT core for the process lifetime; a provider change requires restart.
- Through the production settings → catalog → factory → loader → session path, all three verified
  Parakeet graphs ran with synthetic tensors. ORT profiling confirmed `DmlExecutionProvider` plus
  `CPUExecutionProvider` on each graph. `Auto` selected DirectML; an intentionally invalid DML device
  triggered the tested CPU-only fallback inside the same DirectML ORT core and exposed its reason.
- DirectML sessions use sequential execution and disable memory patterns; the adapter serializes
  `Run` calls on a session. The provider profile, not DLL inventory, supplies active-backend identity.
- The cleaned optional DirectML pack is **36,921,995 bytes** locally, including runtime files and
  required license/third-party notices. Release workflow staging has been authored but hosted Actions
  has not run.
- This remains synthetic graph execution only. There is no Parakeet audio preprocessing/decoder service,
  known-audio transcription result, accuracy comparison, or latency/memory benchmark.

### 3.3 — ONNX Runtime CUDA

- **Provider implementation and local smoke verified through the application's production loader.**
  `WhisperWriter.OrtRuntime.Cuda` pins `Microsoft.ML.OnnxRuntime.Gpu.Windows` 1.31.1 in its own x64
  project. It is explicit-only; it does not silently fall back to another provider/core.
- With the host CUDA 13.2 toolkit and cuDNN 9.19 from the existing ComfyUI PyTorch package configured
  as the process-local native dependency path, all three Parakeet graphs ran with synthetic tensors.
  Profiles confirmed `CUDAExecutionProvider` plus `CPUExecutionProvider` on each. The encoder profile
  logged 742 memcpy nodes and the decoder/joint profile logged 21; performance impact is unmeasured.
- The final locally staged optional CUDA provider pack is **215,636,125 bytes** (ZIP:
  **160,381,369 bytes**), including license notices but excluding CUDA/cuDNN runtime DLLs. The host adds
  the configured directory, the app's existing Whisper CUDA directory when present, and the current
  process PATH to native DLL lookup. This does not prove an end-user CUDA/cuDNN installation.
- The CUDA provider ZIP is not attached to v1.4.0. Release workflow builds it as a short-lived CI
  artifact for verification only. The README now separates current Whisper.net CUDA setup from the
  future Parakeet ONNX CUDA path; neither manual runtime setup nor the provider smoke is audio-to-text.
- CUDA version is left unknown (`null`) in backend metadata; the provider profile confirms CUDA use but
  no authoritative runtime-version query is integrated.
- This is not known-audio speech-to-text, accuracy, or performance validation. The full Parakeet engine
  is now planned in Phase 3.5; hosted release packaging remains unverified.

## Remaining verification gates

- Phase 3.4 is mandatory but unimplemented: the optional CUDA provider ZIP still lacks CUDA/cuDNN runtime DLLs.
  A manual `OrtCudaRuntimePath` is not an acceptable end-user completion; choose a legally supported bundle or
  consented official-source provisioning, then verify on a clean x64 environment.
- Phase 3.5 is mandatory but unimplemented: implement the Parakeet V3 download/model catalog and
  `ParakeetService` through the existing ORT provider factory. Known-audio transcription is required; synthetic
  graph/session smoke alone is not enough.
- Phase 3.6 is mandatory but unimplemented: engine selection must pause new admission, drain active and queued
  jobs, preflight the candidate, and enable Save only after success. Failure/cancel must preserve the old
  engine and settings; the one-ORT-core process constraint needs a safe preflight/restart boundary.
- Phase 3.1 full-corpus Vulkan/CUDA comparison and fresh x86 CPU launch passed locally. A representative AMD
  GPU run and confirmed physical Vulkan-device mapping remain open; do not claim broad AMD compatibility.
- ORT provider graph smoke used synthetic inputs. Known-audio Parakeet transcription, accuracy, latency, memory
  usage, and the complete Phase 3.5 decoder pipeline remain unverified.
- The v1.4.0 Release workflow attaches the two app ZIPs and the optional DirectML ZIP. It builds and
  checks the CUDA provider ZIP but does not attach it; hosted GitHub Actions has not run yet. Equivalent
  local x64/x86 package-layout checks and extracted-ZIP loader smoke passed.
- Whisper remains the only implemented transcription engine; Parakeet audio-to-text is now planned in Phase 3.5.
- `AGENTS.md` Sections 3–4 were refreshed for the organization refactor on November 12, 2026, under explicit
  approval. Section 2's version now reflects the approved v1.4.0 release; Sections 1 and 5 onward remain unchanged.
- The SHA-verified Parakeet archive remains under scratch only. No model files were added to the repository.

## ORT isolation design (production plugin path locally verified)

- The user approved three package references, implemented in separate x64 provider projects: CPU
  `Microsoft.ML.OnnxRuntime` 1.31.1, DirectML `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4, and CUDA
  `Microsoft.ML.OnnxRuntime.Gpu.Windows` 1.31.1. The main WPF project references only the app-owned
  contracts project; its own package graph does not include an ORT native core.
- `Services/OnnxRuntime/PluginLoader` loads one provider plugin through a provider-specific
  `AssemblyLoadContext`/`AssemblyDependencyResolver`, sharing only the host contract. It caches one
  provider for the process lifetime; changing providers requires restart. Tests cover contract sharing
  and rejection of a core switch after disposal.
- `Services/OnnxRuntime/ProviderCatalog` treats a plugin DLL plus `.deps.json` only as a present pack, not as
  proof of an active provider. `Services/OnnxRuntime/BackendFactory` applies the saved preference, adds native
  search directories to the process-only PATH, creates sessions, and derives `ActiveBackendInfo` only
  from actual ORT profile events after `Run`.
- `Auto` chooses DirectML when its pack is installed, otherwise CPU. On DirectML session initialization
  failure only `Auto` may retry CPU inside the same DirectML ORT core; an explicit DirectML/CUDA selection
  fails rather than changing provider. The adapter serializes calls to each session. CUDA is explicit-only.
- Before loading ORT, the factory sets process-local `ORT_DISABLE_TELEMETRY=1`; each plugin also calls
  `OrtEnv.Instance().DisableTelemetryEvents()` before session creation. A focused test passes, and the
  production smoke printed the flag as `1` before inference.
- The production loader/factory smoke ran each of the three verified Parakeet graphs with synthetic tensors:
  CPU profiles reported `CPUExecutionProvider`; DirectML and CUDA profiles each reported CPU plus the selected
  GPU EP. `Auto` selected DirectML and an invalid DML device fell back to a CPU-only session within the same
  core. Repeating the invalid-device request with explicit DirectML returned a nonzero failure instead of
  falling back. No known-audio transcription, WER/CER, latency, or memory benchmark was performed.
- Local release staging measured the CPU pack at **18,334,389 bytes** (bundled in x64 app), the cleaned
  DirectML pack at **36,921,995 bytes** (ZIP: **16,042,915 bytes**), and the cleaned CUDA pack at
  **215,636,125 bytes** (ZIP: **160,381,369 bytes**), including license notices/readmes. CUDA/cuDNN runtime
  DLLs are excluded from the CUDA pack.
- v1.4.0 distribution includes the CPU pack in the x64 app ZIP and the DirectML pack as an optional ZIP.
  The CUDA pack remains a separate CI verification artifact and is not attached to the release until
  CUDA/cuDNN provisioning is resolved. `win-x86` stays CPU-only for Whisper and has no ORT pack.
- Fresh x64 app publish has no ORT native DLL in the app root; fresh x86 publish has no ORT provider files.
  The x64 app output was staged with the CPU pack, and separate DirectML/CUDA ZIPs were generated locally
  with runtime files, notices, and the expected `runtimes/onnx/<provider>` layout. Extracting these ZIPs
  into a scratch app base and running the production factory/ALC smoke succeeded for CPU, `Auto`→DirectML,
  and explicit CUDA. The workflow still has not run in GitHub Actions.


## Final local verification (2026-11-11)

- Full solution tests passed **257/257 in Debug** and **257/257 in Release** after the final UI wording correction.
- Full solution builds passed in Debug and Release with **0 warnings and 0 errors**.
- The targeted Settings copy test first failed on the old text, then passed after the runtime-folder clarification.
- Local x64/x86 package-layout checks and optional ZIP extraction/loader smoke passed; CPU, DirectML, and CUDA
  provider packs have the expected runtime layout and license/third-party notices.
- Final repository audit passed: `git diff --check` is clean, the HEAD is unchanged at
  `6ca61f5d4458c42834318019e49a6bb4b47da840`, and the solution lists all six expected projects. All changes
  remain uncommitted.

## Next action

The local Phase 3.1 full-corpus comparison and x86 CPU launch are complete. Closing the remaining 3.1/3.2/3.3
gates requires a representative AMD GPU for the vendor test and explicit authorization to create/push a version tag
for hosted Release Actions. Phase 3.4–3.6 remain separately required for full Phase 3 closure.

## Source documents

- `.agents/ROADMAP.md`
- `.agents/specs/phase-3-gpu-and-onnx-backends.md`
- `.agents/specs/gpu-backend-capability-analysis.md`
- `.agents/specs/gpu-backend-benchmark-handoff.md`
- `.agents/specs/phase-2-parallel-transcription.md`
- `.agents/specs/phase-0-coverage-inventory.md`
