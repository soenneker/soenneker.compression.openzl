using System;
using System.IO;

namespace Soenneker.Compression.OpenZl.Internal;

internal sealed class CodecStream(byte[] data, OpenZlDataType type = OpenZlDataType.Serial, int width = 1, int[]? lengths = null)
{
    public byte[] Data { get; } = data;
    public OpenZlDataType Type { get; } = type;
    public int Width { get; } = width;
    public int[]? Lengths { get; } = lengths;
    public int Count => Lengths?.Length ?? Data.Length / Width;
    public CodecStream As(OpenZlDataType type, int width)
    {
        if (width <= 0 || Data.Length % width != 0 || (type == OpenZlDataType.Numeric && !Bits.NumericWidth(width)))
            throw new InvalidDataException("Invalid typed stream dimensions.");
        return new(Data, type, width);
    }
    public ulong Number(int index) => Bits.Number(Data.AsSpan(checked(index * Width), Width));
}
