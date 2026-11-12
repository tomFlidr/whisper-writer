# Phase 3 — GPU backends and ONNX Runtime

**Status:** Phase 3.0 is locally verified. Vulkan 1.9.0 is referenced; clean x64/x86 publish assets and an x86
CPU launch are locally verified. A full 10-recording Vulkan/CUDA comparison also passed on the available
NVIDIA/Intel host; the representative AMD matrix remains open. DirectML 1.24.4 and CUDA 1.31.1 run from
separate provider plugins through the WPF host's production ALC/session path. Hosted CI has not run. Required
3.4 CUDA/cuDNN provisioning, 3.5 Parakeet engine, and 3.6 transactional engine selection are not implemented,
so Phase 3 is not complete.
**Last package metadata check:** 2026-11-11
**Phase 3.0 code verification:** 2026-11-11
**Roadmap:** `.agents/ROADMAP.md`, items 3.1–3.6.
**Progress checkpoint:** `.agents/phase-3-progress.md` (read when resuming Phase 3 and before a context handoff).
**Related:** `gpu-backend-capability-analysis.md` (architecture),
`gpu-backend-benchmark-handoff.md` (Phase 1 evidence).

## Purpose and boundaries

Phase 3 contains four distinct integration paths, not one interchangeable “GPU” switch:

1. **Whisper.net Vulkan** for the existing Whisper GGML engine, alongside its current CUDA/CPU runtimes.
2. **ONNX Runtime DirectML** for the Parakeet V3 engine implemented in Phase 3.5.
3. **ONNX Runtime CUDA** as a separate NVIDIA execution provider for that ONNX engine.
4. **Parakeet V3 transcription** using the app-owned ONNX provider factory and isolated CPU/DirectML/CUDA packs.
   Phase 3.6 adds transactional selection between the implemented Whisper and Parakeet engines.

Whisper.net Vulkan is not the same provider as ONNX Runtime Vulkan, and neither is
ONNX Runtime DirectML. Keep model format, inference engine, execution provider, and physical device
as separate concepts. The existing `.agents/specs/gpu-backend-capability-analysis.md` contains the
shared model catalog, capability resolver, Settings, and concurrency architecture.

## Phase 3.0 — Shared prerequisite: verified runtime identity and ETA fingerprint

Before implementing 3.1–3.6, audit every production use of CUDA/DLL presence, GPU inventory, or runtime
version that claims a backend is available, supported, or active. A DLL or adapter being present is a
prerequisite/diagnostic signal only; it does not prove that the selected model initialized or that
inference uses that provider. Keep required PATH setup and native-library packaging where needed, but do
not present them as capability validation.

The implementation now uses an immutable `ActiveBackendInfo` descriptor with stable engine, runtime, and
provider IDs, a verified `cpu`/`gpu` device class, and optional authoritative CUDA major version. It is
created only after `WhisperFactory` successfully initializes. The main widget, each `TranscriptionJob`, ETA
lookup/result recording, and inference-capacity fingerprints use that descriptor. Unknown runtimes remain
unclassified; they are not silently mapped to CPU or GPU. No physical GPU name is claimed because the
Whisper.net library identity does not provide a stable selected-device name.

`EtaCalc` no longer probes the machine for CUDA. It serializes the descriptor under the nested `backend`
object; old SQLite rows are preserved but their provider-ambiguous environment fingerprints are not reused.
The old `Whisper.DetectCudaVersion()` probe and its P/Invoke helper were removed. The CUDA PATH setup and
build-time DLL copy remain loader/deployment prerequisites only, not capability detection.

### ETA environment JSON

Replace the current top-level `backend: "GPU" | "CPU"` plus top-level `cudaVersion` with a structured
`backend` object. Keep the rest of the environment fingerprint, including `gpus`, separate: it describes
hardware inventory, not the device selected for this inference.

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

For a verified CPU runtime, use `"device": "cpu"`, `"providers": ["cpu"]`, and `"cudaVersion": null`.

