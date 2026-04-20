using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WhisperWriter.Services.EtaStatsServices;
public static class NativeMethods {
	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetSystemPowerStatus (out SystemPowerStatus lpSystemPowerStatus);
}