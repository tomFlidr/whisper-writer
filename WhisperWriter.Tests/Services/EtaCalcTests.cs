using System.IO;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.EtaCalcs;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class EtaCalcTests {
	private static readonly Log _log = new();
	private static readonly ActiveBackendInfo _cpuBackend = new("cpu", "whisper", "whisper.net", ["cpu"]);
	private static readonly ActiveBackendInfo _cuda12Backend = new("gpu", "whisper", "whisper.net", ["cuda"], 12);

	private static string createDatabasePath () => Path.Combine(
		AppContext.BaseDirectory, "phase0-tests", $"{Guid.NewGuid():N}.db"
	);

	private static void deleteDatabase (string databasePath) {
		if (File.Exists(databasePath))
			File.Delete(databasePath);
		string? directory = Path.GetDirectoryName(databasePath);
		if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
			Directory.Delete(directory);
	}

	private static TestEtaCalc createEtaCalc (FakeEtaCalcEnvironment environment) =>
		new(EtaCalcTests._log, EtaCalcTests.createDatabasePath(), environment);

	[Fact]
	public void RecordAndEstimate_UsesWindowedAndFallbackProcessingRatios () {
		string databasePath = Path.Combine(
			AppContext.BaseDirectory, "phase0-tests", $"{Guid.NewGuid():N}.db"
		);
		var etaCalc = new TestEtaCalc(EtaCalcTests._log, databasePath);
		try {
			string modelKey = $"phase0-{Guid.NewGuid():N}";
			etaCalc.Record(modelKey, audioSeconds: 10, processingSeconds: 4, EtaCalcTests._cpuBackend);
			etaCalc.Record(modelKey, audioSeconds: 20, processingSeconds: 20, EtaCalcTests._cpuBackend);
			Assert.Equal(new[] {
				"Environments", "Models", "Stats", "Versions",
				"idx_environments_fingerprint", "idx_environments_value",
				"idx_stats_model_env_audio", "idx_stats_model_env_id",
			}, etaCalc.GetRequiredSchemaObjects());
			Assert.Equal(1, etaCalc.GetVersionRowCount());

			Assert.Equal(4, etaCalc.EstimateProcessingSeconds(modelKey, audioSeconds: 10, EtaCalcTests._cpuBackend));
			Assert.Equal(20, etaCalc.EstimateProcessingSeconds(modelKey, audioSeconds: 20, EtaCalcTests._cpuBackend));
			Assert.Equal(10.5, etaCalc.EstimateProcessingSeconds(modelKey, audioSeconds: 15, EtaCalcTests._cpuBackend));

			double? fallbackEstimate = etaCalc.EstimateProcessingSeconds(
				modelKey, audioSeconds: 45, EtaCalcTests._cpuBackend);
			Assert.NotNull(fallbackEstimate);
			Assert.Equal(31.5, fallbackEstimate.Value, precision: 10);

			string invalidModelKey = $"phase0-invalid-{Guid.NewGuid():N}";
			etaCalc.Record(invalidModelKey, audioSeconds: 0, processingSeconds: 5, EtaCalcTests._cpuBackend);
			Assert.Null(etaCalc.EstimateProcessingSeconds(invalidModelKey, audioSeconds: 10, EtaCalcTests._cpuBackend));
			Assert.Null(etaCalc.EstimateProcessingSeconds(modelKey, audioSeconds: 0, EtaCalcTests._cpuBackend));
		} finally {
			etaCalc.ClearDatabasePool();
			etaCalc.Dispose();
			if (File.Exists(databasePath))
				File.Delete(databasePath);
		}
	}

	[Fact]
	public void Dispose_ClosesAndClearsAnOpenDatabaseConnection () {
		string databasePath = EtaCalcTests.createDatabasePath();
		var etaCalc = new TestEtaCalc(EtaCalcTests._log, databasePath);
		SqliteConnection? connection = null;
		try {
			etaCalc.Record("phase0-dispose", audioSeconds: 1, processingSeconds: 1, EtaCalcTests._cpuBackend);
			connection = etaCalc.Connection;
			Assert.NotNull(connection);
			Assert.Equal(System.Data.ConnectionState.Open, connection.State);

			etaCalc.Dispose();

			Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
			Assert.Null(etaCalc.Connection);
		} finally {
			etaCalc.ClearDatabasePool();
			etaCalc.Dispose();
			EtaCalcTests.deleteDatabase(databasePath);
		}
	}

	[Fact]
	public void Record_IgnoresSamplesWithNonPositiveAudioOrProcessingDurations () {
		string databasePath = EtaCalcTests.createDatabasePath();
		var etaCalc = new TestEtaCalc(EtaCalcTests._log, databasePath);
		try {
			etaCalc.Record("phase0-invalid", audioSeconds: 0, processingSeconds: 4, EtaCalcTests._cpuBackend);
			etaCalc.Record("phase0-invalid", audioSeconds: 10, processingSeconds: 0, EtaCalcTests._cpuBackend);
			etaCalc.Record("phase0-invalid", audioSeconds: 10, processingSeconds: -1, EtaCalcTests._cpuBackend);

			Assert.Equal(0, etaCalc.GetModelCount());
			Assert.Equal(0, etaCalc.GetStatCount());
		} finally {
			etaCalc.ClearDatabasePool();
			etaCalc.Dispose();
			EtaCalcTests.deleteDatabase(databasePath);
		}
	}

	[Fact]
	public void Record_WithoutActiveBackendDoesNotInferGpuFromCudaDllPresence () {
		string databasePath = EtaCalcTests.createDatabasePath();
		var environment = new FakeEtaCalcEnvironment();
		environment.SetWmiValues("Win32_VideoController", "Name", "NVIDIA GPU");
		var etaCalc = new TestEtaCalc(
			EtaCalcTests._log, databasePath, environment, useProductionEnvironment: true);
		try {
			etaCalc.Record("phase3-unclassified", audioSeconds: 10, processingSeconds: 5, activeBackend: null);

			Assert.Equal(0, etaCalc.GetEnvironmentCount());
			Assert.Equal(0, etaCalc.GetStatCount());
		} finally {
			etaCalc.ClearDatabasePool();
			etaCalc.Dispose();
			EtaCalcTests.deleteDatabase(databasePath);
		}
	}

	[Fact]
	public void Estimate_WithoutActiveBackendDoesNotReuseExistingEnvironmentSamples () {
		string databasePath = EtaCalcTests.createDatabasePath();
		var etaCalc = new TestEtaCalc(EtaCalcTests._log, databasePath);
		try {
			etaCalc.SeedSamples("phase3-unclassified", count: 1, EtaCalcTests._cpuBackend);

			Assert.Null(etaCalc.EstimateProcessingSeconds(
				"phase3-unclassified", audioSeconds: 10, activeBackend: null));
		} finally {
			etaCalc.ClearDatabasePool();
			etaCalc.Dispose();
			EtaCalcTests.deleteDatabase(databasePath);
		}
	}

	[Fact]
	public void Estimate_DoesNotReuseProviderAmbiguousLegacyEnvironmentRows () {
		string databasePath = EtaCalcTests.createDatabasePath();
		var environment = new FakeEtaCalcEnvironment();
		var etaCalc = new TestEtaCalc(
			EtaCalcTests._log, databasePath, environment, useProductionEnvironment: true);
		try {
			string modelKey = "phase3-legacy-provider";
			etaCalc.SeedLegacySample(modelKey, "{\"backend\":\"GPU\",\"cudaVersion\":12}", 10, 5);

			Assert.Null(etaCalc.EstimateProcessingSeconds(
				modelKey, audioSeconds: 10, EtaCalcTests._cuda12Backend));
			Assert.Equal(2, etaCalc.GetEnvironmentCount());
			Assert.Equal(1, etaCalc.GetStatCount());
		} finally {
			etaCalc.ClearDatabasePool();
			etaCalc.Dispose();
			EtaCalcTests.deleteDatabase(databasePath);
		}
	}

	[Fact]
	public void BuildEnvironmentJson_SerializesVerifiedBackendIdentityAsANestedObject () {
		string databasePath = EtaCalcTests.createDatabasePath();
		var etaCalc = new TestEtaCalc(EtaCalcTests._log, databasePath, useProductionEnvironment: true);
		var backend = new ActiveBackendInfo("gpu", "whisper", "whisper.net", ["vulkan"]);
		try {
			string json = etaCalc.BuildProductionEnvironmentJson(backend);
			using var document = System.Text.Json.JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			JsonElement actualBackend = root.GetProperty("backend");

			Assert.Equal(System.Text.Json.JsonValueKind.Object, actualBackend.ValueKind);
			Assert.Equal("gpu", actualBackend.GetProperty("device").GetString());
			Assert.Equal("whisper", actualBackend.GetProperty("engine").GetString());
			Assert.Equal("whisper.net", actualBackend.GetProperty("runtime").GetString());
			Assert.Equal(new[] { "vulkan" }, actualBackend.GetProperty("providers").EnumerateArray()
				.Select(provider => provider.GetString()));
			Assert.Equal(System.Text.Json.JsonValueKind.Null, actualBackend.GetProperty("cudaVersion").ValueKind);
			Assert.False(root.TryGetProperty("cudaVersion", out _));
		} finally {
			etaCalc.Dispose();
			EtaCalcTests.deleteDatabase(databasePath);
		}
	}

	[Fact]
	public void EstimateAndRecord_WhenDatabaseCannotOpenReturnSafely () {
		string databasePath = EtaCalcTests.createDatabasePath();
		Directory.CreateDirectory(databasePath);
		var etaCalc = new TestEtaCalc(EtaCalcTests._log, databasePath);
		try {
			Assert.Null(etaCalc.EstimateProcessingSeconds("model", audioSeconds: 10, EtaCalcTests._cpuBackend));
			etaCalc.Record("model", audioSeconds: 10, processingSeconds: 5, EtaCalcTests._cpuBackend);
			Assert.Null(etaCalc.Connection);
		} finally {
			etaCalc.Dispose();
			Directory.Delete(databasePath, recursive: true);
			EtaCalcTests.deleteDatabase(databasePath);
		}
	}

	[Fact]
	public void Record_TrimsSamplesToTheNewestThousandForEachModelAndEnvironment () {
		string databasePath = EtaCalcTests.createDatabasePath();
		var etaCalc = new TestEtaCalc(EtaCalcTests._log, databasePath);
		try {
			etaCalc.SeedSamples("phase0-retention", count: 1001, EtaCalcTests._cpuBackend);
			etaCalc.Record("phase0-retention", audioSeconds: 1002, processingSeconds: 1002, EtaCalcTests._cpuBackend);

			(int count, double oldestProcessingSeconds) = etaCalc.GetStatSummary();
			Assert.Equal(1000, count);
			Assert.Equal(3, oldestProcessingSeconds);
		} finally {
			etaCalc.ClearDatabasePool();
			etaCalc.Dispose();
			EtaCalcTests.deleteDatabase(databasePath);
		}
	}

	[Fact]
	public void BuildEnvironmentJson_UsesInjectedEnvironmentAndSerializesTheFingerprintContract () {
		string databasePath = EtaCalcTests.createDatabasePath();
		var environment = new FakeEtaCalcEnvironment();
		environment.SetWmiValues("Win32_Processor", "Name", " Test CPU ");
		environment.SetWmiValues("Win32_Processor", "NumberOfCores", "8");
		environment.SetWmiValues("Win32_VideoController", "Name", " NVIDIA GPU ", "nvidia gpu", " Intel GPU ");
		environment.SetWmiValues("Win32_ComputerSystem", "TotalPhysicalMemory", 17179869184L);
		environment.PowerStatusAvailable = true;
		environment.PowerStatus = new SystemPowerStatus { ACLineStatus = 1, SystemStatusFlag = 1 };
		var etaCalc = new TestEtaCalc(EtaCalcTests._log, databasePath, environment);
		try {
			using var document = System.Text.Json.JsonDocument.Parse(
				etaCalc.BuildProductionEnvironmentJson(EtaCalcTests._cuda12Backend));
			var root = document.RootElement;
			JsonElement backend = root.GetProperty("backend");
			Assert.Equal("Test CPU", root.GetProperty("cpuModel").GetString());
			Assert.True(root.GetProperty("cpuLogicalCores").GetInt32() > 0);
			Assert.Equal(8, root.GetProperty("cpuPhysicalCores").GetInt32());
			Assert.Equal(new[] { "Intel GPU", "NVIDIA GPU" },
				root.GetProperty("gpus").EnumerateArray().Select(gpu => gpu.GetString()));
			Assert.Equal("gpu", backend.GetProperty("device").GetString());
			Assert.Equal("whisper", backend.GetProperty("engine").GetString());
			Assert.Equal("whisper.net", backend.GetProperty("runtime").GetString());
			Assert.Equal(new[] { "cuda" }, backend.GetProperty("providers").EnumerateArray()
				.Select(provider => provider.GetString()));
			Assert.Equal(12, backend.GetProperty("cudaVersion").GetInt32());
			Assert.False(root.TryGetProperty("cudaVersion", out _));
			Assert.Equal(16, root.GetProperty("ramTotalGb").GetDouble());
			Assert.True(root.GetProperty("onAcPower").GetBoolean());
			Assert.True(root.GetProperty("powerSaverEnabled").GetBoolean());
			Assert.True(root.GetProperty("whisperThreads").GetInt32() > 0);
		} finally {
			etaCalc.Dispose();
			EtaCalcTests.deleteDatabase(databasePath);
		}
	}

	[Fact]
	public void WmiHelpers_TrimTextAndConvertNumericValuesInvariantly () {
		var environment = new FakeEtaCalcEnvironment();
		environment.SetWmiValues("Win32_Processor", "Name", null, "   ", "  Test CPU  ");
		environment.SetWmiValues("Win32_Processor", "NumberOfCores", null, " 8 ");
		environment.SetWmiValues("Win32_ComputerSystem", "TotalPhysicalMemory", 17179869184L);
		var etaCalc = EtaCalcTests.createEtaCalc(environment);

		Assert.Equal("Test CPU", etaCalc.ReadWmiString("Win32_Processor", "Name"));
		Assert.Equal(8, etaCalc.ReadWmiInt("Win32_Processor", "NumberOfCores"));
		Assert.Equal(17179869184L, etaCalc.ReadWmiLong("Win32_ComputerSystem", "TotalPhysicalMemory"));
	}

	[Fact]
	public void WmiHelpers_ReturnNullOrEmptyWhenTheProviderThrows () {
		var environment = new FakeEtaCalcEnvironment { ReadException = new InvalidOperationException("WMI unavailable") };
		var etaCalc = EtaCalcTests.createEtaCalc(environment);

		Assert.Null(etaCalc.ReadWmiString("Win32_Processor", "Name"));
		Assert.Empty(etaCalc.ReadWmiStrings("Win32_Processor", "Name"));
		Assert.Null(etaCalc.ReadWmiInt("Win32_Processor", "NumberOfCores"));
		Assert.Null(etaCalc.ReadWmiLong("Win32_ComputerSystem", "TotalPhysicalMemory"));
	}

	[Fact]
	public void ReadGpuModels_FiltersSortsDeduplicatesAndUsesBothFallbacks () {
		var environment = new FakeEtaCalcEnvironment();
		var etaCalc = EtaCalcTests.createEtaCalc(environment);
		environment.SetWmiValues("Win32_VideoController", "Name",
			" Microsoft Basic Display Adapter ", " NVIDIA GPU ", "nvidia gpu", " Intel GPU ");
		Assert.Equal(new[] { "Intel GPU", "NVIDIA GPU" }, etaCalc.ReadGpuModels());
		Assert.Single(environment.ReadCalls);

		environment.SetWmiValues("Win32_VideoController", "Name", "Microsoft Basic Display Adapter");
		environment.ReadCalls.Clear();
		Assert.Equal(new[] { "Microsoft Basic Display Adapter" }, etaCalc.ReadGpuModels());
		Assert.Equal(2, environment.ReadCalls.Count);

		environment.SetWmiValues("Win32_VideoController", "Name");
		Assert.Equal(new[] { "Unknown GPU" }, etaCalc.ReadGpuModels());
	}

	[Theory]
	[InlineData(false, 0, 0, true, false)]
	[InlineData(true, 1, 1, true, true)]
	[InlineData(true, 0, 1, false, true)]
	[InlineData(true, 1, 0, true, false)]
	[InlineData(true, 2, 2, false, false)]
	public void ReadSystemPowerStatus_UsesNativeStatusOrSafeFallback (
		bool available, byte acStatus, byte saverStatus, bool expectedAc, bool expectedSaver
	) {
		var environment = new FakeEtaCalcEnvironment {
			PowerStatusAvailable = available,
			PowerStatus = new SystemPowerStatus { ACLineStatus = acStatus, SystemStatusFlag = saverStatus },
		};
		var etaCalc = EtaCalcTests.createEtaCalc(environment);

		PowerStatusSnapshot status = etaCalc.ReadSystemPowerStatus();

		Assert.Equal(expectedAc, status.OnAcPower);
		Assert.Equal(expectedSaver, status.PowerSaverEnabled);
	}

	[Fact]
	public void BuildEnvironmentJson_UsesVerifiedCpuBackendWithSafeEnvironmentDefaults () {
		string databasePath = EtaCalcTests.createDatabasePath();
		var etaCalc = new TestEtaCalc(EtaCalcTests._log, databasePath, new FakeEtaCalcEnvironment());
		try {
			using var document = System.Text.Json.JsonDocument.Parse(
				etaCalc.BuildProductionEnvironmentJson(EtaCalcTests._cpuBackend));
			var root = document.RootElement;
			JsonElement backend = root.GetProperty("backend");
			Assert.Equal("Unknown CPU", root.GetProperty("cpuModel").GetString());
			Assert.Equal(Environment.ProcessorCount, root.GetProperty("cpuPhysicalCores").GetInt32());
			Assert.Equal(new[] { "Unknown GPU" },
				root.GetProperty("gpus").EnumerateArray().Select(gpu => gpu.GetString()));
			Assert.Equal("cpu", backend.GetProperty("device").GetString());
			Assert.Equal("whisper", backend.GetProperty("engine").GetString());
			Assert.Equal("whisper.net", backend.GetProperty("runtime").GetString());
			Assert.Equal(new[] { "cpu" }, backend.GetProperty("providers").EnumerateArray()
				.Select(provider => provider.GetString()));
			Assert.Equal(System.Text.Json.JsonValueKind.Null, backend.GetProperty("cudaVersion").ValueKind);
			Assert.False(root.TryGetProperty("cudaVersion", out _));
			Assert.Equal(0, root.GetProperty("ramTotalGb").GetDouble());
			Assert.True(root.GetProperty("onAcPower").GetBoolean());
			Assert.False(root.GetProperty("powerSaverEnabled").GetBoolean());
		} finally {
			etaCalc.Dispose();
			EtaCalcTests.deleteDatabase(databasePath);
		}
	}

	[Fact]
	public void ComputeFingerprint_ReturnsSha256OfTheUtf8EnvironmentJson () {
		var etaCalc = new TestEtaCalc(EtaCalcTests._log, Path.Combine(
			AppContext.BaseDirectory, "phase0-tests", $"{Guid.NewGuid():N}.db"
		));

		Assert.Equal(
			"BB4402D9610155C96945FA3217FC69C12CC7D15F43F80C8AFB9E7A34A84BEF93",
			etaCalc.ComputeFingerprint("phase0")
		);
	}

	private class FakeEtaCalcEnvironment {
		public List<(string ClassName, string PropertyName)> ReadCalls { get; } = [];
		public Exception? ReadException { get; set; }
		public bool PowerStatusAvailable { get; set; }
		public SystemPowerStatus PowerStatus { get; set; }

		private readonly Dictionary<(string ClassName, string PropertyName), object?[]> _values = new();

		public void SetWmiValues (string className, string propertyName, params object?[] values) =>
			this._values[(className, propertyName)] = values;

		public IReadOnlyList<object?> ReadWmiValues (string className, string propertyName) {
			this.ReadCalls.Add((className, propertyName));
			if (this.ReadException is not null)
				throw this.ReadException;
			return this._values.TryGetValue((className, propertyName), out object?[]? values)
				? values
				: Array.Empty<object?>();
		}
	}

	private class TestEtaCalc : EtaCalc {
		public SqliteConnection? Connection {
			get {
				this._lastConnection ??= this.connection;
				return this.connection;
			}
		}

		private readonly FakeEtaCalcEnvironment _environment;
		private readonly bool _useProductionEnvironment;
		private SqliteConnection? _lastConnection;

		public TestEtaCalc (
			Log logger,
			string databasePath,
			FakeEtaCalcEnvironment? environment = null,
			bool useProductionEnvironment = false
		) : base(databasePath) {
			this.log = logger;
			this._environment = environment ?? new FakeEtaCalcEnvironment();
			this._useProductionEnvironment = useProductionEnvironment;
		}

		public string? ReadWmiString (string className, string propertyName) =>
			this.readWmiString(className, propertyName);

		public List<string> ReadWmiStrings (string className, string propertyName) =>
			this.readWmiStrings(className, propertyName);

		public int? ReadWmiInt (string className, string propertyName) =>
			this.readWmiInt(className, propertyName);

		public long? ReadWmiLong (string className, string propertyName) =>
			this.readWmiLong(className, propertyName);

		public string[] ReadGpuModels () => this.readGpuModels();
		public PowerStatusSnapshot ReadSystemPowerStatus () => this.readSystemPowerStatus();

		public int GetModelCount () => this.getCount("Models");
		public int GetEnvironmentCount () => this.getCount("Environments");
		public int GetStatCount () => this.getCount("Stats");

		public string[] GetRequiredSchemaObjects () {
			using var command = this.connection!.CreateCommand();
			command.CommandText = @"
				SELECT name FROM sqlite_master
				WHERE name IN (
					'Environments', 'Models', 'Stats', 'Versions',
					'idx_environments_fingerprint', 'idx_environments_value',
					'idx_stats_model_env_audio', 'idx_stats_model_env_id'
				)
				ORDER BY name;
			";
			using var reader = command.ExecuteReader();
			var names = new List<string>();
			while (reader.Read())
				names.Add(reader.GetString(0));
			return names.ToArray();
		}

		public int GetVersionRowCount () => this.getCount("Versions");

		public void SeedSamples (string modelKey, int count, ActiveBackendInfo activeBackend) {
			if (!this.initConnection())
				throw new InvalidOperationException("Test database could not be opened.");
			long environmentId = this.findOrCreateEnvironmentId(activeBackend);
			long modelId = this.findOrCreateModelId(modelKey);
			using var transaction = this.connection!.BeginTransaction();
			using var command = this.connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = @"
				INSERT INTO Stats (model_id, environment_id, audio_seconds, processing_seconds)
				VALUES ($modelId, $environmentId, $audio, $processing);
			";
			command.Parameters.AddWithValue("$modelId", modelId);
			command.Parameters.AddWithValue("$environmentId", environmentId);
			SqliteParameter audio = command.Parameters.Add("$audio", SqliteType.Real);
			SqliteParameter processing = command.Parameters.Add("$processing", SqliteType.Real);
			for (int sample = 1; sample <= count; sample++) {
				audio.Value = sample;
				processing.Value = sample;
				command.ExecuteNonQuery();
			}
			transaction.Commit();
		}

		public void SeedLegacySample (
			string modelKey, string legacyEnvironmentJson, double audioSeconds, double processingSeconds
		) {
			if (!this.initConnection())
				throw new InvalidOperationException("Test database could not be opened.");
			using var environmentCommand = this.connection!.CreateCommand();
			environmentCommand.CommandText = "INSERT INTO Environments (fingerprint, value) VALUES ($fingerprint, $value);";
			environmentCommand.Parameters.AddWithValue("$fingerprint", this.computeFingerprint(legacyEnvironmentJson));
			environmentCommand.Parameters.AddWithValue("$value", legacyEnvironmentJson);
			environmentCommand.ExecuteNonQuery();
			using var idCommand = this.connection.CreateCommand();
			idCommand.CommandText = "SELECT id FROM Environments WHERE fingerprint = $fingerprint;";
			idCommand.Parameters.AddWithValue("$fingerprint", this.computeFingerprint(legacyEnvironmentJson));
			long environmentId = Convert.ToInt64(idCommand.ExecuteScalar());
			long modelId = this.findOrCreateModelId(modelKey);
			using var statCommand = this.connection.CreateCommand();
			statCommand.CommandText = @"
				INSERT INTO Stats (model_id, environment_id, audio_seconds, processing_seconds)
				VALUES ($modelId, $environmentId, $audio, $processing);
			";
			statCommand.Parameters.AddWithValue("$modelId", modelId);
			statCommand.Parameters.AddWithValue("$environmentId", environmentId);
			statCommand.Parameters.AddWithValue("$audio", audioSeconds);
			statCommand.Parameters.AddWithValue("$processing", processingSeconds);
			statCommand.ExecuteNonQuery();
		}

		public (int Count, double OldestProcessingSeconds) GetStatSummary () {
			using var command = this.connection!.CreateCommand();
			command.CommandText = "SELECT COUNT(*), MIN(processing_seconds) FROM Stats;";
			using var reader = command.ExecuteReader();
			Assert.True(reader.Read());
			return (reader.GetInt32(0), reader.GetDouble(1));
		}

		public string ComputeFingerprint (string json) {
			return this.computeFingerprint(json);
		}

		public string BuildProductionEnvironmentJson (ActiveBackendInfo activeBackend) =>
			base.buildEnvironmentJson(activeBackend);

		public void ClearDatabasePool () {
			SqliteConnection? connection = this.connection ?? this._lastConnection;
			if (connection != null)
				SqliteConnection.ClearPool(connection);
		}

		protected override IReadOnlyList<object?> readWmiValues (string className, string propertyName) =>
			this._environment.ReadWmiValues(className, propertyName);

		protected override bool getSystemPowerStatus (out SystemPowerStatus status) {
			status = this._environment.PowerStatus;
			return this._environment.PowerStatusAvailable;
		}

		protected override string buildEnvironmentJson (ActiveBackendInfo activeBackend) {
			return this._useProductionEnvironment
				? base.buildEnvironmentJson(activeBackend)
				: "{\"testEnvironment\":true}";
		}

		private int getCount (string tableName) {
			if (!this.initConnection())
				return -1;
			using var command = this.connection!.CreateCommand();
			command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
			return Convert.ToInt32(command.ExecuteScalar());
		}
	}
}