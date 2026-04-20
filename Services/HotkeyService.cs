using System.Runtime.InteropServices;
using WhisperWriter.Utils;

namespace WhisperWriter.Services;

/// <summary>
/// Polls GetAsyncKeyState for a configurable set of VK codes (all must be held).
/// Using polling instead of RegisterHotKey because Win key combinations behave
/// unreliably with RegisterHotKey on Windows 10/11.
/// </summary>
public class HotkeyService: IDisposable {
	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	public event Action? PushToTalkStarted;
	public event Action? PushToTalkStopped;

	private bool _disposed;
	private bool _isHeld;
	private Thread? _pollThread;
	private CancellationTokenSource? _cts;

	// Guarded by _keysLock; replaced atomically by UpdateKeys().
	private readonly object _keysLock = new();
	private int[] _virtualKeyCodes;

	/// <summary>
	/// Initialises the service with an explicit list of VK codes.
	/// All listed keys must be held simultaneously to trigger PTT.
	/// </summary>
	public HotkeyService (IReadOnlyList<int> virtualKeyCodes) {
		this._virtualKeyCodes = [..virtualKeyCodes];
	}

	/// <summary>
	/// Legacy constructor – derives VK codes from a HotkeyModifiers bitmask.
	/// Kept for code paths that have not been migrated yet.
	/// </summary>
	public HotkeyService (HotkeyModifiers modifiers) {
		this._virtualKeyCodes = HotkeyService._modifiersToVirtualKeyCodes(modifiers);
	}

	/// <summary>
	/// Replaces the active key combination at runtime without restarting the poll thread.
	/// Safe to call from any thread.
	/// </summary>
	public void UpdateKeys (IReadOnlyList<int> vkCodes) {
		lock (this._keysLock) {
			this._virtualKeyCodes = [..vkCodes];
		}
		// If the old combo was being held, synthesise a release so recording stops cleanly.
		if (this._isHeld) {
			this._isHeld = false;
			this.PushToTalkStopped?.Invoke();
		}
	}

	/// <summary>Starts the background polling loop.</summary>
	public void Start () {
		this._cts = new CancellationTokenSource();
		this._pollThread = new Thread(this._pollLoop) {
			IsBackground = true,
			Name = "HotkeyPoll",
		};
		this._pollThread.Start(this._cts.Token);
	}

	public void Stop () {
		this._cts?.Cancel();
	}

	public void Dispose () {
		if (this._disposed) return;
		this._disposed = true;
		this.Stop();
	}

	/// <summary>
	/// Derives a minimal set of VK codes from a HotkeyModifiers bitmask.
	/// Uses the left-hand variant of each modifier key.
	/// </summary>
	private static int[] _modifiersToVirtualKeyCodes (HotkeyModifiers modifiers) {
		var list = new List<int>();
		if ((modifiers & HotkeyModifiers.Alt) != 0) list.Add(0xA4);		// VK_LMENU
		if ((modifiers & HotkeyModifiers.Control) != 0) list.Add(0xA2);	// VK_LCONTROL
		if ((modifiers & HotkeyModifiers.Shift) != 0) list.Add(0xA0);	// VK_LSHIFT
		if ((modifiers & HotkeyModifiers.Win) != 0) list.Add(0x5B);		// _VK_LWIN
		return [..list];
	}

	private void _pollLoop (object? obj) {
		var ct = (CancellationToken)obj!;
		while (!ct.IsCancellationRequested) {
			bool held = this._isComboHeld();
			if (held && !this._isHeld) {
				this._isHeld = true;
				this.PushToTalkStarted?.Invoke();
			} else if (!held && this._isHeld) {
				this._isHeld = false;
				this.PushToTalkStopped?.Invoke();
			}
			Thread.Sleep(20);
		}
	}

	private bool _isComboHeld () {
		int[] codes;
		lock (this._keysLock) {
			codes = this._virtualKeyCodes;
		}
		if (codes.Length == 0)
			return false;
		foreach (var vk in codes) {
			if ((HotkeyService.GetAsyncKeyState(vk) & 0x8000) == 0)
				return false;
		}
		return true;
	}
}
