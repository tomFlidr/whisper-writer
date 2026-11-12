using System.IO;
using System.Text.Json;
using WhisperWriter.DI;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.Inference.Capacity;

/// <summary>Stores non-audio inference capacity metadata outside application settings.</summary>
public class ProfileStore : IProfileStore, IService, ISingleton {
	private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
	private readonly string _profilePath;
	private readonly object _gate = new();
	private Dictionary<string, Profile> _profiles;

	public ProfileStore () : this(ProfileStore.defaultPath()) {
	}

	public ProfileStore (string profilePath) {
		ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
		this._profilePath = profilePath;
		this._profiles = ProfileStore.load(profilePath);
	}

	private static Dictionary<string, Profile> load (string path) {
		if (!File.Exists(path))
			return new Dictionary<string, Profile>(StringComparer.Ordinal);
		try {
			return JsonSerializer.Deserialize<Dictionary<string, Profile>>(
				File.ReadAllText(path), ProfileStore._jsonOptions)
				?? new Dictionary<string, Profile>(StringComparer.Ordinal);
		} catch {
			return new Dictionary<string, Profile>(StringComparer.Ordinal);
		}
	}

	private static string defaultPath () {
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(localAppData))
			localAppData = AppContext.BaseDirectory;
		return Path.Combine(localAppData, "WhisperWriter", "inference-capacity.json");
	}

	public void Set (string fingerprint, Profile profile) {
		ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
		ArgumentNullException.ThrowIfNull(profile);
		lock (this._gate) {
			this._profiles[fingerprint] = profile;
			this.save();
		}
	}

	public bool TryGet (string fingerprint, out Profile? profile) {
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
			File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this._profiles, ProfileStore._jsonOptions));
			File.Move(temporaryPath, this._profilePath, overwrite: true);
		} finally {
			if (File.Exists(temporaryPath))
				File.Delete(temporaryPath);
		}
	}
}