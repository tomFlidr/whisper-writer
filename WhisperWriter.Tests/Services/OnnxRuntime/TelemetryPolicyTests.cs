using WhisperWriter.Services;
using WhisperWriter.Services.OnnxRuntime;
using Xunit;

namespace WhisperWriter.Tests.Services.OnnxRuntime;

public class TelemetryPolicyTests {
	[Fact]
	public void DisableTelemetryForcesTheProcessFlagOn () {
		string? previousValue = Environment.GetEnvironmentVariable(
			"ORT_DISABLE_TELEMETRY", EnvironmentVariableTarget.Process);
		try {
			Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "0", EnvironmentVariableTarget.Process);
			var policy = new PrivacyPolicy();
			policy.DisableTelemetry();
			Assert.Equal("1", Environment.GetEnvironmentVariable(
				"ORT_DISABLE_TELEMETRY", EnvironmentVariableTarget.Process));
		} finally {
			Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", previousValue, EnvironmentVariableTarget.Process);
		}
	}
}