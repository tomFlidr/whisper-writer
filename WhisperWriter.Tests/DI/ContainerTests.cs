using Autofac;
using WhisperWriter.DI;
using WhisperWriter.Services;
using WhisperWriter.Services.SpeechPipeline;
using Xunit;

namespace WhisperWriter.Tests.DI;

public class ContainerTests {
	[Fact]
	public void GetInstance_ReturnsTheSameContainerEachTime () {
		Container first = Container.GetInstance();
		Container second = Container.GetInstance();

		Assert.Same(first, second);
	}

	[Fact]
	public void Constructor_RegistersSettingsAsASingletonWithProtectedInjection () {
		var container = new Container();
		try {
			Settings first = container.Provider.Resolve<Settings>();
			Settings second = container.Provider.Resolve<Settings>();

			Assert.Same(first, second);
			Assert.NotNull(first);
		} finally {
			container.Provider.Dispose();
		}
	}

	[Fact]
	public void Constructor_ResolvesTranscriptionQueueWithTheSettingsService () {
		var container = new Container();
		try {
			TranscriptionQueue queue = container.Provider.Resolve<TranscriptionQueue>();

			Assert.Equal(128L * 1024L * 1024L, queue.GetStatus().BufferedAudioBudgetBytes);
		} finally {
			container.Provider.Dispose();
		}
	}
}