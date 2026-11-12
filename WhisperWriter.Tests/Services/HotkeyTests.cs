using System.Collections.Concurrent;
using WhisperWriter.Services;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class HotkeyTests {
	private static short readKeyState (ConcurrentDictionary<int, bool> pressed, int virtualKey) {
		return pressed.TryGetValue(virtualKey, out bool isPressed) && isPressed
			? unchecked((short)0x8000)
			: (short)0;
	}

	[Fact]
	public void SetVirtualKeyCodes_ReturnsTheHotkeyInstance () {
		var hotkey = new Hotkey();

		Hotkey result = hotkey.SetVirtualKeyCodes(new[] { 0xA4, 0x5B });

		Assert.Same(hotkey, result);
	}

	[Fact]
	public async Task Polling_RaisesTransitionsAndUsesCopiedKeysThenUpdatedKeys () {
		var pressed = new ConcurrentDictionary<int, bool>();
		pressed[1] = true;
		pressed[2] = true;
		var firstStart = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondStart = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int startCount = 0;
		var hotkey = new Hotkey(vk => HotkeyTests.readKeyState(pressed, vk), 1);
		hotkey.Push2TalkStarted += () => {
			int count = Interlocked.Increment(ref startCount);
			if (count == 1)
				firstStart.TrySetResult(true);
			else
				secondStart.TrySetResult(true);
		};
		hotkey.Push2TalkStopped += () => stop.TrySetResult(true);
		int[] keys = [1, 2];
		hotkey.SetVirtualKeyCodes(keys);
		keys[0] = 9;

		try {
			hotkey.Start();
			await firstStart.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
			pressed[2] = false;
			await stop.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

			hotkey.UpdateKeys([3]);
			pressed[3] = true;
			await secondStart.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
			Assert.Equal(2, startCount);
		} finally {
			hotkey.Dispose();
		}
	}

	[Fact]
	public async Task UpdateKeys_StopsAnActiveHoldImmediately () {
		var pressed = new ConcurrentDictionary<int, bool>();
		pressed[1] = true;
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var hotkey = new Hotkey(vk => HotkeyTests.readKeyState(pressed, vk), 1);
		hotkey.Push2TalkStarted += () => started.TrySetResult(true);
		hotkey.Push2TalkStopped += () => stopped.TrySetResult(true);
		hotkey.SetVirtualKeyCodes([1]);

		try {
			hotkey.Start();
			await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
			hotkey.UpdateKeys([2]);
			await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
		} finally {
			hotkey.Dispose();
		}
	}

	[Fact]
	public async Task Dispose_StopsPollingAndCanBeCalledRepeatedly () {
		var pressed = new ConcurrentDictionary<int, bool>();
		var firstRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int startCount = 0;
		var hotkey = new Hotkey(vk => {
			firstRead.TrySetResult(true);
			return HotkeyTests.readKeyState(pressed, vk);
		}, 1);
		hotkey.Push2TalkStarted += () => Interlocked.Increment(ref startCount);
		hotkey.SetVirtualKeyCodes([1]);

		try {
			hotkey.Start();
			await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
			hotkey.Dispose();
			hotkey.Dispose();
			pressed[1] = true;
			await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

			Assert.Equal(0, startCount);
		} finally {
			hotkey.Dispose();
		}
	}

	[Fact]
	public async Task Stop_PreventsFurtherPollingTransitions () {
		var pressed = new ConcurrentDictionary<int, bool>();
		pressed[1] = true;
		int startCount = 0;
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var hotkey = new Hotkey(vk => HotkeyTests.readKeyState(pressed, vk), 100);
		hotkey.Push2TalkStarted += () => {
			Interlocked.Increment(ref startCount);
			started.TrySetResult(true);
		};
		hotkey.SetVirtualKeyCodes([1]);

		try {
			hotkey.Start();
			await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
			hotkey.Stop();
			pressed[1] = false;
			await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);

			Assert.Equal(1, startCount);
		} finally {
			hotkey.Dispose();
		}
	}
}