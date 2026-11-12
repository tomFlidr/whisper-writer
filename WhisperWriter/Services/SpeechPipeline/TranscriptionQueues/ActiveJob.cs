using WhisperWriter.Models;

namespace WhisperWriter.Services.SpeechPipeline.TranscriptionQueues;

internal record ActiveJob (TranscriptionJob Job, DateTimeOffset StartedAt);