using System.Runtime.InteropServices;
using WhisperWriter.DI;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.Inference.Memory;

/// <summary>Reads current global physical-memory availability from Windows.</summary>
public class WindowsSystemBudgetSource : ISystemBudgetSource, IService, ISingleton {
	[DllImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx (ref MemoryStatusEx status);

	public SystemBudget GetSnapshot () {
		var status = new MemoryStatusEx {
			Length = (uint)Marshal.SizeOf<MemoryStatusEx>(),
		};
		return GlobalMemoryStatusEx(ref status)
			? new SystemBudget(status.TotalPhysicalBytes, status.AvailablePhysicalBytes)
			: new SystemBudget(0, 0);
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MemoryStatusEx {
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhysicalBytes;
		public ulong AvailablePhysicalBytes;
		public ulong TotalPageFileBytes;
		public ulong AvailablePageFileBytes;
		public ulong TotalVirtualBytes;
		public ulong AvailableVirtualBytes;
		public ulong AvailableExtendedVirtualBytes;
	}
}