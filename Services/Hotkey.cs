using System.Runtime.InteropServices;
using WhisperWriter.Utils;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

/// <summary>
/// Polls GetAsyncKeyState for a configurable set of VK codes (all must be held).
/// Using polling instead of RegisterHotKey because Win key combinations behave
/// unreliably with RegisterHotKey on Windows 10/11.
/// </summary>
/// <summary>
/// Polling-based hotkey detector. Monitors a configurable set of virtual-key codes
/// by calling GetAsyncKeyState on a background thread and raises start/stop events.
/// </summary>
public class Hotkey: IDisposable, IService, ISingleton {
	[DllImport("user32.dll")]
	internal static extern short GetAsyncKeyState(int vKey);

	public event Action? Push2TalkStarted;
	public event Action? Push2TalkStopped;

	private bool _disposed;
	private bool _isHeld;
	private Thread? _pollThread;
	private CancellationTokenSource? _cts;

	// Guarded by _keysLock; replaced atomically by UpdateKeys().
	private readonly object _keysLock = new();
	private int[] _virtualKeyCodes = null!;
	
	/// <summary>
	/// Sets the virtual-key codes that constitute the push-to-talk hotkey.
	/// The provided list is copied and used by the poll loop.
	/// </summary>
	public Hotkey SetVirtualKeyCodes (IReadOnlyList<int> virtualKeyCodes) {
		this._virtualKeyCodes = [..virtualKeyCodes];
		return this;
	}

	/// <summary>
	/// Replaces the active key combination at runtime without restarting the poll thread.
	/// Safe to call from any thread.
	/// </summary>
	/// <summary>
	/// Replaces the active key combination at runtime without restarting the poll thread.
	/// If recording was in progress the Push2TalkStopped event is fired immediately.
	/// Safe to call from any thread.
	/// </summary>
	public void UpdateKeys (IReadOnlyList<int> vkCodes) {
		lock (this._keysLock) {
			this._virtualKeyCodes = [..vkCodes];
		}
		// If the old combo was being held, synthesise a release so recording stops cleanly.
		if (this._isHeld) {
			this._isHeld = false;
			this.Push2TalkStopped?.Invoke();
		}
	}

	/// <summary>Starts the background polling loop.</summary>
	/// <summary>
	/// Starts the background polling thread which checks the hotkey state every 20 ms.
	/// </summary>
	public void Start () {
		this._cts = new CancellationTokenSource();
		this._pollThread = new Thread(this._pollLoop) {
			IsBackground = true,
			Name = "HotkeyPoll",
		};
		this._pollThread.Start(this._cts.Token);
	}

	/// <summary>
	/// Stops the background polling loop. The thread will exit shortly after cancellation.
	/// </summary>
	public void Stop () {
		this._cts?.Cancel();
	}

	/// <summary>
	/// Disposes the service and stops the poll thread.
	/// </summary>
	public void Dispose () {
		if (this._disposed) return;
		this._disposed = true;
		this.Stop();
	}

	/// <summary>
	/// Derives a minimal set of VK codes from a HotkeyModifiers bitmask.
	/// Uses the left-hand variant of each modifier key.
	/// </summary>
	/*private static int[] _modifiersToVirtualKeyCodes (HotkeyModifiers modifiers) {
		var list = new List<int>();
		if ((modifiers & HotkeyModifiers.Alt) != 0) list.Add(0xA4);		// VK_LMENU
		if ((modifiers & HotkeyModifiers.Control) != 0) list.Add(0xA2);	// VK_LCONTROL
		if ((modifiers & HotkeyModifiers.Shift) != 0) list.Add(0xA0);	// VK_LSHIFT
		if ((modifiers & HotkeyModifiers.Win) != 0) list.Add(0x5B);		// _VK_LWIN
		return [..list];
	}*/

	/// <summary>
	/// Background loop executed on a dedicated thread. Evaluates the pressed state of the
	/// configured VK codes and raises Push2TalkStarted/Stopped events on transitions.
	/// </summary>
	private void _pollLoop (object? obj) {
		var ct = (CancellationToken)obj!;
		while (!ct.IsCancellationRequested) {
			bool held = this._isComboHeld();
			if (held && !this._isHeld) {
				this._isHeld = true;
				this.Push2TalkStarted?.Invoke();
			} else if (!held && this._isHeld) {
				this._isHeld = false;
				this.Push2TalkStopped?.Invoke();
			}
			Thread.Sleep(20);
		}
	}

	/// <summary>
	/// Returns true when all configured virtual-key codes are currently held down.
	/// Reads the shared array under a lock to avoid races with UpdateKeys().
	/// </summary>
	private bool _isComboHeld () {
		int[] codes;
		lock (this._keysLock) {
			codes = this._virtualKeyCodes;
		}
		if (codes.Length == 0)
			return false;
		foreach (var vk in codes) {
			if ((Hotkey.GetAsyncKeyState(vk) & 0x8000) == 0)
				return false;
		}
		return true;
	}
}
