using System.IO;
using Microsoft.ML.OnnxRuntime;
using WhisperWriter.OrtRuntime.Contracts;
using WhisperWriter.OrtRuntime.Shared;

namespace WhisperWriter.OrtRuntime.DirectML;

/// <summary>Creates DirectML sessions and uses CPU only when the Auto policy permits fallback.</summary>
public class ProviderPlugin : IOnnxRuntimeProviderPlugin {
	public string ProviderId => "directml";

	static ProviderPlugin () {
		Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1", EnvironmentVariableTarget.Process);
		OrtEnv.DisableDllImportResolver = true;
		OrtEnv.Instance().DisableTelemetryEvents();
	}

	private static SessionOptions createSessionOptions (OrtSessionSettings settings) {
		var options = new SessionOptions {
			ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
			EnableMemoryPattern = false,
		};
		if (settings.TrackExecutionProviders) {
			options.EnableProfiling = true;
			options.ProfileOutputPathPrefix = Path.Combine(
				Path.GetTempPath(), "WhisperWriter-ORT-" + Guid.NewGuid().ToString("N"));
		}
		return options;
	}

	public IOnnxRuntimeSession CreateSession (string modelPath, OrtSessionSettings settings) {
		try {
			using var options = ProviderPlugin.createSessionOptions(settings);
			options.AppendExecutionProvider_DML(settings.DeviceIndex);
			var session = new InferenceSession(modelPath, options);
			return new OnnxRuntimeSessionAdapter(
				session,
				supportsConcurrentRuns: false,
				trackExecutionProviders: settings.TrackExecutionProviders);
		} catch (Exception directMlException) when (
			settings.AllowCpuFallback && directMlException is not OutOfMemoryException) {
			try {
				using var options = ProviderPlugin.createSessionOptions(settings);
				var session = new InferenceSession(modelPath, options);
				return new OnnxRuntimeSessionAdapter(
					session,
					supportsConcurrentRuns: false,
					trackExecutionProviders: settings.TrackExecutionProviders,
					fallbackReason: directMlException.Message);
			} catch (Exception cpuException) {
				throw new AggregateException(
					"DirectML initialization failed and CPU fallback in the same ORT runtime also failed.",
					directMlException,
					cpuException);
			}
		}
	}

	public void Dispose () { }
}