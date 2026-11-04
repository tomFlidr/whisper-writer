using WhisperWriter.Utils;
using Xunit;

namespace WhisperWriter.Tests.Utils;

public class HotkeyModifiersTests {
	[Fact]
	public void ModifierValues_AreStableBitFlags() {
		Assert.Equal(0, (int)HotkeyModifiers.None);
		Assert.Equal(1, (int)HotkeyModifiers.Alt);
		Assert.Equal(2, (int)HotkeyModifiers.Control);
		Assert.Equal(4, (int)HotkeyModifiers.Shift);
		Assert.Equal(8, (int)HotkeyModifiers.Win);
		Assert.Equal(9, (int)(HotkeyModifiers.Alt | HotkeyModifiers.Win));
	}
}
