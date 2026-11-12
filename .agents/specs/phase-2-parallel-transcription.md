# Phase 2 — Parallel recording and transcription

**Status:** Implementation complete (November 10, 2026). Per-job target routing, same-target recording
order, ETA display, queue limits, and responsive startup are implemented. Debug and Release each pass
217/217 tests; both app builds have 0 warnings/errors. The production DXGI monitor has a read-only smoke
result on this Windows host. Live microphone/PTT was not smoke-tested; active Vulkan physical-device
mapping remains a separate, unverified Phase 3 check.
**Roadmap:** `.agents/ROADMAP.md`, Phase 2.
**Baseline verified:** Debug application build succeeded with 0 warnings/errors; 185/185 Debug tests passed before changes.
**Related:** `.agents/specs/phase-0-coverage-inventory.md`,
`.agents/specs/gpu-backend-capability-analysis.md`,
`.agents/specs/phase-3-gpu-and-onnx-backends.md`.

## Goal

After a push-to-talk release, the audio batch becomes an independent transcription job. A new recording
must be able to start while older jobs are still transcribing. If all active inference slots are full,
the completed audio job waits in an explicit FIFO transcription queue; the recording itself never waits
for inference. No batch, result, error, target window, or job status may be overwritten or assigned to
a different recording.

## Baseline findings (before Phase 2 implementation)

- `Hotkey` raises start/stop events from its polling thread. `MainWindow` marshals WPF operations to the
  Dispatcher.
- `MainWindow.handlePushToTalkStopped()` invokes an `async` lambda through `Dispatcher.Invoke` and
  awaits the transcription. Because the lambda is `async void`, Dispatcher control can return at the
  first incomplete await; some overlap may already occur. It is not an explicit, tracked job system and
  its exceptions/state are difficult to correlate.
- `MainWindow` reuses one ETA timer and one status label. A later job can reset the earlier job's timer
  or status; a completion/error from one request can visually overwrite another request's state.
- `Whisper.TranscribeAsync()` creates a processor per call using one shared `WhisperFactory`, but also
  emits the same global `StateChanged` event for each request. The runtime's thread-safety and GPU memory
  behavior under concurrent calls have not been verified.
- `TextInjector` stores the saved foreground HWND in a static field. A second PTT start overwrites it,
  so the first job may restore focus to the second job's target. Focus capture currently occurs at PTT
  start; preserve that timing, but carry an immutable target per job.
- `AudioRecorder.StopRecording()` returns a fresh WAV byte array and disposes the current capture;
  `StartRecording()` can create a new capture. Each released batch can therefore own its audio bytes.
- `TranscriptionHistory.Add()` is thread-safe and inserts at the top when a result completes. With
  overlap, history order will otherwise reflect completion time rather than recording order.
- `MainWindowTest` currently uses immediately completed fake transcriptions, so it does not prove a
  second job starts before a first delayed job completes.

## Required behavior

1. Capture an independent immutable request at each PTT release, with a stable job ID/sequence, audio
   bytes, target HWND captured at that recording's PTT start, and the settings needed for that request.
2. Start a transcription immediately when a semaphore slot is available. If all active slots are full,
   enqueue the completed immutable job FIFO and start it as soon as a slot is released. Do not delay or
   serialize audio capture because inference slots are occupied.
3. Keep job results and errors isolated. A failure, blank output, or completion from one job must not
   cancel, relabel, or overwrite another job's status.
4. Restore focus and inject each result into the target window captured for that job. Do not read a
   shared “latest foreground window” when the transcription completes.
5. Keep the UI thread responsive. Audio stop/start and WPF updates are dispatched synchronously; the
   transcription task itself must not be awaited inside an `async void` Dispatcher callback.
6. Keep the current CPU/CUDA runtime behavior unless a separate provider preference says otherwise.
   Limit active inference with an explicit semaphore; the FIFO waiting queue is intentional, visible,
   and bounded separately from the active-job limit.
7. Keep the standard automated suite independent of a microphone, GPU, or downloaded model. Use
   controlled task completions and fakes for deterministic concurrency and request-isolation tests.
