/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 *
 *  P/Invoke bindings for the libpulse-simple API used by the PulseAudio backend.
 */

using System.Runtime.InteropServices;

namespace Vban.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct PaSampleSpec
{
    public int Format;     // pa_sample_format_t
    public uint Rate;
    public byte Channels;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PaBufferAttr
{
    public uint MaxLength;
    public uint TLength;
    public uint PreBuf;
    public uint MinReq;
    public uint FragSize;
}

internal static class PulseAudioNative
{
    private const string Lib = "libpulse-simple.so.0";
    private const string LibPulse = "libpulse.so.0";

    // pa_stream_direction_t
    public const int StreamPlayback = 1;
    public const int StreamRecord = 2;

    // pa_sample_format_t
    public const int SampleU8 = 0;
    public const int SampleS16Le = 3;
    public const int SampleFloat32Le = 5;
    public const int SampleS32Le = 7;
    public const int SampleS24Le = 9;
    public const int SampleInvalid = -1;

    [DllImport(Lib)]
    public static extern IntPtr pa_simple_new(
        IntPtr server, string name, int dir, string? dev, string streamName,
        IntPtr ss, IntPtr map, IntPtr attr, out int error);

    [DllImport(Lib)]
    public static extern void pa_simple_free(IntPtr s);

    [DllImport(Lib)]
    public static extern int pa_simple_write(IntPtr s, IntPtr data, nuint bytes, out int error);

    [DllImport(Lib)]
    public static extern int pa_simple_read(IntPtr s, IntPtr data, nuint bytes, out int error);

    [DllImport(LibPulse)]
    public static extern IntPtr pa_strerror(int error);

    public static string StrError(int error) => Marshal.PtrToStringAnsi(pa_strerror(error)) ?? "unknown";
}
