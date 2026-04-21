using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WhisperWriter.Services.TextInjectors;

[StructLayout(LayoutKind.Sequential)]
struct InputKeyboard {
#pragma warning disable IDE1006
	public ushort wVk;
	public ushort wScan;
	public uint dwFlags;
	public uint time;
	public IntPtr dwExtraInfo;
#pragma warning restore
}
