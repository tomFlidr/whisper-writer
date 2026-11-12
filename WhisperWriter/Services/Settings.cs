using System.IO;
using System.Text.Json;
using WhisperWriter.DI;
using WhisperWriter.Models;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

public class Settings : IService, ISingleton {
	/// <summary>
	/// In-memory representation of the application settings loaded from disk.
	/// </summary>
	public AppSettings Data { get; private set; } = new();

	[Inject]
	protected Log log { get; set; } = null!;

	private static readonly JsonSerializerOptions _jsonOptions = new() {
		WriteIndented = true,
	};

	private readonly string _settingsPath;

	public Settings () : this(Path.Combine(AppContext.BaseDirectory, "settings.json")) {
	}

	protected Settings (string settingsPath) {
		ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
		this._settingsPath = settingsPath;
	}

	/// <summary>
	/// Loads settings from the JSON file. On deserialization error defaults are used.
	/// When the settings file is missing a new file is created with defaults.
	/// </summary>
	public void Load () {
		if (!File.Exists(this._settingsPath)) {
			this.Data = new AppSettings();
			this.Save();
			return;
		}

		try {
			var json = File.ReadAllText(this._settingsPath);
			this.Data = JsonSerializer.Deserialize<AppSettings>(
				json, Settings._jsonOptions
			) ?? new AppSettings();
		} catch (Exception ex) {
			this.log.Error("Failed to load settings, using defaults", ex);
			this.Data = new AppSettings();
		}
	}

	/// <summary>
	/// Saves the current settings to disk as pretty-printed JSON. Swallows IO exceptions
	/// and logs them via the Log service.
	/// </summary>
	public void Save () {
		try {
			var json = JsonSerializer.Serialize(this.Data, Settings._jsonOptions);
			File.WriteAllText(this._settingsPath, json);
		} catch (Exception ex) {
			this.log.Error("Failed to save settings", ex);
		}
	}
}