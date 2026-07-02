/*
 *  This file is part of vban.
 *  Copyright (c) 2015 by Benoît Quiniou <quiniouben@yahoo.fr>
 *  Copyright (c) 2017 by Markus Buschhoff <markus.buschhoff@tu-dortmund.de>
 *
 *  C# port of src/common/backend/file_backend.c
 *  Licensed under the GNU General Public License v3 (or later).
 */

namespace Vban.Audio.Backends;

/// <summary>
/// Reads/writes raw PCM data to a file. When no name is given, standard output
/// is used (mirroring the use of STDOUT_FILENO in the C backend).
/// </summary>
public sealed class FileBackend : AudioBackend
{
    public const string Name = "file";

    private System.IO.Stream? _stream;
    private bool _isStdout;

    public override int Open(string outputName, AudioDirection direction, int bufferSize, in StreamConfig config)
    {
        try
        {
            if (!string.IsNullOrEmpty(outputName))
            {
                _isStdout = false;
                _stream = direction == AudioDirection.Out
                    ? new FileStream(outputName, FileMode.Create, FileAccess.Write)
                    : new FileStream(outputName, FileMode.Open, FileAccess.Read);
            }
            else
            {
                _isStdout = true;
                _stream = Console.OpenStandardOutput();
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
        if (_stream is not null && !_isStdout)
        {
            _stream.Dispose();
        }

        _stream = null;
        return 0;
    }

    public override int Write(ReadOnlySpan<byte> data, int size)
    {
        if (_stream is null)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Write));
            return Errno.ENODEV;
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
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Read));
            return Errno.ENODEV;
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
