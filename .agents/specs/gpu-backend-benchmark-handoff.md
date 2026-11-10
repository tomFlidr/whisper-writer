# Whisper GPU Backend Benchmark Handoff

**Updated:** 2026-11-05
**Status:** Both the 10-recording Media Foundation comparison and the 10-recording direct-PCM `iAudioBuffer` comparison are complete.

## User's current direction

The user accepts a practical reduction in transcription time even if it is below the roadmap's approximate 3x target. Compare real elapsed time and Czech transcription quality; do not reject a backend solely for missing 3x. The user requests a chat progress update after each recording, with percentage progress across the full test.

## Workspace and preservation

- Project root: `D:/tec/cs/whisper-writer-wpf`
- Continue to preserve the pre-existing worktree changes: modified `.agents/ROADMAP.md`, modified `WhisperWriter/settings.json`, and untracked `.agents/specs/gpu-backend-capability-analysis.md`.
- At this checkpoint, production source and the user's audio files have not been modified.
- Existing architecture analysis remains in `.agents/specs/gpu-backend-capability-analysis.md`.

## Verified source and integration facts

- The current source defines and uses `ITranscription` (`WhisperWriter/Services/ITranscription.cs`); `Whisper` implements it and `MainWindowRuntime` consumes it. The current roadmap instead names `ITranscriptionService`. The Phase 3.1 abstraction is functionally present, with an identifier mismatch; do not add a duplicate interface.
- The application pins Whisper.net 1.9.0. Its startup adds `runtimes/cuda/win-x64` to `PATH`; reproduce this before checking the baseline backend. Verify `RuntimeOptions.LoadedLibrary` rather than inferring active CUDA from DLL presence.
- A clean, isolated Const-me source build succeeded through the solution-level `WhisperNet` target. Building `WhisperNet.csproj` alone fails its packaging step because its nuspec expects the native DLL at the solution-root `x64/Release` path; the solution build supplies that path. Build output includes upstream warnings.
- A .NET 8 Windows x64 single-file probe referencing the locally built `WhisperNet` 1.12.0 package published without `Whisper.dll` in a clean output and failed with `DllNotFoundException`. Manually staging the package's native `Whisper.dll` beside the probe executable in scratch restored adapter enumeration, model loading, and transcription. Production publishing therefore needs an explicit native-DLL copy or a validated extraction configuration; this is not fixed in the app.
- The managed wrapper package adds `ComLightInterop` 1.3.7 and is MPL-2.0; review distribution obligations before any production dependency addition. The shipped backend is x64/Direct3D-GPU oriented; retain a working Whisper.net fallback, especially for x86.

## Prior preliminary benchmarks (not the user recordings)

All used the existing `ggml-medium.bin` and the upstream `jfk.wav` sample. These are smoke/performance probes only, not an acceptance benchmark.

- 11-second file: Whisper.net 1.9.0 CUDA took 5.681 s; Const-me's file-stream example took 4.509 s, while the intended in-memory WAV/Media Foundation path took 5.664 s. The in-memory adapter path is the relevant integration comparison and showed effectively no difference on this one sample.
- A synthetic 45.5-second clip made from four repetitions of the 11-second JFK sample: Whisper.net CUDA took 12.838 s; Const-me's in-memory WAV path took 13.451 s. This synthetic input is not representative of natural Czech speech.
- The native file-stream and in-memory paths have different setup boundaries; do not compare their timings as if they were identical.

## User-provided recordings: preflight completed

- Directory: `./WhisperWriter.Tests/Audio/`
- The user referred to `01.vaw`–`10.vaw`, but the actual files on disk are `01.wav`–`10.wav`; `TEXTS.md` section headings end in `.vaw`. Pair audio and transcript by the shared two-digit number. Do not rename either side.
- All ten actual `.wav` files opened successfully as 16 kHz, mono, 16-bit PCM.
- Durations in order: `24.866, 30.975, 41.935, 30.861, 29.295, 40.051, 31.824, 44.281, 34.472, 41.219` seconds; total `349.780` seconds (5.83 minutes).
- `TEXTS.md` contains ten matching numbered transcripts. Do not copy their contents into another report; use it as local ground truth and pair by numeric stem.

## Planned test

1. Use the verified local settings: model `llms/ggml-medium.bin`, language `cs`, and the configured non-empty prompt from `WhisperWriter/settings.json`. Read the prompt locally at run time; do not print or copy its text into chat, logs, or the checkpoint.
2. Baseline: Whisper.net 1.9.0 with its CUDA runtime and the app's PATH setup; verify the loaded backend is actually CUDA.
3. Candidate: Const-me Whisper through the .NET 8-compatible wrapper, using the in-memory WAV path that matches `ITranscription.TranscribeAsync(byte[] wavBytes, ...)`.
4. For each numbered input, compare both transcript outputs against the corresponding `TEXTS.md` section; record per-engine elapsed time, active backend, and any failures. Normalize case/spacing/punctuation for WER/CER; keep raw reference and hypothesis text only in memory and persist metrics only.
5. Report progress after each paired file as 10%, 20%, ..., 100%, and save each result before moving to the next file. Keep all audio and benchmark outputs local; do not upload the audio to external services.
6. The user's files total 349.780 s. A rough estimate is 5–7 minutes if each file pair starts fresh model processes to allow a paired progress message; inference-only scaling suggests about 3.4 minutes when each backend model stays resident, plus startup/scoring. Actual Czech results may vary.

