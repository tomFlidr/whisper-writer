using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhisperWriter.DI;
using WhisperWriter.Services.EtaStatsServices;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

/// <summary>
/// Persists environment-aware transcription timing statistics in a local SQLite database
/// and provides an evidence-based ETA estimate for new recordings.
///
/// Database location: &lt;BaseDirectory&gt;/llms/eta-time-stats.db
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
public class EtaService : IDisposable, IService, ISingleton {
	[Inject]
	protected LogService logService { get; set; } = null!;

	private static readonly int _maxRecordsPerModelEnvironment = 1000;
	private static readonly int _minimumSamplesForEta = 1;

	protected static string currentDatabaseVersion => typeof(App).Assembly.GetName().Version?.ToString()!;

	protected readonly string dbPath;
	protected SqliteConnection? connection;

	public EtaService () {
		var dir = Path.Combine(AppContext.BaseDirectory, "llms");
		Directory.CreateDirectory(dir);
		this.dbPath = Path.Combine(dir, "eta-time-stats.db");
	}

	protected bool initConnection () {
		if (this.connection != null)
			return true;
		bool result = false;
		try {
			this.connection = new SqliteConnection($"Data Source={this.dbPath}");
			this.connection.Open();
			this.ensureSchema();
			this.logService.Info($"EtaService: database opened at {this.dbPath}");
			result = true;
		} catch (Exception ex) {
			this.logService.Error("EtaService: failed to open database", ex);
			this.connection?.Dispose();
			this.connection = null;
		}
		return result;
	}

	public double? EstimateProcessingSeconds (string modelKey, double audioSeconds) {
		if (!this.initConnection() || audioSeconds <= 0)
			return null;

		try {
			var environmentId = this.findOrCreateEnvironmentId();
			var modelId = this.findModelId(modelKey);
			if (modelId == null)
				return null;

			var windows = new[] {
				(minFactor: 0.70, maxFactor: 1.30),
				(minFactor: 0.50, maxFactor: 1.50),
			};

			foreach (var window in windows) {
				var estimate = this.estimateForWindow(modelId.Value, environmentId,
					audioSeconds * window.minFactor, audioSeconds * window.maxFactor, audioSeconds);
				if (estimate.HasValue)
					return estimate.Value;
			}

			return this.estimateWithoutWindow(modelId.Value, environmentId, audioSeconds);
		} catch (Exception ex) {
			this.logService.Error("EtaService: EstimateProcessingSeconds failed", ex);
			return null;
		}
	}

	public void Record (string modelKey, double audioSeconds, double processingSeconds) {
		if (!this.initConnection())
			return;
		if (audioSeconds <= 0 || processingSeconds <= 0)
			return;

		try {
			var environmentId = this.findOrCreateEnvironmentId();
			var modelId = this.findOrCreateModelId(modelKey);

			using var tx = this.connection.BeginTransaction();

			using var ins = this.connection.CreateCommand();
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

			using var del = this.connection.CreateCommand();
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
			del.Parameters.AddWithValue("$maxRows", EtaService._maxRecordsPerModelEnvironment);
			del.ExecuteNonQuery();

			tx.Commit();
		} catch (Exception ex) {
			this.logService.Error("EtaService: Record failed", ex);
		}
	}

	public void Dispose () {
		this.connection?.Dispose();
		this.connection = null;
	}

