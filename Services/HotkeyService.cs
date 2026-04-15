using System.Runtime.InteropServices;
using WhisperWriter.Util;

namespace WhisperWriter.Services;

/// <summary>
/// Polls GetAsyncKeyState for a configurable set of VK codes (all must be held).
/// Using polling instead of RegisterHotKey because Win key combinations behave
/// unreliably with RegisterHotKey on Windows 10/11.
/// </summary>
public sealed class HotkeyService: IDisposable {
	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState (int vKey);

	public event Action? PushToTalkStarted;
	public event Action? PushToTalkStopped;

	private bool _disposed;
	private bool _isHeld;
	private Thread? _pollThread;
	private CancellationTokenSource? _cts;

	// Guarded by _keysLock; replaced atomically by UpdateKeys().
	private readonly object _keysLock = new();
	private int[] _vkCodes;

	/// <summary>
	/// Initialises the service with an explicit list of VK codes.
	/// All listed keys must be held simultaneously to trigger PTT.
	/// </summary>
	public HotkeyService (IReadOnlyList<int> vkCodes) {
		_vkCodes = [..vkCodes];
	}

	/// <summary>
	/// Legacy constructor – derives VK codes from a HotkeyModifiers bitmask.
	/// Kept for code paths that have not been migrated yet.
	/// </summary>
	public HotkeyService (HotkeyModifiers modifiers) {
		_vkCodes = ModifiersToVkCodes(modifiers);
	}

	/// <summary>
	/// Replaces the active key combination at runtime without restarting the poll thread.
	/// Safe to call from any thread.
	/// </summary>
	public void UpdateKeys (IReadOnlyList<int> vkCodes) {
		lock (_keysLock) {
			_vkCodes = [..vkCodes];
		}
		// If the old combo was being held, synthesise a release so recording stops cleanly.
		if (_isHeld) {
			_isHeld = false;
			PushToTalkStopped?.Invoke();
		}
	}

	/// <summary>Starts the background polling loop.</summary>
	public void Start () {
		_cts = new CancellationTokenSource();
		_pollThread = new Thread(PollLoop) {
			IsBackground = true,
			Name = "HotkeyPoll",
		};
		_pollThread.Start(_cts.Token);
	}

	public void Stop () {
		_cts?.Cancel();
	}

	private void PollLoop (object? obj) {
		var ct = (CancellationToken)obj!;
		while (!ct.IsCancellationRequested) {
			bool held = IsComboHeld();
			if (held && !_isHeld) {
				_isHeld = true;
				PushToTalkStarted?.Invoke();
			} else if (!held && _isHeld) {
				_isHeld = false;
				PushToTalkStopped?.Invoke();
			}
			Thread.Sleep(20);
		}
	}

	private bool IsComboHeld () {
		int[] codes;
		lock (_keysLock) {
			codes = _vkCodes;
		}
		if (codes.Length == 0)
			return false;
		foreach (var vk in codes) {
			if ((GetAsyncKeyState(vk) & 0x8000) == 0)
				return false;
		}
		return true;
	}

	/// <summary>
	/// Derives a minimal set of VK codes from a HotkeyModifiers bitmask.
	/// Uses the left-hand variant of each modifier key.
	/// </summary>
	private static int[] ModifiersToVkCodes (HotkeyModifiers modifiers) {
		var list = new List<int>();
		if ((modifiers & HotkeyModifiers.Alt)     != 0) list.Add(0xA4); // VK_LMENU
		if ((modifiers & HotkeyModifiers.Control) != 0) list.Add(0xA2); // VK_LCONTROL
		if ((modifiers & HotkeyModifiers.Shift)   != 0) list.Add(0xA0); // VK_LSHIFT
		if ((modifiers & HotkeyModifiers.Win)     != 0) list.Add(0x5B); // VK_LWIN
		return [..list];
	}

	public void Dispose () {
		if (_disposed) return;
		_disposed = true;
		Stop();
	}
}