## Stop conditions

- Stop before changing production code or adding NuGet packages. First report measured results, dependency/license/packaging findings, and a concrete recommendation; obtain approval for the new package before implementation.
- If any input is corrupt or its transcript cannot be paired unambiguously, skip that file and report it rather than renaming or guessing.

## Paired-file progress

- `01.wav` — 10% complete; both engines exited successfully and selected their expected backends. Whisper.net CUDA: 9.668 s; Const-me D3D11: 9.766 s; measured request-time delta: -0.098 s (candidate slightly slower). WER: 0.14286 for both; CER: 0.07512 baseline, 0.06573 candidate. No transcript text or prompt content was copied into the result file.
- `02.wav` — 20% complete; Whisper.net CUDA: 14.365 s; Const-me D3D11: 14.742 s; measured request-time delta: -0.377 s (candidate slower). WER: 0.07463 for both; CER: 0.02564 for both. No transcript text or prompt content was copied into the result file.
- `03.wav` — 30% complete; Whisper.net CUDA: 16.671 s; Const-me D3D11: 16.754 s; measured request-time delta: -0.083 s (candidate slightly slower). WER: 0.20000 baseline, 0.18824 candidate; CER: 0.10769 baseline, 0.10000 candidate. No transcript text or prompt content was copied into the result file.
- `04.wav` — 40% complete; Whisper.net CUDA: 14.101 s; Const-me D3D11: 15.111 s; measured request-time delta: -1.010 s (candidate slower). WER: 0.07692 baseline, 0.06154 candidate; CER: 0.01543 baseline, 0.00926 candidate. No transcript text or prompt content was copied into the result file.
- `05.wav` — 50% complete; Whisper.net CUDA: 10.338 s; Const-me D3D11: 10.358 s; measured request-time delta: -0.020 s (candidate essentially equal but slightly slower). WER: 0.10000 baseline, 0.13333 candidate; CER: 0.02251 baseline, 0.01608 candidate. No transcript text or prompt content was copied into the result file.
- `06.wav` — 60% complete; Whisper.net CUDA: 15.763 s; Const-me D3D11: 16.269 s; measured request-time delta: -0.506 s (candidate slower). WER: 0.15625 baseline, 0.17188 candidate; CER: 0.02145 baseline, 0.02681 candidate. No transcript text or prompt content was copied into the result file.
- `07.wav` — 70% complete; Whisper.net CUDA: 14.844 s; Const-me D3D11: 15.443 s; measured request-time delta: -0.599 s (candidate slower). WER: 0.08475 for both; CER: 0.00865 for both. No transcript text or prompt content was copied into the result file.
- `08.wav` — 80% complete; Whisper.net CUDA: 16.948 s; Const-me D3D11: 17.095 s; measured request-time delta: -0.147 s (candidate slightly slower). WER: 0.01408 for both; CER: 0.00221 for both. No transcript text or prompt content was copied into the result file.
- `09.wav` — 90% complete; Whisper.net CUDA: 14.716 s; Const-me D3D11: 15.283 s; measured request-time delta: -0.567 s (candidate slower). WER: 0.20000 for both; CER: 0.02292 for both. No transcript text or prompt content was copied into the result file.
- `10.wav` — 100% complete; Whisper.net CUDA: 16.626 s; Const-me D3D11: 17.475 s; measured request-time delta: -0.849 s (candidate slower). WER: 0.07143 baseline, 0.05714 candidate; CER: 0.00787 for both. No transcript text or prompt content was copied into the result file.

## Aggregate result: Media Foundation in-memory adapter

- All 10 baseline runs loaded `RuntimeOptions.LoadedLibrary = Cuda`; all candidate runs used Const-me D3D11. All 20 processes exited successfully.
- Total audio: 349.779 s. Request-time total: Whisper.net CUDA 144.040 s; Const-me D3D11 148.296 s. Const-me was 4.256 s slower overall (2.95% slower; average +0.426 s per clip), and slower on all 10 files.
- Weighted WER: both 0.11231 (73/650 word errors). Weighted CER: Whisper.net 0.02925 (101/3453 character errors), Const-me 0.02722 (94/3453), a small character-level improvement but no word-error improvement.
- Actual test wall time was 324.830 s (~5.41 min), including per-file model startup/process overhead. The per-request timings exclude model load.
- The tested Media Foundation byte-array adapter did not save time on this user's corpus; the direct-PCM `iAudioBuffer` comparison below evaluates the alternative input path.

## Direct-PCM `iAudioBuffer` pilot

