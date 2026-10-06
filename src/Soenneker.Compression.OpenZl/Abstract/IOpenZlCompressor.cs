using System;

namespace Soenneker.Compression.OpenZl.Abstract;

/// <summary>Encodes and decodes OpenZL frames entirely in managed code.</summary>
public interface IOpenZlCompressor
{
    /// <summary>Compresses a byte sequence into a single serial-output OpenZL frame.</summary>
    byte[] Compress(ReadOnlySpan<byte> source, OpenZlOptions? options = null);
    /// <summary>Decodes a single serial-output frame. Rejects trailing bytes, corruption, unsupported codecs, and resource-limit violations.</summary>
    byte[] Decompress(ReadOnlySpan<byte> source, OpenZlOptions? options = null);
    /// <summary>Decodes one frame from the start of a buffer and reports the number of bytes consumed.</summary>
    byte[] Decompress(ReadOnlySpan<byte> source, out int bytesConsumed, OpenZlOptions? options = null);
    /// <summary>Reads and validates a frame header, including its checksum when present.</summary>
    OpenZlFrameInfo GetFrameInfo(ReadOnlySpan<byte> source);
    /// <summary>Compresses into caller-provided storage; returns false if the destination is too small.</summary>
    bool TryCompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten, OpenZlOptions? options = null);
    /// <summary>Decompresses into caller-provided storage; returns false if it is too small. Invalid frames still throw.</summary>
    bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten, OpenZlOptions? options = null);
}
