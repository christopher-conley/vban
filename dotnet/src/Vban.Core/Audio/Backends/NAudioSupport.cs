/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 *
 *  Shared plumbing for the NAudio-based backends (naudio-alsa, pipewire,
 *  wasapi, mme, asio). These have no C counterpart: the original tool talks to
 *  each audio API directly, while NAudio drives playback by pulling from an
 *  IWaveProvider on its own thread and capture by pushing DataAvailable
 *  events. The ring buffer below adapts that to the blocking write/read
 *  contract of AudioBackend.
 */

using NAudio.Wave;

namespace Vban.Audio.Backends;

/// <summary>
/// Byte FIFO connecting the vban main loop to NAudio's audio thread.
/// Playback: WriteBlocking (main loop, backpressure) / ReadAvailable (device
/// pull, zero-fill handled by the caller). Capture: WriteOverwrite (device
/// push, drops oldest on overflow) / ReadBlocking (main loop).
/// </summary>
internal sealed class BlockingRingBuffer
{
    private readonly byte[] _data;
    private readonly object _lock = new();
    private int _start;
    private int _count;
    private bool _closed;

    public BlockingRingBuffer(int capacity)
    {
        _data = new byte[capacity];
    }

    public int WriteBlocking(ReadOnlySpan<byte> src, int size)
    {
        int remaining = size;
        int srcPos = 0;

        lock (_lock)
        {
            while (remaining > 0)
            {
                while (!_closed && _count == _data.Length)
                {
                    Monitor.Wait(_lock);
                }

                if (_closed)
                {
                    return -1;
                }

                int chunk = Math.Min(remaining, _data.Length - _count);
                CopyIn(src.Slice(srcPos, chunk));
                srcPos += chunk;
                remaining -= chunk;
                Monitor.PulseAll(_lock);
            }
        }

        return size;
    }

    public void WriteOverwrite(ReadOnlySpan<byte> src)
    {
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            if (src.Length > _data.Length)
            {
                src = src.Slice(src.Length - _data.Length);
            }

            int overflow = _count + src.Length - _data.Length;
            if (overflow > 0)
            {
                // drop the oldest data
                _start = (_start + overflow) % _data.Length;
                _count -= overflow;
            }

            CopyIn(src);
            Monitor.PulseAll(_lock);
        }
    }

    public int ReadAvailable(Span<byte> dst)
    {
        lock (_lock)
        {
            int n = Math.Min(dst.Length, _count);
            CopyOut(dst.Slice(0, n));
            Monitor.PulseAll(_lock);
            return n;
        }
    }

    public int ReadBlocking(Span<byte> dst, int count)
    {
        lock (_lock)
        {
            while (!_closed && _count < count)
            {
                Monitor.Wait(_lock);
            }

            if (_count == 0)
            {
                return _closed ? -1 : 0;
            }

            int n = Math.Min(count, _count);
            CopyOut(dst.Slice(0, n));
            Monitor.PulseAll(_lock);
            return n;
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            _closed = true;
            Monitor.PulseAll(_lock);
        }
    }

    // both helpers assume _lock is held and the size was checked to fit
    private void CopyIn(ReadOnlySpan<byte> src)
    {
        int end = (_start + _count) % _data.Length;
        int first = Math.Min(src.Length, _data.Length - end);
        src.Slice(0, first).CopyTo(_data.AsSpan(end));
        src.Slice(first).CopyTo(_data);
        _count += src.Length;
    }

    private void CopyOut(Span<byte> dst)
    {
        int first = Math.Min(dst.Length, _data.Length - _start);
        _data.AsSpan(_start, first).CopyTo(dst);
        _data.AsSpan(0, dst.Length - first).CopyTo(dst.Slice(first));
        _start = (_start + dst.Length) % _data.Length;
        _count -= dst.Length;
    }
}

/// <summary>
/// IWaveProvider the NAudio players pull from. Underruns are padded with
/// silence so the device keeps running while the network is late. VBAN 8 bit
/// samples are signed but WAV-convention 8 bit PCM is unsigned, hence the
/// optional 0x80 conversion (silence for unsigned 8 bit is 0x80 as well).
/// </summary>
internal sealed class RingWaveProvider : IWaveProvider
{
    private readonly BlockingRingBuffer _ring;
    private readonly bool _toUnsigned8;

    public RingWaveProvider(BlockingRingBuffer ring, WaveFormat format, bool toUnsigned8)
    {
        _ring = ring;
        WaveFormat = format;
        _toUnsigned8 = toUnsigned8;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<byte> buffer)
    {
        int n = _ring.ReadAvailable(buffer);

        if (_toUnsigned8)
        {
            for (int i = 0; i != n; ++i)
            {
                buffer[i] ^= 0x80;
            }

            buffer.Slice(n).Fill(0x80);
        }
        else
        {
            buffer.Slice(n).Clear();
        }

        return buffer.Length;
    }
}

/// <summary>
/// Base class for backends built on NAudio: turns an IWavePlayer or IWaveIn
/// plus a ring buffer into the blocking AudioBackend write/read contract.
/// Concrete backends implement Open (device lookup and player creation) and
/// call OpenPlayback / OpenCapture.
/// </summary>
public abstract class NAudioBackendBase : AudioBackend
{
    private IWavePlayer? _player;
    private IWaveIn? _capture;

