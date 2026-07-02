/*
 *  This file is part of vban.
 *  Copyright (c) 2017 by Benoît Quiniou <quiniouben@yahoo.fr>
 *
 *  C# port of src/common/packet.{c,h}
 *  Licensed under the GNU General Public License v3 (or later).
 */

using System.Buffers.Binary;
using System.Text;

namespace Vban;

/// <summary>
/// VBAN packet header inspection and construction. All methods operate directly
/// on the packet byte buffer to mirror the in-place handling of the C sources.
/// </summary>
public static class Packet
{
    /// <summary>Offset of the payload within a packet buffer.</summary>
    public const int PayloadOffset = VBan.HeaderSize;

    public static int PayloadSize(int totalSize) => totalSize - VBan.HeaderSize;

    // ---- raw header field accessors ----
    private static byte FormatSR(ReadOnlySpan<byte> b) => b[VBan.OffsetFormatSR];
    private static byte FormatNbs(ReadOnlySpan<byte> b) => b[VBan.OffsetFormatNbs];
    private static byte FormatNbc(ReadOnlySpan<byte> b) => b[VBan.OffsetFormatNbc];
    private static byte FormatBit(ReadOnlySpan<byte> b) => b[VBan.OffsetFormatBit];

    private static string ReadStreamName(ReadOnlySpan<byte> b)
    {
        var name = b.Slice(VBan.OffsetStreamName, VBan.StreamNameSize);
        int len = name.IndexOf((byte)0);
        if (len < 0)
        {
            len = VBan.StreamNameSize;
        }

        return Encoding.ASCII.GetString(name.Slice(0, len));
    }

    /// <summary>
    /// Check packet content; returns 0 only for a valid audio PCM packet.
    /// </summary>
    public static int Check(string streamName, ReadOnlySpan<byte> buffer, int size)
    {
        if (streamName is null)
        {
            Logger.Log(LogLevel.Fatal, "%s: null pointer argument", nameof(Check));
            return Errno.EINVAL;
        }

        if (size <= VBan.HeaderSize)
        {
            Logger.Log(LogLevel.Warning, "%s: packet too small", nameof(Check));
            return Errno.EINVAL;
        }

        if (!buffer.Slice(0, 4).SequenceEqual(VBan.Fourc))
        {
            Logger.Log(LogLevel.Warning, "%s: invalid vban magic fourc", nameof(Check));
            return Errno.EINVAL;
        }

        if (!string.Equals(streamName, ReadStreamName(buffer), StringComparison.Ordinal))
        {
            Logger.Log(LogLevel.Debug, "%s: different streamname", nameof(Check));
            return Errno.EINVAL;
        }

        // the reserved bit must be 0
        if ((FormatBit(buffer) & VBan.ReservedMask) != 0)
        {
            Logger.Log(LogLevel.Warning, "%s: reserved format bit invalid value", nameof(Check));
            return Errno.EINVAL;
        }

        var protocol = (VBanProtocol)(FormatSR(buffer) & VBan.ProtocolMask);
        var codec = (VBanCodec)(FormatBit(buffer) & VBan.CodecMask);

        switch (protocol)
        {
            case VBanProtocol.Audio:
                return codec == VBanCodec.Pcm ? PcmCheck(buffer, size) : Errno.EINVAL;

            case VBanProtocol.Serial:
            case VBanProtocol.Txt:
            case VBanProtocol.Undefined1:
            case VBanProtocol.Undefined2:
            case VBanProtocol.Undefined3:
            case VBanProtocol.Undefined4:
                // not supported yet
                return Errno.EINVAL;

            default:
                Logger.Log(LogLevel.Error, "%s: packet with unknown protocol", nameof(Check));
                return Errno.EINVAL;
        }
    }

    private static int PcmCheck(ReadOnlySpan<byte> buffer, int size)
    {
        int bitResolution = FormatBit(buffer) & VBan.BitResolutionMask;
        int sampleRate = FormatSR(buffer) & VBan.SrMask;
        int nbSamples = FormatNbs(buffer) + 1;
        int nbChannels = FormatNbc(buffer) + 1;

        Logger.Log(LogLevel.Debug,
            "%s: packet is vban, sr: %d, nbs: %d, nbc: %d, bit: %d, name: %s, nu: %u",
            nameof(PcmCheck), FormatSR(buffer), FormatNbs(buffer), FormatNbc(buffer),
            FormatBit(buffer), ReadStreamName(buffer), BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(VBan.OffsetNuFrame)));