- `device` is `cpu` or `gpu`, derived from the verified active provider.
- `engine` is a stable application engine ID (for example, `whisper` or `parakeet`); `runtime` identifies
  the inference runtime (for example, `whisper.net` or `onnxruntime`); `providers` is a deterministically
  ordered list of execution providers confirmed to perform work (for example, `cuda`, or `directml` plus
  `cpu` when ONNX Runtime partitions the graph across both).
- Set `device` to `gpu` only when a GPU provider actually performs work; if all assigned work is on CPU,
  use `cpu`. A registered or available GPU provider alone is not sufficient.
- Put `cudaVersion` inside `backend`; populate it only when the initialized CUDA runtime provides an
  authoritative version. Otherwise store `null`; never infer it from the presence of versioned DLLs.
- Do not create or reuse an ETA environment profile until the engine/runtime/provider identity can be
  classified. Preserve existing SQLite rows, but do not reuse their old provider-ambiguous fingerprints
  for the new schema; verify that the new JSON produces distinct environment fingerprints.

Add deterministic tests for the supported provider-to-device mappings, neutral loading state, actual
fallback identity, JSON serialization, and fingerprint separation. DLL-only presence must never pass a
test that claims backend support.

**Phase 3.0 result:** locally verified on 2026-11-11: **235/235 Debug tests**, **235/235 Release tests**,
and application builds with **0 warnings and 0 errors** in both configurations. Hosted GitHub Actions was
not run.

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
  `Whisper.net.Runtime`, `Whisper.net.Runtime.Cuda`, and `Whisper.net.Runtime.Vulkan` to **1.9.0**.
  CUDA/Vulkan package references are conditioned out of `win-x86`; the release restore passes an explicit
  `RuntimeIdentifier` property so the NuGet graph evaluates the RID conditions.
- `.github/workflows/release.yml` publishes `win-x64` and `win-x86`; the x86 artifact is CPU-only.
  The Vulkan NuGet runtime is Windows x64 only, so x86 must retain a working CPU path.
- `WhisperWriter/Services/Whispers/NetRuntime.cs` calls `WhisperFactory.FromPath(modelPath)` without
  assigning a custom runtime order. The installed 1.9.0 package XML documents this default order:
  `Cuda`, `Vulkan`, `CoreML`, `OpenVino`, `Cpu`, `CpuNoAvx`. It also exposes
  `RuntimeOptions.LoadedLibrary` for reporting which library actually loaded.
- `NetRuntime.ActiveLibrary` reads `RuntimeOptions.LoadedLibrary`; after successful factory creation,
  `Whisper.ActiveBackend` maps recognized library IDs into the verified descriptor. `ITranscription` no
  longer exposes a parallel string-only `ActiveProvider` contract.
- `Whisper.InitializeAsync()` and `MainWindow.OnActivated` show neutral `Loading model…`; ready status uses
  only the descriptor. Unknown runtime IDs remain unclassified.
- `EtaCalc` accepts the job's backend snapshot for both estimate and record. Its JSON nests device, engine,
  runtime, provider list, and optional CUDA version. A job without a descriptor creates no ETA sample.
- `Services/Inference/Capacity/Manager` fingerprints the complete descriptor. Queue/capacity memory checks use its
  primary provider ID; the provider label is not inferred from CUDA DLL presence.
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
version pinned by this app; verify the exact NuGet package/version before considering it for Phase 3.5.
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

1. The approved `Whisper.net.Runtime.Vulkan` **1.9.0** reference now matches the project's pinned core,
   CPU, and CUDA runtime package set. The 1.9.1 stable package was not mixed into this release; upgrade
   all Whisper.net references together only after a separate compatibility decision.
2. Let the pinned Whisper.net loader perform its documented default runtime selection unless a
   measured requirement calls for an explicit order. Do not implement `LoadLibraryEx("vulkan-1.dll")`
   as proof of an active Vulkan inference backend. A loader DLL may exist while device initialization
   or model loading still fails.
3. After factory/model initialization, map `RuntimeOptions.LoadedLibrary` into `ActiveBackendInfo` and use
   that descriptor for status and ETA identity. Report a physical device only if a stable API provides it.
4. Preserve Whisper.net CPU fallback and the x86 CPU artifact. If a Vulkan preference is ever exposed,
   distinguish “requested” from “actually loaded” and display the fallback reason.
