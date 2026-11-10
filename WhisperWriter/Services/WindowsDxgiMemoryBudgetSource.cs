using System.Runtime.InteropServices;
using WhisperWriter.DI;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

/// <summary>Reads per-process adapter budgets from DXGI without a vendor-specific NuGet dependency.</summary>
public sealed class WindowsDxgiMemoryBudgetSource : IDxgiMemoryBudgetSource, IService, ISingleton {
	private static readonly Guid _factory1Id = new("770AAE78-F26F-4DBA-A829-253C83D1B387");
	private static readonly Guid _adapter3Id = new("645967A4-1392-4310-A798-8053CE3E93FD");
	private const int _factoryEnumAdapters1Slot = 12;
	private const int _adapterGetDesc1Slot = 10;
	private const int _adapterQueryVideoMemoryInfoSlot = 14;
	private const int _dxgiErrorNotFound = unchecked((int)0x887A0002);
	private const uint _softwareAdapterFlag = 0x2;

	public IReadOnlyList<DxgiAdapterMemoryBudget> GetAdapters() {
		if (!OperatingSystem.IsWindows())
			return Array.Empty<DxgiAdapterMemoryBudget>();

		var adapters = new List<DxgiAdapterMemoryBudget>();
		try {
			Guid factoryId = WindowsDxgiMemoryBudgetSource._factory1Id;
			int result = WindowsDxgiMemoryBudgetSource.CreateDXGIFactory1(ref factoryId, out IntPtr factory);
			if (result < 0)
				return adapters;
			try {
				var enumAdapters = WindowsDxgiMemoryBudgetSource.getMethod<EnumAdapters1Delegate>(
					factory, WindowsDxgiMemoryBudgetSource._factoryEnumAdapters1Slot);
				for (uint index = 0; ; index++) {
					result = enumAdapters(factory, index, out IntPtr adapter1);
					if (result == WindowsDxgiMemoryBudgetSource._dxgiErrorNotFound)
						break;
					if (result < 0)
						continue;
					try {
						var getDescription = WindowsDxgiMemoryBudgetSource.getMethod<GetDesc1Delegate>(
							adapter1, WindowsDxgiMemoryBudgetSource._adapterGetDesc1Slot);
						if (getDescription(adapter1, out AdapterDescription description) < 0)
							continue;

						Guid adapter3Id = WindowsDxgiMemoryBudgetSource._adapter3Id;
						if (Marshal.QueryInterface(adapter1, ref adapter3Id, out IntPtr adapter3) < 0)
							continue;
						try {
							var query = WindowsDxgiMemoryBudgetSource.getMethod<QueryVideoMemoryInfoDelegate>(
								adapter3, WindowsDxgiMemoryBudgetSource._adapterQueryVideoMemoryInfoSlot);
							if (query(adapter3, 0, 0, out VideoMemoryInfo localMemory) < 0)
								continue;
							string deviceKey = $"{description.VendorId:X4}:{description.DeviceId:X4}:{description.AdapterLuid.HighPart:X8}{description.AdapterLuid.LowPart:X8}";
							adapters.Add(new DxgiAdapterMemoryBudget(
								deviceKey,
								description.Description?.TrimEnd('\0') ?? string.Empty,
								description.VendorId,
								description.DeviceId,
								(ulong)description.DedicatedVideoMemory,
								localMemory.Budget,
								localMemory.CurrentUsage,
								(description.Flags & WindowsDxgiMemoryBudgetSource._softwareAdapterFlag) != 0));
						} finally {
							Marshal.Release(adapter3);
						}
					} finally {
						Marshal.Release(adapter1);
					}
				}
			} finally {
				Marshal.Release(factory);
			}
		} catch (Exception exception) when (
			exception is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException or COMException) {
			return Array.Empty<DxgiAdapterMemoryBudget>();
		}
		return adapters;
	}

	private static T getMethod<T>(IntPtr instance, int slot) where T : Delegate {
		IntPtr vtable = Marshal.ReadIntPtr(instance);
		IntPtr method = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
		return Marshal.GetDelegateForFunctionPointer<T>(method);
	}

	[DllImport("dxgi.dll", ExactSpelling = true)]
	private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int EnumAdapters1Delegate(IntPtr factory, uint adapterIndex, out IntPtr adapter);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int GetDesc1Delegate(IntPtr adapter, out AdapterDescription description);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int QueryVideoMemoryInfoDelegate(IntPtr adapter, uint nodeIndex, uint segmentGroup, out VideoMemoryInfo info);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct AdapterDescription {
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Description;
		public uint VendorId;
		public uint DeviceId;
		public uint SubSysId;
		public uint Revision;
		public nuint DedicatedVideoMemory;
		public nuint DedicatedSystemMemory;
		public nuint SharedSystemMemory;
		public Luid AdapterLuid;
		public uint Flags;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Luid {
		public uint LowPart;
		public int HighPart;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct VideoMemoryInfo {
		public ulong Budget;
		public ulong CurrentUsage;
		public ulong AvailableForReservation;
		public ulong CurrentReservation;
	}
}
