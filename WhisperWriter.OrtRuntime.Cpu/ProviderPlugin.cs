using System.IO;
using Microsoft.ML.OnnxRuntime;
using WhisperWriter.OrtRuntime.Contracts;
using WhisperWriter.OrtRuntime.Shared;

namespace WhisperWriter.OrtRuntime.Cpu;

/// <summary>Creates ONNX Runtime sessions using the CPU-only provider package.</summary>
public class ProviderPlugin : IOnnxRuntimeProviderPlugin {
	public string ProviderId => "cpu";

	static ProviderPlugin () {
		Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1", EnvironmentVariableTarget.Process);
		OrtEnv.DisableDllImportResolver = true;
		OrtEnv.Instance().DisableTelemetryEvents();
	}

	private static SessionOptions createSessionOptions (OrtSessionSettings settings) {
		var options = new SessionOptions();
		if (settings.TrackExecutionProviders) {
			options.EnableProfiling = true;
			options.ProfileOutputPathPrefix = Path.Combine(
				Path.GetTempPath(), "WhisperWriter-ORT-" + Guid.NewGuid().ToString("N"));
		}
		return options;
	}

	public IOnnxRuntimeSession CreateSession (string modelPath, OrtSessionSettings settings) {
		using var options = ProviderPlugin.createSessionOptions(settings);
		var session = new InferenceSession(modelPath, options);
		return new OnnxRuntimeSessionAdapter(
			session,
			supportsConcurrentRuns: false,
			trackExecutionProviders: settings.TrackExecutionProviders);
	}

	public void Dispose () { }
}