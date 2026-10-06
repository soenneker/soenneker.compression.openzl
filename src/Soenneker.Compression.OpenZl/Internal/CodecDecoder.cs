// Wire semantics ported from Meta OpenZL; see THIRD-PARTY-NOTICES.
using System;
using System.IO;
using System.Linq;

namespace Soenneker.Compression.OpenZl.Internal;

internal static class CodecDecoder
{
    public static int FixedInputs(int id) => id switch
    {
        4 or 40 or 41 or 47 or 48 => 0,
        12 or 18 or 30 or 36 or 37 or 38 or 46 or 55 or 57 or 58 or 59 => 2,
        31 => 4, 32 => 8,
        1 or 2 or 3 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 13 or 14 or 27 or 28 or 35 or 44 or 45 or 53 or 56 or 62 => 1,
        _ => throw new NotSupportedException($"OpenZL standard codec {id} is not implemented.")
    };
    private static void Require(bool condition, string message = "Invalid codec input.") { if (!condition) throw new InvalidDataException(message); }
    private static void Numeric(CodecStream input) => Require(input.Type == OpenZlDataType.Numeric && Bits.NumericWidth(input.Width));
    public static CodecStream[] Decode(GraphNode node, CodecStream[] inputs, DecodeBudget budget)
    {
        int id = node.Codec;
        var h = new WireReader(node.Header);
        CodecStream? a = inputs.Length > 0 ? inputs[0] : null;
        if (id is not (55 or 56 or 57 or 58 or 59)) Require(node.OutputCount == 1, "Unexpected regenerated stream count.");
        switch (id)
        {
            case 5: case 9: case 11:
                h.End(); Require(a!.Type == (id == 5 ? OpenZlDataType.Struct : id == 9 ? OpenZlDataType.Numeric : OpenZlDataType.String));
                return [a.As(OpenZlDataType.Serial, 1)];
            case 6:
                Require(a!.Type == OpenZlDataType.Serial);
                int width = h.Size(); h.End(); return [a.As(OpenZlDataType.Struct, width)];
            case 10:
                Require(a!.Type == OpenZlDataType.Serial);
                int log = h.Byte(); h.End(); Require(log <= 3); return [a.As(OpenZlDataType.Numeric, 1 << log)];
            case 7: case 8:
                h.End(); Require(a!.Type == (id == 7 ? OpenZlDataType.Numeric : OpenZlDataType.Struct));
                return [a.As(id == 7 ? OpenZlDataType.Struct : OpenZlDataType.Numeric, a.Width)];
            case 13: case 14:
                h.End(); Numeric(a!);
                byte[] swapped = budget.Allocate(a!.Data.Length);
                for (int i = 0; i < a.Count; i++) for (int j = 0; j < a.Width; j++) swapped[i * a.Width + j] = a.Data[(i + 1) * a.Width - j - 1];
                return [new(swapped, id == 13 ? OpenZlDataType.Struct : OpenZlDataType.Serial, id == 13 ? a.Width : 1)];
            case 12:
                h.End(); Require(a!.Type == OpenZlDataType.Serial); Numeric(inputs[1]); Require(inputs[1].Width == 4);
                int[] lengths = new int[inputs[1].Count]; long total = 0;
                for (int i = 0; i < lengths.Length; i++) { ulong length = inputs[1].Number(i); Require(length <= int.MaxValue); lengths[i] = (int)length; total += lengths[i]; }
                Require(total == a.Data.Length);
                return [new(a.Data, OpenZlDataType.String, 1, lengths)];
            case 1:
                Numeric(a!);
                if (node.Header.Length == 0) { Require(a!.Count == 0); return [a]; }
                Require(node.Header.Length == a!.Width);
                ulong previous = Bits.Number(node.Header);
                byte[] delta = budget.Allocate((long)a.Data.Length + a.Width);
                Bits.Number(delta.AsSpan(0, a.Width), previous);
                for (int i = 0; i < a.Count; i++) { previous = unchecked(previous + a.Number(i)); Bits.Number(delta.AsSpan((i + 1) * a.Width, a.Width), previous); }
                return [new(delta, a.Type, a.Width)];
            case 3:
                h.End(); Numeric(a!);
                byte[] zigzag = budget.Allocate(a!.Data.Length);
                for (int i = 0; i < a.Count; i++) { ulong v = a.Number(i); Bits.Number(zigzag.AsSpan(i * a.Width, a.Width), (v >> 1) ^ unchecked(0UL - (v & 1))); }
                return [new(zigzag, a.Type, a.Width)];
            case 27: case 28:
                Require(a!.Type == OpenZlDataType.Serial && node.Header.Length is 1 or 2);
                byte first = h.Byte(); int bits = (first & 63) + 1, elementWidth = 1 << (first >> 6);
                int extra = h.Remaining > 0 ? h.Byte() : 0; h.End();
                Require(bits <= elementWidth * 8 && (id != 27 || elementWidth == 1));
                long count = (long)a.Data.Length * 8 / bits - extra;
                Require(count >= 0 && (count * bits + 7) / 8 == a.Data.Length);
                byte[] unpacked = budget.Allocate(count * elementWidth);
                for (int i = 0; i < count; i++) Bits.Number(unpacked.AsSpan(i * elementWidth, elementWidth), Bits.Read(a.Data, (long)i * bits, bits));
                return [new(unpacked, id == 27 ? OpenZlDataType.Serial : OpenZlDataType.Numeric, elementWidth)];
            case 35:
                Numeric(a!); int rangeWidth = h.Byte(); Require(Bits.NumericWidth(rangeWidth));
                ulong min = h.Remaining == 0 ? 0 : Bits.Number(h.Read(rangeWidth)); h.End();
                byte[] range = budget.Allocate((long)a!.Count * rangeWidth); ulong max = Bits.Mask(rangeWidth);
                for (int i = 0; i < a.Count; i++) { ulong v = a.Number(i); Require(v <= max - min); Bits.Number(range.AsSpan(i * rangeWidth, rangeWidth), v + min); }
                return [new(range, OpenZlDataType.Numeric, rangeWidth)];
            case 44: case 45:
                Require(a!.Type == (id == 44 ? OpenZlDataType.Serial : OpenZlDataType.Struct) && a.Count == 1);
                ulong copies = h.Varint(); h.End(); Require(copies <= int.MaxValue);
                byte[] constant = budget.Allocate((long)copies * a.Width);
                for (int p = 0; p < constant.Length; p += a.Width) a.Data.CopyTo(constant, p);
                return [new(constant, a.Type, a.Width)];
            case 53:
                Numeric(a!); ulong divisor = h.Varint(); h.End(); ulong mask = Bits.Mask(a!.Width);
                Require(divisor != 0 && divisor <= mask);
                byte[] product = budget.Allocate(a.Data.Length);
                for (int i = 0; i < a.Count; i++) { ulong v = a.Number(i); Require(v <= mask / divisor); Bits.Number(product.AsSpan(i * a.Width, a.Width), v * divisor); }
                return [new(product, a.Type, a.Width)];
            case 62:
                Require(a!.Type == OpenZlDataType.Serial); int size = h.Size(); h.End();
                return [new(Lz4Codec.Decode(a.Data, size, budget))];
            case 2:
                h.End(); Require(a!.Type == OpenZlDataType.Struct);
                if (a.Count == 0) return [a];
                byte[] transposed = budget.Allocate(a.Data.Length);
                for (int i = 0; i < a.Count; i++) for (int j = 0; j < a.Width; j++) transposed[j * a.Count + i] = a.Data[i * a.Width + j];
                return [new(transposed, OpenZlDataType.Struct, a.Count)];
            case 4: case 30: case 31: case 32: case 41:
                h.End(); Require(inputs.Length > 0);
                int rows = inputs[0].Count, combinedWidth = 0;
                foreach (var stream in inputs) { Require(stream.Count == rows && stream.Type == (id == 41 ? OpenZlDataType.Struct : OpenZlDataType.Serial)); combinedWidth = checked(combinedWidth + stream.Width); }
                byte[] interleaved = budget.Allocate((long)rows * combinedWidth); int field = 0;
                foreach (var stream in inputs) { for (int i = 0; i < rows; i++) stream.Data.AsSpan(i * stream.Width, stream.Width).CopyTo(interleaved.AsSpan(i * combinedWidth + field)); field += stream.Width; }
                return [new(interleaved, id == 41 ? OpenZlDataType.Serial : OpenZlDataType.Struct, id == 41 ? 1 : combinedWidth)];
            case 40: case 47: case 48:
                OpenZlDataType joinType = id == 40 ? OpenZlDataType.Serial : id == 47 ? OpenZlDataType.Struct : OpenZlDataType.Numeric;
                int joinWidth = a?.Width ?? (h.Remaining == 0 ? 1 : h.Size()); h.End(); Require(joinWidth > 0);
                long joinSize = 0; foreach (var stream in inputs) { Require(stream.Type == joinType && stream.Width == joinWidth); joinSize += stream.Data.Length; }
                byte[] joined = budget.Allocate(joinSize); int at = 0;
                foreach (var stream in inputs) { stream.Data.CopyTo(joined, at); at += stream.Data.Length; }
                return [new(joined, joinType, joinWidth)];
            case 56:
                h.End(); Numeric(a!); return Enumerable.Repeat(a!, node.OutputCount).ToArray();
            case 18:
                Numeric(a!); Numeric(inputs[1]); var exceptions = inputs[1]; Require(a!.Width <= exceptions.Width);
                ulong sentinel = h.Remaining == 0 ? Bits.Mask(a.Width) : h.Varint(); h.End(); Require(sentinel <= Bits.Mask(a.Width));
                byte[] values = budget.Allocate((long)a.Count * exceptions.Width); int exception = 0;
                for (int i = 0; i < a.Count; i++) { ulong v = a.Number(i); if (v == sentinel) { Require(exception < exceptions.Count); v = exceptions.Number(exception++); } Bits.Number(values.AsSpan(i * exceptions.Width, exceptions.Width), v); }
                Require(exception == exceptions.Count); return [new(values, OpenZlDataType.Numeric, exceptions.Width)];
            case 36: case 37: case 38:
                h.End(); Require(a!.Type == (id == 36 ? OpenZlDataType.Struct : id == 37 ? OpenZlDataType.Numeric : OpenZlDataType.String));
                var indices = inputs[1]; Numeric(indices);
                if (id == 38) return [TokenizeStrings(a, indices, budget)];
                byte[] tokens = budget.Allocate((long)indices.Count * a.Width);
                for (int i = 0; i < indices.Count; i++) { ulong index = indices.Number(i); Require(index < (ulong)a.Count); a.Data.AsSpan((int)index * a.Width, a.Width).CopyTo(tokens.AsSpan(i * a.Width)); }
                return [new(tokens, a.Type, a.Width)];
            case 46:
                h.End(); Require(a!.Type == OpenZlDataType.String); Numeric(inputs[1]); Require(a.Count == inputs[1].Count);
                int[] decodedLengths = new int[a.Count]; long prefixTotal = 0;
                for (int i = 0; i < a.Count; i++) { ulong prefix = inputs[1].Number(i); Require(prefix <= (ulong)(i == 0 ? 0 : decodedLengths[i - 1])); decodedLengths[i] = checked((int)prefix + a.Lengths![i]); prefixTotal += decodedLengths[i]; }
                byte[] prefixed = budget.Allocate(prefixTotal); int sourceOffset = 0, targetOffset = 0, priorOffset = 0;
                for (int i = 0; i < a.Count; i++) { int prefix = (int)inputs[1].Number(i); prefixed.AsSpan(priorOffset, prefix).CopyTo(prefixed.AsSpan(targetOffset)); a.Data.AsSpan(sourceOffset, a.Lengths![i]).CopyTo(prefixed.AsSpan(targetOffset + prefix)); priorOffset = targetOffset; targetOffset += decodedLengths[i]; sourceOffset += a.Lengths[i]; }
                return [new(prefixed, OpenZlDataType.String, 1, decodedLengths)];
            case 55: case 57: case 58: case 59:
                h.End(); Numeric(a!); Require(a!.Width == 4 && a.Count == node.OutputCount);
                var joinedInput = inputs[1]; Require(joinedInput.Type == (id == 55 ? OpenZlDataType.Serial : id == 57 ? OpenZlDataType.Numeric : id == 58 ? OpenZlDataType.Struct : OpenZlDataType.String));
                CodecStream[] segments = new CodecStream[node.OutputCount]; int elementOffset = 0, byteOffset = 0;
                for (int i = 0; i < segments.Length; i++)
                {
                    ulong countValue = a.Number(i); Require(countValue <= (ulong)(joinedInput.Count - elementOffset)); int segmentCount = (int)countValue;
                    int[]? segmentLengths = joinedInput.Lengths?.AsSpan(elementOffset, segmentCount).ToArray();
                    int segmentSize = segmentLengths is null ? checked(segmentCount * joinedInput.Width) : segmentLengths.Sum();
                    byte[] segment = budget.Allocate(segmentSize); joinedInput.Data.AsSpan(byteOffset, segmentSize).CopyTo(segment);
                    segments[i] = new(segment, joinedInput.Type, joinedInput.Width, segmentLengths); elementOffset += segmentCount; byteOffset += segmentSize;
                }
                Require(elementOffset == joinedInput.Count); return segments;
            default: throw new NotSupportedException($"OpenZL standard codec {id} is not implemented.");
        }
    }
    private static CodecStream TokenizeStrings(CodecStream alphabet, CodecStream indices, DecodeBudget budget)
    {
        int[] offsets = new int[alphabet.Count + 1];
        for (int i = 0; i < alphabet.Count; i++) offsets[i + 1] = checked(offsets[i] + alphabet.Lengths![i]);
        int[] lengths = new int[indices.Count]; long size = 0;
        for (int i = 0; i < indices.Count; i++) { ulong index = indices.Number(i); Require(index < (ulong)alphabet.Count); lengths[i] = alphabet.Lengths![(int)index]; size += lengths[i]; }
        byte[] output = budget.Allocate(size); int p = 0;
        for (int i = 0; i < indices.Count; i++) { alphabet.Data.AsSpan(offsets[(int)indices.Number(i)], lengths[i]).CopyTo(output.AsSpan(p)); p += lengths[i]; }
        return new(output, OpenZlDataType.String, 1, lengths);
    }
}
