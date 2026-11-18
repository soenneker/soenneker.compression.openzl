using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Soenneker.Compression.OpenZl.Tests;

public sealed class OpenZlCompressorTests
{
    [Test]
    [Arguments(OpenZlCompressionMode.Auto, "91E5EE82565AD3A33080CA6A3C3BF5D586EF58C63C81599CBC08514040447293")]
    [Arguments(OpenZlCompressionMode.Lz4, "25C111373C11CAAA80810F6A8F885D644A38E8A16C4F8C3373BD87B62B2A25E2")]
    [Arguments(OpenZlCompressionMode.Numeric, "F7A1ACC39D9749AB4A1D2E751430E66FAB229C149C41BFD3CF1CD090BE0D5369")]
    [Arguments(OpenZlCompressionMode.Store, "83DB9CA927270295C5F3D0D4876A111B71FE09C9F4C78DCEA98CA9F87035E640")]
    public async Task Encoded_frames_remain_byte_compatible(OpenZlCompressionMode mode, string expectedHash)
    {
        var input = new byte[512];
        for (int i = 0; i < input.Length; i++)
            input[i] = (byte)(i / 8);

        var compressor = new OpenZlCompressor();
        byte[] encoded = compressor.Compress(input, new OpenZlOptions { Mode = mode });
        await Assert.That(Convert.ToHexString(SHA256.HashData(encoded))).IsEqualTo(expectedHash);
        await Assert.That(compressor.Decompress(encoded).AsSpan().SequenceEqual(input)).IsTrue();
    }
}
