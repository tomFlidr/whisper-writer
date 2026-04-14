using System.Runtime.InteropServices;
using WhisperWriter.Models;

namespace WhisperWriter.Services;

/// <summary>
/// Registers a system-wide hotkey using RegisterHotKey Win32 API.
/// Listens via a hidden message-only window pump on a background thread.
/// </summary>
public sealed class HotkeyService : IDisposable
{
	private const int WM_HOTKEY = 0x0312;
	private const int HOTKEY_ID_PTT = 9001;

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	// VK codes
	private const int VK_LCONTROL = 0xA2;
	private const int VK_LWIN = 0x5B;

	public event Action? PushToTalkStarted;
	public event Action? PushToTalkStopped;

	private bool _disposed;
	private bool _isHeld;
	private Thread? _pollThread;
	private CancellationTokenSource? _cts;
	private readonly HotkeyModifiers _modifiers;

	public HotkeyService(HotkeyModifiers modifiers)
	{
		_modifiers = modifiers;
	}

	/// <summary>
	/// Starts polling for the configured key combination.
	/// Using polling instead of RegisterHotKey because Win key combinations
	/// behave unreliably with RegisterHotKey on Windows 10/11.
	/// </summary>
	public void Start()
	{
		_cts = new CancellationTokenSource();
		_pollThread = new Thread(PollLoop)
		{
			IsBackground = true,
			Name = "HotkeyPoll",
		};
		_pollThread.Start(_cts.Token);
	}

	public void Stop()
	{
		_cts?.Cancel();
	}

	private void PollLoop(object? obj)
	{
		var ct = (CancellationToken)obj!;
		while (!ct.IsCancellationRequested)
		{
			bool held = IsComboHeld();
			if (held && !_isHeld)
			{
				_isHeld = true;
				PushToTalkStarted?.Invoke();
			}
			else if (!held && _isHeld)
			{
				_isHeld = false;
				PushToTalkStopped?.Invoke();
			}
			Thread.Sleep(20);
		}
	}

	private bool IsComboHeld()
	{
		// Check Left Ctrl + Left Win (both must be down simultaneously)
		bool ctrl = (_modifiers & HotkeyModifiers.Control) != 0
			? (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0
			: true;
		bool win = (_modifiers & HotkeyModifiers.Win) != 0
			? (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0
			: true;
		return ctrl && win;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Stop();
	}
}
