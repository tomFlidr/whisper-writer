using System.IO;
using System.Text.Json;
using WhisperWriter.Util;

namespace WhisperWriter.Services;

public class SettingsService {
	private static readonly string _settingsPath = Path.Combine(
		AppContext.BaseDirectory, "settings.json");

	private static readonly JsonSerializerOptions _jsonOptions = new() {
		WriteIndented = true,
	};

	public AppSettings Settings { get; private set; } = new();

	public void Load () {
		if (!File.Exists(SettingsService._settingsPath)) {
			this.Settings = new AppSettings();
			this.Save();
			return;
		}

		try {
			var json = File.ReadAllText(SettingsService._settingsPath);
			this.Settings = JsonSerializer.Deserialize<AppSettings>(
				json, SettingsService._jsonOptions
			) ?? new AppSettings();
		} catch (Exception ex) {
			LogService.Error("Failed to load settings, using defaults", ex);
			this.Settings = new AppSettings();
		}
	}

	public void Save () {
		try {
			var json = JsonSerializer.Serialize(this.Settings, SettingsService._jsonOptions);
			File.WriteAllText(SettingsService._settingsPath, json);
		} catch (Exception ex) {
			LogService.Error("Failed to save settings", ex);
		}
	}
}
