using System.Runtime.InteropServices;
using System.Text;

namespace WhisperWriter.Utils;

/// <summary>
/// Helpers for working with Windows virtual-key codes:
/// human-readable display names and WPF Key ↔ VK conversion.
/// </summary>
public static class VkCodeHelper {
	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey (uint uCode, uint uMapType);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetKeyNameText (int lParam, StringBuilder lpString, int nSize);

	// Hand-crafted names for keys that GetKeyNameText returns poorly or not at all.
	private static readonly Dictionary<int, string> _overrides = new() {
		{ 0x5B, "L Win" },
		{ 0x5C, "R Win" },
		{ 0x5D, "Menu" },
		{ 0xA0, "L Shift" },
		{ 0xA1, "R Shift" },
		{ 0xA2, "L Ctrl" },
		{ 0xA3, "R Ctrl" },
		{ 0xA4, "L Alt" },
		{ 0xA5, "R Alt" },
		{ 0x2C, "Print Screen" },
		{ 0x2D, "Insert" },
		{ 0x2E, "Delete" },
		{ 0x24, "Home" },
		{ 0x23, "End" },
		{ 0x21, "Page Up" },
		{ 0x22, "Page Down" },
		{ 0x26, "Up" },
		{ 0x28, "Down" },
		{ 0x25, "Left" },
		{ 0x27, "Right" },
		{ 0x09, "Tab" },
		{ 0x14, "Caps Lock" },
		{ 0x90, "Num Lock" },
		{ 0x91, "Scroll Lock" },
		{ 0x08, "Backspace" },
		{ 0x0D, "Enter" },
		{ 0x1B, "Escape" },
		{ 0x20, "Space" },
		{ 0x70, "F1" }, { 0x71, "F2" }, { 0x72, "F3" }, { 0x73, "F4" },
		{ 0x74, "F5" }, { 0x75, "F6" }, { 0x76, "F7" }, { 0x77, "F8" },
		{ 0x78, "F9" }, { 0x79, "F10" }, { 0x7A, "F11" }, { 0x7B, "F12" },
		{ 0x60, "Num 0" }, { 0x61, "Num 1" }, { 0x62, "Num 2" },
		{ 0x63, "Num 3" }, { 0x64, "Num 4" }, { 0x65, "Num 5" },
		{ 0x66, "Num 6" }, { 0x67, "Num 7" }, { 0x68, "Num 8" },
		{ 0x69, "Num 9" }, { 0x6A, "Num *" }, { 0x6B, "Num +" },
		{ 0x6D, "Num -" }, { 0x6E, "Num ." }, { 0x6F, "Num /" },
	};

	// Extended-key VK codes (need the extended bit for GetKeyNameText / correct scan).
	private static readonly HashSet<int> _extendedKeys = [
		0x5B, 0x5C, 0x5D, // Win, Menu
		0xA1, 0xA3, 0xA5, // R Shift, R Ctrl, R Alt
		0x2C, 0x2D, 0x2E, // Print, Insert, Delete
		0x24, 0x23, 0x21, 0x22, // Home, End, PgUp, PgDn
		0x25, 0x26, 0x27, 0x28, // arrow keys
		0x90, // Num Lock (on some keyboards)
		0x6F, // Num /
	];

	/// <summary>Returns a short, human-readable name for a virtual-key code.</summary>
	public static string GetName (int vk) {
		if (VkCodeHelper._overrides.TryGetValue(vk, out var name))
			return name;

		// Ask Windows for a name via the scan code.
		uint scanCode = VkCodeHelper.MapVirtualKey((uint)vk, 0); // MAPVK_VK_TO_VSC
		if (scanCode == 0)
			return $"0x{vk:X2}";

		int lParam = (int)(scanCode << 16);
		if (VkCodeHelper._extendedKeys.Contains(vk))
			lParam |= (1 << 24); // extended-key flag

		var sb = new StringBuilder(64);
		int len = VkCodeHelper.GetKeyNameText(lParam, sb, sb.Capacity);
		if (len > 0) {
			// Title-case (GetKeyNameText may return "SPACE", "ENTER" etc.)
			var raw = sb.ToString();
			return char.ToUpper(raw[0]) + raw[1..].ToLower();
		}

		return $"0x{vk:X2}";
	}

	/// <summary>
	/// Formats a list of VK codes as a human-readable combo string,
	/// e.g. "L Alt + L Win".
	/// </summary>
	public static string FormatCombo (IReadOnlyList<int> vkCodes) {
		if (vkCodes.Count == 0)
			return "(none)";
		return string.Join(" + ", vkCodes.Select(GetName));
	}
}