5. Validate the exact package version, Windows 11, architecture, driver/runtime, and clean publish
   output before release. Windows 10 is not a Phase 3 support target.

### Verification gates

- **Locally completed:** the production Whisper runtime reports the actual loaded library; clean x64/x86
  publish assets were inspected; a fresh x86 app launch loaded `ggml-small.bin` and reported `Ready (Cpu)`.
- **Locally completed:** CUDA `Auto` baseline and Vulkan-restricted order were exercised across all ten
  user-provided Czech WAV files, with actual `Cuda`/`Vulkan` runtimes asserted. Detailed times and WER/CER
  are recorded below; transcripts were not persisted.
- **Open:** a representative AMD GPU test is still required before claiming AMD compatibility. The current
  host has Intel UHD and NVIDIA Quadro T2000 only; Vulkan used requested device index 0, but the runtime did
  not provide a stable physical-device name.
- **Open:** hosted GitHub Actions has not run. Local packaging or smoke tests do not substitute for hosted CI.
- Add deterministic tests around provider selection/status/fallback using runtime fakes. Do not make hardware
  availability a requirement for the standard CI suite; reserve real GPU tests for an opt-in/manual matrix.
- Verify the Phase 3.0 ETA JSON schema and ensure only the actually loaded engine/runtime/provider contributes
  to its environment fingerprint; preserve old rows without treating their DLL-derived labels as history.

### Full-corpus Vulkan versus CUDA comparison (2026-11-11)

- Compared the ten WAV files in `WhisperWriter.Tests/Audio/` against their matching `TEXTS.md` references.
  All audio was 16 kHz mono 16-bit PCM, totaling **349.780 seconds**. Both engines used the configured
  `ggml-medium.bin`, language `cs`, and the local Settings prompt; prompt and transcript contents were not
  written to the metrics file.
- The x64 scratch probe ran one process per provider per recording. CUDA `Auto` loaded `Cuda` on all ten files;
  the forced Vulkan runs loaded `Vulkan` on all ten. Vulkan used requested `GpuDevice=0`; no physical adapter
  name was returned by Whisper.net.
- Sum of transcription request times: CUDA **198.134 s**, Vulkan **125.208 s**. Vulkan was faster on all
  **10/10** recordings and saved **72.926 s (36.81%)** of request time. Model initialization totals were
  CUDA **17.994 s** and Vulkan **21.999 s**; combined fresh-process totals were 216.128 s and 147.207 s.
- Weighted WER was identical: **73/650 words (11.231%)**. Weighted CER was CUDA **121/4093 (2.956%)** and
  Vulkan **122/4093 (2.981%)** (one additional character error on Vulkan). This is one host/corpus comparison,
  not AMD evidence or a general performance guarantee.
- Metrics only are stored under scratch at
  `D:/llms/hermes/cache/scratch/phase3-vulkan-full-corpus-results.json`; no raw hypotheses were saved.

### Local package/publish verification (2026-11-11)

- NuGet restore and Debug solution build passed after adding Vulkan 1.9.0; the focused backend/Whisper/UI
  selection passed **50/50 tests**.
- A first `win-x86` publish reproduced an asset leak: the Vulkan package copied five x64 DLLs and the CUDA
  package copied its x64 runtime despite the x86 RID. The project now excludes both GPU package references
  and CUDA copy targets for `win-x86`; release restore passes `-p:RuntimeIdentifier=<RID>` because
  `dotnet restore -r win-x86` alone did not apply the package conditions.
- Fresh local `win-x64` publish contains all five Vulkan DLLs and the three CUDA toolkit dependencies;
  fresh `win-x86` publish contains the x86 CPU runtime and no CUDA/Vulkan assets. The x64 output is under
  `D:/llms/hermes/cache/scratch/phase3-vulkan-x64-cuda-20261111`; x86 is under
  `D:/llms/hermes/cache/scratch/phase3-vulkan-x86-cpu-20261111`.