8. Keep the startup widget responsive while CUDA availability and the model are initialized. Show
   `GPU Detection…` immediately, report the detected CUDA/CPU path, and run synchronous native/model
   initialization work off the WPF Dispatcher.

## Design proposal

- Introduce a small per-recording job/context object (stable ID, capture sequence/time, immutable WAV
  bytes, focus target, language/prompt/hotkey snapshots, model key, per-job state/error).
- Capture the HWND at PTT start; pass that exact handle through the job to explicit
  `RestoreFocus(targetHwnd)` and background `InjectText(..., targetHwnd)` operations. Remove reliance on
  `TextInjector`'s shared static `_savedHwnd` for concurrent jobs.
- Replace shared inference-state handling for request lifecycle with job-scoped status/ID, or have the
  coordinator own request status while `ITranscription.StateChanged` remains limited to model
  initialization. A single global `TranscriptionState` event cannot distinguish overlapping jobs.
- Track active and queued jobs in a coordinator. The queue contains completed immutable audio jobs only;
  each job starts inference immediately when it acquires an active semaphore slot. Use a separate
  completion path for history, clipboard, focus restoration, and insertion.
- Keep ETA per job or aggregate it when multiple jobs are active; never let one job stop or reset
  another job's ETA. The user confirmed the next three result-ready estimates in recording order,
  each marked with `~` and `s`, followed by `+N` when more jobs remain.
- Preserve the existing “capture target on PTT start” behavior and current settings snapshot timing
  unless the implementation tests reveal a required correction.

## User-confirmed target routing and remaining decisions

**Confirmed by the user:** Every recording owns its captured target HWND and the request values needed
to process and inject that result. On completion, use that job's saved target even if another job
finished first; never consult a shared “most recently saved” foreground window. The implementation will
snapshot the active target and settings per job.

For two jobs targeting the same HWND, the user confirmed that results must be inserted in recording
order. Inference remains parallel; a later completed result waits only for insertion until the earlier
job to that same HWND has committed or failed.

The user also clarified that recordings must continue while inference slots are occupied. Queueing
completed jobs for later inference is desired; the FIFO is for inference scheduling, not for starting or
capturing a recording.

## User-confirmed queue bounds and overflow behavior

- Settings dialog defaults: **10 total outstanding jobs** and **128 MiB** for retained audio payloads.
  Both values are user-configurable and persisted through the existing settings JSON.
- The job count includes active transcriptions, jobs waiting in FIFO, and the reservation for the
  recording currently in progress.
- The audio budget is a **soft admission threshold**, not a hard allocation ceiling. A recording
  admitted while capacity is available is never truncated and may push retained audio above the
  configured threshold. A PTT attempt that cannot be admitted is declined with a visible `Busy` status;
  the user must retry after admission resumes. A recording already accepted
  remains queued and is transcribed normally; it is never discarded or required to be repeated.
- If a recording pushes audio above the budget, the pause remains latched until all outstanding jobs
  drain, even if an earlier completion temporarily drops the buffer below the threshold. If only the job
  count limit is reached, admission resumes when a slot becomes free. The pill is gray while paused and
  returns to blue automatically when the applicable condition clears.
- The effective budget is the lower of the user's setting and the live system-memory admission guard.
  It covers retained WAV payloads, not the Whisper model or inference workspace; recording finalization
  may temporarily require additional memory.
- Keep the audio buffer until inference completes, whether the job is waiting or actively transcribing.
  Do not rely on garbage collection or paging as the queue's backpressure policy.

## ETA display (user-confirmed)

The current WPF pill uses `SizeToContent="WidthAndHeight"` and places status and ETA on one horizontal
line, so it can grow horizontally. Confirmed display:

- One unfinished job: preserve the current `~8s` style.
- Multiple active/queued jobs: show up to the next three estimated **result-ready** times in recording
  order, e.g. `~8s; ~17s; ~25s`; append `+N` for additional jobs. Keep active/queued counts in the status
  text (e.g. `Transcribing 2 · Queued 4`).
