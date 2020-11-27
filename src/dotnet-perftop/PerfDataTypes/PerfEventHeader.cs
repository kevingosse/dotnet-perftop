using System.Runtime.InteropServices;

namespace Perftop.PerfDataTypes
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct PerfEventHeader
    {
        public readonly PerfEventType Type;
        public readonly ushort Misc;
        public readonly ushort Size;

        public unsafe int GetRemainingBytes()
        {
            return Size - sizeof(PerfEventHeader);
        }
    };
}