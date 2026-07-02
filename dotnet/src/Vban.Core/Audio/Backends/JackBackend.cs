/*
 *  This file is part of vban.
 *
 *  C# port of src/common/backend/jack_backend.c (P/Invoke into libjack).
 *  Licensed under the GNU General Public License v3 (or later).
 *
 *  JACK is output (playback) only here, exactly like the C backend; capture
 *  (read) is not implemented.
 */

using System.Runtime.InteropServices;
using Vban.Native;

namespace Vban.Audio.Backends;

public sealed unsafe class JackBackend : AudioBackend
{
    public const string Name = "jack";
    private const int NbBuffers = 2;

    private IntPtr _client;
    private readonly IntPtr[] _ports = new IntPtr[VBan.ChannelsMaxNb];
    private IntPtr _ringBuffer;
    private VBanBitResolution _bitFmt;
    private int _nbChannels;
    private volatile int _active;

    private byte[] _scratch = Array.Empty<byte>();

    // keep delegates alive for the lifetime of the backend so the GC does not
    // collect the thunks JACK calls into
    private JackProcessCallback? _processCb;
    private JackShutdownCallback? _shutdownCb;

    public override int Open(string outputName, AudioDirection direction, int bufferSize, in StreamConfig config)
    {
        Logger.Log(LogLevel.Debug, "%s", nameof(Open));

        if (_client == IntPtr.Zero)
        {
            _client = JackNative.jack_client_open(string.IsNullOrEmpty(outputName) ? "vban" : outputName, 0, IntPtr.Zero);
            if (_client == IntPtr.Zero)
            {
                Logger.Log(LogLevel.Error, "%s: could not open jack client", nameof(Open));
                return Errno.ENODEV;
            }
        }

        int sampleSize = VBan.BitResolutionSize[(int)config.BitFmt];
        long jackBufferSize = (long)JackNative.jack_get_buffer_size(_client) * config.NbChannels * sampleSize;
        long ringSize = Math.Max(bufferSize, jackBufferSize) * NbBuffers;
        _nbChannels = (int)config.NbChannels;
        _bitFmt = config.BitFmt;

        _ringBuffer = JackNative.jack_ringbuffer_create((nuint)ringSize);
        _scratch = new byte[ringSize];

        // prime the ring buffer with one buffer of silence
        long primeSize = ringSize / NbBuffers;
        var zeros = new byte[primeSize];
        fixed (byte* z = zeros)
        {
            JackNative.jack_ringbuffer_write(_ringBuffer, (IntPtr)z, (nuint)primeSize);
        }

        _processCb = ProcessCallback;
        _shutdownCb = ShutdownCallback;

        int ret = JackNative.jack_set_process_callback(_client, _processCb, IntPtr.Zero);
        if (ret != 0)
        {
            Logger.Log(LogLevel.Error, "%s: impossible to set jack process callback", nameof(Open));
            Close();
            return ret;
        }

        JackNative.jack_on_shutdown(_client, _shutdownCb, IntPtr.Zero);

        return Start();
    }

    private int Start()
    {
        int ret = JackNative.jack_activate(_client);
        if (ret != 0)
        {
            Logger.Log(LogLevel.Error, "%s: can't activate client", nameof(Start));
            return ret;
        }

        Logger.Log(LogLevel.Debug, "%s: jack activated", nameof(Start));

        for (int port = 0; port != _nbChannels; ++port)
        {
            _ports[port] = JackNative.jack_port_register(
                _client, $"playback_{port + 1}", JackNative.DefaultAudioType, JackNative.PortIsOutput, 0);
            if (_ports[port] == IntPtr.Zero)
            {
                Logger.Log(LogLevel.Error, "%s: impossible to set jack port for channel %d", nameof(Start), port);
                Close();
                return Errno.ENODEV;
            }
        }

        // autoconnect to the physical inputs (XXX should really be an option)
        IntPtr ports = JackNative.jack_get_ports(_client, null, null, JackNative.PortIsPhysical | JackNative.PortIsInput);
        if (ports != IntPtr.Zero)
        {
            int portId = 0;
            while (portId != _nbChannels)
            {
                IntPtr namePtr = Marshal.ReadIntPtr(ports, portId * IntPtr.Size);
                if (namePtr == IntPtr.Zero)
                {
                    break;
                }

                string? dst = Marshal.PtrToStringAnsi(namePtr);
                string? src = Marshal.PtrToStringAnsi(JackNative.jack_port_name(_ports[portId]));
                ret = JackNative.jack_connect(_client, src!, dst!);
                if (ret != 0)
                {
                    Logger.Log(LogLevel.Warning, "%s: could not autoconnect channel %d", nameof(Start), portId);
                }
                else
                {
                    Logger.Log(LogLevel.Debug, "%s: channel %d autoconnected", nameof(Start), portId);
                }

                ++portId;
            }

            JackNative.jack_free(ports);
        }
        else
        {
            Logger.Log(LogLevel.Warning, "%s: could not autoconnect channels", nameof(Start));
        }

        return ret;
    }

