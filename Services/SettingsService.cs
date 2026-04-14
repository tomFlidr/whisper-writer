using System.IO;
using System.Text.Json;
using WhisperWriter.Models;

namespace WhisperWriter.Services;

public class SettingsService
{
	private static readonly string _settingsPath = Path.Combine(
		AppContext.BaseDirectory, "settings.json");

	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
	};

	public AppSettings Settings { get; private set; } = new();

	public void Load()
	{
		if (!File.Exists(_settingsPath))
		{
			Settings = new AppSettings();
			Save();
			return;
		}

		try
		{
			var json = File.ReadAllText(_settingsPath);
			Settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
		}
		catch
		{
			Settings = new AppSettings();
		}
	}

	public void Save()
	{
		var json = JsonSerializer.Serialize(Settings, _jsonOptions);
		File.WriteAllText(_settingsPath, json);
	}
}
