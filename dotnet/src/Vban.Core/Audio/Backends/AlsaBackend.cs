/*
 *  This file is part of vban.
 *
 *  C# port of src/common/backend/alsa_backend.c (P/Invoke into libasound).
 *  Licensed under the GNU General Public License v3 (or later).
 */

using Vban.Native;

namespace Vban.Audio.Backends;

/// <summary>ALSA playback/capture backend, binding the same libasound API as the C version.</summary>
public sealed unsafe class AlsaBackend : AudioBackend
{
    public const string Name = "alsa";
    private const string DefaultDeviceName = "default";

    private IntPtr _handle;
    private int _frameSize;

    private static int VbanToAlsaFormat(VBanBitResolution bitResolution) => bitResolution switch
    {
        VBanBitResolution.Int8 => AlsaNative.FormatS8,
        VBanBitResolution.Int16 => AlsaNative.FormatS16Le,
        VBanBitResolution.Int24 => AlsaNative.FormatS24Le,
        VBanBitResolution.Int32 => AlsaNative.FormatS32Le,
        VBanBitResolution.Float32 => AlsaNative.FormatFloatLe,
        VBanBitResolution.Float64 => AlsaNative.FormatFloat64Le,
        _ => AlsaNative.FormatUnknown,
    };

    public override int Open(string outputName, AudioDirection direction, int bufferSize, in StreamConfig config)
    {
        _frameSize = VBan.BitResolutionSize[(int)config.BitFmt] * (int)config.NbChannels;

        int ret = AlsaNative.snd_pcm_open(
            out _handle,
            string.IsNullOrEmpty(outputName) ? DefaultDeviceName : outputName,
            direction == AudioDirection.Out ? AlsaNative.StreamPlayback : AlsaNative.StreamCapture,
            0);
        if (ret < 0)
        {
            Logger.Log(LogLevel.Fatal, "%s: open error: %s", nameof(Open), AlsaNative.StrError(ret));
            _handle = IntPtr.Zero;
            return ret;
        }

        Logger.Log(LogLevel.Debug, "%s: snd_pcm_open", nameof(Open));

        if ((ret = AlsaNative.snd_pcm_hw_params_malloc(out IntPtr hwParams)) < 0)
        {
            Logger.Log(LogLevel.Fatal, "cannot allocate hardware parameter structure (%s)", AlsaNative.StrError(ret));
            Close();
            return ret;
        }

        if ((ret = AlsaNative.snd_pcm_hw_params_any(_handle, hwParams)) < 0)
        {
            Logger.Log(LogLevel.Fatal, "cannot initialize hardware parameter structure (%s)", AlsaNative.StrError(ret));
            Close();
            return ret;
        }

        if ((ret = AlsaNative.snd_pcm_hw_params_set_access(_handle, hwParams, AlsaNative.AccessRwInterleaved)) < 0)
        {
            Logger.Log(LogLevel.Fatal, "cannot set access type (%s)", AlsaNative.StrError(ret));
            Close();
            return ret;
        }

        if ((ret = AlsaNative.snd_pcm_hw_params_set_format(_handle, hwParams, VbanToAlsaFormat(config.BitFmt))) < 0)
        {
            Logger.Log(LogLevel.Fatal, "cannot set sample format (%s)", AlsaNative.StrError(ret));
            Close();
            return ret;
        }

        if ((ret = AlsaNative.snd_pcm_hw_params_set_rate(_handle, hwParams, config.SampleRate, 0)) < 0)
        {
            Logger.Log(LogLevel.Fatal, "cannot set sample rate (%s)", AlsaNative.StrError(ret));
            Close();
            return ret;
        }

        if ((ret = AlsaNative.snd_pcm_hw_params_set_channels(_handle, hwParams, config.NbChannels)) < 0)
        {
            Logger.Log(LogLevel.Fatal, "cannot set channel count (%s)", AlsaNative.StrError(ret));
            Close();
            return ret;
        }

        if ((ret = AlsaNative.snd_pcm_hw_params(_handle, hwParams)) < 0)
        {
            Logger.Log(LogLevel.Fatal, "cannot set parameters (%s)", AlsaNative.StrError(ret));
            Close();
            return ret;
        }

        AlsaNative.snd_pcm_hw_params_free(hwParams);

        ret = AlsaNative.snd_pcm_prepare(_handle);
        if (ret < 0)
        {
            Logger.Log(LogLevel.Error, "%s: prepare error: %s", nameof(Open), AlsaNative.StrError(ret));
            Close();
            return ret;
        }

        return 0;
    }

    public override int Close()
    {
        if (_handle == IntPtr.Zero)
        {
            return 0;
        }

        int ret = AlsaNative.snd_pcm_close(_handle);
        _handle = IntPtr.Zero;
        return ret;
    }

    public override int Write(ReadOnlySpan<byte> data, int size)
    {
        if (_handle == IntPtr.Zero)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Write));
            return Errno.ENODEV;
        }

        nint nbFrame = size / _frameSize;
        nint ret;
        fixed (byte* p = data)
        {
            ret = AlsaNative.snd_pcm_writei(_handle, (IntPtr)p, (nuint)nbFrame);
        }

        if (ret < 0)
        {
            Logger.Log(LogLevel.Error, "%s: snd_pcm_writei failed: %s", nameof(Write), AlsaNative.StrError((int)ret));
            ret = AlsaNative.snd_pcm_recover(_handle, (int)ret, 0);
            if (ret < 0)
            {
                Logger.Log(LogLevel.Error, "%s: snd_pcm_recover failed: %s", nameof(Write), AlsaNative.StrError((int)ret));
            }
        }
        else if (ret > 0 && ret < nbFrame)
        {
            Logger.Log(LogLevel.Error, "%s: short write (expected %lu, wrote %i)", nameof(Write), nbFrame, ret);
        }

        return (int)(ret * _frameSize);
    }

    public override int Read(Span<byte> data, int size)
    {
        if (_handle == IntPtr.Zero)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Read));
            return Errno.ENODEV;
        }

        nint nbFrame = size / _frameSize;
        nint ret;
        fixed (byte* p = data)
        {
            ret = AlsaNative.snd_pcm_readi(_handle, (IntPtr)p, (nuint)nbFrame);
        }

        if (ret < 0)
        {
            Logger.Log(LogLevel.Error, "%s: snd_pcm_readi failed: %s", nameof(Read), AlsaNative.StrError((int)ret));
            ret = AlsaNative.snd_pcm_recover(_handle, (int)ret, 0);
            if (ret < 0)
            {
                Logger.Log(LogLevel.Error, "%s: snd_pcm_recover failed: %s", nameof(Read), AlsaNative.StrError((int)ret));
            }
        }
        else if (ret > 0 && ret < nbFrame)
        {
            Logger.Log(LogLevel.Error, "%s: short read (expected %lu, wrote %i)", nameof(Read), nbFrame, ret);
        }

        return (int)(ret * _frameSize);
    }
}