- The release workflow was updated to use the same RID-aware restore and no longer edits the project file
  to remove the CUDA copy target. Hosted GitHub Actions has not run. A fresh x86 WPF launch under
  64-bit Windows/WOW64 loaded `ggml-small.bin` and reported `Ready (Cpu)`; an x64 fresh-app launch was not
  separately run.
- A follow-up isolated Whisper.net smoke constrained runtime order to CUDA then CPU and launched with a
  child `PATH` that excluded CUDA runtime directories. `RuntimeOptions.LoadedLibrary` reported `Cpu` and
  transcription exited successfully. The probe transcript was not saved. This verifies the CPU fallback in
  an isolated x64 process, but not x86 audio transcription or Auto selection while Vulkan remains available.

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
  65.330 s, later CUDA 2.252 s; Vulkan 2.446–3.442 s). This preliminary one-recording smoke is not a
  performance benchmark; see the full 10-recording comparison below.
- The Intel UHD is a weak integrated GPU, per the user. Its Vulkan index-1 timing is device-specific
  and must not be generalized to AMD/Intel GPUs. No AMD card was tested, and the physical adapter chosen
  by the runtime was not directly named in its output.

### Remaining Phase 3.1 validation

- Test a representative AMD GPU before claiming broad AMD compatibility. The current host has an Intel UHD and
  NVIDIA Quadro T2000 but no AMD card.
- Vulkan used requested device index 0; Whisper.net does not return a physical device name. `vulkaninfo` lists
  adapters, but the exact runtime-to-device mapping is not confirmed. Use utilization or vendor diagnostics
  before claiming which adapter handled the full-corpus run.
- The fresh x86 app package launched and reached `Ready (Cpu)` with `ggml-small.bin`; x86 audio transcription
  was not tested. The package-layout gate and app launch are locally verified.
- The Release GitHub Actions workflow has not run. It is triggered by a version-tag push; a hosted run needs
  explicit authorization to create/push a tag. Local package validation is not a hosted CI result.

---

## Phase 3.2 — ONNX Runtime DirectML for Parakeet

DirectML is a separate ONNX Runtime execution provider, not a Whisper.net backend. Do not describe it
as available on every Windows 11 system: supported device, driver, Windows version, and model-operator
coverage must be checked. ONNX Runtime can partition a graph so provider support for some operators does
not prove that the whole model runs on that provider.[8][9]

Microsoft's current Windows guidance prefers WinML for new Windows applications and places DirectML in
sustained engineering.[10][14][15] The approved Phase 3 implementation nevertheless uses the named ONNX
Runtime DirectML package. This is not a claim that DirectML is the preferred choice for every new Windows app.

The DirectML provider is implemented in the x64-only `WhisperWriter.OrtRuntime.DirectML` plugin project
with `Microsoft.ML.OnnxRuntime.DirectML` **1.24.4**. Its native core is not referenced by the main WPF
project. The host loads the selected plugin through an `AssemblyLoadContext` and `AssemblyDependencyResolver`,
sharing only the app-owned contract assembly. A process uses one ORT native core; switching provider
requires a full restart. Native search directories are added to the application process only.

`AppSettings.OrtBackend` supports `Auto`, `Cpu`, `DirectMl`, and `Cuda`. `Auto` selects DirectML when its
provider pack is installed, otherwise CPU. If DirectML initialization fails under `Auto`, the plugin can
create a CPU-only session inside the same DirectML ORT core. An explicit `DirectMl` choice fails instead
of crossing to another provider/core. The session uses `ExecutionMode.ORT_SEQUENTIAL`, disables
`EnableMemoryPattern`, and serializes `Run` calls on that session.[9]

The app's active-backend descriptor is derived after inference from the ORT profile, not from package/DLL
presence. DirectML profiles on all three verified Parakeet graphs (`nemo128`, encoder, decoder/joint)
showed `DmlExecutionProvider` plus `CPUExecutionProvider`, so the implementation reports a mixed graph.
An intentionally invalid DML device exercised the `Auto` same-core CPU fallback and returned a CPU-only
profile plus the fallback reason. Running the same failure under explicit `DirectMl` exited nonzero rather
than silently falling back. Inputs were synthetic; there is no known-audio transcription, WER/CER,
latency, memory, or full Parakeet decoder pipeline test.