	protected void ensureSchema () {
		this.execute(@"
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
			CREATE UNIQUE INDEX IF NOT EXISTS idx_environments_fingerprint 
				ON Environments (fingerprint);
			CREATE UNIQUE INDEX IF NOT EXISTS idx_environments_value 
				ON Environments (value);
			CREATE INDEX IF NOT EXISTS idx_stats_model_env_id 
				ON Stats (model_id, environment_id, id DESC);
			CREATE INDEX IF NOT EXISTS idx_stats_model_env_audio 
				ON Stats (model_id, environment_id, audio_seconds);
		");

		using var countCmd = this.connection!.CreateCommand();
		countCmd.CommandText = "SELECT COUNT(*) FROM Versions;";
		var rowCount = Convert.ToInt32(countCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
		if (rowCount == 0) {
			using var insertCmd = this.connection.CreateCommand();
			insertCmd.CommandText = "INSERT INTO Versions (value) VALUES ($value);";
			insertCmd.Parameters.AddWithValue("$value", EtaService.currentDatabaseVersion);
			insertCmd.ExecuteNonQuery();
		}
	}

	protected double? estimateForWindow (
		long modelId, long environmentId, double minAudioSeconds, 
		double maxAudioSeconds, double currentAudioSeconds
	) {
		using var cmd = this.connection!.CreateCommand();
		cmd.CommandText = @"
			SELECT 
				COUNT(*), 
				AVG(processing_seconds / audio_seconds)
			FROM Stats
			WHERE 
				model_id = $modelId AND
				environment_id = $environmentId AND
				audio_seconds BETWEEN $minAudio AND $maxAudio AND
				audio_seconds > 0;
		";
		cmd.Parameters.AddWithValue("$modelId", modelId);
		cmd.Parameters.AddWithValue("$environmentId", environmentId);
		cmd.Parameters.AddWithValue("$minAudio", minAudioSeconds);
		cmd.Parameters.AddWithValue("$maxAudio", maxAudioSeconds);

		using var reader = cmd.ExecuteReader();
		if (!reader.Read())
			return null;
		var sampleCount = reader.GetInt32(0);
		if (sampleCount < EtaService._minimumSamplesForEta || reader.IsDBNull(1))
			return null;
		var ratio = reader.GetDouble(1);
		return currentAudioSeconds * ratio;
	}

	protected double? estimateWithoutWindow (long modelId, long environmentId, double currentAudioSeconds) {
		using var cmd = this.connection!.CreateCommand();
		cmd.CommandText = @"
			SELECT
				COUNT(*),
				AVG(processing_seconds / audio_seconds)
			FROM Stats
			WHERE
				model_id = $modelId AND
				environment_id = $environmentId AND
				audio_seconds > 0;
		";
		cmd.Parameters.AddWithValue("$modelId", modelId);
		cmd.Parameters.AddWithValue("$environmentId", environmentId);

		using var reader = cmd.ExecuteReader();
		if (!reader.Read())
			return null;
		var sampleCount = reader.GetInt32(0);
		if (sampleCount < EtaService._minimumSamplesForEta || reader.IsDBNull(1))
			return null;
		var ratio = reader.GetDouble(1);
		return currentAudioSeconds * ratio;
	}

	protected long findOrCreateEnvironmentId () {
		var json = this.buildEnvironmentJson();
		var fingerprint = this.computeFingerprint(json);

		using var cmd = this.connection!.CreateCommand();
		cmd.CommandText = @"
			INSERT INTO Environments (
				fingerprint, value
			) VALUES (
				$fingerprint, $value
			) ON CONFLICT (fingerprint) DO NOTHING;
			SELECT id 
			FROM Environments 
			WHERE fingerprint = $fingerprint;
		";
		cmd.Parameters.AddWithValue("$fingerprint", fingerprint);
		cmd.Parameters.AddWithValue("$value", json);
		return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	protected string buildEnvironmentJson () {
		var cpuModel = this.readWmiString("Win32_Processor", "Name") ?? "Unknown CPU";
		var cpuPhysicalCores = this.readWmiInt("Win32_Processor", "NumberOfCores") ?? Environment.ProcessorCount;
		var gpus = this.readGpuModels();
		var cudaVersion = WhisperService.DetectCudaVersion();
		var backend = cudaVersion.HasValue ? "GPU" : "CPU";
		var ramBytes = this.readWmiLong("Win32_ComputerSystem", "TotalPhysicalMemory") ?? 0L;
		var ramTotalGb = ramBytes > 0 ? Math.Round(ramBytes / 1024d / 1024d / 1024d, 2) : 0d;
		var osVersion = Environment.OSVersion.VersionString;
		var osBuild = Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
		var whisperThreads = WhisperService.GetInferenceThreadCount();
		var power = this.readSystemPowerStatus();

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

	protected string[] readGpuModels () {
		var gpuNames = this.readWmiStrings(
			"Win32_VideoController", "Name", skipMicrosoftBasicDisplayAdapter: true
		);
		if (gpuNames.Count == 0)
			gpuNames = this.readWmiStrings("Win32_VideoController", "Name");
		if (gpuNames.Count == 0)
			gpuNames = ["Unknown GPU"];
		return [
			..gpuNames
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
		];
	}

	protected string? readWmiString (
		string className, string propertyName, bool skipMicrosoftBasicDisplayAdapter = false
	) {
		try {
			using var searcher = new ManagementObjectSearcher(
				$"SELECT {propertyName} FROM {className}"
			);
			foreach (ManagementObject obj in searcher.Get()) {
				var value = Convert.ToString(obj[propertyName], CultureInfo.InvariantCulture)?.Trim();
				if (string.IsNullOrWhiteSpace(value))
					continue;
				if (
					skipMicrosoftBasicDisplayAdapter &&
					value.Contains(
						"Microsoft Basic Display Adapter", 
						StringComparison.OrdinalIgnoreCase
					)
				) {
					continue;
				}
				return value;
			}
		} catch (Exception ex) {
			this.logService.Warning(
				$"EtaService: WMI string read failed for {className}.{propertyName}", ex
			);
		}
		return null;
	}

	protected List<string> readWmiStrings (
		string className, string propertyName, bool skipMicrosoftBasicDisplayAdapter = false
	) {
		var values = new List<string>();
		try {
			using var searcher = new ManagementObjectSearcher(
				$"SELECT {propertyName} FROM {className}"
			);
			foreach (ManagementObject obj in searcher.Get()) {
				var value = Convert.ToString(
					obj[propertyName], 
					CultureInfo.InvariantCulture
				)?.Trim();
				if (string.IsNullOrWhiteSpace(value))
					continue;
				if (
					skipMicrosoftBasicDisplayAdapter &&
					value.Contains(
						"Microsoft Basic Display Adapter", 
						StringComparison.OrdinalIgnoreCase
					)
				) {
					continue;
				}
				values.Add(value);
			}
		} catch (Exception ex) {
			this.logService.Warning(
				$"EtaService: WMI string list read failed for {className}.{propertyName}", ex
			);
		}
		return values;
	}

	protected int? readWmiInt (string className, string propertyName) {
		try {
			using var searcher = new ManagementObjectSearcher(
				$"SELECT {propertyName} FROM {className}"
			);
			foreach (ManagementObject obj in searcher.Get()) {
				if (obj[propertyName] == null)
					continue;
				return Convert.ToInt32(obj[propertyName], CultureInfo.InvariantCulture);
			}
		} catch (Exception ex) {
			this.logService.Warning(
				$"EtaService: WMI int read failed for {className}.{propertyName}", ex
			);
		}
		return null;
	}

	protected long? readWmiLong (string className, string propertyName) {
		try {
			using var searcher = new ManagementObjectSearcher(
				$"SELECT {propertyName} FROM {className}"
			);
			foreach (ManagementObject obj in searcher.Get()) {
				if (obj[propertyName] == null)
					continue;
				return Convert.ToInt64(obj[propertyName], CultureInfo.InvariantCulture);
			}
		} catch (Exception ex) {
			this.logService.Warning(
				$"EtaService: WMI long read failed for {className}.{propertyName}", ex
			);
		}
		return null;
	}

	protected string computeFingerprint (string json) {
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
		return Convert.ToHexString(bytes);
	}
	
	protected PowerStatusSnapshot readSystemPowerStatus () {
		if (!NativeMethods.GetSystemPowerStatus(out var status)) {
			return new PowerStatusSnapshot {
				OnAcPower = true,
				PowerSaverEnabled = false
			};
		}
		return new PowerStatusSnapshot {
			OnAcPower = status.ACLineStatus == 1u,
			PowerSaverEnabled = status.SystemStatusFlag == 1u,
		};
	}

	protected long? findModelId (string modelKey) {
		using var cmd = this.connection!.CreateCommand();
		cmd.CommandText = "SELECT id FROM Models WHERE model_key = $key;";
		cmd.Parameters.AddWithValue("$key", modelKey);
		var result = cmd.ExecuteScalar();
		if (result == null || result is DBNull)
			return null;
		return Convert.ToInt64(result, CultureInfo.InvariantCulture);
	}

	protected long findOrCreateModelId (string modelKey) {
		var existing = this.findModelId(modelKey);
		if (existing.HasValue)
			return existing.Value;

		using var cmd = this.connection!.CreateCommand();
		cmd.CommandText = @"
			INSERT INTO Models (
				model_key
			) VALUES (
				$key
			) ON CONFLICT (model_key) DO NOTHING;
			SELECT id 
			FROM Models 
			WHERE model_key = $key;
		";
		cmd.Parameters.AddWithValue("$key", modelKey);
		return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	protected void execute (string sql) {
		using var cmd = this.connection!.CreateCommand();
		cmd.CommandText = sql;
		cmd.ExecuteNonQuery();
	}

}