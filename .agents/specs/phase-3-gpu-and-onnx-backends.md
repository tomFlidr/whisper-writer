# Phase 3 — GPU backends and ONNX Runtime

**Status:** Pre-implementation research; no Phase 3 runtime or production package has been added.
**Last verified:** 2026-11-09
**Roadmap:** `.agents/ROADMAP.md`, items 3.1–3.3.
**Related:** `gpu-backend-capability-analysis.md` (architecture),
`gpu-backend-benchmark-handoff.md` (Phase 1 evidence).

## Purpose and boundaries

Phase 3 contains three distinct integration paths, not one interchangeable “GPU” switch:

1. **Whisper.net Vulkan** for the existing Whisper GGML engine, alongside its current CUDA/CPU runtimes.
2. **ONNX Runtime DirectML** for the planned ONNX-based Parakeet engine.
3. **ONNX Runtime CUDA** as a separate NVIDIA execution provider for that ONNX engine.

Whisper.net Vulkan is not the same provider as ONNX Runtime Vulkan, and neither is
ONNX Runtime DirectML. Keep model format, inference engine, execution provider, and physical device
as separate concepts. The existing `.agents/specs/gpu-backend-capability-analysis.md` contains the
shared model catalog, capability resolver, Settings, and concurrency architecture.

## User-confirmed selection semantics

- Keep the **engine/library**, **execution provider**, and **physical device** as separate settings
  concepts. The current Roadmap's `EngineType` covers Whisper versus Parakeet; provider choice is
  specific to that engine.
- If a user explicitly selects an engine/library, do not silently replace it with another engine or
  model family. If the selected engine cannot initialize, report the failure and available action.
- If provider selection is `Auto`, use the selected engine's verified provider order. For Whisper.net,
  `Auto` delegates to its runtime loader. For ONNX Runtime, `Auto` uses a separate ORT policy.
- If a provider is explicitly selected, verify after initialization that the loaded provider matches.
  If it does not, report the requested provider as unavailable; do not show it as active.
- A top-level engine `Auto` may choose only among validated engine/provider/model-artifact combinations.
  It must not imply that a Whisper GGML model can be processed by an ONNX Parakeet engine or vice versa.

## Current application baseline

- `WhisperWriter.csproj` targets `net8.0-windows7.0` and pins `Whisper.net`,
  `Whisper.net.Runtime`, and `Whisper.net.Runtime.Cuda` to **1.9.0**.
- `.github/workflows/release.yml` publishes `win-x64` and `win-x86`; the x86 artifact is CPU-only.
  The Vulkan NuGet runtime is Windows x64 only, so x86 must retain a working CPU path.
- `WhisperWriter/Services/WhisperBackend.cs` calls `WhisperFactory.FromPath(modelPath)` without
  assigning a custom runtime order. The installed 1.9.0 package XML documents this default order:
  `Cuda`, `Vulkan`, `CoreML`, `OpenVino`, `Cpu`, `CpuNoAvx`. It also exposes
  `RuntimeOptions.LoadedLibrary` for reporting which library actually loaded.
- `WhisperWriter/Services/Whisper.cs` and `WhisperWriter/Views/MainWindow.xaml.cs` currently use
  `DetectCudaVersion()`, which only probes `cudart`/`cublas` DLL presence. That probe is not the active
  backend. A Vulkan-only system can be reported as CPU; finding CUDA DLLs does not prove CUDA inference
  initialized. After initialization, status must use the actual loaded runtime. It must not claim a
  physical device name unless the selected API reports one.
- The project uses single-file publish with `IncludeNativeLibrariesForSelfExtract=false` and a custom
  CUDA DLL copy target. Test every new native runtime in a fresh publish directory. The prior
  Const-me probe's missing `Whisper.dll` is a warning to test packaging, not proof that Whisper.net's
  Vulkan package has the same failure.

## Whisper.net runtime catalog and upstream-documented variants

