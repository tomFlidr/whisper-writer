namespace WhisperWriter.Services.EtaCalcs;

/// <summary>
/// Snapshot of the system power state captured during environment fingerprinting.
/// Contains only the information relevant for ETA environment hashing.
/// </summary>
public struct PowerStatusSnapshot {
	public required bool OnAcPower { get; init; }
	public required bool PowerSaverEnabled { get; init; }
}
