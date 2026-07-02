/*
 *  This file is part of vban_emitter.
 *  Copyright (c) 2018 by Benoît Quiniou <quiniouben@yahoo.fr>
 *
 *  C# port of src/sendtext/main.c
 *  Licensed under the GNU General Public License v3 (or later).
 */

using System.Buffers.Binary;
using System.Text;
using Vban;

internal static class Program
{
    private sealed class Config
    {
        public SocketConfig Socket;
        public string StreamName = string.Empty;
        public int Bps;
        public int Ident;
        public int Format;
    }

    private static void Usage()
    {
        Console.Write("\nUsage: vban_sendtext [OPTIONS] MESSAGE\n\n");
        Console.Write("-i, --ipaddress=IP      : MANDATORY. ipaddress to send stream to\n");
        Console.Write("-p, --port=PORT         : MANDATORY. port to use\n");
        Console.Write("-s, --streamname=NAME   : MANDATORY. streamname to use\n");
        Console.Write("-b, --bps=VALUE         : Data bitrate indicator. default 0 (no special bitrate)\n");
        Console.Write("-n, --ident=VALUE       : Subchannel identification. default 0\n");
        Console.Write("-f, --format=VALUE      : Text format used. can be: 0 (ASCII), 1 (UTF8), 2 (WCHAR), 240 (USER). default 1\n");
        Console.Write("-l, --loglevel=LEVEL    : Log level, from 0 (FATAL) to 4 (DEBUG). default is 1 (ERROR)\n");
        Console.Write("-h, --help              : display this message\n\n");
    }

    private static int GetOptions(Config config, string[] args, out string? message)
    {
        message = null;

        // default values
        config.Bps = 0;
        config.Ident = 0;
        config.Format = 1;
        config.Socket.Direction = SocketDirection.Out;

        var opts = new GetOpt()
            .Add('i', GetOpt.Arg.Required, "ipaddress")
            .Add('p', GetOpt.Arg.Required, "port")
            .Add('s', GetOpt.Arg.Required, "streamname")
            .Add('b', GetOpt.Arg.Required, "bps")
            .Add('n', GetOpt.Arg.Required, "ident")
            .Add('f', GetOpt.Arg.Required, "format")
            .Add('l', GetOpt.Arg.Required, "loglevel")
            .Add('h', GetOpt.Arg.None, "help");

        if (!opts.Parse(args))
        {
            Usage();
            return 1;
        }

        foreach (var (opt, val) in opts.Options)
        {
            switch (opt)
            {
                case 'i': config.Socket.IpAddress = val!; break;
                case 'p': config.Socket.Port = ParseInt(val); break;
                case 's': config.StreamName = Truncate(val!, VBan.StreamNameSize - 1); break;
                case 'b': config.Bps = ParseInt(val); break;
                case 'n': config.Ident = ParseInt(val); break;
                case 'f': config.Format = ParseInt(val); break;
                case 'l': Logger.SetOutputLevel((LogLevel)ParseInt(val)); break;
                case 'h':
                default:
                    Usage();
                    return 1;
            }
        }

        if (string.IsNullOrEmpty(config.Socket.IpAddress)
            || config.Socket.Port == 0
            || string.IsNullOrEmpty(config.StreamName))
        {
            Logger.Log(LogLevel.Fatal, "Missing ip address, port or stream name");
            Usage();
            return 1;
        }

        if (opts.Positionals.Count == 0)
        {
            Logger.Log(LogLevel.Fatal, "Missing message argument");
            Usage();
            return 1;
        }

        if (opts.Positionals.Count > 1)
        {
            Logger.Log(LogLevel.Fatal, "Too many arguments");
            Usage();
            return 1;
        }

        message = opts.Positionals[0];
        return 0;
    }

    private static int Main(string[] args)
    {
        Console.Write($"vban_sendtext version {VBan.Version}\n\n");

        var config = new Config { Socket = new SocketConfig { IpAddress = string.Empty } };

        int ret = GetOptions(config, args, out string? message);
        if (ret != 0)
        {
            return ret;
        }

        byte[] msg = Encoding.UTF8.GetBytes(message!);
        int len = msg.Length;
        if (len > VBan.DataMaxSize - 1)
        {
            Logger.Log(LogLevel.Fatal, "Message too long. max lenght is %d", VBan.DataMaxSize - 1);
            Usage();
            return 1;
        }

        var buffer = new byte[VBan.ProtocolMaxSize];
        msg.CopyTo(buffer, VBan.HeaderSize);

        ret = VbanSocket.Init(out VbanSocket? socket, config.Socket);
        if (ret != 0 || socket is null)
        {
            return ret;
        }

        // build the TXT sub-protocol header directly
        VBan.Fourc.CopyTo(buffer.AsSpan(VBan.OffsetFourc, 4));
        buffer[VBan.OffsetFormatSR] = (byte)(config.Bps | (int)VBanProtocol.Txt);
        buffer[VBan.OffsetFormatNbs] = 0;
        buffer[VBan.OffsetFormatNbc] = (byte)config.Ident;
        buffer[VBan.OffsetFormatBit] = (byte)config.Format;
        WriteStreamName(buffer, config.StreamName);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(VBan.OffsetNuFrame), 0);

        Logger.Log(LogLevel.Debug,
            "%s: packet is vban, sr: %d, nbs: %d, nbc: %d, bit: %d, name: %s, msg: %s",
            nameof(Main), buffer[VBan.OffsetFormatSR], buffer[VBan.OffsetFormatNbs],
            buffer[VBan.OffsetFormatNbc], buffer[VBan.OffsetFormatBit], config.StreamName, message);

        ret = socket.Write(buffer, len + VBan.HeaderSize);

        socket.Release();

        return ret;
    }

    // strncpy(hdr->streamname, name, 16): copies up to 16 bytes, no forced NUL
    private static void WriteStreamName(byte[] buffer, string name)
    {
        var field = buffer.AsSpan(VBan.OffsetStreamName, VBan.StreamNameSize);
        field.Clear();
        for (int i = 0; i < name.Length && i < VBan.StreamNameSize; ++i)
        {
            field[i] = (byte)name[i];
        }
    }

    private static int ParseInt(string? s) => int.TryParse(s, out int v) ? v : 0;

    private static string Truncate(string s, int max) => s.Length > max ? s.Substring(0, max) : s;
}
