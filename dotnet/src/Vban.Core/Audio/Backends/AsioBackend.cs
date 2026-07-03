/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 *
 *  ASIO backend through NAudio.Asio. Windows only; compiled solely into the
 *  net10.0-windows build. The device name is a substring of the ASIO driver
 *  name; empty means the first installed driver.
 *
 *  Playback goes through the common NAudio plumbing (AsioOut is an
 *  IWavePlayer). Capture is ASIO-specific: the driver pushes AudioAvailable
 *  events whose samples are recovered as interleaved 32 bit floats and
 *  converted here to the stream bit format.
 */

#if WINDOWS

using System.Buffers.Binary;
using NAudio.Wave;

namespace Vban.Audio.Backends;

public sealed class AsioBackend : NAudioBackendBase
{
    public const string Name = "asio";

    private AsioOut? _record;
    private VBanBitResolution _bitFmt;
    private float[] _samples = Array.Empty<float>();
    private byte[] _converted = Array.Empty<byte>();

    public override int Open(string outputName, AudioDirection direction, int bufferSize, in StreamConfig config)
    {
        Logger.Log(LogLevel.Debug, "%s", nameof(Open));

        int ret = GetWaveFormat(config, out var format, out int frameSize);
        if (ret != 0)
        {
            return ret;
        }

        string? driver = FindDriver(outputName);
        if (driver is null)
        {
            Logger.Log(LogLevel.Fatal, "%s: no asio driver matching \"%s\"", nameof(Open), outputName);
            return Errno.ENODEV;
        }

        Logger.Log(LogLevel.Info, "%s: using asio driver %s", nameof(Open), driver);

        AsioOut asio;
        try
        {
            asio = new AsioOut(driver);
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Fatal, "%s: unable to open asio driver %s: %s", nameof(Open), driver, e.Message);
            return Errno.ENODEV;
        }

        if (direction == AudioDirection.Out)
        {
            return OpenPlayback(asio, format, bufferSize, frameSize);
        }

        return OpenRecord(asio, config, bufferSize, frameSize);
    }

    private int OpenRecord(AsioOut asio, in StreamConfig config, int bufferSize, int frameSize)
    {
        Ring = new BlockingRingBuffer(RingCapacity(bufferSize, frameSize));
        _bitFmt = config.BitFmt;

        try
        {
            asio.InitRecordAndPlayback(null, (int)config.NbChannels, (int)config.SampleRate);
            asio.AudioAvailable += OnAudioAvailable;
            asio.Play();
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Fatal, "%s: unable to start asio capture: %s", nameof(OpenRecord), e.Message);
            asio.Dispose();
            Ring = null;
            return Errno.ENODEV;
        }

        _record = asio;
        return 0;
    }

    private void OnAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        int nbSamples = e.SamplesPerBuffer * e.InputBuffers.Length;
        int sampleSize = VBan.BitResolutionSize[(int)_bitFmt];

        if (_samples.Length < nbSamples)
        {
            _samples = new float[nbSamples];
            _converted = new byte[nbSamples * sampleSize];
        }

        e.GetAsInterleavedSamples(_samples);

        var dst = _converted.AsSpan();
        for (int i = 0; i != nbSamples; ++i)
        {
            float sample = Math.Clamp(_samples[i], -1.0f, 1.0f);
            int offset = i * sampleSize;

            switch (_bitFmt)
            {
                case VBanBitResolution.Int8:
                    dst[offset] = (byte)(sbyte)(sample * sbyte.MaxValue);
                    break;
                case VBanBitResolution.Int16:
                    BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(offset), (short)(sample * short.MaxValue));
                    break;
                case VBanBitResolution.Int24:
                    int v24 = (int)(sample * 8388607.0f);
                    dst[offset] = (byte)v24;
                    dst[offset + 1] = (byte)(v24 >> 8);
                    dst[offset + 2] = (byte)(v24 >> 16);
                    break;
                case VBanBitResolution.Int32:
                    BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(offset), (int)(sample * int.MaxValue));
                    break;
                case VBanBitResolution.Float32:
                    BinaryPrimitives.WriteSingleLittleEndian(dst.Slice(offset), sample);
                    break;
                case VBanBitResolution.Float64:
                    BinaryPrimitives.WriteDoubleLittleEndian(dst.Slice(offset), sample);
                    break;
            }
        }

        Ring?.WriteOverwrite(dst.Slice(0, nbSamples * sampleSize));
    }

    public override int Read(Span<byte> data, int size)
    {
        if (_record is null)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Read));
            return Errno.ENODEV;
        }

        return base.Read(data, size);
    }

    public override int Close()
    {
        Ring?.Close();

        try
        {
            if (_record is not null)
            {
                _record.AudioAvailable -= OnAudioAvailable;
                _record.Stop();
                _record.Dispose();
            }
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Warning, "%s: error stopping asio capture: %s", nameof(Close), e.Message);
        }

        _record = null;
        return base.Close();
    }

    private static string? FindDriver(string name)
    {
        string[] drivers;
        try
        {
            drivers = AsioOut.GetDriverNames();
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Error, "%s: unable to enumerate asio drivers: %s", nameof(FindDriver), e.Message);
            return null;
        }

        if (drivers.Length == 0)
        {
            return null;
        }

        if (string.IsNullOrEmpty(name))
        {
            return drivers[0];
        }

        return Array.Find(drivers, d => d.Contains(name, StringComparison.OrdinalIgnoreCase));
    }
}

#endif
