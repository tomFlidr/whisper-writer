using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WhisperWriter.Services.TextInjectors;

[StructLayout(LayoutKind.Sequential)]
/// <summary>
/// Managed representation of the native MOUSEINPUT structure used in the INPUT union.
/// </summary>
struct InputMouse {
#pragma warning disable IDE1006
	public int dx;
	public int dy;
	public uint mouseData;
	public uint dwFlags;
	public uint time;
	public IntPtr dwExtraInfo;
#pragma warning restore
}
