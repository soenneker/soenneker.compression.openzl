using System;
using System.IO;

namespace Soenneker.Compression.OpenZl.Internal;

internal sealed class WireWriter : MemoryStream
{
    public void Varint(ulong value)
    {
        while (value >= 128) { WriteByte((byte)(value | 128)); value >>= 7; }
        WriteByte((byte)value);
    }
    public void Packed(ReadOnlySpan<int> values, int bits)
    {
        byte[] buffer = new byte[checked((int)(((long)values.Length * bits + 7) / 8))];
        for (int i = 0; i < values.Length; i++) Bits.Write(buffer, (long)i * bits, bits, (ulong)values[i]);
        Write(buffer);
    }
    public static byte[] EncodeVarint(ulong value) { using var w = new WireWriter(); w.Varint(value); return w.ToArray(); }
}