- Each value means estimated time from now until that job can be committed/injected, including queue
  wait and same-target insertion ordering. Mark estimates with `~`; do not imply precision.

Limit the list to the next three result-ready estimates and append `+N` for the remainder so a long FIFO
queue cannot make the floating pill grow without bound.

## Whisper.net concurrency scratch probes

Isolated probes using the pinned Whisper.net 1.9.0 packages created multiple processors from the same
`WhisperFactory`, released them together, and used separate user WAV files. No production project
references changed and no transcript text was persisted.

- **CUDA, two jobs:** `LoadedLibrary=Cuda`; both requests started at 0.006 s. Pair wall time was
  23.616 s; request durations were 21.967 s (`01`) and 23.610 s (`02`). WER matched baseline: 0.14286
  and 0.07463. During a monitored repeat, GPU memory peaked at 3047/4096 MB (74.4%) and utilization
  reached 100%.
- **CUDA, three jobs:** all started at 0.006 s and completed. Pair wall time was 41.591 s; request
  durations were 35.832 s (`01`), 39.362 s (`02`), and 41.584 s (`03`). WER matched baseline:
  0.14286, 0.07463, and 0.20000. Monitored GPU memory peaked at 3612/4096 MB (88.2%) and utilization
  reached 100%; only about 484 MB remained free. Three succeeded on this host but are too close to the
  4 GB limit to recommend as its default.
- **CPU, two jobs:** both started at 0.006 s; pair wall time was 54.609 s, with durations 52.061 s and
  54.604 s. WER matched baseline: 0.14286 and 0.07463.
- **CPU, three jobs:** all started at 0.006 s; pair wall time was 95.778 s, with durations 79.553 s,
  91.227 s, and 95.772 s. WER matched baseline: 0.14286, 0.07463, and 0.20000. CPU RAM was not sampled.
- These tests show that 2–3 independent calls can overlap on this one `ggml-medium` setup. They do not
  establish a universal maximum, guarantee other hardware, or exercise an OOM/recovery path. The
  conservative recommendation for this Quadro T2000 + medium + CUDA profile is two active jobs.

## Future runtime/device follow-up checks

- If a different Whisper.net runtime is added, repeat the same shared-factory concurrency probe with
  that runtime and the target model. If two calls fail or exceed safe memory, test independent factories
  and measure their memory before choosing a cap or fallback.
- Keep the transcript/reference contents in memory and persist only metrics. Do not assume a new
  processor means a thread-safe, memory-safe concurrent native context on every supported device.

## Concurrency-limit discovery and recovery proposal

Whisper.net exposes runtime order, the loaded library, and a GPU-device index, but the inspected 1.9.0
API does not provide a documented “safe concurrent slot count” or reliable free inference-memory
budget. Do not derive the limit from the GPU marketing name alone.

Recommended policy:

1. Use a semaphore for active inference and the FIFO queue for waiting completed jobs. Keep the cap
   specific to engine, runtime/provider version, model/quantization, device identity, and driver where
   that information is available.
2. Use only a validated capability profile as a firm limit. On this Quadro T2000 + `ggml-medium`
   profile, two CUDA jobs peaked at 3047/4096 MB (74.4%, about 1049 MB free); three peaked at
   3612/4096 MB (88.2%, about 484 MB free). Both completed, but the remaining headroom makes two the
   recommended cap for this exact profile. CPU tests also completed three jobs, but RAM was not sampled.
   These runs do not establish a universal maximum or validate other hardware.
3. Treat a concurrency failure as evidence only when it is classified as a resource/allocation failure
   and occurred while multiple inference slots were active. A corrupt model, invalid audio, cancellation,
   or unrelated provider error must not lower the saved concurrency limit.
4. If a recoverable resource error occurs at active limit `N > 1`, retain the failed job's immutable WAV,
   HWND, settings, sequence, and retry count. Stop dequeuing, drain/release the other active slots, reduce
   the effective limit to `N-1`, recreate the runtime if required, and retry only that same job once. Do
   not duplicate history or injection. If retry still fails, surface the error and keep later jobs queued.
