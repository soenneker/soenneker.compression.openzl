// Scalar managed translation of xxHash's XXH3_64bits, as vendored by Meta OpenZL.
// Copyright (c) Yann Collet. BSD-2-Clause; see THIRD-PARTY-NOTICES.
using System;
using System.Buffers.Binary;
using System.Numerics;

namespace Soenneker.Compression.OpenZl.Internal;

internal static class XxHash3
{
    private const ulong P1 = 0x9E3779B185EBCA87, P2 = 0xC2B2AE3D27D4EB4F, P3 = 0x165667B19E3779F9,
        P4 = 0x85EBCA77C2B2AE63, P5 = 0x27D4EB2F165667C5;
    private static ReadOnlySpan<byte> Secret => [
        0xb8,0xfe,0x6c,0x39,0x23,0xa4,0x4b,0xbe,0x7c,0x01,0x81,0x2c,0xf7,0x21,0xad,0x1c,
        0xde,0xd4,0x6d,0xe9,0x83,0x90,0x97,0xdb,0x72,0x40,0xa4,0xa4,0xb7,0xb3,0x67,0x1f,
        0xcb,0x79,0xe6,0x4e,0xcc,0xc0,0xe5,0x78,0x82,0x5a,0xd0,0x7d,0xcc,0xff,0x72,0x21,
        0xb8,0x08,0x46,0x74,0xf7,0x43,0x24,0x8e,0xe0,0x35,0x90,0xe6,0x81,0x3a,0x26,0x4c,
        0x3c,0x28,0x52,0xbb,0x91,0xc3,0x00,0xcb,0x88,0xd0,0x65,0x8b,0x1b,0x53,0x2e,0xa3,
        0x71,0x64,0x48,0x97,0xa2,0x0d,0xf9,0x4e,0x38,0x19,0xef,0x46,0xa9,0xde,0xac,0xd8,
        0xa8,0xfa,0x76,0x3f,0xe3,0x9c,0x34,0x3f,0xf9,0xdc,0xbb,0xc7,0xc7,0x0b,0x4f,0x1d,
        0x8a,0x51,0xe0,0x4b,0xcd,0xb4,0x59,0x31,0xc8,0x9f,0x7e,0xc9,0xd9,0x78,0x73,0x64,
        0xea,0xc5,0xac,0x83,0x34,0xd3,0xeb,0xc3,0xc5,0x81,0xa0,0xff,0xfa,0x13,0x63,0xeb,
        0x17,0x0d,0xdd,0x51,0xb7,0xf0,0xda,0x49,0xd3,0x16,0x55,0x26,0x29,0xd4,0x68,0x9e,
        0x2b,0x16,0xbe,0x58,0x7d,0x47,0xa1,0xfc,0x8f,0xf8,0xb8,0xd1,0x7a,0xd0,0x31,0xce,
        0x45,0xcb,0x3a,0x8f,0x95,0x16,0x04,0x28,0xaf,0xd7,0xfb,0xca,0xbb,0x4b,0x40,0x7e];
    private static ulong U64(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    private static ulong Fold(ulong a, ulong b) { UInt128 p = (UInt128)a * b; return (ulong)p ^ (ulong)(p >> 64); }
    private static ulong Avalanche(ulong h) { h ^= h >> 37; h *= 0x165667919E3779F9; return h ^ (h >> 32); }
    private static ulong OldAvalanche(ulong h) { h ^= h >> 33; h *= P2; h ^= h >> 29; h *= P3; return h ^ (h >> 32); }
    private static ulong Mix(ReadOnlySpan<byte> data, int offset, int secret) => Fold(U64(data, offset) ^ U64(Secret, secret), U64(data, offset + 8) ^ U64(Secret, secret + 8));
    public static ulong Hash(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            int n = data.Length;
            if (n <= 16)
            {
                if (n > 8)
                {
                    ulong lo = U64(data, 0) ^ U64(Secret, 24) ^ U64(Secret, 32);
                    ulong hi = U64(data, n - 8) ^ U64(Secret, 40) ^ U64(Secret, 48);
                    return Avalanche((ulong)n + BinaryPrimitives.ReverseEndianness(lo) + hi + Fold(lo, hi));
                }
                if (n >= 4)
                {
                    ulong h = (U32(data, n - 4) + ((ulong)U32(data, 0) << 32)) ^ U64(Secret, 8) ^ U64(Secret, 16);
                    h ^= BitOperations.RotateLeft(h, 49) ^ BitOperations.RotateLeft(h, 24);
                    h *= 0x9FB21C651E98DF25; h ^= (h >> 35) + (ulong)n; h *= 0x9FB21C651E98DF25;
                    return h ^ (h >> 28);
                }
                if (n > 0)
                {
                    uint combined = ((uint)data[0] << 16) | ((uint)data[n >> 1] << 24) | data[n - 1] | ((uint)n << 8);
                    return OldAvalanche(combined ^ U32(Secret, 0) ^ U32(Secret, 4));
                }
                return OldAvalanche(U64(Secret, 56) ^ U64(Secret, 64));
            }
            ulong acc = (ulong)n * P1;
            if (n <= 128)
            {
                for (int i = 0; i <= (n - 1) / 32; i++)
                    acc += Mix(data, 16 * i, 32 * i) + Mix(data, n - 16 * (i + 1), 32 * i + 16);
                return Avalanche(acc);
            }
            if (n <= 240)
            {
                for (int i = 0; i < 8; i++) acc += Mix(data, 16 * i, 16 * i);
                acc = Avalanche(acc);
                ulong end = Mix(data, n - 16, 119);
                for (int i = 8; i < n / 16; i++) end += Mix(data, 16 * i, 16 * (i - 8) + 3);
                return Avalanche(acc + end);
            }
            Span<ulong> lanes = stackalloc ulong[] { 0xC2B2AE3D, P1, P2, P3, P4, 0x85EBCA77, P5, 0x9E3779B1 };
            int blocks = (n - 1) / 1024;
            for (int b = 0; b < blocks; b++)
            {
                for (int i = 0; i < 16; i++) Stripe(lanes, data, b * 1024 + i * 64, i * 8);
                for (int i = 0; i < 8; i++) lanes[i] = ((lanes[i] ^ (lanes[i] >> 47)) ^ U64(Secret, 128 + i * 8)) * 0x9E3779B1;
            }
            for (int i = 0; i < (n - 1 - blocks * 1024) / 64; i++) Stripe(lanes, data, blocks * 1024 + i * 64, i * 8);
            Stripe(lanes, data, n - 64, 121);
            for (int i = 0; i < 4; i++) acc += Fold(lanes[i * 2] ^ U64(Secret, 11 + i * 16), lanes[i * 2 + 1] ^ U64(Secret, 19 + i * 16));
            return Avalanche(acc);
        }
    }
    private static void Stripe(Span<ulong> lanes, ReadOnlySpan<byte> data, int offset, int secret)
    {
        unchecked
        {
            for (int i = 0; i < 8; i++)
            {
                ulong value = U64(data, offset + i * 8), key = value ^ U64(Secret, secret + i * 8);
                lanes[i ^ 1] += value;
                lanes[i] += (ulong)(uint)key * (key >> 32);
            }
        }
    }
}
