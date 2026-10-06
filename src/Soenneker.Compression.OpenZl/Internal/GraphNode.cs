namespace Soenneker.Compression.OpenZl.Internal;

internal sealed record GraphNode(int Codec, byte[] Header, int InputCount = 1, int OutputCount = 1)
{
    public int InputStart { get; set; }
    public int[] Outputs { get; set; } = [];
}
