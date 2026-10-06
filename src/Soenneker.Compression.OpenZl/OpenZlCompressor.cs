using System;
using System.IO;
using Soenneker.Compression.OpenZl.Abstract;
using Soenneker.Compression.OpenZl.Internal;

namespace Soenneker.Compression.OpenZl;

public sealed class OpenZlCompressor : IOpenZlCompressor
{
    public byte[] Compress(ReadOnlySpan<byte> source, OpenZlOptions? options = null) => FrameEncoder.Encode(source, Validate(options));
    public byte[] Decompress(ReadOnlySpan<byte> source, OpenZlOptions? options = null)
    {
        byte[] result = Decompress(source, out int consumed, options);
        if (consumed != source.Length) throw new InvalidDataException("Trailing bytes after the OpenZL frame.");
        return result;
    }
    public byte[] Decompress(ReadOnlySpan<byte> source, out int bytesConsumed, OpenZlOptions? options = null)
    {
        options = Validate(options);
        try { return FrameDecoder.Decode(source, out bytesConsumed, options); }
        catch (OverflowException e) { throw new InvalidDataException("Integer overflow in OpenZL frame.", e); }
    }
    public OpenZlFrameInfo GetFrameInfo(ReadOnlySpan<byte> source) { var reader = new WireReader(source); return FrameHeader.Read(ref reader, source); }
    public bool TryCompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten, OpenZlOptions? options = null)
    {
        byte[] encoded = Compress(source, options); bytesWritten = 0;
        if (!encoded.AsSpan().TryCopyTo(destination)) return false;
        bytesWritten = encoded.Length; return true;
    }
    public bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten, OpenZlOptions? options = null)
    {
        byte[] decoded = Decompress(source, options); bytesWritten = 0;
        if (!decoded.AsSpan().TryCopyTo(destination)) return false;
        bytesWritten = decoded.Length; return true;
    }
    private static OpenZlOptions Validate(OpenZlOptions? options)
    {
        options ??= new();
        if (options.FormatVersion is < 21 or > 27 || !Enum.IsDefined(options.Mode) || !Bits.NumericWidth(options.NumericWidth)
            || options.MaxOutputBytes < 0 || options.MaxIntermediateBytes < 0 || options.MaxGraphSize is < 2 or > 1048576)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid OpenZL options.");
        return options;
    }
}
