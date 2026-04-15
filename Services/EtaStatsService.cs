using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WhisperWriter.Services;

/// <summary>
/// Persists environment-aware transcription timing statistics in a local SQLite database
/// and provides an evidence-based ETA estimate for new recordings.
///
/// Database location: <BaseDirectory>/models/eta-time-stats.db
///
/// Fresh schema (no migrations):
///   Versions     (value TEXT)
///   Environments (id INTEGER PK, fingerprint TEXT UNIQUE, value TEXT UNIQUE)
///   Models       (id INTEGER PK, model_key TEXT UNIQUE)
///   Stats        (id INTEGER PK, model_id INTEGER FK, environment_id INTEGER FK,
///                 audio_seconds REAL, processing_seconds REAL)
///
/// ETA is based on the same model + same runtime environment. It first searches rows with
/// similar audio length (±30%), then widens to ±50%, then falls back to all rows for the same
/// model/environment. ETA is shown only when at least two matching samples exist.
/// </summary>
public sealed class EtaStatsService : IDisposable {
	private const int MaxRecordsPerModelEnvironment = 1000;
	private const string CurrentDatabaseVersion = "1.1.0.0";
	private const int MinimumSamplesForEta = 1;

	private readonly string _dbPath;
	private SqliteConnection? _connection;

	public EtaStatsService () {
		var dir = Path.Combine(AppContext.BaseDirectory, "models");
		Directory.CreateDirectory(dir);
		_dbPath = Path.Combine(dir, "eta-time-stats.db");
	}

	// ── Initialisation ────────────────────────────────────────────────────────

	public void Initialize () {
		try {
			_connection = new SqliteConnection($"Data Source={_dbPath}");
			_connection.Open();
			EnsureSchema();
			LogService.Info($"EtaStatsService: database opened at {_dbPath}");
		} catch (Exception ex) {
			LogService.Error("EtaStatsService: failed to open database", ex);
			_connection?.Dispose();
			_connection = null;
		}
	}

