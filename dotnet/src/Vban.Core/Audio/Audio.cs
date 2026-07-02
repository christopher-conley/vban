/*
 *  This file is part of vban.
 *  Copyright (c) 2015 by Benoît Quiniou <quiniouben@yahoo.fr>
 *
 *  C# port of src/common/audio.{c,h}
 *  Licensed under the GNU General Public License v3 (or later).
 */

using Vban.Audio.Backends;

namespace Vban.Audio;

/// <summary>
/// Audio object tying together a backend, the stream configuration and an
/// optional channel map. Equivalent to struct audio_t and the audio_* functions.
/// </summary>
public sealed class Audio
{
    private AudioConfig _config;
    private StreamConfig _stream;
    private AudioMapConfig _map = new();
    private AudioBackend _backend = null!;

    // only used when a channel map is configured
    private readonly byte[] _buffer = new byte[VBan.DataMaxSize];

    private Audio()
    {
    }

    public static int Init(out Audio? handle, in AudioConfig config)
    {
        handle = new Audio { _config = config };

        Logger.Log(LogLevel.Info, "%s: config is direction %s, backend %s, device %s, buffer size %d",
            nameof(Init), config.Direction == AudioDirection.In ? "in" : "out",
            config.BackendName, config.DeviceName, config.BufferSize);

        int ret = AudioBackendRegistry.GetByName(config.BackendName, out var backend);
        if (ret != 0 || backend is null)
        {
            Logger.Log(LogLevel.Fatal, "%s: %s backend not available", nameof(Init), config.BackendName);
            handle = null;
            return Errno.ENODEV;
        }

        handle._backend = backend;
        return ret;
    }

    public int Release()
    {
        return _backend.Close();
    }

    private void GetDeviceConfig(out StreamConfig deviceConfig)
    {
        deviceConfig = _stream;
        if (_config.Direction == AudioDirection.Out && _map.NbChannels != 0)
        {
            deviceConfig.NbChannels = (uint)_map.NbChannels;
        }
    }

    public int SetStreamConfig(in StreamConfig config)
    {
        if (_stream.NbChannels == config.NbChannels
            && _stream.SampleRate == config.SampleRate
            && _stream.BitFmt == config.BitFmt)
        {
            // nothing to do
            return 0;
        }

        Logger.Log(LogLevel.Info, "%s: new stream config is nb channels %d, sample rate %d, bit_fmt %s",
            nameof(SetStreamConfig), config.NbChannels, config.SampleRate, StreamFormat.PrintBitFmt(config.BitFmt));

        int ret = _backend.Close();
        if (ret < 0)
        {
            Logger.Log(LogLevel.Error, "%s: could not close backend", nameof(SetStreamConfig));
            return ret;
        }

        _stream = config;
        GetDeviceConfig(out var deviceConfig);

        ret = _backend.Open(_config.DeviceName, _config.Direction, _config.BufferSize, deviceConfig);
        if (ret < 0)
        {
            _stream = default;
            Logger.Log(LogLevel.Error, "%s: could not open backend with new config", nameof(SetStreamConfig));
            return ret;
        }

        return ret;
    }

    public int GetStreamConfig(out StreamConfig config)
    {
        config = _stream;
        if (_config.Direction == AudioDirection.In && _map.NbChannels != 0)
        {
            config.NbChannels = (uint)_map.NbChannels;
        }

        return 0;
    }

    public int SetMapConfig(AudioMapConfig config)
    {
        Logger.Log(LogLevel.Info, "%s: new map config is nb channels %d", nameof(SetMapConfig), config.NbChannels);
        _map = config;
        return 0;
    }

    public int Write(byte[] buffer, int offset, int size)
    {
        Logger.Log(LogLevel.Debug, "%s invoked with size %d", nameof(Write), size);

        int ret = MapChannels(buffer, offset, size, reverse: false);
        if (ret < 0)
        {
            Logger.Log(LogLevel.Error, "%s: audio_map_channels failed", nameof(Write));
            return ret;
        }

        bool mapped = _map.NbChannels != 0;
        ReadOnlySpan<byte> outSpan = mapped ? _buffer : buffer.AsSpan(offset);
        int written = _backend.Write(outSpan, OutputSize(size));
        return ReverseInputSize(written);
    }

    public int Read(byte[] buffer, int offset, int size)
    {
        Logger.Log(LogLevel.Debug, "%s invoked with size %d", nameof(Read), size);

        bool mapped = _map.NbChannels != 0;
        Span<byte> inSpan = mapped ? _buffer : buffer.AsSpan(offset);
        int ret = _backend.Read(inSpan, ReverseInputSize(size));
        if (ret < 0)
        {
            Logger.Log(LogLevel.Error, "%s: backend read failed", nameof(Read));
            return ret;
        }

        size = ret;

        ret = MapChannels(buffer, offset, size, reverse: true);
        if (ret < 0)
        {
            Logger.Log(LogLevel.Error, "%s: audio_map_channels failed", nameof(Read));
            return ret;
        }

        return OutputSize(size);
    }

    private int OutputSize(int size)
        => _map.NbChannels != 0 ? (int)(size * _map.NbChannels / _stream.NbChannels) : size;

    private int ReverseInputSize(int size)
        => _map.NbChannels != 0 ? (int)(size * _stream.NbChannels / _map.NbChannels) : size;

    private int MapChannels(byte[] buffer, int offset, int size, bool reverse)
    {
        int sampleSize = VBan.BitResolutionSize[(int)_stream.BitFmt];

        if (_map.NbChannels == 0)
        {
            // nothing to do
            return 0;
        }

        int streamFrameSize = sampleSize * (int)_stream.NbChannels;
        int mapFrameSize = sampleSize * _map.NbChannels;

        if (reverse)
        {
            Array.Clear(buffer, offset, size);
        }
        else
        {
            Array.Clear(_buffer, 0, size);
        }

        if (streamFrameSize == 0)
        {
            return 0;
        }

        int frameCount = size / streamFrameSize;
        byte[] origBase = reverse ? _buffer : buffer;
        int origOffset = reverse ? 0 : offset;
        byte[] destBase = reverse ? buffer : _buffer;
        int destOffset = reverse ? offset : 0;

        for (int chan = 0; chan != _map.NbChannels; ++chan)
        {
            if (_map.Channels[chan] < _stream.NbChannels)
            {
                for (int frame = 0; frame != frameCount; ++frame)
                {
                    int origIdx = origOffset + frame * streamFrameSize + _map.Channels[chan] * sampleSize;
                    int destIdx = destOffset + frame * mapFrameSize + chan * sampleSize;
                    Array.Copy(origBase, origIdx, destBase, destIdx, sampleSize);
                }
            }
        }

        return 0;
    }
}
