namespace WhisperWriter.Services;

/// <summary>Capacity decision and feedback boundary used by the transcription queue.</summary>
public interface IInferenceCapacityManager {
	int GetMaximumConcurrentJobs(TranscriptionJob job, int activeJobs);
	void ReportSuccess(TranscriptionJob job, int concurrentJobs);
	int ReportResourceFailure(TranscriptionJob job, int activeJobs);
}