	private void EnsureSchema () {
		Execute(@"
			CREATE TABLE IF NOT EXISTS Versions (
				value TEXT NOT NULL
			);
			CREATE TABLE IF NOT EXISTS Environments (
				id          INTEGER PRIMARY KEY AUTOINCREMENT,
				fingerprint TEXT    NOT NULL UNIQUE,
				value       TEXT    NOT NULL UNIQUE
			);
			CREATE TABLE IF NOT EXISTS Models (
				id        INTEGER PRIMARY KEY AUTOINCREMENT,
				model_key TEXT    NOT NULL UNIQUE
			);
			CREATE TABLE IF NOT EXISTS Stats (
				id                  INTEGER PRIMARY KEY AUTOINCREMENT,
				model_id            INTEGER NOT NULL REFERENCES Models(id),
				environment_id      INTEGER NOT NULL REFERENCES Environments(id),
				audio_seconds       REAL    NOT NULL,
				processing_seconds  REAL    NOT NULL
			);
			CREATE UNIQUE INDEX IF NOT EXISTS idx_environments_fingerprint ON Environments (fingerprint);
			CREATE UNIQUE INDEX IF NOT EXISTS idx_environments_value ON Environments (value);
			CREATE INDEX IF NOT EXISTS idx_stats_model_env_id ON Stats (model_id, environment_id, id DESC);
			CREATE INDEX IF NOT EXISTS idx_stats_model_env_audio ON Stats (model_id, environment_id, audio_seconds);
		");

		using var countCmd = _connection!.CreateCommand();
		countCmd.CommandText = "SELECT COUNT(*) FROM Versions;";
		var rowCount = Convert.ToInt32(countCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
		if (rowCount == 0) {
			using var insertCmd = _connection.CreateCommand();
			insertCmd.CommandText = "INSERT INTO Versions (value) VALUES ($value);";
			insertCmd.Parameters.AddWithValue("$value", CurrentDatabaseVersion);
			insertCmd.ExecuteNonQuery();
		}
	}

	// ── Public API ────────────────────────────────────────────────────────────

	public double? EstimateProcessingSeconds (string modelKey, double audioSeconds) {
		if (_connection == null || audioSeconds <= 0)
			return null;

		try {
			var environmentId = FindOrCreateEnvironmentId();
			var modelId = FindModelId(modelKey);
			if (modelId == null)
				return null;

			var windows = new[] {
				(minFactor: 0.70, maxFactor: 1.30),
				(minFactor: 0.50, maxFactor: 1.50),
			};

			foreach (var window in windows) {
				var estimate = EstimateForWindow(modelId.Value, environmentId,
					audioSeconds * window.minFactor, audioSeconds * window.maxFactor, audioSeconds);
				if (estimate.HasValue)
					return estimate.Value;
			}

			return EstimateWithoutWindow(modelId.Value, environmentId, audioSeconds);
		} catch (Exception ex) {
			LogService.Error("EtaStatsService: EstimateProcessingSeconds failed", ex);
			return null;
		}
	}

	public void Record (string modelKey, double audioSeconds, double processingSeconds) {
		if (_connection == null)
			return;
		if (audioSeconds <= 0 || processingSeconds <= 0)
			return;

		try {
			var environmentId = FindOrCreateEnvironmentId();
			var modelId = FindOrCreateModelId(modelKey);

			using var tx = _connection.BeginTransaction();

			using var ins = _connection.CreateCommand();
			ins.Transaction = tx;
			ins.CommandText = @"
				INSERT INTO Stats (model_id, environment_id, audio_seconds, processing_seconds)
				VALUES ($modelId, $environmentId, $audio, $proc);
			";
			ins.Parameters.AddWithValue("$modelId", modelId);
			ins.Parameters.AddWithValue("$environmentId", environmentId);
			ins.Parameters.AddWithValue("$audio", audioSeconds);
			ins.Parameters.AddWithValue("$proc", processingSeconds);
			ins.ExecuteNonQuery();

			using var del = _connection.CreateCommand();
			del.Transaction = tx;
			del.CommandText = @"
				DELETE FROM Stats
				WHERE model_id = $modelId
				  AND environment_id = $environmentId
				  AND id NOT IN (
					SELECT id FROM Stats
					WHERE model_id = $modelId
					  AND environment_id = $environmentId
					ORDER BY id DESC
					LIMIT $maxRows
				);
			";
			del.Parameters.AddWithValue("$modelId", modelId);
			del.Parameters.AddWithValue("$environmentId", environmentId);
			del.Parameters.AddWithValue("$maxRows", MaxRecordsPerModelEnvironment);
			del.ExecuteNonQuery();

			tx.Commit();
		} catch (Exception ex) {
			LogService.Error("EtaStatsService: Record failed", ex);
		}
	}

	// ── ETA helpers ───────────────────────────────────────────────────────────

	private double? EstimateForWindow (long modelId, long environmentId, double minAudioSeconds, double maxAudioSeconds, double currentAudioSeconds) {
		using var cmd = _connection!.CreateCommand();
		cmd.CommandText = @"
			SELECT COUNT(*), AVG(processing_seconds / audio_seconds)
			FROM   Stats
			WHERE  model_id = $modelId
			  AND  environment_id = $environmentId
			  AND  audio_seconds BETWEEN $minAudio AND $maxAudio
			  AND  audio_seconds > 0;
		";
		cmd.Parameters.AddWithValue("$modelId", modelId);
		cmd.Parameters.AddWithValue("$environmentId", environmentId);
		cmd.Parameters.AddWithValue("$minAudio", minAudioSeconds);
		cmd.Parameters.AddWithValue("$maxAudio", maxAudioSeconds);

		using var reader = cmd.ExecuteReader();
		if (!reader.Read())
			return null;
		var sampleCount = reader.GetInt32(0);
		if (sampleCount < MinimumSamplesForEta || reader.IsDBNull(1))
			return null;
		var ratio = reader.GetDouble(1);
		return currentAudioSeconds * ratio;
	}

	private double? EstimateWithoutWindow (long modelId, long environmentId, double currentAudioSeconds) {
		using var cmd = _connection!.CreateCommand();
		cmd.CommandText = @"
			SELECT COUNT(*), AVG(processing_seconds / audio_seconds)
			FROM   Stats
			WHERE  model_id = $modelId
			  AND  environment_id = $environmentId
			  AND  audio_seconds > 0;
		";
		cmd.Parameters.AddWithValue("$modelId", modelId);
		cmd.Parameters.AddWithValue("$environmentId", environmentId);

		using var reader = cmd.ExecuteReader();
		if (!reader.Read())
			return null;
		var sampleCount = reader.GetInt32(0);
		if (sampleCount < MinimumSamplesForEta || reader.IsDBNull(1))
			return null;
		var ratio = reader.GetDouble(1);
		return currentAudioSeconds * ratio;
	}

	// ── Environment discovery ────────────────────────────────────────────────

	private long FindOrCreateEnvironmentId () {
		var json = BuildEnvironmentJson();
		var fingerprint = ComputeFingerprint(json);

		using var cmd = _connection!.CreateCommand();
		cmd.CommandText = @"
			INSERT INTO Environments (fingerprint, value)
			VALUES ($fingerprint, $value)
			ON CONFLICT(fingerprint) DO NOTHING;
			SELECT id FROM Environments WHERE fingerprint = $fingerprint;
		";
		cmd.Parameters.AddWithValue("$fingerprint", fingerprint);
		cmd.Parameters.AddWithValue("$value", json);
		return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	private static string BuildEnvironmentJson () {
		var cpuModel = ReadWmiString("Win32_Processor", "Name") ?? "Unknown CPU";
		var cpuPhysicalCores = ReadWmiInt("Win32_Processor", "NumberOfCores") ?? Environment.ProcessorCount;
		var gpus = ReadGpuModels();
		var cudaVersion = WhisperService.DetectCudaVersion();
		var backend = cudaVersion.HasValue ? "GPU" : "CPU";
		var ramBytes = ReadWmiLong("Win32_ComputerSystem", "TotalPhysicalMemory") ?? 0L;
		var ramTotalGb = ramBytes > 0 ? Math.Round(ramBytes / 1024d / 1024d / 1024d, 2) : 0d;
		var osVersion = Environment.OSVersion.VersionString;
		var osBuild = Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
		var whisperThreads = WhisperService.GetInferenceThreadCount();
		var power = ReadSystemPowerStatus();

		var payload = new {
			cpuModel,
			cpuLogicalCores = Environment.ProcessorCount,
			cpuPhysicalCores,
			gpus,
			backend,
			cudaVersion,
			ramTotalGb,
			osVersion,
			osBuild,
			processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
			whisperThreads,
			onAcPower = power.OnAcPower,
			powerSaverEnabled = power.PowerSaverEnabled,
		};

		return JsonSerializer.Serialize(payload);
	}

	private static string[] ReadGpuModels () {
		var gpuNames = ReadWmiStrings("Win32_VideoController", "Name", skipMicrosoftBasicDisplayAdapter: true);
		if (gpuNames.Count == 0)
			gpuNames = ReadWmiStrings("Win32_VideoController", "Name");
		if (gpuNames.Count == 0)
			gpuNames = ["Unknown GPU"];
		return [..gpuNames
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
	}

	private static string? ReadWmiString (string className, string propertyName, bool skipMicrosoftBasicDisplayAdapter = false) {
		try {
			using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
			foreach (ManagementObject obj in searcher.Get()) {
				var value = Convert.ToString(obj[propertyName], CultureInfo.InvariantCulture)?.Trim();
				if (string.IsNullOrWhiteSpace(value))
					continue;
				if (skipMicrosoftBasicDisplayAdapter
					&& value.Contains("Microsoft Basic Display Adapter", StringComparison.OrdinalIgnoreCase))
					continue;
				return value;
			}
		} catch (Exception ex) {
			LogService.Warning($"EtaStatsService: WMI string read failed for {className}.{propertyName}", ex);
		}
		return null;
	}

	private static List<string> ReadWmiStrings (string className, string propertyName, bool skipMicrosoftBasicDisplayAdapter = false) {
		var values = new List<string>();
		try {
			using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
			foreach (ManagementObject obj in searcher.Get()) {
				var value = Convert.ToString(obj[propertyName], CultureInfo.InvariantCulture)?.Trim();
				if (string.IsNullOrWhiteSpace(value))
					continue;
				if (skipMicrosoftBasicDisplayAdapter
					&& value.Contains("Microsoft Basic Display Adapter", StringComparison.OrdinalIgnoreCase))
					continue;
				values.Add(value);
			}
		} catch (Exception ex) {
			LogService.Warning($"EtaStatsService: WMI string list read failed for {className}.{propertyName}", ex);
		}
		return values;
	}

	private static int? ReadWmiInt (string className, string propertyName) {
		try {
			using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
			foreach (ManagementObject obj in searcher.Get()) {
				if (obj[propertyName] == null)
					continue;
				return Convert.ToInt32(obj[propertyName], CultureInfo.InvariantCulture);
			}
		} catch (Exception ex) {
			LogService.Warning($"EtaStatsService: WMI int read failed for {className}.{propertyName}", ex);
		}
		return null;
	}

	private static long? ReadWmiLong (string className, string propertyName) {
		try {
			using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
			foreach (ManagementObject obj in searcher.Get()) {
				if (obj[propertyName] == null)
					continue;
				return Convert.ToInt64(obj[propertyName], CultureInfo.InvariantCulture);
			}
		} catch (Exception ex) {
			LogService.Warning($"EtaStatsService: WMI long read failed for {className}.{propertyName}", ex);
		}
		return null;
	}

	private static string ComputeFingerprint (string json) {
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
		return Convert.ToHexString(bytes);
	}

	private static PowerStatusSnapshot ReadSystemPowerStatus () {
		if (!NativeMethods.GetSystemPowerStatus(out var status))
			return new PowerStatusSnapshot { OnAcPower = true, PowerSaverEnabled = false };

		return new PowerStatusSnapshot {
			OnAcPower = status.ACLineStatus == 1,
			PowerSaverEnabled = status.SystemStatusFlag == 1,
		};
	}

	// ── Model helpers ────────────────────────────────────────────────────────

	private long? FindModelId (string modelKey) {
		using var cmd = _connection!.CreateCommand();
		cmd.CommandText = "SELECT id FROM Models WHERE model_key = $key;";
		cmd.Parameters.AddWithValue("$key", modelKey);
		var result = cmd.ExecuteScalar();
		if (result == null || result is DBNull)
			return null;
		return Convert.ToInt64(result, CultureInfo.InvariantCulture);
	}

	private long FindOrCreateModelId (string modelKey) {
		var existing = FindModelId(modelKey);
		if (existing.HasValue)
			return existing.Value;

		using var cmd = _connection!.CreateCommand();
		cmd.CommandText = @"
			INSERT INTO Models (model_key) VALUES ($key)
			ON CONFLICT(model_key) DO NOTHING;
			SELECT id FROM Models WHERE model_key = $key;
		";
		cmd.Parameters.AddWithValue("$key", modelKey);
		return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	private void Execute (string sql) {
		using var cmd = _connection!.CreateCommand();
		cmd.CommandText = sql;
		cmd.ExecuteNonQuery();
	}

	// ── IDisposable ───────────────────────────────────────────────────────────

	public void Dispose () {
		_connection?.Dispose();
		_connection = null;
	}

	private sealed class PowerStatusSnapshot {
		public required bool OnAcPower { get; init; }
		public required bool PowerSaverEnabled { get; init; }
	}

	private static class NativeMethods {
		[StructLayout(LayoutKind.Sequential)]
		internal struct SYSTEM_POWER_STATUS {
			internal byte ACLineStatus;
			internal byte BatteryFlag;
			internal byte BatteryLifePercent;
			internal byte SystemStatusFlag;
			internal int BatteryLifeTime;
			internal int BatteryFullLifeTime;
		}

		[DllImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool GetSystemPowerStatus (out SYSTEM_POWER_STATUS lpSystemPowerStatus);
	}
}