5. Separate the **profile cap** (largest controlled/validated concurrency with headroom) from the
   **effective cap** (profile cap reduced temporarily by current memory pressure). A single failure during
   transient pressure must not permanently lower the machine profile. Cache a lower profile only after a
   classified failure and successful retry confirm it; key it by engine/provider/package version,
   model/quantization, GPU identity and driver (and CPU/RAM data for CPU mode). Revalidate when the
   fingerprint changes. Persist capability metadata only, not audio/transcript text.
6. Failure-driven reduction is a recovery mechanism, not the only first-use detector. Unknown hardware
   should start with a conservative cap of two, subject to a memory-budget guard; do not enable three
   slots until a profile/preflight shows adequate headroom. Test whether an OOM/error leaves the native
   session usable before promising in-process retry; a native process crash cannot be recovered this way
   without a separate durable audio-queue design.
7. On Windows, query the OS-provided per-process video-memory budget/current usage through DXGI for the
   selected adapter and local/non-local segments. Use budget-change notifications or periodic sampling
   to stop dequeuing jobs when headroom is low. For Vulkan, use `VK_EXT_memory_budget` when available.
   These are admission-control signals, not an exact prediction of memory per Whisper request; budgets
   vary with other applications and system load.
8. The first-use flow on an unprofiled machine should therefore be: inspect the selected adapter/runtime
   and model, start conservatively at two only if the budget guard permits, and keep all jobs in the FIFO
   queue. If a recognized allocation error occurs at two active jobs, retry the retained job at one. Cache
   one as the profile cap only if the retry confirms a persistent capacity limit; if it was temporary
   pressure, lower only the effective cap and reevaluate when the budget recovers. Permit three only after
   a controlled profile/preflight confirms adequate remaining headroom. Do not intentionally drive the
   GPU to zero free memory.

Lowering the inference-slot count means limiting simultaneous jobs, not switching the user's selected
library/provider per request. Whisper.net loads one runtime for subsequent processing; explicit engine
and provider preferences remain authoritative.

## Test-first acceptance tests

- With the first fake transcription held by a `TaskCompletionSource`, release a second recording and
  assert that the second `TranscribeAsync` starts before the first is completed.
- Start two recordings with different audio bytes, settings snapshots, and focus handles; complete
  them in reverse order and assert each result uses its own request data and target.
- Verify a transcription exception or blank result from one job does not stop, relabel, or suppress
  another job's completion.
- Verify repeated start/stop cycles preserve every accepted audio batch and keep recording available
  while prior jobs remain active.
- Verify per-job status/ETA cleanup, history ordering, and the selected out-of-order insertion policy.
- Verify the active-slot semaphore, FIFO queue-start order, queue-full policy, and that recording is not
  blocked while queued jobs wait for inference capacity.
- Update `.agents/specs/phase-0-coverage-inventory.md` for any changed public/protected contract or XAML
  event behavior. Run focused Debug tests, the complete Debug/Release test suites, and app builds using
  the commands in `.agents/skills/testing.md`.

## Explicit non-goals

- Do not change the audio format or model family.
- Do not queue or block audio capture itself; only completed immutable jobs may wait for inference slots.
- Do not change provider selection or add new NuGet packages as part of Phase 2.
- Do not reorder results or change target-focus semantics without implementing the decisions above.

## References

- Microsoft, [IDXGIAdapter3::QueryVideoMemoryInfo](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_4/nf-dxgi1_4-idxgiadapter3-queryvideomemoryinfo)
  and [DXGI_QUERY_VIDEO_MEMORY_INFO](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_4/ns-dxgi1_4-dxgi_query_video_memory_info).
- Khronos, [Vulkan 1.4 specification](https://registry.khronos.org/vulkan/specs/latest-ratified/pdf/vkspec.pdf),
  `VK_EXT_memory_budget` (`heapBudget` / `heapUsage`).
- Whisper.net, [RuntimeOptions.cs](https://github.com/sandrohanea/whisper.net/blob/main/Whisper.net/LibraryLoader/RuntimeOptions.cs)
  (`LoadedLibrary` remains the runtime used for subsequent processing).
