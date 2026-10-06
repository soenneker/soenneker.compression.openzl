using System.Collections.Generic;

namespace Soenneker.Compression.OpenZl.Internal;

internal sealed record CompressionPlan(byte[] Stored, List<GraphNode> Nodes);
