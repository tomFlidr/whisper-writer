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

	public AppSettings Data { get; private set; } = new();

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

	public void Save () {
		try {
			var json = JsonSerializer.Serialize(this.Data, Services.Settings._jsonOptions);
			File.WriteAllText(Services.Settings._settingsPath, json);
		} catch (Exception ex) {
			this.log.Error("Failed to save settings", ex);
		}
	}
}