The x64 application release includes the CPU provider pack. DirectML is a separate optional ZIP attached
under `runtimes/onnx/directml` and includes its Microsoft license and third-party notices. For v1.4.0,
the workflow builds the CUDA provider pack for CI verification but does not attach it to the GitHub
Release. Hosted release workflow has not run, so verify the final ZIPs before claiming a published artifact.[14]

## Phase 3.3 — ONNX Runtime CUDA

ONNX Runtime CUDA is separate from Whisper.net CUDA. The x64-only `WhisperWriter.OrtRuntime.Cuda` plugin
uses `Microsoft.ML.OnnxRuntime.Gpu.Windows` **1.31.1**. Its native core is isolated from both the app and
the DirectML plugin. The explicit `Cuda` choice does not silently switch providers; `Auto` does not select
CUDA. Active identity is reported only after inference profiling and can include CPU partitions.

The CI-only CUDA provider ZIP contains the ORT CUDA core/provider, not the CUDA/cuDNN runtime. It is not
attached to the v1.4.0 release. ORT 1.31.1 requires a compatible CUDA 13 runtime and cuDNN 9. The host
prepends the configured `OrtCudaRuntimePath`, the app's existing `runtimes/cuda/win-x64` folder when
present, and the provider's native directories to the process search path while preserving the existing
`PATH`. The Whisper.net CUDA runtime is separate and may not provide every ORT dependency, especially
cuDNN. The local smoke used CUDA Toolkit 13.2 and cuDNN 9.19 from the existing ComfyUI PyTorch
installation; that is not an end-user runtime bundle.[12][15]

Through the production settings/catalog/factory/ALC path, all three Parakeet graphs ran with synthetic
inputs. Profiles showed `CUDAExecutionProvider` plus `CPUExecutionProvider`; the encoder graph logged 742
memcpy nodes and the decoder/joint graph 21. This confirms provider assignment, not a speed advantage.
No audio-to-text pipeline, accuracy result, real-time factor, memory benchmark, or hosted release run has
been produced. Do not claim CUDA is faster than DirectML without an identical-input benchmark.

### Scratch provider initialization smoke (2026-11-11)

- DirectML 1.24.4 and CUDA 1.31.1 were exercised in separate .NET 8 x64 scratch applications against the
  installed ONNX Runtime `mul_1.onnx` test model. Both session creation and inference succeeded and returned
  the expected tensor values. ORT profiling recorded `DmlExecutionProvider` for the DirectML run and
  `CUDAExecutionProvider` for the CUDA run.
- The CUDA smoke used the installed CUDA 13.2 toolkit and cuDNN 9.19 DLL from an existing PyTorch 2.11
  `cu130` environment. No CUDA/cuDNN DLL was copied into the application project. This proves one local
  CUDA package/runtime path works, but does not establish an end-user redistributable package.
- The scratch probes are under `D:/llms/hermes/cache/scratch/phase3-ort-dml-probe-20261111` and
  `D:/llms/hermes/cache/scratch/phase3-ort-cuda-probe-20261111`. The model is a tiny provider smoke, not the
  planned Parakeet graph. These separate runs also do not prove the two different ORT native cores can
  coexist in one WPF process or that provider selection can switch after the first ORT call.

### Parakeet graph session smoke (2026-11-11)

- Archive `parakeet-v3-int8.tar.gz` is 478,517,071 bytes with the pinned SHA256
  `43d37191602727524a7d8c6da0eef11c4ba24320f5b4730f1a2497befc2efa77`; it was extracted only under Hermes
  scratch. The archive contains Mac `._` sidecars, which were not extracted.
- The verified model files are `nemo128.onnx` (139,764 bytes), `encoder-model.int8.onnx` (652,183,999 bytes),
  `decoder_joint-model.int8.onnx` (18,202,004 bytes), `vocab.txt`, and `config.json`. This corrects the earlier
  Parakeet roadmap entry's `encoder-model-int8.onnx` / `decoder_joint-model-int8.onnx` spellings.
- DirectML 1.24.4 loaded each graph and ran one synthetic input. All three ORT profiles reported both
  `DmlExecutionProvider` and `CPUExecutionProvider`.
