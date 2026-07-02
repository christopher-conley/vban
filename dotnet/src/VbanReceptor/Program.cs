/*
 *  This file is part of vban.
 *  Copyright (c) 2015 by Benoît Quiniou <quiniouben@yahoo.fr>
 *
 *  C# port of src/receptor/main.c
 *  Licensed under the GNU General Public License v3 (or later).
 */

using System.Runtime.InteropServices;
using Vban;
using Vban.Audio;
using Vban.Audio.Backends;

internal static class Program
{
    private sealed class Config
    {
        public SocketConfig Socket;
        public AudioConfig Audio;
        public AudioMapConfig Map = new();
        public string StreamName = string.Empty;
    }

    private static volatile bool _mainRun = true;

    private static void Usage()
    {
        Console.Write("\nUsage: vban_receptor [OPTIONS]...\n\n");
        Console.Write("-i, --ipaddress=IP      : MANDATORY. ipaddress to get stream from\n");
        Console.Write("-p, --port=PORT         : MANDATORY. port to listen to\n");
        Console.Write("-s, --streamname=NAME   : MANDATORY. streamname to play\n");
        Console.Write($"-b, --backend=TYPE      : audio backend to use. {AudioBackendRegistry.GetHelp()}\n");
        Console.Write("-q, --quality=ID        : network quality indicator from 0 (low latency) to 4. This also have interaction with jack buffer size. default is 1\n");
        Console.Write("-c, --channels=LIST     : channels from the stream to use. LIST is of form x,y,z,... default is to forward the stream as it is\n");
        Console.Write("-o, --output=NAME       : DEPRECATED. please use -d\n");
        Console.Write("-d, --device=NAME       : Audio device name. This is file name for file backend, server name for jack backend, device for alsa, stream_name for pulseaudio.\n");
        Console.Write("-l, --loglevel=LEVEL    : Log level, from 0 (FATAL) to 4 (DEBUG). default is 1 (ERROR)\n");
        Console.Write("-h, --help              : display this message\n\n");
    }

    private static int ComputeSize(int quality)
    {
        const int nmin = VBan.ProtocolMaxSize;
        int nnn = quality switch
        {
            0 => 512,
            1 => 1024,
            2 => 2048,
            3 => 4096,
            4 => 8192,
            _ => 0,
        };

        nnn *= 3;
        return nnn < nmin ? nmin : nnn;
    }

    private static int GetOptions(Config config, string[] args)
    {
        int quality = 1;
        int ret = 0;

        var opts = new GetOpt()
            .Add('i', GetOpt.Arg.Required, "ipaddress")
            .Add('p', GetOpt.Arg.Required, "port")
            .Add('s', GetOpt.Arg.Required, "streamname")
            .Add('b', GetOpt.Arg.Required, "backend")
            .Add('q', GetOpt.Arg.Required, "quality")
            .Add('c', GetOpt.Arg.Required, "channels")
            .Add('o', GetOpt.Arg.Required, "output")
            .Add('d', GetOpt.Arg.Required, "device")
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
                case 'b': config.Audio.BackendName = val!; break;
                case 'q': quality = ParseInt(val); break;
                case 'c': ret = config.Map.Parse(val!); break;
                case 'o':
                case 'd': config.Audio.DeviceName = val!; break;
                case 'l': Logger.SetOutputLevel((LogLevel)ParseInt(val)); break;
                case 'h':
                default:
                    Usage();
                    return 1;
            }

            if (ret != 0)
            {
                return ret;
            }
        }

        config.Audio.Direction = AudioDirection.Out;
        config.Audio.BufferSize = ComputeSize(quality);
        config.Socket.Direction = SocketDirection.In;

        if (string.IsNullOrEmpty(config.Socket.IpAddress)
            || config.Socket.Port == 0
            || string.IsNullOrEmpty(config.StreamName))
        {
            Logger.Log(LogLevel.Fatal, "Missing ip address, port or stream name");
            Usage();
            return 1;
        }

        return 0;
    }

    private static int Main(string[] args)
    {
        Console.Write($"vban_receptor version {VBan.Version}\n\n");

        var config = new Config
        {
            Audio = new AudioConfig { BackendName = string.Empty, DeviceName = string.Empty },
            Socket = new SocketConfig { IpAddress = string.Empty },
        };

        int ret = GetOptions(config, args);
        if (ret != 0)
        {
            return ret;
        }

        ret = VbanSocket.Init(out VbanSocket? socket, config.Socket);
        if (ret != 0 || socket is null)
        {
            return ret;
        }

        InstallSignalHandlers(socket);

        ret = Audio.Init(out Audio? audio, config.Audio);
        if (ret != 0 || audio is null)
        {
            return ret;
        }

        ret = audio.SetMapConfig(config.Map);
        if (ret != 0)
        {
            return ret;
        }

        var buffer = new byte[VBan.ProtocolMaxSize];

        while (_mainRun)
        {
            int size = socket.Read(buffer, VBan.ProtocolMaxSize);
            if (size < 0)
            {
                _mainRun = false;
                break;
            }

            if (Packet.Check(config.StreamName, buffer, size) == 0)
            {
                Packet.GetStreamConfig(buffer, out var streamConfig);

                ret = audio.SetStreamConfig(streamConfig);
                if (ret < 0)
                {
                    _mainRun = false;
                    break;
                }

                int payloadSize = Packet.PayloadSize(size);
                ret = audio.Write(buffer, Packet.PayloadOffset, payloadSize);
                if (ret != payloadSize)
                {
                    Logger.Log(LogLevel.Warning, "%s: wrote %d bytes, expected %d bytes", nameof(Main), ret, payloadSize);
                }
            }
        }

        audio.Release();
        socket.Release();

        return 0;
    }

    private static void InstallSignalHandlers(VbanSocket socket)
    {
        void Handler(PosixSignalContext ctx)
        {
            ctx.Cancel = true;
            _mainRun = false;
            socket.Release(); // unblock the pending recvfrom
        }

        PosixSignalRegistration.Create(PosixSignal.SIGINT, Handler);
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, Handler);
    }

    private static int ParseInt(string? s) => int.TryParse(s, out int v) ? v : 0;

    private static string Truncate(string s, int max) => s.Length > max ? s.Substring(0, max) : s;
}
