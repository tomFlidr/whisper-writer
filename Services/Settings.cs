using System.IO;
using System.Text.Json;
using WhisperWriter.DI;
using WhisperWriter.Models;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

public class Settings: IService, ISingleton {
	[Inject]
	protected Log log { get; set; } = null!;

	private static readonly string _settingsPath = Path.Combine(
		AppContext.BaseDirectory, "settings.json");

	private static readonly JsonSerializerOptions _jsonOptions = new() {
		WriteIndented = true,
	};

	/// <summary>
	/// In-memory representation of the application settings loaded from disk.
	/// </summary>
	public AppSettings Data { get; private set; } = new();

	/// <summary>
	/// Loads settings from the JSON file. On deserialization error defaults are used.
	/// When the settings file is missing a new file is created with defaults.
	/// </summary>
	public void Load () {
		if (!File.Exists(Services.Settings._settingsPath)) {
			this.Data = new AppSettings();
			this.Save();
			return;
		}

		try {
			var json = File.ReadAllText(Services.Settings._settingsPath);
			this.Data = JsonSerializer.Deserialize<AppSettings>(
				json, Services.Settings._jsonOptions
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
			var json = JsonSerializer.Serialize(this.Data, Services.Settings._jsonOptions);
			File.WriteAllText(Services.Settings._settingsPath, json);
		} catch (Exception ex) {
			this.log.Error("Failed to save settings", ex);
		}
	}
}
