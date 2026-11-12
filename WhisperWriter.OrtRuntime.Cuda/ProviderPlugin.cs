using System.IO;
using Microsoft.ML.OnnxRuntime;
using WhisperWriter.OrtRuntime.Contracts;
using WhisperWriter.OrtRuntime.Shared;

namespace WhisperWriter.OrtRuntime.Cuda;

/// <summary>Creates CUDA sessions without silently switching to another provider.</summary>
public class ProviderPlugin : IOnnxRuntimeProviderPlugin {
	public string ProviderId => "cuda";

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
		options.AppendExecutionProvider_CUDA(settings.DeviceIndex);
		var session = new InferenceSession(modelPath, options);
		return new OnnxRuntimeSessionAdapter(
			session,
			supportsConcurrentRuns: false,
			trackExecutionProviders: settings.TrackExecutionProviders);
	}

	public void Dispose () { }
}