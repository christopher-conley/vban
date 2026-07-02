/*
 *  This file is part of vban.
 *
 *  C# port of src/common/backend/pulseaudio_backend.c (P/Invoke into libpulse-simple).
 *  Licensed under the GNU General Public License v3 (or later).
 */

using Vban.Native;

namespace Vban.Audio.Backends;

/// <summary>PulseAudio backend using the pa_simple API, like the C version.</summary>
public sealed unsafe class PulseAudioBackend : AudioBackend
{
    public const string Name = "pulseaudio";

    private IntPtr _handle;

    private static int VbanToPulseFormat(VBanBitResolution bitResolution) => bitResolution switch
    {
        VBanBitResolution.Int8 => PulseAudioNative.SampleU8,
        VBanBitResolution.Int16 => PulseAudioNative.SampleS16Le,
        VBanBitResolution.Int24 => PulseAudioNative.SampleS24Le,
        VBanBitResolution.Int32 => PulseAudioNative.SampleS32Le,
        VBanBitResolution.Float32 => PulseAudioNative.SampleFloat32Le,
        _ => PulseAudioNative.SampleInvalid,
    };

    public override int Open(string deviceName, AudioDirection direction, int bufferSize, in StreamConfig config)
    {
        var ss = new PaSampleSpec
        {
            Format = VbanToPulseFormat(config.BitFmt),
            Rate = config.SampleRate,
            Channels = (byte)config.NbChannels,
        };

        var ba = new PaBufferAttr
        {
            MaxLength = unchecked((uint)-1),
            TLength = (uint)bufferSize,
            PreBuf = (uint)(bufferSize / 2),
            MinReq = unchecked((uint)-1),
            FragSize = unchecked((uint)-1),
        };

        bool playback = direction == AudioDirection.Out;
        string? dev = string.IsNullOrEmpty(deviceName) ? null : deviceName;
        int error;

        PaSampleSpec* pss = &ss;
        PaBufferAttr* pba = &ba;
        IntPtr attrPtr = playback ? (IntPtr)pba : IntPtr.Zero;
        _handle = PulseAudioNative.pa_simple_new(
            IntPtr.Zero, "vban",
            playback ? PulseAudioNative.StreamPlayback : PulseAudioNative.StreamRecord,
            dev, playback ? "playback" : "record",
            (IntPtr)pss, IntPtr.Zero, attrPtr, out error);

        if (_handle == IntPtr.Zero)
        {
            Logger.Log(LogLevel.Fatal, "pulseaudio_open: open error: %s", PulseAudioNative.StrError(error));
            return error;
        }

        return 0;
    }

    public override int Close()
    {
        if (_handle == IntPtr.Zero)
        {
            return 0;
        }

        PulseAudioNative.pa_simple_free(_handle);
        _handle = IntPtr.Zero;
        return 0;
    }

    public override int Write(ReadOnlySpan<byte> data, int size)
    {
        if (_handle == IntPtr.Zero)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Write));
            return Errno.ENODEV;
        }

        int ret;
        fixed (byte* p = data)
        {
            ret = PulseAudioNative.pa_simple_write(_handle, (IntPtr)p, (nuint)size, out int error);
            if (ret < 0)
            {
                Logger.Log(LogLevel.Error, "%s: pa_simple_write failed: %s", nameof(Write), PulseAudioNative.StrError(error));
            }
        }

        return ret < 0 ? ret : size;
    }

    public override int Read(Span<byte> data, int size)
    {
        if (_handle == IntPtr.Zero)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Read));
            return Errno.ENODEV;
        }

        int ret;
        fixed (byte* p = data)
        {
            ret = PulseAudioNative.pa_simple_read(_handle, (IntPtr)p, (nuint)size, out int error);
            if (ret < 0)
            {
                Logger.Log(LogLevel.Error, "%s: pa_simple_read failed: %s", nameof(Read), PulseAudioNative.StrError(error));
            }
        }

        return ret < 0 ? ret : size;
    }
}
