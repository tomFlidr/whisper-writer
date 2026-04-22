using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WhisperWriter.Services.EtaCalcs;

[StructLayout(LayoutKind.Sequential)]
public struct SystemPowerStatus {
	public byte ACLineStatus;
	public byte BatteryFlag;
	public byte BatteryLifePercent;
	public byte SystemStatusFlag;
	public uint BatteryLifeTime;
	public uint BatteryFullLifeTime;
}
