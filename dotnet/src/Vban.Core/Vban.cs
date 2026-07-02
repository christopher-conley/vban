/*
 *  This file is part of vban.
 *  Copyright (c) 2015 by Benoît Quiniou <quiniouben@yahoo.fr>
 *
 *  vban is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  vban is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with vban.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  C# port of src/vban/vban.h
 */

namespace Vban;

/// <summary>
/// VBAN protocol constants, enumerations and lookup tables.
/// </summary>
public static class VBan
{
    /// <summary>Version string, mirrors src/common/version.h.</summary>
    public const string Version = "2.2.0";

    public const int HeaderSize = 4 + 4 + 16 + 4;          // 28 bytes
    public const int StreamNameSize = 16;
    public const int ProtocolMaxSize = 1464;
    public const int DataMaxSize = ProtocolMaxSize - HeaderSize;
    public const int ChannelsMaxNb = 256;
    public const int SamplesMaxNb = 256;

    /// <summary>The four magic bytes 'V','B','A','N' at the start of every packet.</summary>
    public static ReadOnlySpan<byte> Fourc => "VBAN"u8;

    // ---- header field byte offsets within a packet buffer ----
    public const int OffsetFourc = 0;       // 4 bytes
    public const int OffsetFormatSR = 4;    // 1 byte
    public const int OffsetFormatNbs = 5;   // 1 byte
    public const int OffsetFormatNbc = 6;   // 1 byte
    public const int OffsetFormatBit = 7;   // 1 byte
    public const int OffsetStreamName = 8;  // 16 bytes
    public const int OffsetNuFrame = 24;    // 4 bytes (little-endian)

    public const int SrMask = 0x1F;
    public const int SrMaxNumber = 21;

    public static readonly long[] SrList =
    {
        6000, 12000, 24000, 48000, 96000, 192000, 384000,
        8000, 16000, 32000, 64000, 128000, 256000, 512000,
        11025, 22050, 44100, 88200, 176400, 352800, 705600,
    };

    public const int ProtocolMask = 0xE0;
    public const int BitResolutionMask = 0x07;
    public const int ReservedMask = 0x08;
    public const int CodecMask = 0xF0;

    /// <summary>
    /// Size in bytes of one sample for each <see cref="VBanBitResolution"/>.
    /// Mirrors the original C table, which only defines the first 6 entries
    /// (12I and 10I are left at 0).
    /// </summary>
    public static readonly int[] BitResolutionSize = { 1, 2, 3, 4, 4, 8, 0, 0 };

    // ---- text sub-protocol ----
    public const int BpsMask = 0xE0;
    public const int BpsMaxNumber = 25;

    public static readonly long[] BpsList =
    {
        0, 110, 150, 300, 600,
        1200, 2400, 4800, 9600, 14400,
        19200, 31250, 38400, 57600, 115200,
        128000, 230400, 250000, 256000, 460800,
        921600, 1000000, 1500000, 2000000, 3000000,
    };
}

public enum VBanProtocol
{
    Audio = 0x00,
    Serial = 0x20,
    Txt = 0x40,
    Undefined1 = 0x80,
    Undefined2 = 0xA0,
    Undefined3 = 0xC0,
    Undefined4 = 0xE0,
}

public enum VBanBitResolution
{
    Int8 = 0,
    Int16,
    Int24,
    Int32,
    Float32,
    Float64,
    Int12,
    Int10,
    Max,
}

public enum VBanCodec
{
    Pcm = 0x00,
    Vbca = 0x10,
    Vbcv = 0x20,
    User = 0xF0,
}

public enum VBanStreamType
{
    Ascii = 0x00,
    Utf8 = 0x10,
    Wchar = 0x20,
    User = 0xF0,
}
