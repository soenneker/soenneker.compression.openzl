using System.Collections.Generic;

namespace Soenneker.Compression.OpenZl;

/// <summary>Describes the frame header without decoding its payload.</summary>
public sealed record OpenZlFrameInfo(int FormatVersion, int HeaderSize, IReadOnlyList<OpenZlDataType> OutputTypes,
    IReadOnlyList<long?> OutputSizes, bool ContentChecksum, bool CompressedChecksum, string? Comment);
