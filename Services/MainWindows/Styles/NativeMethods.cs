using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WhisperWriter.Services.MainWindows.Styles;
public static class NativeMethods {
	
	internal const int GWL_EX_STYLE			= -20;
	internal const int WS_EX_TOOL_WINDOW	= 0x00000080;
	internal const int WS_EX_APP_WINDOW		= 0x00040000;
	internal const int WM_DISPLAY_CHANGE	= 0x007E;

	[DllImport("user32.dll")]
	internal static extern int GetWindowLong (IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	internal static extern int SetWindowLong (IntPtr hwnd, int index, int newStyle);
	
	[DllImport("user32.dll")]
	internal static extern int GetWindowLongPtr (IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	internal static extern int SetWindowLongPtr (IntPtr hwnd, int index, long newStyle);

}