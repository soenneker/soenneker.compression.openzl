using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Soenneker.Compression.OpenZl.Internal;

internal static class FrameEncoder
{
    public static byte[] Encode(ReadOnlySpan<byte> source, OpenZlOptions options)
    {
        var plans = new List<CompressionPlan>();
        if (options.Mode is OpenZlCompressionMode.Auto or OpenZlCompressionMode.Store) plans.Add(new(source.ToArray(), []));
        if (options.Mode is OpenZlCompressionMode.Auto or OpenZlCompressionMode.Lz4)
            plans.Add(new(Lz4Codec.Encode(source), [new(62, WireWriter.EncodeVarint((ulong)source.Length))]));
        if (options.Mode == OpenZlCompressionMode.Auto && source.Length > 0 && source.IndexOfAnyExcept(source[0]) < 0)
            plans.Add(new([source[0]], [new(44, WireWriter.EncodeVarint((ulong)source.Length))]));
        if (options.Mode == OpenZlCompressionMode.Numeric)
        {
            if (source.Length % options.NumericWidth != 0) throw new ArgumentException("Input length must be divisible by NumericWidth.", nameof(source));
            AddNumeric(plans, source, options.NumericWidth);
        }
        else if (options.Mode == OpenZlCompressionMode.Auto)
            foreach (int width in new[] { 1, 2, 4, 8 }) if (source.Length % width == 0) AddNumeric(plans, source, width);
        byte[]? best = null;
        foreach (var plan in plans)
        {
            byte[] frame = Write(source, plan, options);
            if (best == null || frame.Length < best.Length) best = frame;
        }
        return best!;
    }
    private static void AddNumeric(List<CompressionPlan> plans, ReadOnlySpan<byte> source, int width)
    {
        int count = source.Length / width;
        ulong[] raw = new ulong[count];
        for (int i = 0; i < count; i++) raw[i] = Bits.Number(source.Slice(i * width, width));
        AddPacked(plans, raw, width, [new(9, [])]);
        if (count == 0) return;
        ulong[] deltas = new ulong[count - 1]; ulong mask = Bits.Mask(width);
        for (int i = 1; i < count; i++) deltas[i - 1] = unchecked(raw[i] - raw[i - 1]) & mask;
        AddPacked(plans, deltas, width, [new(1, source[..width].ToArray()), new(9, [])]);
        ulong[] zigzag = new ulong[deltas.Length];
        for (int i = 0; i < deltas.Length; i++) zigzag[i] = ((deltas[i] << 1) ^ unchecked(0UL - (deltas[i] >> (width * 8 - 1)))) & mask;
        AddPacked(plans, zigzag, width, [new(3, []), new(1, source[..width].ToArray()), new(9, [])]);
        ulong minimum = raw.Min();
        if (minimum != 0)
        {
            ulong[] shifted = new ulong[count]; for (int i = 0; i < count; i++) shifted[i] = raw[i] - minimum;
            byte[] header = new byte[1 + width]; header[0] = (byte)width; Bits.Number(header.AsSpan(1), minimum);
            AddPacked(plans, shifted, width, [new(35, header), new(9, [])]);
        }
    }
    private static void AddPacked(List<CompressionPlan> plans, ulong[] values, int width, List<GraphNode> tail)
    {
        ulong union = 0; foreach (ulong v in values) union |= v;
        int bits = Math.Max(1, 64 - BitOperations.LeadingZeroCount(union));
        byte[] data = new byte[checked((int)(((long)values.Length * bits + 7) / 8))];
        for (int i = 0; i < values.Length; i++) Bits.Write(data, (long)i * bits, bits, values[i]);
        byte descriptor = (byte)((BitOperations.Log2((uint)width) << 6) | (bits - 1));
        int extra = (int)((long)data.Length * 8 / bits - values.Length);
        byte[] header = extra == 0 ? [descriptor] : [descriptor, (byte)extra];
        List<GraphNode> nodes = [new(28, header), .. tail];
        plans.Add(new(data, nodes));
        if (data.Length > 16)
        {
            byte[] compressed = Lz4Codec.Encode(data);
            if (compressed.Length + 4 < data.Length) plans.Add(new(compressed, [new(62, WireWriter.EncodeVarint((ulong)data.Length)), .. nodes]));
        }
    }
    private static byte[] Write(ReadOnlySpan<byte> source, CompressionPlan plan, OpenZlOptions options)
    {
        using var writer = new WireWriter();
        Span<byte> word = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(word, FrameHeader.Magic + (uint)options.FormatVersion); writer.Write(word);
        byte[] comment = Encoding.UTF8.GetBytes(options.Comment ?? "");
        if (comment.Length > 10000 || (comment.Length > 0 && options.FormatVersion < 22)) throw new ArgumentException("Invalid frame comment for the selected version.", nameof(options));
        writer.WriteByte((byte)((options.ContentChecksum ? 1 : 0) | (options.CompressedChecksum ? 2 : 0) | (comment.Length > 0 ? 4 : 0)));
        writer.WriteByte(1); writer.Varint((ulong)source.Length + 1);
        if (comment.Length > 0) { writer.Varint((ulong)comment.Length); writer.Write(comment); }
        if (options.CompressedChecksum) writer.WriteByte((byte)XxHash3.Hash(writer.GetBuffer().AsSpan(0, (int)writer.Length)));
        int blockStart = (int)writer.Length, count = plan.Nodes.Count;
        writer.Varint((ulong)count + 1); writer.Varint(1);
        writer.Packed(new int[count], 1);
        writer.Packed(plan.Nodes.Select(x => x.Codec).ToArray(), options.FormatVersion < 24 ? 6 : 7);
        writer.Packed(plan.Nodes.Select(x => x.Header.Length == 0 ? 0 : 1).ToArray(), 1);
        foreach (var node in plan.Nodes) if (node.Header.Length > 0) writer.Varint((ulong)node.Header.Length - 1);
        writer.Packed(new int[count], 1); // no variable inputs
        writer.Packed(new int[count], 1); // one regenerated stream per node
        writer.Packed(new int[count], Bits.Width(count + 1)); // linear chain: output immediately follows input
        writer.Varint((ulong)plan.Stored.Length);
        foreach (var node in plan.Nodes) writer.Write(node.Header);
        writer.Write(plan.Stored);
        if (options.ContentChecksum) { BinaryPrimitives.WriteUInt32LittleEndian(word, (uint)XxHash3.Hash(source)); writer.Write(word); }
        if (options.CompressedChecksum)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(word, (uint)XxHash3.Hash(writer.GetBuffer().AsSpan(blockStart, (int)writer.Length - blockStart))); writer.Write(word);
        }
        writer.WriteByte(0); return writer.ToArray();
    }
}
