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
struct Input {
#pragma warning disable IDE1006
	[FieldOffset(0)] public uint type;
	[FieldOffset(8)] public InputKeyboard ki;
	[FieldOffset(8)] public InputMouse mi;
#pragma warning restore
}
