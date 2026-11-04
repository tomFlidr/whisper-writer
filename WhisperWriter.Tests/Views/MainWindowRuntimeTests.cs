using WhisperWriter.Views;
using Xunit;

namespace WhisperWriter.Tests.Views;

public class MainWindowRuntimeTests {
	[Fact]
	public async Task RunBackground_ExecutesWorkOffTheCallingThread () {
		var runtime = new MainWindowRuntime(null!, null!, null!, null!, null!, null!, null!, null!);
		int callingThreadId = Environment.CurrentManagedThreadId;
		int executionThreadId = callingThreadId;

		await runtime.RunBackground(() => executionThreadId = Environment.CurrentManagedThreadId)
			.WaitAsync(TestContext.Current.CancellationToken);

		Assert.NotEqual(callingThreadId, executionThreadId);
	}
}
