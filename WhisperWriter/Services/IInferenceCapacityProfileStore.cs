namespace WhisperWriter.Services;

/// <summary>Storage contract for device/model-specific inference capacity profiles.</summary>
public interface IInferenceCapacityProfileStore {
	bool TryGet(string fingerprint, out InferenceCapacityProfile? profile);
	void Set(string fingerprint, InferenceCapacityProfile profile);
}
