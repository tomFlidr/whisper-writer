namespace WhisperWriter.Services.Inference.Capacity;

/// <summary>Storage contract for device/model-specific inference capacity profiles.</summary>
public interface IProfileStore {
	bool TryGet (string fingerprint, out Profile? profile);
	void Set (string fingerprint, Profile profile);
}