As of 2026-11-09, NuGet lists **1.9.1** as the latest stable release and **1.9.2-preview1** as a
preview. This application still pins 1.9.0. Do not mix versions among the Whisper.net core and runtime
packages; either test the complete pinned 1.9.0 set or update the set together after compatibility
checks.[1][2]

The upstream runtime catalog lists the package families below; package pages provide version-specific
platform prerequisites.[1][4][5][6][7]

- **`Whisper.net.Runtime`** — standard CPU fallback.
- **`Whisper.net.Runtime.NoAvx`** — CPU build for machines lacking the normal AVX requirements;
  not a GPU backend.
- **`Whisper.net.Runtime.Cuda`** — NVIDIA CUDA runtime used by the current application; toolkit
  compatibility is version-specific.
- **`Whisper.net.Runtime.Cuda12`** — separate CUDA 12 variant listed by newer upstream documentation;
  verify its exact published NuGet package/version before use; it is absent from the pinned 1.9.0 order.[1]
- **`Whisper.net.Runtime.Vulkan`** — vendor-neutral API candidate. Its package lists Windows x64 only;
  compatible GPU and driver support are still required.[5][13]
- **`Whisper.net.Runtime.OpenVino`** — Intel-oriented alternative; package docs list Windows x64/Linux
  x64 and an OpenVINO Toolkit prerequisite.[6]
- **`Whisper.net.Runtime.CoreML`** — Apple-platform runtime, not applicable to this Windows WPF app.[1]
- **`Whisper.net.Runtime.Metal`** — separate Apple Metal package, not applicable to Windows.[7]

`Whisper.net.AllRuntimes` is a convenience package for bundling runtime families.[1] Prefer individual
runtime packages for the release artifacts unless there is a deliberate requirement to ship them all.
The current upstream README also describes a separate Parakeet runtime family. Its presence in the
current `main` branch is not enough to assume that matching packages are published for the stable
version pinned by this app; verify the exact NuGet package/version before considering it for Phase 7.
It must not be assumed to consume the ONNX artifacts currently planned for Parakeet.[3]

### Runtime loader and package constraints

- For the **pinned 1.9.0** package, the automatic runtime order is
  `Cuda → Vulkan → CoreML → OpenVino → Cpu → CpuNoAvx`. The newer stable documentation also lists a
  separate CUDA 12 runtime; check the exact order and package set again if upgrading.
- `WhisperFactoryOptions.GpuDevice` in the pinned 1.9.0 API is an integer GPU-device index, defaulting
  to 0. This is not a device-name/identity API; correlate indices with Vulkan enumeration and runtime
  diagnostics before exposing a user-facing device picker.
- Whisper.net 1.9.0 runtime package metadata lists Windows 11 / Windows Server 2022 or newer, x64,
  and CPU instruction requirements including AVX2, FMA, and F16C. The Vulkan 1.9.0 package inherits
  the base runtime prerequisites and adds Windows x64 / Vulkan prerequisites. The user confirms
  Windows 10 is not a target and accepts Windows 11 as the minimum. `AGENTS.md` and the README now
  reflect that support floor; `net8.0-windows7.0` alone does not establish native-runtime compatibility.[4][5]
- The Vulkan package metadata lists **Vulkan Toolkit >= 1.4.321.1**. Confirm in a clean end-user
  deployment whether the toolkit/SDK must be installed, or whether the graphics driver/runtime is
  sufficient; do not add an SDK installer based on the metadata line alone.[5]
- The published Vulkan package is x64-only. Preserve `win-x86` CPU-only behavior.
- OpenVINO is a separate Intel-oriented option, not an AMD-wide fallback; its runtime/toolkit and
  device selection need a separate compatibility test.[3][6]
- CoreML and Metal are Apple-specific; they do not belong in the Windows package-selection policy.[3][7]
- DirectML is **not** a Whisper.net runtime. It belongs to the separate ONNX Runtime path in item 3.2.

