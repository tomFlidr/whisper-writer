using System.IO;
using WhisperWriter.Services;

namespace WhisperWriter.Tests.Views.Screens.MainWindowTests;

internal class TestSettings : Settings, IDisposable {
	public string Path => this._path;

	private static readonly Log _log = new();
	private readonly string _path;

	public TestSettings (string path) : base(path) {
		this._path = path;
		this.log = TestSettings._log;
	}

	public void Dispose () {
		if (File.Exists(this._path))
			File.Delete(this._path);
		string? directory = System.IO.Path.GetDirectoryName(this._path);
		if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
			Directory.Delete(directory);
	}
}