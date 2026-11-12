using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.OnnxRuntime;

/// <summary>Applies the application's no-network telemetry policy before any ORT runtime is loaded.</summary>
public class PrivacyPolicy : IService, ISingleton {
	public void DisableTelemetry () {
		Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1", EnvironmentVariableTarget.Process);
	}
}