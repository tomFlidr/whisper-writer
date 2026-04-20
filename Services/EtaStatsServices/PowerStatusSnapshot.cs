namespace WhisperWriter.Services.EtaStatsServices;

/// <summary>Snapshot of the system power state captured during environment fingerprinting.</summary>
public struct PowerStatusSnapshot {
	public required bool OnAcPower { get; init; }
	public required bool PowerSaverEnabled { get; init; }
}