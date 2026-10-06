using System;
using System.IO;
using System.Text;

namespace Soenneker.Compression.OpenZl.Internal;

internal static class FrameHeader
{
    public const uint Magic = 0xD7B1A5C0;
    public static OpenZlFrameInfo Read(ref WireReader reader, ReadOnlySpan<byte> source)
    {
        uint magic = reader.UInt32();
        if (magic < Magic || magic > Magic + 63) throw new InvalidDataException("Not an OpenZL frame.");
        int version = (int)(magic - Magic);
        if (version is < 21 or > 27) throw new NotSupportedException($"OpenZL format version {version} is not supported; expected 21–27.");
        byte flags = reader.Byte();
        if ((flags & (version >= 25 ? 0xf0 : 0xf8)) != 0) throw new InvalidDataException("Reserved frame flags are set.");
        if ((flags & 8) != 0) throw new NotSupportedException("Dictionary bundles are not supported.");
        int token = reader.Byte(), count = token & 15;
        if (count == 15) count = (reader.Byte() << 4) + (token >> 4) + 15;
        if (count is < 1 or > 2048) throw new InvalidDataException("Invalid output count.");
        OpenZlDataType[] types = new OpenZlDataType[count];
        int done = 0;
        if ((token & 15) != 15)
            for (; done < Math.Min(count, 2); done++) types[done] = (OpenZlDataType)((token >> (4 + done * 2)) & 3);
        if (done < count)
        {
            int[] packed = reader.Packed(count - done, 2);
            for (int i = 0; i < packed.Length; i++) types[done + i] = (OpenZlDataType)packed[i];
        }
        long?[] sizes = new long?[count];
        for (int i = 0; i < count; i++)
        {
            ulong size = reader.Varint();
            if (size == 0) throw new NotSupportedException("Frames with unknown output sizes are not supported by this format implementation.");
            if (size - 1 > long.MaxValue) throw new InvalidDataException("Output size overflow.");
            sizes[i] = (long)(size - 1);
        }
        for (int i = 0; i < count; i++) if (types[i] == OpenZlDataType.String) reader.Varint();
        string? comment = null;
        if ((flags & 4) != 0)
        {
            if (version < 22) throw new InvalidDataException("Comments require version 22.");
            int size = reader.Size(10000);
            if (size == 0) throw new InvalidDataException("Empty comment with comment flag set.");
            comment = Encoding.UTF8.GetString(reader.Read(size));
        }
        if ((flags & 2) != 0)
        {
            byte hash = (byte)XxHash3.Hash(source[..reader.Position]);
            if (reader.Byte() != hash) throw new InvalidDataException("OpenZL header checksum mismatch.");
        }
        return new(version, reader.Position, Array.AsReadOnly(types), Array.AsReadOnly(sizes), (flags & 1) != 0, (flags & 2) != 0, comment);
    }
}
