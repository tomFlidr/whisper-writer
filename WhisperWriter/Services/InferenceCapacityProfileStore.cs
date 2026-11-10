using System.IO;
using System.Text.Json;
using WhisperWriter.DI;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

/// <summary>Stores non-audio inference capacity metadata outside application settings.</summary>
public sealed class InferenceCapacityProfileStore : IInferenceCapacityProfileStore, IService, ISingleton {
	private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
	private readonly string _profilePath;
	private readonly object _gate = new();
	private Dictionary<string, InferenceCapacityProfile> _profiles;

	public InferenceCapacityProfileStore () : this(InferenceCapacityProfileStore.defaultPath()) {
	}

	public InferenceCapacityProfileStore(string profilePath) {
		ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
		this._profilePath = profilePath;
		this._profiles = InferenceCapacityProfileStore.load(profilePath);
	}

	public void Set(string fingerprint, InferenceCapacityProfile profile) {
		ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
		ArgumentNullException.ThrowIfNull(profile);
		lock (this._gate) {
			this._profiles[fingerprint] = profile;
			this.save();
		}
	}

	public bool TryGet(string fingerprint, out InferenceCapacityProfile? profile) {
		ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
		lock (this._gate)
			return this._profiles.TryGetValue(fingerprint, out profile);
	}

	private void save () {
		string? directory = Path.GetDirectoryName(this._profilePath);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);
		string temporaryPath = this._profilePath + ".tmp";
		try {
			File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this._profiles, InferenceCapacityProfileStore._jsonOptions));
			File.Move(temporaryPath, this._profilePath, overwrite: true);
		} finally {
			if (File.Exists(temporaryPath))
				File.Delete(temporaryPath);
		}
	}

	private static Dictionary<string, InferenceCapacityProfile> load (string path) {
		if (!File.Exists(path))
			return new Dictionary<string, InferenceCapacityProfile>(StringComparer.Ordinal);
		try {
			return JsonSerializer.Deserialize<Dictionary<string, InferenceCapacityProfile>>(
				File.ReadAllText(path), InferenceCapacityProfileStore._jsonOptions)
				?? new Dictionary<string, InferenceCapacityProfile>(StringComparer.Ordinal);
		} catch {
			return new Dictionary<string, InferenceCapacityProfile>(StringComparer.Ordinal);
		}
	}

	private static string defaultPath () {
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(localAppData))
			localAppData = AppContext.BaseDirectory;
		return Path.Combine(localAppData, "WhisperWriter", "inference-capacity.json");
	}
}
