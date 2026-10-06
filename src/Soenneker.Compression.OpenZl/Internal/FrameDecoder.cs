using System;
using System.IO;
using System.Linq;

namespace Soenneker.Compression.OpenZl.Internal;

internal static class FrameDecoder
{
    public static byte[] Decode(ReadOnlySpan<byte> source, out int consumed, OpenZlOptions options)
    {
        var reader = new WireReader(source);
        OpenZlFrameInfo info = FrameHeader.Read(ref reader, source);
        if (info.OutputTypes.Count != 1 || info.OutputTypes[0] != OpenZlDataType.Serial)
            throw new NotSupportedException("The byte-buffer API requires one serial output.");
        long outputSize = info.OutputSizes[0]!.Value;
        var budget = new DecodeBudget(options);
        byte[] output = budget.Allocate(outputSize);
        int outputPosition = 0;
        while (true)
        {
            int blockStart = reader.Position;
            int encodedCount = reader.Size(options.MaxGraphSize);
            if (encodedCount == 0) break;
            int nodeCount = encodedCount - 1, storedCount = reader.Size(options.MaxGraphSize);
            int[] custom = reader.Packed(nodeCount, 1);
            int standardCount = custom.Count(x => x == 0);
            int[] standard = reader.Packed(standardCount, info.FormatVersion < 24 ? 6 : 7);
            int[] ids = new int[nodeCount]; int standardIndex = 0;
            for (int i = 0; i < nodeCount; i++)
            {
                ids[i] = custom[i] == 0 ? standard[standardIndex++] : reader.Size();
                if (custom[i] != 0) throw new NotSupportedException($"Custom OpenZL codec {ids[i]} requires an application decoder.");
            }
            int[] headerSizes = Lane(ref reader, nodeCount, 0, 1, options.MaxGraphSize);
            int[] variableInputs = Lane(ref reader, nodeCount, 0, 1, options.MaxGraphSize);
            int[] outputCounts = Lane(ref reader, nodeCount, 1, 2, options.MaxGraphSize);
            long totalRegens = 0; foreach (int count in outputCounts) totalRegens += count;
            if (totalRegens + storedCount > options.MaxGraphSize) throw new InvalidDataException("OpenZL graph exceeds configured limits.");
            int streamCount = (int)totalRegens + storedCount;
            int[] distances = reader.Packed((int)totalRegens, Bits.Width(streamCount));
            int[] storedSizes = new int[storedCount];
            for (int i = 0; i < storedCount; i++) storedSizes[i] = reader.Size(options.MaxOutputBytes);
            GraphNode[] nodes = new GraphNode[nodeCount];
            bool[] regenerated = new bool[streamCount];
            int inputStart = 0, distanceIndex = 0;
            for (int i = 0; i < nodeCount; i++)
            {
                int fixedInputs = CodecDecoder.FixedInputs(ids[i]);
                if (variableInputs[i] != 0 && ids[i] is not (4 or 40 or 41 or 47 or 48)) throw new InvalidDataException("Variable inputs on a fixed-arity codec.");
                int inputCount = checked(fixedInputs + variableInputs[i]);
                int inputEnd = checked(inputStart + inputCount);
                if (inputEnd > streamCount) throw new InvalidDataException("Graph input exceeds stream count.");
                var node = new GraphNode(ids[i], reader.Read(headerSizes[i]).ToArray(), inputCount, outputCounts[i]) { InputStart = inputStart, Outputs = new int[outputCounts[i]] };
                for (int j = 0; j < node.OutputCount; j++)
                {
                    int target = checked(inputEnd + distances[distanceIndex++]);
                    if (target >= streamCount || regenerated[target]) throw new InvalidDataException("Invalid graph edge.");
                    regenerated[target] = true; node.Outputs[j] = target;
                }
                inputStart = inputEnd; nodes[i] = node;
            }
            if (inputStart != streamCount - 1) throw new InvalidDataException("Graph does not terminate in one output.");
            CodecStream?[] streams = new CodecStream?[streamCount]; int storedIndex = 0;
            for (int i = 0; i < streamCount; i++)
            {
                if (regenerated[i]) continue;
                if (storedIndex >= storedCount) throw new InvalidDataException("Missing stored stream.");
                byte[] data = budget.Allocate(storedSizes[storedIndex++]); reader.Read(data.Length).CopyTo(data);
                streams[i] = new(data);
            }
            if (storedIndex != storedCount) throw new InvalidDataException("Unused stored stream.");
            uint contentHash = info.ContentChecksum ? reader.UInt32() : 0;
            if (info.CompressedChecksum)
            {
                uint actual = (uint)XxHash3.Hash(source.Slice(blockStart, reader.Position - blockStart));
                if (reader.UInt32() != actual) throw new InvalidDataException("OpenZL compressed checksum mismatch.");
            }
            foreach (var node in nodes)
            {
                CodecStream[] inputs = new CodecStream[node.InputCount];
                for (int i = 0; i < inputs.Length; i++) inputs[i] = streams[node.InputStart + i] ?? throw new InvalidDataException("Graph input has no producer.");
                CodecStream[] results = CodecDecoder.Decode(node, inputs, budget);
                for (int i = 0; i < inputs.Length; i++) streams[node.InputStart + i] = null;
                if (results.Length != node.OutputCount) throw new InvalidDataException("Codec output arity mismatch.");
                for (int i = 0; i < results.Length; i++) streams[node.Outputs[i]] = results[i];
            }
            CodecStream result = streams[^1] ?? throw new InvalidDataException("Missing decoded output.");
            if (result.Type != OpenZlDataType.Serial || result.Data.Length > output.Length - outputPosition) throw new InvalidDataException("Decoded output dimensions mismatch.");
            if (info.ContentChecksum && (uint)XxHash3.Hash(result.Data) != contentHash) throw new InvalidDataException("OpenZL content checksum mismatch.");
            result.Data.CopyTo(output, outputPosition); outputPosition += result.Data.Length;
        }
        if (outputPosition != output.Length) throw new InvalidDataException("OpenZL output size mismatch.");
        consumed = reader.Position;
        return output;
    }
    private static int[] Lane(ref WireReader reader, int count, int zero, int bias, int limit)
    {
        int[] values = reader.Packed(count, 1);
        for (int i = 0; i < values.Length; i++) values[i] = values[i] == 0 ? zero : checked(reader.Size(limit - bias) + bias);
        return values;
    }
}
