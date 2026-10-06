using System.IO;

namespace Soenneker.Compression.OpenZl.Internal;

internal sealed class DecodeBudget(OpenZlOptions options)
{
    private long _allocated;
    public byte[] Allocate(long size)
    {
        if (size < 0 || size > options.MaxOutputBytes || size > options.MaxIntermediateBytes - _allocated)
            throw new InvalidDataException("OpenZL decompression memory limit exceeded.");
        _allocated += size;
        return new byte[(int)size];
    }
}
