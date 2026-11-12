using System.Runtime.InteropServices;
using WhisperWriter.Services.EtaCalcs;
using Xunit;

namespace WhisperWriter.Tests.Services.EtaCalcs;

public class SystemPowerStatusTests {
	[Fact]
	public void StructLayout_MatchesTheWin32SystemPowerStatusContract () {
		Assert.Equal(12, Marshal.SizeOf<SystemPowerStatus>());
		Assert.Equal(0, Marshal.OffsetOf<SystemPowerStatus>(nameof(SystemPowerStatus.ACLineStatus)).ToInt32());
		Assert.Equal(3, Marshal.OffsetOf<SystemPowerStatus>(nameof(SystemPowerStatus.SystemStatusFlag)).ToInt32());
		Assert.Equal(4, Marshal.OffsetOf<SystemPowerStatus>(nameof(SystemPowerStatus.BatteryLifeTime)).ToInt32());
		Assert.Equal(8, Marshal.OffsetOf<SystemPowerStatus>(nameof(SystemPowerStatus.BatteryFullLifeTime)).ToInt32());
	}
}