- CUDA 1.31.1 loaded each graph and ran one synthetic input using local CUDA 13.2 and cuDNN 9.19. All three
  profiles reported both `CUDAExecutionProvider` and `CPUExecutionProvider`. ORT logged 742 memcpy nodes for
  the encoder and 21 for the decoder/joint graph; benchmark impact is not yet measured.
- Scratch projects are under `D:/llms/hermes/cache/scratch/phase3-parakeet-dml-probe-20261111` and
  `D:/llms/hermes/cache/scratch/phase3-parakeet-cuda-probe-20261111`. Inputs were synthetic tensors, not
  audio, so this is not a validated transcription pipeline, accuracy result, or full-graph GPU-only claim.

### Provider ALC isolation smoke (2026-11-11)

- A separate .NET 8 host used provider-specific plugin assemblies loaded through a custom
  `AssemblyLoadContext` and `AssemblyDependencyResolver`. The host shared only an app-owned contract assembly;
  each plugin used its own ORT-managed/native package version. ORT's default resolver was disabled inside the
  selected plugin before its first session, and its ALC resolved the native `onnxruntime.dll`.
- DirectML 1.24.4 and CUDA 1.31.1 were run in separate processes; each plugin loaded and ran all three
  Parakeet graphs with synthetic tensors. Profiles again identified CPU plus the selected GPU EP. The host
  did not load both native ORT cores in the same process and did not attempt hot switching.
- The WPF application now includes the same contract-sharing ALC loader, provider catalog, settings-based
  factory, and process-local native dependency search. Local smoke runs through that production factory
  exercised CPU, DirectML, and CUDA packs; switching ORT cores remains restart-only. The final GitHub
  release packaging and hosted workflow are still unverified.

## Phase 3.4 — CUDA/cuDNN runtime provisioning (required for Phase 3 closure)

### Current gap

The ONNX CUDA provider pack built for CI contains the ORT CUDA core/provider but not the compatible
CUDA 13 and cuDNN 9 runtime DLLs. It is not attached to the v1.4.0 release. `OrtCudaRuntimePath` accepts
a manually selected directory, and the local smoke depended on the developer's CUDA Toolkit 13.2 and cuDNN
9.19 from a PyTorch environment. That is engineering evidence, not an end-user distribution solution.
Do not mark Phase 3 complete with this gap.

### Required outcome

A user selecting the optional ONNX CUDA pack must not have to find, install, or copy individual DLLs by
hand. Resolve and implement a legally supportable provisioning path for every native runtime dependency
required by the pinned ORT CUDA provider:

- Bundle compatible, redistributable runtime DLLs in the optional CUDA package after recording the exact
  license/redistribution review; or
- provide an app-managed download/bootstrap from an authoritative source, with explicit user consent,
  pinned compatible versions, integrity verification, clear progress/errors, and safe caching.

If neither path is legally supportable, stop and present the owner with the concrete licensing constraint
and supported alternatives; a manual DLL path alone does not satisfy this phase. `OrtCudaRuntimePath` may
remain as an advanced override, not as the ordinary installation requirement.

### Acceptance criteria

1. Record the exact CUDA/cuDNN/runtime dependency versions, official source or redistribution basis, license
   notices, integrity checks, and final package/install layout.
2. On a clean Windows x64 test environment without a developer CUDA Toolkit, Python/PyTorch/ComfyUI, or
   preconfigured global PATH, the user can provision the CUDA provider through the supported app/release
   flow without manually locating or copying DLLs.
3. A compatible NVIDIA test machine can create a session and run the verified Parakeet graphs through the
   production loader using only the provisioned dependencies; ORT profiles must confirm actual assignment.
4. Missing, corrupt, incompatible, or declined dependencies produce a specific actionable result and leave
   CPU/DirectML options and existing Whisper behavior intact. Do not report CUDA active from file presence.
5. No installer/download modifies machine-wide PATH or silently downloads runtime binaries without consent.
   x86 remains without the ONNX CUDA provider.
6. The final optional package size and all license/third-party notices are verified from the actual staged
   archive. A hosted workflow is reported separately from local package/smoke results.