## Phase 1 evidence and user interpretation

Phase 1 is closed as a feasibility evaluation without production integration. The detailed per-file
results, methodology, source commit, and source locations remain in
`.agents/specs/gpu-backend-benchmark-handoff.md`.

- Ten user-provided Czech WAV recordings were compared with `ggml-medium.bin`, language `cs`, and the
  same configured prompt. Whisper.net CUDA took **144.040 s** total; Const-me D3D11 via Media Foundation
  took **148.296 s**; Const-me direct PCM took **147.110 s**. Neither Const-me route was faster overall.
- Weighted WER: Whisper.net and Media Foundation Const-me **11.231%**; direct PCM Const-me **11.385%**.
  Weighted CER: Whisper.net **2.925%**, Media Foundation **2.722%**, direct PCM **2.664%**. The CER
  change is small; direct PCM had one additional word error.
- A separate Const-me D3D11 smoke test loaded the model on the user's weak integrated Intel UHD
  adapter. The user explicitly regards the long latency on this low-end iGPU as a device-specific
  curiosity, not a representative D3D11 performance result. It is **not** evidence about Vulkan
  performance and does not establish AMD support. No AMD GPU or older computer was tested.
- Do not reopen Const-me as a speed-up candidate based on that smoke test. The agreed possible reason
  for a later cross-vendor backend is compatibility coverage; Vulkan is the Phase 3.1 candidate.

## Phase 3.1 — Whisper.net Vulkan

### Implementation guidance

1. First test `Whisper.net.Runtime.Vulkan` **1.9.0**, matching the project's current package set. If
   upgrading, align `Whisper.net`, `Whisper.net.Runtime`, and all selected runtime packages to the same
   version; do not take an isolated preview package.
2. Let the pinned Whisper.net loader perform its documented default runtime selection unless a
   measured requirement calls for an explicit order. Do not implement `LoadLibraryEx("vulkan-1.dll")`
   as proof of an active Vulkan inference backend. A loader DLL may exist while device initialization
   or model loading still fails.
3. After factory/model initialization, report `RuntimeOptions.LoadedLibrary`. Update the current
   CUDA-only status path so it cannot label an active Vulkan backend as CPU or label a failed CUDA
   initialization as active GPU inference. Report the device only if a stable API provides it.
4. Preserve Whisper.net CPU fallback and the x86 CPU artifact. If a Vulkan preference is ever exposed,
   distinguish “requested” from “actually loaded” and display the fallback reason.
5. Validate the exact package version, Windows 11, architecture, driver/runtime, and clean publish
   output before release. Windows 10 is not a Phase 3 support target.

### Verification gates

- Initialize and transcribe an existing GGML model through the factory; record the actual
  `RuntimeOptions.LoadedLibrary` after initialization.
- Exercise CUDA → Vulkan → CPU selection/fallback on appropriate machines. A single machine with
  NVIDIA CUDA present does not prove that Vulkan is selected on non-NVIDIA hardware.
- Run on at least one representative AMD GPU and one representative Intel GPU if broad AMD/Intel
  compatibility is the acceptance objective. Record adapter, driver, OS, model, actual runtime, and
  whether the native Vulkan path initialized.
- Compare latency and WER/CER against the existing Whisper.net CUDA/CPU baseline on the same 10-file
  corpus. Treat performance as measured data, not an assumption based on the Vulkan API or the weak
  Intel UHD D3D11 smoke test.
- Publish into a fresh directory for `win-x64` and `win-x86`; verify native libraries are present,
  loadable, and consistent with each release artifact. Confirm x86 remains CPU-only.
- Add deterministic tests around provider selection/status/fallback using runtime fakes. Do not make
  hardware availability a requirement for the standard CI suite; reserve real GPU tests for an
  opt-in/manual hardware matrix.

### Scratch Vulkan smoke test already executed

