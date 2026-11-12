using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.OnnxRuntime;
using Xunit;

namespace WhisperWriter.Tests.Services.OnnxRuntime;

public class ProviderSelectionPolicyTests {
	[Fact]
	public void AutoSelectsDirectMlAndEnablesCpuFallbackWhenBothPacksAreInstalled () {
		var selection = Assert.IsType<ProviderSelection>(
			ProviderSelectionPolicy.Select("Auto", ["cpu", "directml", "cuda"]));

		Assert.Equal("directml", selection.ProviderId);
		Assert.True(selection.AllowCpuFallback);
	}

	[Fact]
	public void AutoSelectsCpuWhenDirectMlPackIsMissing () {
		var selection = Assert.IsType<ProviderSelection>(
			ProviderSelectionPolicy.Select("Auto", ["cpu", "cuda"]));

		Assert.Equal("cpu", selection.ProviderId);
		Assert.False(selection.AllowCpuFallback);
	}

	[Fact]
	public void AutoDoesNotSelectCudaWhenOnlyCudaPackIsInstalled () =>
		Assert.Null(ProviderSelectionPolicy.Select("Auto", ["cuda"]));

	[Fact]
	public void ExplicitCudaDoesNotFallBackWhenTheCudaPackIsInstalled () {
		var selection = Assert.IsType<ProviderSelection>(
			ProviderSelectionPolicy.Select("Cuda", ["cpu", "cuda"]));

		Assert.Equal("cuda", selection.ProviderId);
		Assert.False(selection.AllowCpuFallback);
	}

	[Fact]
	public void ExplicitDirectMlReturnsUnavailableWhenOnlyCpuPackIsInstalled () =>
		Assert.Null(ProviderSelectionPolicy.Select("DirectMl", ["cpu"]));

	[Fact]
	public void UnknownPreferenceDoesNotSilentlySelectAnotherProvider () =>
		Assert.Null(ProviderSelectionPolicy.Select("Vulkan", ["cpu", "directml", "cuda"]));
}