## Phase 3.5 — Parakeet V3 transcription engine (moved from the former Phase 7)

### Scope

Implement the planned Parakeet V3 int8 ONNX engine using the verified model archive and the isolated ONNX
provider factory. The existing 3.2/3.3 smoke proves only graph/session bring-up with synthetic tensors; it is
not a completed transcription engine. Phase 3.5 now owns the former Phase 7 model download, model discovery,
service, and speech-to-text pipeline work.

### Required implementation

- Extend `WhisperWriter/download-models.ps1` to download the pinned Parakeet archive, verify its SHA256 before
  extraction, reject unsafe archive paths, and place the verified model directory under `llms/`.
- Add engine/model metadata so `SettingsWindow` can distinguish Whisper `.bin` files from a complete Parakeet
  model directory. Persist `EngineType` with a backward-compatible default of `Whisper`; do not silently
  change engine because a directory or DLL is present.
- Implement `ParakeetService` against the current `ITranscription` contract. Use `Services/OnnxRuntime/BackendFactory`
  and app-owned contracts; keep ORT bindings and native cores inside the existing provider projects.
- Implement preprocessing, encoder, TDT decoder loop, vocabulary lookup, and the agreed 250 ms Parakeet audio
  pre-roll. Keep model-specific input/output names and tensor shapes aligned with the verified archive.
- Pass `ActiveBackendInfo` from the actual ORT profile into each transcription job and ETA path. Keep CPU
  fallback and provider-specific selection truthful; do not equate provider registration with inference use.

### Acceptance criteria

1. Model download verifies the pinned archive hash before extraction and produces all required model files;
   missing, corrupt, or incomplete model directories fail clearly.
2. CPU runs a known-audio Parakeet transcription through the production service from model loading through
   decoder output. Record the transcript and WER/CER or other agreed quality metric; synthetic graph runs alone
   do not pass this gate.
3. Deterministic tests cover model discovery, preprocessing/decoder orchestration, errors, and engine/runtime
   identity without requiring a GPU. Keep real GPU checks opt-in.
4. On compatible hardware, the production service can run with DirectML or CUDA and reports the actual mixed
   CPU/GPU provider profile. A selected provider that fails must not silently become another engine.
5. Preserve Whisper behavior and existing settings/history. The Parakeet engine must use the shared `ITranscription`
   contract rather than adding a parallel `ITranscriptionService` API.

## Phase 3.6 — transactional interactive engine selection

### Goal

Settings must distinguish the transcription engine (Whisper versus the now-implemented Parakeet engine) from
that engine's execution provider (for example, CUDA or DirectML). Changing engines must be a guarded
transaction, not a setting that is saved before the selected engine has been checked.

### Required flow

1. When the user selects a different engine, enter a visible switching/preflight state. Stop admitting new
   transcription jobs and show that existing work is draining; do not discard already accepted audio.
2. Wait until every active and queued transcription job has completed. Only then initialize/preflight the
   candidate engine with its selected model and provider. Show the actual initialization result and verified
   runtime/provider identity; never infer success from a pack or DLL being present.
3. Keep **Save** disabled until candidate initialization succeeds. On failure, show the reason, keep the
   persisted selection and active engine unchanged, and do not permit saving the failed engine selection.
4. On success, enable **Save**. Persist/activate the candidate only when the user confirms Save; Cancel or
   closing Settings restores the prior selection and resumes the prior engine.
5. Prevent new PTT work from entering the queue during the drain/preflight transaction. Resume admission
   after success, failure, or cancel, and preserve FIFO/result ordering for work already accepted.
6. The candidate check must not irreversibly pin the live process to an uncommitted ORT native core. Choose
   and verify a safe preflight/commit boundary (for example, an isolated helper process or an explicit
   restart transaction); do not rely on unloading an `AssemblyLoadContext` to switch native ORT cores.
7. If activation requires restart, say so before committing and do not label the candidate active until it
   initializes in the process that will perform inference. Only enable Parakeet after its Phase 3.5 service
   initializes successfully.

### Acceptance tests

