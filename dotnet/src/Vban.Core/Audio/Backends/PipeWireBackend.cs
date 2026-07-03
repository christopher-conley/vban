/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 *
 *  PipeWire backend driving the pw-play / pw-record command line tools
 *  directly (the same mechanism as the NAudio.PipeWirePlay package, which is
 *  binary-incompatible with NAudio.Core 3.0 and could not be used). The
 *  process pipes give natural blocking backpressure, so no intermediate
 *  buffering is needed. No C counterpart.
 *
 *  The device name is the target node to connect to (a sink for playback,
 *  a source for capture); empty means the default.
 */

using System.Diagnostics;

namespace Vban.Audio.Backends;

public sealed class PipeWireBackend : AudioBackend
{
    public const string Name = "pipewire";

    private Process? _process;
    private System.IO.Stream? _stdin;
    private System.IO.Stream? _stdout;

    public override int Open(string outputName, AudioDirection direction, int bufferSize, in StreamConfig config)
    {
        Logger.Log(LogLevel.Debug, "%s", nameof(Open));

        if (!OperatingSystem.IsLinux())
        {
            Logger.Log(LogLevel.Fatal, "%s: %s backend is only available on Linux", nameof(Open), Name);
            return Errno.ENODEV;
        }

        string? format = MapFormat(config.BitFmt);
        if (format is null)
        {
            Logger.Log(LogLevel.Error, "%s: unsupported bit format %s",
                nameof(Open), StreamFormat.PrintBitFmt(config.BitFmt));
            return Errno.EINVAL;
        }

        bool playback = direction == AudioDirection.Out;
        var info = new ProcessStartInfo
        {
            FileName = playback ? "pw-play" : "pw-record",
            RedirectStandardInput = playback,
            RedirectStandardOutput = !playback,
            // stderr is left alone so pw-cat diagnostics reach the console
            UseShellExecute = false,
        };

        info.ArgumentList.Add("--raw");
        info.ArgumentList.Add("--rate");
        info.ArgumentList.Add(config.SampleRate.ToString());
        info.ArgumentList.Add("--channels");
        info.ArgumentList.Add(config.NbChannels.ToString());
        info.ArgumentList.Add("--format");
        info.ArgumentList.Add(format);
        if (!string.IsNullOrEmpty(outputName))
        {
            info.ArgumentList.Add("--target");
            info.ArgumentList.Add(outputName);
        }

        if (bufferSize > 0)
        {
            // pw-cat interprets a bare number as a buffer size in samples
            info.ArgumentList.Add("--latency");
            info.ArgumentList.Add(bufferSize.ToString());
        }

        info.ArgumentList.Add("-");

        try
        {
            _process = Process.Start(info);
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Fatal, "%s: unable to start %s: %s", nameof(Open), info.FileName, e.Message);
            return Errno.ENODEV;
        }

        if (_process is null)
        {
            Logger.Log(LogLevel.Fatal, "%s: unable to start %s", nameof(Open), info.FileName);
            return Errno.ENODEV;
        }

        if (playback)
        {
            _stdin = _process.StandardInput.BaseStream;
        }
        else
        {
            _stdout = _process.StandardOutput.BaseStream;
        }

        return 0;
    }

    public override int Close()
    {
        try
        {
            _stdin?.Close();

            if (_process is not null)
            {
                // give pw-play a moment to drain what it already received
                if (!_process.WaitForExit(500))
                {
                    _process.Kill();
                }

                _process.Dispose();
            }

            _stdout?.Close();
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Warning, "%s: %s", nameof(Close), e.Message);
        }

        _process = null;
        _stdin = null;
        _stdout = null;
        return 0;
    }

    public override int Write(ReadOnlySpan<byte> data, int size)
    {
        if (_stdin is null)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Write));
            return Errno.ENODEV;
        }

        try
        {
            _stdin.Write(data.Slice(0, size));
            return size;
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Error, "%s: %s", nameof(Write), e.Message);
            return Errno.ENODEV;
        }
    }

    public override int Read(Span<byte> data, int size)
    {
        if (_stdout is null)
        {
            Logger.Log(LogLevel.Error, "%s: device not open", nameof(Read));
            return Errno.ENODEV;
        }

        try
        {
            int total = 0;
            while (total < size)
            {
                int n = _stdout.Read(data.Slice(total, size - total));
                if (n == 0)
                {
                    Logger.Log(LogLevel.Error, "%s: pw-record terminated", nameof(Read));
                    return total != 0 ? total : Errno.ENODEV;
                }

                total += n;
            }

            return total;
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Error, "%s: %s", nameof(Read), e.Message);
            return Errno.ENODEV;
        }
    }

    /// <summary>pw-cat sample formats are native-endian, which matches VBAN's
    /// little-endian data on every platform .NET 10 supports here.</summary>
    private static string? MapFormat(VBanBitResolution bitFmt) => bitFmt switch
    {
        VBanBitResolution.Int8 => "s8",
        VBanBitResolution.Int16 => "s16",
        VBanBitResolution.Int24 => "s24",
        VBanBitResolution.Int32 => "s32",
        VBanBitResolution.Float32 => "f32",
        VBanBitResolution.Float64 => "f64",
        _ => null,
    };
}