- A scratch-only adapter converts the existing 16 kHz mono 16-bit WAV payload to float PCM, stores it in unmanaged memory, and implements Const-me's public `iAudioBuffer` interface. It uses the already-present NAudio 2.2.1 dependency and adds no production project reference.
- `01.wav`: direct PCM 8.482 s vs Whisper.net CUDA 9.668 s (1.186 s saved); WER equal at 0.14286 and CER 0.06573 vs 0.07512.
- `02.wav`: direct PCM 14.644 s vs Whisper.net CUDA 14.365 s (0.279 s slower); WER/CER equal.
- `03.wav`: direct PCM 16.785 s vs Whisper.net CUDA 16.671 s (0.114 s slower); WER equal at 0.20000; CER 0.09487 vs baseline 0.10769.
- `04.wav`: direct PCM 15.281 s vs Whisper.net CUDA 14.101 s (1.180 s slower); WER 0.06154 vs baseline 0.07692; CER 0.00926 vs baseline 0.01543.
- `05.wav`: direct PCM 10.206 s vs Whisper.net CUDA 10.338 s (0.132 s saved); WER 0.13333 vs baseline 0.10000; CER 0.01608 vs baseline 0.02251.
- `06.wav`: direct PCM 16.123 s vs Whisper.net CUDA 15.763 s (0.360 s slower); WER 0.17188 vs baseline 0.15625; CER 0.02681 vs baseline 0.02145.
- `07.wav`: direct PCM 15.424 s vs Whisper.net CUDA 14.844 s (0.580 s slower); WER/CER equal.
- `08.wav`: direct PCM 17.693 s vs Whisper.net CUDA 16.948 s (0.745 s slower); WER/CER equal.
- `09.wav`: direct PCM 15.500 s vs Whisper.net CUDA 14.716 s (0.784 s slower); WER/CER equal.
- `10.wav`: direct PCM 16.972 s vs Whisper.net CUDA 16.626 s (0.346 s slower); WER 0.05714 vs baseline 0.07143; CER equal at 0.00787.
- Direct-PCM aggregate: 147.110 s vs Whisper.net CUDA 144.040 s (3.070 s / 2.13% slower), slower on 8/10 files and faster on 2/10. Weighted WER was 0.11385 (74/650 errors) vs baseline 0.11231 (73/650); weighted CER was 0.02664 (92/3453 errors) vs baseline 0.02925 (101/3453). Its aggregate word error and speed were slightly worse; character error was slightly better.
- The direct buffer path works, but does not save time overall on this corpus. The two candidate input paths therefore provide no evidence to integrate Const-me for speed. Any later decision to adopt it for character-level quality would need an explicit quality/latency trade-off and a separately approved packaging/dependency plan.

## Proposed backend selection policy

- The project currently uses `ITranscription`; the roadmap's Phase 4.1 uses the different name `ITranscriptionService`.
- In `Auto`, use Whisper.net's reported active runtime, not CUDA DLL presence. Keep CUDA when `RuntimeOptions.LoadedLibrary` reports CUDA; preserve its CPU fallback.
- Do not select Const-me D3D11 solely because an adapter enumerates or model load succeeds. The Intel UHD smoke used a weak integrated GPU; its slow timing is a device-specific curiosity, not representative evidence about D3D11 or AMD GPUs. Offer D3D11 as an explicit experimental choice until representative non-NVIDIA devices prove a useful benefit, or add a carefully bounded hardware calibration before automatic selection.
- Skip the x64-only candidate in the existing x86 release; keep CPU inference available there. Show the actual engine/device and any fallback reason.
- Source-based platform constraints remain in `.agents/specs/gpu-backend-capability-analysis.md`.

## Non-NVIDIA compatibility smoke

- `Library.listGraphicAdapters()` enumerated `Intel(R) UHD Graphics` on this Quadro T2000 laptop. Explicitly selecting that adapter loaded the medium model with Direct3D feature level 12.1 and successfully transcribed `01.wav` via direct PCM; this confirms this one Intel adapter is usable, not general Intel/AMD support.
- The user clarified that this Intel UHD is a weak, motherboard-integrated GPU. On the 24.866 s clip it took 218.637 s (WER 0.14286), while forced Whisper.net CPU took 19.310 s (WER 0.14286). Treat the timing as a curiosity for this weak iGPU, not as a general D3D11 performance result.
- No AMD GPU or older machine was tested. Treat cross-vendor/legacy coverage as a possible compatibility benefit requiring hardware-specific validation, not as proven acceleration.

- The first candidate attempt failed before transcription because the clean single-file publish omitted `Whisper.dll`; the retry only succeeded after copying that package DLL into the scratch publish directory. Preserve this packaging blocker for the eventual integration decision.
- Machine-readable Media Foundation metrics are in `D:/llms/hermes/cache/scratch/whisper-writer-gpu-benchmark/results.json`; direct-PCM pilot metrics are in `pcm_pilot.json` beside it. Both contain metrics only, not transcript text or the configured prompt.