    private protected BlockingRingBuffer? Ring { get; set; }

    /// <summary>True when the stream is signed 8 bit and the device speaks
    /// unsigned (WAV convention); capture data then needs the 0x80 flip.</summary>
    protected bool Signed8 { get; set; }

    protected static int GetWaveFormat(in StreamConfig config, out WaveFormat format, out int frameSize)
    {
        int rate = (int)config.SampleRate;
        int channels = (int)config.NbChannels;
        frameSize = VBan.BitResolutionSize[(int)config.BitFmt] * channels;

        switch (config.BitFmt)
        {
            case VBanBitResolution.Int8:
            case VBanBitResolution.Int16:
            case VBanBitResolution.Int24:
            case VBanBitResolution.Int32:
                format = new WaveFormat(rate, VBan.BitResolutionSize[(int)config.BitFmt] * 8, channels);
                return 0;
            case VBanBitResolution.Float32:
                format = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
                return 0;
            case VBanBitResolution.Float64:
                format = WaveFormat.CreateCustomFormat(WaveFormatEncoding.IeeeFloat, rate, channels,
                    rate * channels * 8, channels * 8, 64);
                return 0;
            default:
                Logger.Log(LogLevel.Error, "%s: unsupported bit format %s",
                    nameof(GetWaveFormat), StreamFormat.PrintBitFmt(config.BitFmt));
                format = null!;
                return Errno.EINVAL;
        }
    }

    protected static int LatencyMs(int bufferSize, in StreamConfig config)
    {
        long ms = config.SampleRate != 0 ? bufferSize * 1000L / config.SampleRate : 0;
        return (int)Math.Clamp(ms, 10, 500);
    }

    protected static int RingCapacity(int bufferSize, int frameSize)
    {
        return Math.Max(bufferSize * frameSize * 4, 64 * 1024);
    }

    protected int OpenPlayback(IWavePlayer player, WaveFormat format, int bufferSize, int frameSize)
    {
        Ring = new BlockingRingBuffer(RingCapacity(bufferSize, frameSize));
        Signed8 = format is { Encoding: WaveFormatEncoding.Pcm, BitsPerSample: 8 };

        try
        {
            player.PlaybackStopped += (_, e) =>
            {
                if (e.Exception is not null)
                {
                    Logger.Log(LogLevel.Error, "%s: playback stopped: %s", GetType().Name, e.Exception.Message);
                }
            };
            player.Init(new RingWaveProvider(Ring, format, Signed8));
            player.Play();
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Fatal, "%s: unable to start playback: %s", nameof(OpenPlayback), e.Message);
            player.Dispose();
            Ring = null;
            return Errno.ENODEV;
        }

        _player = player;
        return 0;
    }

    protected int OpenCapture(IWaveIn capture, WaveFormat format, int bufferSize, int frameSize)
    {
        Ring = new BlockingRingBuffer(RingCapacity(bufferSize, frameSize));
        Signed8 = format is { Encoding: WaveFormatEncoding.Pcm, BitsPerSample: 8 };

        try
        {
            // some capture devices (wasapi recorder) are already configured at
            // construction time; only set the format when it differs
            if (!format.Equals(capture.WaveFormat))
            {
                capture.WaveFormat = format;
            }

            capture.DataAvailable += OnDataAvailable;
            capture.StartRecording();
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Fatal, "%s: unable to start capture: %s", nameof(OpenCapture), e.Message);
            capture.Dispose();
            Ring = null;
            return Errno.ENODEV;
        }

        _capture = capture;
        return 0;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        HandleCaptureData(e.Buffer, e.BytesRecorded);
    }

    /// <summary>Feeds captured device data into the ring buffer, converting
    /// unsigned 8 bit back to signed when needed. For backends whose capture
    /// object does not implement IWaveIn (wasapi recorder, asio).</summary>
    protected void HandleCaptureData(byte[] buffer, int bytes)
    {
        if (Signed8)
        {
            for (int i = 0; i != bytes; ++i)
            {
                buffer[i] ^= 0x80;
            }
        }

        Ring?.WriteOverwrite(buffer.AsSpan(0, bytes));
    }

    public override int Write(ReadOnlySpan<byte> data, int size)
    {
        if (_player is null || Ring is null)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Write));
            return Errno.ENODEV;
        }

        int ret = Ring.WriteBlocking(data, size);
        return ret < 0 ? Errno.ENODEV : ret;
    }

    public override int Read(Span<byte> data, int size)
    {
        if (Ring is null)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Read));
            return Errno.ENODEV;
        }

        int ret = Ring.ReadBlocking(data, size);
        return ret < 0 ? Errno.ENODEV : ret;
    }

    public override int Close()
    {
        Ring?.Close();

        try
        {
            _player?.Stop();
            _player?.Dispose();
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Warning, "%s: error stopping playback: %s", nameof(Close), e.Message);
        }

        try
        {
            if (_capture is not null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.StopRecording();
                _capture.Dispose();
            }
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Warning, "%s: error stopping capture: %s", nameof(Close), e.Message);
        }

        _player = null;
        _capture = null;
        Ring = null;
        return 0;
    }
}
