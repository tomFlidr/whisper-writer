using WhisperWriter.Utils;
using Xunit;

namespace WhisperWriter.Tests.Utils;

public class VirtualKeyCodeHelperTests {
	public static TheoryData<int, string> FriendlyNameCases => new() {
		{ 0x5B, "L Win" }, { 0x5C, "R Win" }, { 0x5D, "Menu" },
		{ 0xA0, "L Shift" }, { 0xA1, "R Shift" }, { 0xA2, "L Ctrl" },
		{ 0xA3, "R Ctrl" }, { 0xA4, "L Alt" }, { 0xA5, "R Alt" },
		{ 0x2C, "Print Screen" }, { 0x2D, "Insert" }, { 0x2E, "Delete" },
		{ 0x24, "Home" }, { 0x23, "End" }, { 0x21, "Page Up" }, { 0x22, "Page Down" },
		{ 0x26, "Up" }, { 0x28, "Down" }, { 0x25, "Left" }, { 0x27, "Right" },
		{ 0x09, "Tab" }, { 0x14, "Caps Lock" }, { 0x90, "Num Lock" },
		{ 0x91, "Scroll Lock" }, { 0x08, "Backspace" }, { 0x0D, "Enter" },
		{ 0x1B, "Escape" }, { 0x20, "Space" },
		{ 0x70, "F1" }, { 0x71, "F2" }, { 0x72, "F3" }, { 0x73, "F4" },
		{ 0x74, "F5" }, { 0x75, "F6" }, { 0x76, "F7" }, { 0x77, "F8" },
		{ 0x78, "F9" }, { 0x79, "F10" }, { 0x7A, "F11" }, { 0x7B, "F12" },
		{ 0x60, "Num 0" }, { 0x61, "Num 1" }, { 0x62, "Num 2" },
		{ 0x63, "Num 3" }, { 0x64, "Num 4" }, { 0x65, "Num 5" },
		{ 0x66, "Num 6" }, { 0x67, "Num 7" }, { 0x68, "Num 8" }, { 0x69, "Num 9" },
		{ 0x6A, "Num *" }, { 0x6B, "Num +" }, { 0x6D, "Num -" },
		{ 0x6E, "Num ." }, { 0x6F, "Num /" },
	};

	[Theory]
	[MemberData(nameof(VirtualKeyCodeHelperTests.FriendlyNameCases))]
	public void GetName_ReturnsFriendlyNameForConfiguredKeys (int virtualKey, string expected) {
		Assert.Equal(expected, VirtualKeyCodeHelper.GetName(virtualKey));
	}

	[Fact]
	public void GetName_ReturnsFriendlyNameForLeftAlt () {
		string result = VirtualKeyCodeHelper.GetName(0xA4);

		Assert.Equal("L Alt", result);
	}

	[Fact]
	public void GetName_UsesHexFallbackForUnknownVirtualKey () {
		Assert.Equal("0xFFFF", VirtualKeyCodeHelper.GetName(0xFFFF));
	}

	[Fact]
	public void FormatCombo_ReturnsNoneForEmptyKeyList () {
		string result = VirtualKeyCodeHelper.FormatCombo(Array.Empty<int>());

		Assert.Equal("(none)", result);
	}

	[Fact]
	public void FormatCombo_JoinsFriendlyNamesInInputOrder () {
		int[] keyCodes = [0xA4, 0x5B];

		string result = VirtualKeyCodeHelper.FormatCombo(keyCodes);

		Assert.Equal("L Alt + L Win", result);
	}
}