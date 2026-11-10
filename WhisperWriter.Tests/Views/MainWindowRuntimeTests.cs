using WhisperWriter.Tests.TestSupport;
using WhisperWriter.Views;
using Xunit;

namespace WhisperWriter.Tests.Views;

public class MainWindowRuntimeTests {
	[Fact]
	public async Task RunBackground_ExecutesWorkOffTheDispatcherThread () {
		var runtime = new MainWindowRuntime(null!, null!, null!, null!, null!, null!, null!, null!);
		int? executionThreadId = null;
		Task backgroundWork = WpfTestHost.Invoke(() => runtime.RunBackground(
			() => executionThreadId = Environment.CurrentManagedThreadId));

		await backgroundWork.WaitAsync(TestContext.Current.CancellationToken);

		Assert.NotEqual(WpfTestHost.ThreadId, executionThreadId);
	}
}
