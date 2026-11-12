using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WhisperWriter.Contracts;
using WhisperWriter.DI;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.Inference.Capacity;

/// <summary>Combines measured memory budgets, persisted profiles, and recovery feedback into slot limits.</summary>
public class Manager : IManager, IService, ISingleton {
	private static readonly long _minimumEstimatedSlotBytes = 512L * 1024L * 1024L;
	private static readonly TimeSpan _temporaryProfileLifetime = TimeSpan.FromHours(24);

	private readonly ITranscription _transcription;
	private readonly IMonitor _memoryMonitor;
	private readonly Policy _policy;
	private readonly IProfileStore _profileStore;
	private readonly ConcurrentDictionary<string, (int Limit, DateTimeOffset ExpiresAt)> _sessionLimits = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<Guid, (string Fingerprint, int Limit, long EstimatedSlotBytes)> _pendingDownshifts = new();

	public Manager (
		ITranscription transcription,
		IMonitor memoryMonitor,
		Policy policy,
		IProfileStore profileStore
	) {
		ArgumentNullException.ThrowIfNull(transcription);
		ArgumentNullException.ThrowIfNull(memoryMonitor);
		ArgumentNullException.ThrowIfNull(policy);
		ArgumentNullException.ThrowIfNull(profileStore);
		this._transcription = transcription;
		this._memoryMonitor = memoryMonitor;
		this._policy = policy;
		this._profileStore = profileStore;
	}

	private static long estimateSlotBytes (string modelPath) {
		long modelSize = 0;
		try {
			if (File.Exists(modelPath))
				modelSize = new FileInfo(modelPath).Length;
		} catch { }
		return Math.Max(Manager._minimumEstimatedSlotBytes, modelSize / 2);
	}

	public int GetMaximumConcurrentJobs (TranscriptionJob job, int activeJobs) {
		InferenceMemorySnapshot memory = this._memoryMonitor.GetSnapshot(this._transcription.ActiveBackend?.PrimaryProvider);
		string fingerprint = this.createFingerprint(job, memory);
		Profile? profile = this.getActiveProfile(fingerprint);
		long estimatedSlotBytes = profile?.EstimatedSlotBytes ?? Manager.estimateSlotBytes(job.ModelPath);
		int profileLimit = profile?.MaxConcurrentJobs ?? 3;
		if (this._sessionLimits.TryGetValue(fingerprint, out var sessionLimit)) {
			if (sessionLimit.ExpiresAt > DateTimeOffset.UtcNow)
				profileLimit = Math.Min(profileLimit, sessionLimit.Limit);
			else
				this._sessionLimits.TryRemove(fingerprint, out _);
		}
		return this._policy.GetMaximumConcurrentJobs(activeJobs, estimatedSlotBytes, memory, profileLimit);
	}

	public int ReportResourceFailure (TranscriptionJob job, int activeJobs) {
		int reducedLimit = Math.Max(1, activeJobs - 1);
		InferenceMemorySnapshot memory = this._memoryMonitor.GetSnapshot(this._transcription.ActiveBackend?.PrimaryProvider);
		string fingerprint = this.createFingerprint(job, memory);
		var temporaryLimit = (reducedLimit, DateTimeOffset.UtcNow + Manager._temporaryProfileLifetime);
		this._sessionLimits.AddOrUpdate(fingerprint, temporaryLimit, (_, previous) =>
			(Math.Min(previous.Limit, reducedLimit), temporaryLimit.Item2));
		this._pendingDownshifts[job.Id] = (fingerprint, reducedLimit, Manager.estimateSlotBytes(job.ModelPath));
		return reducedLimit;
	}

	public void ReportSuccess (TranscriptionJob job, int concurrentJobs) {
		if (concurrentJobs < 1)
			return;
		if (!this._pendingDownshifts.TryRemove(job.Id, out var downshift))
			return;

		try {
			DateTimeOffset now = DateTimeOffset.UtcNow;
			this._profileStore.Set(downshift.Fingerprint, new Profile(
				Math.Min(concurrentJobs, downshift.Limit),
				downshift.EstimatedSlotBytes,
				now,
				now + Manager._temporaryProfileLifetime));
		} catch {
			// Failure to persist tuning metadata must not fail a completed transcription.
		}
	}

	private Profile? getActiveProfile (string fingerprint) {
		if (!this._profileStore.TryGet(fingerprint, out Profile? profile))
			return null;
		if (profile is null || profile.ExpiresAt is DateTimeOffset expiresAt && expiresAt <= DateTimeOffset.UtcNow)
			return null;
		return profile;
	}

	private string createFingerprint (TranscriptionJob job, InferenceMemorySnapshot memory) {
		long modelSize = 0;
		long modelLastWrite = 0;
		try {
			var file = new FileInfo(job.ModelPath);
			if (file.Exists) {
				modelSize = file.Length;
				modelLastWrite = file.LastWriteTimeUtc.Ticks;
			}
		} catch { }

		string engineVersion = this._transcription.GetType().Assembly.GetName().Version?.ToString() ?? "unknown";
		ActiveBackendInfo? activeBackend = this._transcription.ActiveBackend;
		string backendIdentity = activeBackend is null
			? "unclassified"
			: string.Join('/',
				activeBackend.Device,
				activeBackend.Engine,
				activeBackend.Runtime,
				string.Join(',', activeBackend.Providers),
				activeBackend.CudaVersion?.ToString(CultureInfo.InvariantCulture) ?? "none");
		string value = string.Join('|',
			backendIdentity,
			engineVersion,
			Path.GetFileName(job.ModelPath),
			modelSize,
			modelLastWrite,
			memory.DeviceKey,
			RuntimeInformation.ProcessArchitecture,
			Environment.OSVersion.Version.ToString());
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
	}
}