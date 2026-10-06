namespace Soenneker.Compression.OpenZl;

/// <summary>Controls frame encoding and bounds memory consumption during decoding.</summary>
public sealed record OpenZlOptions
{
    /// <summary>The emitted wire format version, from 21 through 27.</summary>
    public int FormatVersion { get; init; } = 27;
    /// <summary>The compression strategy. Auto compares candidate graphs by encoded size.</summary>
    public OpenZlCompressionMode Mode { get; init; } = OpenZlCompressionMode.Auto;
    /// <summary>The little-endian numeric element width (1, 2, 4, or 8).</summary>
    public int NumericWidth { get; init; } = 4;
    /// <summary>Whether to emit and verify the compressed-data checksum.</summary>
    public bool CompressedChecksum { get; init; } = true;
    /// <summary>Whether to emit the decoded-data checksum.</summary>
    public bool ContentChecksum { get; init; } = true;
    /// <summary>Optional UTF-8 frame comment (at most 10,000 encoded bytes; version 22 or later).</summary>
    public string? Comment { get; init; }
    /// <summary>Maximum total decoded output bytes accepted by a single operation.</summary>
    public int MaxOutputBytes { get; init; } = 256 * 1024 * 1024;
    /// <summary>Maximum cumulative intermediate stream bytes allocated while decoding a frame.</summary>
    public long MaxIntermediateBytes { get; init; } = 1024L * 1024 * 1024;
    /// <summary>Maximum number of graph nodes or streams in a block.</summary>
    public int MaxGraphSize { get; init; } = 65536;
}
