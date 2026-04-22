using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WhisperWriter.Services.Whispers;
public static class NativeMethods {
	internal const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	internal static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);
	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern bool FreeLibrary(IntPtr hModule);
}