    public override int Close()
    {
        if (_client == IntPtr.Zero)
        {
            return 0;
        }

        _active = 0;

        int ret = JackNative.jack_deactivate(_client);
        if (ret != 0)
        {
            Logger.Log(LogLevel.Fatal, "%s: jack_deactivate failed with error %d", nameof(Close), ret);
        }

        ret = JackNative.jack_client_close(_client);
        if (ret != 0)
        {
            Logger.Log(LogLevel.Fatal, "%s: jack_client_close failed with error %d", nameof(Close), ret);
        }

        if (_ringBuffer != IntPtr.Zero)
        {
            JackNative.jack_ringbuffer_free(_ringBuffer);
            _ringBuffer = IntPtr.Zero;
        }

        _client = IntPtr.Zero;
        Array.Clear(_ports);

        return ret;
    }

    public override int Write(ReadOnlySpan<byte> data, int size)
    {
        Logger.Log(LogLevel.Debug, "%s", nameof(Write));

        if (_client == IntPtr.Zero)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Write));
            return Errno.ENODEV;
        }

        if (_active == 0)
        {
            Logger.Log(LogLevel.Debug, "%s: server not active yet", nameof(Write));
            return size;
        }

        if ((long)JackNative.jack_ringbuffer_write_space(_ringBuffer) < size)
        {
            Logger.Log(LogLevel.Warning, "%s: short write", nameof(Write));
            return 0;
        }

        fixed (byte* p = data)
        {
            JackNative.jack_ringbuffer_write(_ringBuffer, (IntPtr)p, (nuint)size);
        }

        return size;
    }

    public override int Read(Span<byte> data, int size)
    {
        // JACK capture is not supported, matching the C backend (no read callback).
        Logger.Log(LogLevel.Error, "%s: jack capture is not supported", nameof(Read));
        return Errno.ENODEV;
    }

    private int ProcessCallback(uint nframes, IntPtr arg)
    {
        Logger.Log(LogLevel.Debug, "%s", nameof(ProcessCallback));

        _active = 1;
        int sampleSize = VBan.BitResolutionSize[(int)_bitFmt];
        long need = (long)nframes * _nbChannels * sampleSize;

        if ((long)JackNative.jack_ringbuffer_read_space(_ringBuffer) < need)
        {
            Logger.Log(LogLevel.Warning, "%s: short read", nameof(ProcessCallback));
            return 0;
        }

        // gather the destination port buffers
        Span<IntPtr> buffers = stackalloc IntPtr[_nbChannels];
        for (int channel = 0; channel != _nbChannels; ++channel)
        {
            buffers[channel] = JackNative.jack_port_get_buffer(_ports[channel], nframes);
        }

        fixed (byte* tmp = _scratch)
        {
            JackNative.jack_ringbuffer_read(_ringBuffer, (IntPtr)tmp, (nuint)need);

            int offset = 0;
            for (uint sample = 0; sample != nframes; ++sample)
            {
                for (int channel = 0; channel != _nbChannels; ++channel)
                {
                    float value = ConvertSample(tmp + offset, _bitFmt);
                    ((float*)buffers[channel])[sample] = value;
                    offset += sampleSize;
                }
            }
        }

        return 0;
    }

    private void ShutdownCallback(IntPtr arg)
    {
        Close();
        // XXX how to notify the upper layer that we are done ?
    }

    private static float ConvertSample(byte* ptr, VBanBitResolution bitFmt)
    {
        switch (bitFmt)
        {
            case VBanBitResolution.Int8:
                return (float)(*(sbyte*)ptr) / (1 << 7);

            case VBanBitResolution.Int16:
                return (float)(*(short*)ptr) / (1 << 15);

            case VBanBitResolution.Int24:
                int value = ((sbyte)ptr[2] << 16) | (ptr[1] << 8) | ptr[0];
                return (float)value / (1 << 23);

            case VBanBitResolution.Int32:
                return *(int*)ptr / 2147483648.0f;

            case VBanBitResolution.Float32:
                return *(float*)ptr;

            default:
                return 0.0f;
        }
    }
}
