/*
 *  This file is part of vban.
 *
 *  C# port of src/common/backend/pipe_backend.c
 *  Licensed under the GNU General Public License v3 (or later).
 */

using System.IO.Pipes;
using System.Runtime.InteropServices;
using Vban.Native;

namespace Vban.Audio.Backends;

/// <summary>
/// Chains tools through a FIFO (named pipe). On Unix a FIFO is created with
/// mkfifo and opened as a stream; on Windows a named pipe is used instead.
/// </summary>
public sealed class PipeBackend : AudioBackend
{
    public const string Name = "pipe";

    private static readonly string FifoFilename =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\.\pipe\vban_0" : "/tmp/vban_0";

    private const string WindowsPipeName = "vban_0";

    private System.IO.Stream? _stream;
    private string _path = FifoFilename;

    public override int Open(string outputName, AudioDirection direction, int bufferSize, in StreamConfig config)
    {
        _path = string.IsNullOrEmpty(outputName) ? FifoFilename : outputName;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var server = new NamedPipeServerStream(
                    WindowsPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message);
                server.WaitForConnection();
                _stream = server;
            }
            else
            {
                int ret = Libc.mkfifo(_path, Convert.ToUInt32("666", 8));
                if (ret < 0)
                {
                    int err = Marshal.GetLastPInvokeError();
                    Logger.Log(LogLevel.Fatal, "%s: mkfifo error %d", nameof(Open), err);
                    return Errno.ENXIO;
                }

                _stream = new FileStream(
                    _path,
                    FileMode.Open,
                    direction == AudioDirection.Out ? FileAccess.Write : FileAccess.Read);
            }
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Fatal, "%s: open error: %s", nameof(Open), e.Message);
            return Errno.ENXIO;
        }

        return 0;
    }

    public override int Close()
    {
        _stream?.Dispose();
        _stream = null;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Libc.unlink(_path);
        }

        return 0;
    }

    public override int Write(ReadOnlySpan<byte> data, int size)
    {
        if (_stream is null)
        {
            Logger.Log(LogLevel.Error, "%s: handle or data pointer is null", nameof(Write));
            return Errno.EINVAL;
        }

        try
        {
            _stream.Write(data.Slice(0, size));
            return size;
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Error, "%s: %s", nameof(Write), e.Message);
            return Errno.EPERM;
        }
    }

    public override int Read(Span<byte> data, int size)
    {
        if (_stream is null)
        {
            Logger.Log(LogLevel.Error, "%s: handle or data pointer is null", nameof(Read));
            return Errno.EINVAL;
        }

        try
        {
            return _stream.Read(data.Slice(0, size));
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Error, "%s: %s", nameof(Read), e.Message);
            return Errno.EPERM;
        }
    }
}