The probe is isolated under
`D:\llms\hermes\cache\scratch\whispernet-vulkan-probe-2026-11-09`; it added no production
project/package references and did not persist transcript text.

- `Whisper.net.Runtime.Vulkan` 1.9.0 restored and built with the project's matching Whisper.net 1.9.0
  packages. On the short `01.wav` input, default `Auto` loaded `Cuda`; a Vulkan-restricted order loaded
  `Vulkan`. Both completed transcription with WER 0.14286.
- One repeated Vulkan run loaded device index 1 through `WhisperFactoryOptions.GpuDevice` and completed
  transcription with WER 0.12245. `vulkaninfo --summary` enumerated the Quadro T2000 as GPU 0 and Intel
  UHD as GPU 1 on this host. Whisper.net did not return the physical device name, so the index-to-device
  mapping remains an inference, not a confirmed runtime identity.
- Single-run request times varied: Auto/CUDA 11.164 and 11.248 s; default Vulkan/device 0 11.708 and
  7.289 s; requested Vulkan/device 1 27.432 s. Cold factory/model-load times also varied (first CUDA
  65.330 s, later CUDA 2.252 s; Vulkan 2.446–3.442 s). Treat this as a provider/device-selection smoke,
  not a performance benchmark; repeated/full-corpus runs are still required.
- The Intel UHD is a weak integrated GPU, per the user. Its Vulkan index-1 timing is device-specific
  and must not be generalized to AMD/Intel GPUs. No AMD card was tested, and the physical adapter chosen
  by the runtime was not directly named in its output.

### Remaining scratch tests before production integration

1. Confirm device-index mapping with runtime diagnostics or GPU-utilization evidence; then run repeated
   and full 10-recording comparisons for the actual Vulkan device(s). Test a representative AMD card
   before claiming AMD coverage.
2. If explicit backend selection is exposed, test `Auto`, Vulkan, CUDA, and CPU paths separately and
   assert `RuntimeOptions.LoadedLibrary` equals the request. A mismatching explicit request must be
   reported unavailable, not silently treated as success.
3. When the planned Parakeet ONNX artifact is available, test DirectML, CUDA, and CPU sessions against
   that actual model. Check provider initialization/assignment, a known-audio output, operator coverage,
   latency, and memory; a tiny synthetic ONNX graph proves provider setup only, not Parakeet compatibility.
4. For DirectML, verify the documented restriction against concurrent `Run` calls on one session and
   test a session-pool strategy that fits Phase 2's explicit FIFO queue and active-session semaphore.
5. Publish into empty `win-x64` and `win-x86` folders and launch the artifacts. Verify native runtime
   files, CPU fallback, and that x86 does not try to load the x64 Vulkan runtime.

## Phase 3.2 — ONNX Runtime DirectML for Parakeet

DirectML is a separate ONNX Runtime execution provider, not a Whisper.net backend. Do not describe it
as available on every Windows 11 system: supported device, driver, Windows version, and model-operator
coverage must be checked. ONNX Runtime can partition a graph so provider support for some operators does
not prove that the whole model runs on that provider.[8][9]

Microsoft's current Windows ONNX guidance should be checked before committing to DirectML for a new
Windows application; the existing capability analysis records the DirectML/WinML comparison and links
the official docs.[10][11]

DirectML does not permit concurrent `Run` calls on the same session. Phase 2 explicitly allows completed
audio jobs to wait in a FIFO queue while the active-session semaphore limits concurrent inference.
Therefore, use a verified pool of independent DirectML sessions (or another tested design), measure its
memory cost, and start queued jobs as sessions become available. Do not silently serialize recording
capture or drop queued audio.[9]

Before adding a persistent `OrtBackend` setting, validate a real Parakeet model through the proposed
provider. A provider package alone does not implement an inference engine, and Phase 7's current ONNX
int8 model is the actual consumer. Confirm how Phase 3 will be exercised before Phase 7: a scratch
provider probe or a reusable ORT abstraction is needed so Phase 3 does not leave an unused setting.

