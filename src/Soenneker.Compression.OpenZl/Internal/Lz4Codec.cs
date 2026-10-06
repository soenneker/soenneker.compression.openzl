using System;
using System.Buffers.Binary;
using System.IO;

namespace Soenneker.Compression.OpenZl.Internal;

internal static class Lz4Codec
{
    public static byte[] Decode(ReadOnlySpan<byte> source, int outputSize, DecodeBudget budget)
    {
        byte[] output = budget.Allocate(outputSize);
        int p = 0, q = 0;
        while (p < source.Length)
        {
            int token = source[p++];
            int literals = Length(source, ref p, token >> 4);
            if (literals > source.Length - p || literals > output.Length - q) throw new InvalidDataException("Invalid LZ4 literals.");
            source.Slice(p, literals).CopyTo(output.AsSpan(q)); p += literals; q += literals;
            if (p == source.Length) break;
            if (p + 2 > source.Length) throw new InvalidDataException("Truncated LZ4 offset.");
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(source[p..]); p += 2;
            int match = checked(Length(source, ref p, token & 15) + 4);
            if (offset == 0 || offset > q || match > output.Length - q) throw new InvalidDataException("Invalid LZ4 match.");
            for (int i = 0; i < match; i++) output[q + i] = output[q + i - offset];
            q += match;
        }
        if (q != outputSize) throw new InvalidDataException("LZ4 output size mismatch.");
        return output;
    }
    private static int Length(ReadOnlySpan<byte> input, ref int p, int length)
    {
        if (length == 15)
        {
            int next;
            do { if (p == input.Length) throw new InvalidDataException("Truncated LZ4 length."); next = input[p++]; length = checked(length + next); } while (next == 255);
        }
        return length;
    }
    public static byte[] Encode(ReadOnlySpan<byte> input)
    {
        using var output = new WireWriter();
        int[] table = new int[65536]; Array.Fill(table, -1);
        int p = 0, anchor = 0;
        while (p <= input.Length - 12)
        {
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(input[p..]);
            int hash = (int)((word * 2654435761U) >> 16);
            int previous = table[hash]; table[hash] = p;
            if (previous < 0 || p - previous > 65535 || !input.Slice(previous, 4).SequenceEqual(input.Slice(p, 4))) { p++; continue; }
            int length = 4;
            while (p + length < input.Length - 5 && input[p + length] == input[previous + length]) length++;
            int literal = p - anchor, encodedMatch = length - 4;
            output.WriteByte((byte)((Math.Min(literal, 15) << 4) | Math.Min(encodedMatch, 15)));
            Extra(output, literal); output.Write(input.Slice(anchor, literal));
            int distance = p - previous; output.WriteByte((byte)distance); output.WriteByte((byte)(distance >> 8));
            Extra(output, encodedMatch); p += length; anchor = p;
        }
        int tail = input.Length - anchor;
        output.WriteByte((byte)(Math.Min(tail, 15) << 4)); Extra(output, tail); output.Write(input[anchor..]);
        return output.ToArray();
    }
    private static void Extra(WireWriter writer, int value)
    {
        if (value < 15) return;
        value -= 15;
        while (value >= 255) { writer.WriteByte(255); value -= 255; }
        writer.WriteByte((byte)value);
    }
}
