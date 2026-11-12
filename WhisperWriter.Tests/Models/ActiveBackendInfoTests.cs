using WhisperWriter.Models;
using Xunit;

namespace WhisperWriter.Tests.Models;

public class ActiveBackendInfoTests {
	[Theory]
	[InlineData("Cuda", "gpu", "cuda", 13)]
	[InlineData("Cuda12", "gpu", "cuda", 12)]
	[InlineData("Vulkan", "gpu", "vulkan", null)]
	[InlineData("Cpu", "cpu", "cpu", null)]
	[InlineData("CpuNoAvx", "cpu", "cpu-no-avx", null)]
	public void FromWhisperNetMapsRecognizedLibrariesToStableBackendIds (
		string library, string expectedDevice, string expectedProvider, int? expectedCudaVersion
	) {
		ActiveBackendInfo backend = Assert.IsType<ActiveBackendInfo>(
			ActiveBackendInfo.FromWhisperNet(library));
		Assert.Equal(expectedDevice, backend.Device);
		Assert.Equal("whisper", backend.Engine);
		Assert.Equal("whisper.net", backend.Runtime);
		Assert.Equal(expectedCudaVersion, backend.CudaVersion);
		Assert.Equal(new[] { expectedProvider }, backend.Providers);
	}

	[Theory]
	[InlineData("")]
	[InlineData("OpenVino")]
	[InlineData("CoreML")]
	[InlineData("Unknown")]
	public void FromWhisperNetDoesNotGuessUnmappedLibraries (string library) =>
		Assert.Null(ActiveBackendInfo.FromWhisperNet(library));

	[Fact]
	public void PrimaryProvider_PrefersGpuExecutionProviderForPartitionedGraph () {
		var backend = new ActiveBackendInfo(
			"gpu", "parakeet", "onnxruntime", ["cpu", "directml"]);

		Assert.Equal("directml", backend.PrimaryProvider);
	}

	[Fact]
	public void FromOnnxRuntimeMapsTheProviderProfileToStableBackendIdentity () {
		var backend = Assert.IsType<ActiveBackendInfo>(ActiveBackendInfo.FromOnnxRuntime(
			"parakeet", ["CPUExecutionProvider", "DmlExecutionProvider"]));
		Assert.Equal("gpu", backend.Device);
		Assert.Equal("parakeet", backend.Engine);
		Assert.Equal("onnxruntime", backend.Runtime);
		Assert.Equal(new[] { "cpu", "directml" }, backend.Providers);
	}

	[Fact]
	public void FromOnnxRuntimeUsesCpuDeviceForCpuOnlyProfiles () {
		var backend = Assert.IsType<ActiveBackendInfo>(ActiveBackendInfo.FromOnnxRuntime(
			"parakeet", ["CPUExecutionProvider"]));

		Assert.Equal("cpu", backend.Device);
		Assert.Equal("cpu", backend.PrimaryProvider);
	}

	[Fact]
	public void FromOnnxRuntimePreservesCudaVersionOnlyForActiveCudaProfiles () {
		var backend = Assert.IsType<ActiveBackendInfo>(ActiveBackendInfo.FromOnnxRuntime(
			"parakeet", ["CUDAExecutionProvider", "CPUExecutionProvider"], 13));

		Assert.Equal("gpu", backend.Device);
		Assert.Equal(13, backend.CudaVersion);
		Assert.Equal(new[] { "cpu", "cuda" }, backend.Providers.OrderBy(provider => provider));
	}

	[Theory]
	[InlineData("UnrecognizedExecutionProvider")]
	[InlineData("")]
	public void FromOnnxRuntimeDoesNotClassifyUnknownProviders (string provider) =>
		Assert.Null(ActiveBackendInfo.FromOnnxRuntime("parakeet", [provider]));

	[Fact]
	public void FromOnnxRuntimeDoesNotAttachCudaVersionWithoutCudaProvider () =>
		Assert.Null(ActiveBackendInfo.FromOnnxRuntime("parakeet", ["CPUExecutionProvider"], 13));
}