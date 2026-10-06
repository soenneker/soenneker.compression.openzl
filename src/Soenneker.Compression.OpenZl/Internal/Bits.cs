using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Soenneker.Compression.OpenZl.Internal;

internal static class Bits
{
    public static int Width(int exclusiveMaximum) => exclusiveMaximum <= 1 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)(exclusiveMaximum - 1));
    public static bool NumericWidth(int width) => width is 1 or 2 or 4 or 8;
    public static ulong Read(ReadOnlySpan<byte> data, long offset, int bits)
    {
        ulong value = 0;
        for (int done = 0; done < bits;)
        {
            int index = checked((int)(offset >> 3));
            int shift = (int)(offset & 7);
            int take = Math.Min(8 - shift, bits - done);
            if ((uint)index >= data.Length) throw new InvalidDataException("Truncated bitstream.");
            value |= (ulong)((data[index] >> shift) & ((1 << take) - 1)) << done;
            done += take; offset += take;
        }
        return value;
    }
    public static void Write(Span<byte> data, long offset, int bits, ulong value)
    {
        for (int done = 0; done < bits;)
        {
            int shift = (int)(offset & 7), take = Math.Min(8 - shift, bits - done);
            data[checked((int)(offset >> 3))] |= (byte)(((value >> done) & (ulong)((1 << take) - 1)) << shift);
            done += take; offset += take;
        }
    }
    public static ulong Number(ReadOnlySpan<byte> bytes) => bytes.Length switch
    {
        1 => bytes[0], 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
        4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes), 8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
        _ => throw new InvalidDataException("Invalid numeric element width.")
    };
    public static void Number(Span<byte> bytes, ulong value)
    {
        switch (bytes.Length)
        {
            case 1: bytes[0] = (byte)value; break;
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value); break;
            case 4: BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value); break;
            case 8: BinaryPrimitives.WriteUInt64LittleEndian(bytes, value); break;
            default: throw new InvalidDataException("Invalid numeric element width.");
        }
    }
    public static ulong Mask(int width) => width == 8 ? ulong.MaxValue : (1UL << (width * 8)) - 1;
}