- Active plus queued jobs drain before candidate initialization begins; new recording admission is paused
  only for the transaction and resumes afterward.
- A failed candidate leaves persisted settings, active engine, and accepted job results unchanged; Save
  remains disabled and the error is visible.
- A successful candidate enables Save but does not change persisted settings before the user confirms.
  Cancel restores the original engine; Save commits only the validated selection.
- ORT core isolation/restart behavior is exercised without relying on hardware in the required suite; a
  compatible-hardware smoke separately verifies real candidate initialization and active-provider reporting.

## Cross-cutting implementation decisions

- The current source contract is `ITranscription`. The roadmap's completed Phase 4.1 records
  `ITranscriptionService`, which does not match the current source. Phase 3.5 must use and, if needed,
  deliberately evolve the existing `ITranscription` contract rather than creating a duplicate API.
  `Whisper`/`IWhisperRuntime` remains the existing Whisper boundary.
- Existing Whisper.net model downloading always fetches `GgmlType.Medium`, regardless of the selected
  path. Phase 3.1 must not imply that arbitrary model filenames are automatically downloaded correctly;
  model-specific download mapping is separate work.
- ORT provider packs live in separate x64 projects with their own version-matched managed binding and
  native core. The WPF host shares only the app-owned contracts and loads one selected pack through an
  ALC before the first ORT call. Changing provider/core requires a full app restart. Never flatten the
  DirectML 1.24.4 and CUDA 1.31.1 cores into the main app root or hot-switch them.
- The x64 app distribution contains the CPU pack; DirectML is an optional ZIP in v1.4.0. The CUDA pack
  remains a short-lived CI artifact and is not attached to the release. Provider packs include their
  license and third-party notices. The CUDA pack excludes CUDA/cuDNN runtime DLLs; the host also searches
  the app's existing Whisper CUDA folder when present, but users may need compatible CUDA 13/cuDNN 9
  dependencies via the process-local path setting.
- Before an ORT core initializes, the host sets process-local `ORT_DISABLE_TELEMETRY=1`; each provider
  plugin also calls `OrtEnv.Instance().DisableTelemetryEvents()` before creating a session. Active backend
  and ETA identity still come only from the actual ORT profile after inference, never from pack presence.
- The single-file WPF publish does not flatten ORT native files into the executable/root. Fresh x64
  publish validation found no ORT core at the app root; the x86 artifact contains no ORT provider pack.
  The optional packs retain their own `.deps.json` and native dependencies.
- Keep provider and hardware status truthful: “runtime package present”, “backend initialized”, and
  “backend used for inference” are separate states.

## Open validation questions

1. Which AMD GPU model and driver should represent the compatibility acceptance matrix? No AMD device has been
   tested; broad vendor support remains unverified.
2. Confirm the physical Vulkan adapter used for requested `GpuDevice=0` with utilization/vendor diagnostics;
   Whisper.net does not expose a stable device name. The full-corpus comparison and fresh x86 app launch are
   complete locally.
3. Phase 3.5 must add known-audio Parakeet transcription, accuracy, latency, and memory validation; current ORT
   graph tests used synthetic tensors only.
4. Phase 3.5 must implement against the verified ONNX int8 artifacts; do not assume a separate stable
   Whisper.net Parakeet runtime consumes those files.
5. Phase 3.4 must choose a legally supported, verifiable CUDA/cuDNN distribution/bootstrap source; until then,
   the ONNX CUDA pack is not a complete end-user installation.
6. Phase 3.6 must choose a safe candidate-engine preflight/commit boundary that does not pin the live process
   to an uncommitted ORT core, and define when Save applies immediately versus requiring restart.

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
[14] https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime.DirectML/1.24.4 — DirectML native package
[15] https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime.Gpu.Windows/1.31.1 — CUDA native package
[16] https://blob.handy.computer/parakeet-v3-int8.tar.gz — planned Parakeet archive; verify the pinned SHA256 before use
[17] https://onnxruntime.ai/docs/reference/privacy.html — ONNX Runtime telemetry and privacy controls
[18] https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support — .NET plugin load contexts
[19] https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/loading-unmanaged — native library resolution in .NET
