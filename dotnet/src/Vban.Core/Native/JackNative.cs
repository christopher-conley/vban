/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 *
 *  P/Invoke bindings for the subset of libjack used by the JACK backend.
 */

using System.Runtime.InteropServices;

namespace Vban.Native;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int JackProcessCallback(uint nframes, IntPtr arg);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void JackShutdownCallback(IntPtr arg);

internal static class JackNative
{
    private const string Lib = "libjack.so.0";

    public const uint PortIsInput = 0x1;
    public const uint PortIsOutput = 0x2;
    public const uint PortIsPhysical = 0x4;

    public const string DefaultAudioType = "32 bit float mono audio";

    // client management (jack_client_open is variadic in C; the only call site
    // passes no extra arguments, so a fixed 3-argument cdecl declaration works)
    [DllImport(Lib)] public static extern IntPtr jack_client_open(string clientName, int options, IntPtr status);
    [DllImport(Lib)] public static extern int jack_client_close(IntPtr client);
    [DllImport(Lib)] public static extern int jack_activate(IntPtr client);
    [DllImport(Lib)] public static extern int jack_deactivate(IntPtr client);
    [DllImport(Lib)] public static extern uint jack_get_buffer_size(IntPtr client);

    [DllImport(Lib)] public static extern int jack_set_process_callback(IntPtr client, JackProcessCallback cb, IntPtr arg);
    [DllImport(Lib)] public static extern void jack_on_shutdown(IntPtr client, JackShutdownCallback cb, IntPtr arg);

    // ports
    [DllImport(Lib)] public static extern IntPtr jack_port_register(IntPtr client, string portName, string portType, nuint flags, nuint bufferSize);
    [DllImport(Lib)] public static extern IntPtr jack_port_get_buffer(IntPtr port, uint nframes);
    [DllImport(Lib)] public static extern IntPtr jack_port_name(IntPtr port);
    [DllImport(Lib)] public static extern IntPtr jack_get_ports(IntPtr client, string? portNamePattern, string? typeNamePattern, nuint flags);
    [DllImport(Lib)] public static extern int jack_connect(IntPtr client, string source, string destination);
    [DllImport(Lib)] public static extern void jack_free(IntPtr ptr);

    // lock-free ring buffer
    [DllImport(Lib)] public static extern IntPtr jack_ringbuffer_create(nuint size);
    [DllImport(Lib)] public static extern void jack_ringbuffer_free(IntPtr rb);
    [DllImport(Lib)] public static extern nuint jack_ringbuffer_write(IntPtr rb, IntPtr src, nuint cnt);
    [DllImport(Lib)] public static extern nuint jack_ringbuffer_read(IntPtr rb, IntPtr dest, nuint cnt);
    [DllImport(Lib)] public static extern nuint jack_ringbuffer_write_space(IntPtr rb);
    [DllImport(Lib)] public static extern nuint jack_ringbuffer_read_space(IntPtr rb);
}
