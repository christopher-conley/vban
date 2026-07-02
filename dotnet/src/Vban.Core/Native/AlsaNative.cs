/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 *
 *  P/Invoke bindings for the subset of libasound used by the ALSA backend.
 */

using System.Runtime.InteropServices;

namespace Vban.Native;

internal static class AlsaNative
{
    private const string Lib = "libasound.so.2";

    // snd_pcm_stream_t
    public const int StreamPlayback = 0;
    public const int StreamCapture = 1;

    // snd_pcm_access_t
    public const int AccessRwInterleaved = 3;

    // snd_pcm_format_t (little-endian values; vban targets LE hosts)
    public const int FormatUnknown = -1;
    public const int FormatS8 = 0;
    public const int FormatS16Le = 2;
    public const int FormatS24Le = 6;
    public const int FormatS32Le = 10;
    public const int FormatFloatLe = 14;
    public const int FormatFloat64Le = 16;

    [DllImport(Lib)] public static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);
    [DllImport(Lib)] public static extern int snd_pcm_hw_params_malloc(out IntPtr ptr);
    [DllImport(Lib)] public static extern void snd_pcm_hw_params_free(IntPtr ptr);
    [DllImport(Lib)] public static extern int snd_pcm_hw_params_any(IntPtr pcm, IntPtr ptr);
    [DllImport(Lib)] public static extern int snd_pcm_hw_params_set_access(IntPtr pcm, IntPtr ptr, int access);
    [DllImport(Lib)] public static extern int snd_pcm_hw_params_set_format(IntPtr pcm, IntPtr ptr, int format);
    [DllImport(Lib)] public static extern int snd_pcm_hw_params_set_rate(IntPtr pcm, IntPtr ptr, uint val, int dir);
    [DllImport(Lib)] public static extern int snd_pcm_hw_params_set_channels(IntPtr pcm, IntPtr ptr, uint val);
    [DllImport(Lib)] public static extern int snd_pcm_hw_params(IntPtr pcm, IntPtr ptr);
    [DllImport(Lib)] public static extern int snd_pcm_prepare(IntPtr pcm);
    [DllImport(Lib)] public static extern int snd_pcm_close(IntPtr pcm);
    [DllImport(Lib)] public static extern nint snd_pcm_writei(IntPtr pcm, IntPtr buffer, nuint size);
    [DllImport(Lib)] public static extern nint snd_pcm_readi(IntPtr pcm, IntPtr buffer, nuint size);
    [DllImport(Lib)] public static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);
    [DllImport(Lib)] public static extern IntPtr snd_strerror(int errnum);

    public static string StrError(int errnum) => Marshal.PtrToStringAnsi(snd_strerror(errnum)) ?? "unknown";
}