## Phase 3.3 — ONNX Runtime CUDA

ONNX Runtime CUDA is separate from Whisper.net CUDA. The existing `CopyCudaRuntimeDlls` target only
copies the Whisper application's configured CUDA DLLs; do not assume it supplies every native DLL or
provider binary required by the selected ONNX Runtime CUDA package. At implementation time, pin matching
ONNX Runtime packages and verify their documented CUDA/cuDNN requirements, native provider loading,
actual session provider assignment, x64 publishing, and CPU fallback.[8][12]

The Roadmap's “CUDA will be significantly faster than DirectML” is a hypothesis to benchmark on the
actual Parakeet model and supported hardware, not an acceptance fact. Compare model load, warm inference,
real-time factor, accuracy, memory use, package size, and fallback behavior under the same conditions.

## Cross-cutting implementation decisions

- The current source contract is `ITranscription`. The roadmap's completed Phase 4.1 records
  `ITranscriptionService`, which does not match the current source; reconcile that documentation
  mismatch before Phase 7 depends on the named interface. Phase 3.1 should extend the existing
  `Whisper`/`IWhisperRuntime` boundary rather than create a duplicate contract.
- Existing Whisper.net model downloading always fetches `GgmlType.Medium`, regardless of the selected
  path. Phase 3.1 must not imply that arbitrary model filenames are automatically downloaded correctly;
  model-specific download mapping is separate work.
- Keep provider/runtime versions aligned and avoid `AllRuntimes` in release artifacts unless all included
  native runtimes are intended. Measure package size and verify licensing/deployment requirements.
- The existing single-file publish excludes native-library self-extraction. A clean publish test must
  verify the selected provider's native files; successful NuGet restore or Debug build is insufficient.
- Keep provider and hardware status truthful: “runtime package present”, “backend initialized”, and
  “backend used for inference” are separate states.

## Open questions to settle before implementation

1. Which AMD and Intel GPU models represent the compatibility acceptance matrix?
2. The user requires an explicitly chosen engine/library to be honored. Separately, should Phase 3.1 expose
   a provider-level CUDA/Vulkan selector, or only status plus Whisper.net `Auto`? The current Roadmap
   specifies automatic detection/status, not a provider picker.
3. Should Phase 3.2/3.3 create and validate a reusable ORT backend now, or should provider integration
   be delayed until Phase 7 has an ONNX engine to consume it?
4. Is upstream Whisper.net Parakeet runtime published and compatible with the planned Parakeet model,
   or is custom ONNX Runtime still required?

## Sources

[1] https://github.com/sandrohanea/whisper.net/blob/main/readme.md — Whisper.net runtime catalog
[2] https://www.nuget.org/packages/Whisper.net — published stable/preview version history
[3] https://github.com/sandrohanea/whisper.net/releases — upstream release history
[4] https://www.nuget.org/packages/Whisper.net.Runtime/1.9.0 — pinned runtime requirements
[5] https://www.nuget.org/packages/Whisper.net.Runtime.Vulkan/1.9.0 — Vulkan package requirements
[6] https://www.nuget.org/packages/Whisper.net.Runtime.OpenVino/1.9.1 — OpenVINO package requirements
[7] https://www.nuget.org/packages/Whisper.net.Runtime.Metal/1.9.1 — Apple Metal package
[8] https://onnxruntime.ai/docs/execution-providers — ONNX Runtime execution providers
[9] https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html — DirectML provider
[10] https://onnxruntime.ai/docs/get-started/with-windows.html — ONNX Runtime on Windows / WinML
[11] https://onnxruntime.ai/docs/execution-providers/WebGPU-ExecutionProvider.html — ONNX Runtime WebGPU
[12] https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html — ONNX Runtime CUDA provider
[13] https://www.vulkan.org/ — Vulkan cross-vendor GPU API overview
