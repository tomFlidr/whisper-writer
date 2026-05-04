using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WhisperWriter.Services.TextInjectors;
// On 64-bit Windows the union inside Input is aligned to 8 bytes,
// so ki/mi start at offset 8, not 4.
// Win32 sizeof(Input) = 40 bytes on 64-bit, 28 bytes on 32-bit.
[StructLayout(LayoutKind.Explicit)]
#pragma warning disable IDE1006
/// <summary>
/// Native INPUT union wrapper used for SendInput interop.
/// The explicit layout mirrors the Win32 structure: type at offset 0 and the keyboard/mouse union at offset 8 on 64-bit.
/// The struct size check in TextInjector.SaveFocus validates this layout at runtime.
/// </summary>
struct Input {
#pragma warning disable IDE1006
	[FieldOffset(0)] public uint type;
	[FieldOffset(8)] public InputKeyboard ki;
	[FieldOffset(8)] public InputMouse mi;
#pragma warning restore
}