        if (bitResolution >= (int)VBanBitResolution.Max)
        {
            Logger.Log(LogLevel.Warning, "%s: invalid bit resolution", nameof(PcmCheck));
            return Errno.EINVAL;
        }

        if (sampleRate >= VBan.SrMaxNumber)
        {
            Logger.Log(LogLevel.Warning, "%s: invalid sample rate", nameof(PcmCheck));
            return Errno.EINVAL;
        }

        int sampleSize = VBan.BitResolutionSize[bitResolution];
        int payloadSize = nbSamples * sampleSize * nbChannels;

        if (payloadSize != size - VBan.HeaderSize)
        {
            Logger.Log(LogLevel.Warning, "%s: invalid payload size, expected %d, got %d",
                nameof(PcmCheck), payloadSize, size - VBan.HeaderSize);
            return Errno.EINVAL;
        }

        return 0;
    }

    public static int GetMaxPayloadSize(ReadOnlySpan<byte> buffer)
    {
        // size in bytes cannot exceed DataMaxSize
        // size in samples cannot exceed SamplesMaxNb
        int sampleSize = (FormatNbc(buffer) + 1) * VBan.BitResolutionSize[FormatBit(buffer) & VBan.BitResolutionMask];
        int sampleCount = VBan.DataMaxSize / sampleSize;
        if (sampleCount > VBan.SamplesMaxNb)
        {
            sampleCount = VBan.SamplesMaxNb;
        }

        return sampleCount * sampleSize;
    }

    public static int GetStreamConfig(ReadOnlySpan<byte> buffer, out StreamConfig streamConfig)
    {
        streamConfig = default;

        // no, we don't re-check whether this is a valid audio pcm packet...
        streamConfig.NbChannels = (uint)(FormatNbc(buffer) + 1);
        streamConfig.SampleRate = (uint)VBan.SrList[FormatSR(buffer) & VBan.SrMask];
        streamConfig.BitFmt = (VBanBitResolution)(FormatBit(buffer) & VBan.BitResolutionMask);

        return 0;
    }

    public static int InitHeader(Span<byte> buffer, in StreamConfig streamConfig, string streamName)
    {
        VBan.Fourc.CopyTo(buffer.Slice(VBan.OffsetFourc, 4));
        buffer[VBan.OffsetFormatNbc] = (byte)(streamConfig.NbChannels - 1);
        buffer[VBan.OffsetFormatSR] = (byte)SrFromValue(streamConfig.SampleRate);
        buffer[VBan.OffsetFormatBit] = (byte)streamConfig.BitFmt;
        WriteStreamName(buffer, streamName);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(VBan.OffsetNuFrame), 0);

        return 0;
    }

    public static int SetNewContent(Span<byte> buffer, int payloadSize)
    {
        int frameSize = (FormatNbc(buffer) + 1) * VBan.BitResolutionSize[FormatBit(buffer) & VBan.BitResolutionMask];
        buffer[VBan.OffsetFormatNbs] = (byte)(payloadSize / frameSize - 1);

        uint nuFrame = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(VBan.OffsetNuFrame));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(VBan.OffsetNuFrame), nuFrame + 1);

        return 0;
    }

    /// <summary>Writes a stream name into the header (max 15 chars + NUL), like strncpy.</summary>
    private static void WriteStreamName(Span<byte> buffer, string streamName)
    {
        var field = buffer.Slice(VBan.OffsetStreamName, VBan.StreamNameSize);
        field.Clear();
        int max = VBan.StreamNameSize - 1;
        for (int i = 0; i < streamName.Length && i < max; ++i)
        {
            field[i] = (byte)streamName[i];
        }
    }

    private static int SrFromValue(uint value)
    {
        int index = 0;
        while (index < VBan.SrMaxNumber && value != VBan.SrList[index])
        {
            ++index;
        }

        return index;
    }
}
