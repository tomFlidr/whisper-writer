using WhisperWriter.Tests.TestSupport;
using WhisperWriter.Views.Screens.MainWindows;
using Xunit;

namespace WhisperWriter.Tests.Views.Screens.MainWindows;

public class RuntimeTests {
	[Fact]
	public async Task RunBackground_ExecutesWorkOffTheDispatcherThread () {
		var runtime = new Runtime(null!, null!, null!, null!, null!, null!, null!, null!);
		int? executionThreadId = null;
		Task backgroundWork = WpfTestHost.Invoke(() => runtime.RunBackground(
			() => executionThreadId = Environment.CurrentManagedThreadId));

		await backgroundWork.WaitAsync(TestContext.Current.CancellationToken);

		Assert.NotEqual(WpfTestHost.ThreadId, executionThreadId);
	}
}