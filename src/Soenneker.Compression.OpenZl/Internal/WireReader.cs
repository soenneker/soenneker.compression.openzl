using System;
using System.Buffers.Binary;
using System.IO;

namespace Soenneker.Compression.OpenZl.Internal;

internal ref struct WireReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    public int Position { get; private set; }
    public int Remaining => _data.Length - Position;
    public ReadOnlySpan<byte> Read(int length)
    {
        if (length < 0 || length > Remaining) throw new InvalidDataException("Truncated OpenZL data.");
        var result = _data.Slice(Position, length);
        Position += length;
        return result;
    }
    public byte Byte() => Read(1)[0];
    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Read(4));
    public ulong Varint()
    {
        ulong value = 0;
        for (int shift = 0; shift <= 63; shift += 7)
        {
            byte b = Byte();
            if (shift == 63 && b > 1) throw new InvalidDataException("Varint overflow.");
            value |= (ulong)(b & 127) << shift;
            if (b < 128) return value;
        }
        throw new InvalidDataException("Invalid varint.");
    }
    public int Size(int maximum = int.MaxValue)
    {
        ulong value = Varint();
        if (value > (ulong)maximum) throw new InvalidDataException("OpenZL size exceeds its configured limit.");
        return (int)value;
    }
    public int[] Packed(int count, int bits)
    {
        if (count < 0 || bits is < 0 or > 31) throw new InvalidDataException("Invalid packed integer array.");
        var input = Read(checked((int)(((long)count * bits + 7) / 8)));
        int[] values = new int[count];
        for (int i = 0; i < count; i++) values[i] = (int)Bits.Read(input, (long)i * bits, bits);
        return values;
    }
    public void End() { if (Remaining != 0) throw new InvalidDataException("Unexpected trailing codec data."); }
}
