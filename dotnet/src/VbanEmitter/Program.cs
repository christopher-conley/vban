/*
 *  This file is part of vban_emitter.
 *  Copyright (c) 2015 by Benoît Quiniou <quiniouben@yahoo.fr>
 *
 *  C# port of src/emitter/main.c
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
        public StreamConfig Stream;
        public AudioMapConfig Map = new();
        public string StreamName = string.Empty;
    }

    private static volatile bool _mainRun = true;

    private static void Usage()
    {
        Console.Write("\nUsage: vban_emitter [OPTIONS]...\n\n");
        Console.Write("-i, --ipaddress=IP      : MANDATORY. ipaddress to send stream to\n");
        Console.Write("-p, --port=PORT         : MANDATORY. port to use\n");
        Console.Write("-s, --streamname=NAME   : MANDATORY. streamname to use\n");
        Console.Write($"-b, --backend=TYPE      : audio backend to use. {AudioBackendRegistry.GetHelp()}\n");
        Console.Write("-d, --device=NAME       : Audio device name. This is file name for file backend, server name for jack backend, device for alsa, stream_name for pulseaudio.\n");
        Console.Write("-r, --rate=VALUE        : Audio device sample rate. default 44100\n");
        Console.Write("-n, --nbchannels=VALUE  : Audio device number of channels. default 2\n");
        Console.Write("-f, --format=VALUE      : Audio device sample format (see below). default is 16I (16bits integer)\n");
        Console.Write("-c, --channels=LIST     : channels from the audio device to use. LIST is of form x,y,z,... default is to forward the stream as it is\n");
        Console.Write("-x, --bufsize=VALUE     : Audio device buffer size. default 1024\n");
        Console.Write("-l, --loglevel=LEVEL    : Log level, from 0 (FATAL) to 4 (DEBUG). default is 1 (ERROR)\n");
        Console.Write("-h, --help              : display this message\n\n");
        Console.Write($"{StreamFormat.BitFmtHelp()}\n\n");
    }

    private static int GetOptions(Config config, string[] args)
    {
        int ret = 0;

        // default values
        config.Stream.NbChannels = 2;
        config.Stream.SampleRate = 44100;
        config.Stream.BitFmt = VBanBitResolution.Int16;
        config.Audio.BufferSize = 1024;
        config.Socket.Direction = SocketDirection.Out;

        var opts = new GetOpt()
            .Add('i', GetOpt.Arg.Required, "ipaddress")
            .Add('p', GetOpt.Arg.Required, "port")
            .Add('s', GetOpt.Arg.Required, "streamname")
            .Add('b', GetOpt.Arg.Required, "backend")
            .Add('d', GetOpt.Arg.Required, "device")
            .Add('r', GetOpt.Arg.Required, "rate")
            .Add('n', GetOpt.Arg.Required, "nbchannels")
            .Add('f', GetOpt.Arg.Required, "format")
            .Add('c', GetOpt.Arg.Required, "channels")
            .Add('x', GetOpt.Arg.Optional, "bufsize")
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
                case 'd': config.Audio.DeviceName = val!; break;
                case 'r': config.Stream.SampleRate = (uint)ParseInt(val); break;
                case 'n': config.Stream.NbChannels = (uint)ParseInt(val); break;
                case 'f': config.Stream.BitFmt = StreamFormat.ParseBitFmt(val!); break;
                case 'c': ret = config.Map.Parse(val!); break;
                case 'x': if (val is not null) { config.Audio.BufferSize = ParseInt(val); } break;
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

        if (string.IsNullOrEmpty(config.Socket.IpAddress)
            || config.Socket.Port == 0
            || string.IsNullOrEmpty(config.StreamName))
        {
            Logger.Log(LogLevel.Fatal, "Missing ip address, port or stream name");
            Usage();
            return 1;
        }

        if (string.Equals(config.Audio.BackendName, "jack", StringComparison.Ordinal))
        {
            Logger.Log(LogLevel.Fatal, "Sorry jack backend is not ready for emitter yet");
            return 1;
        }

        return 0;
    }

    private static int Main(string[] args)
    {
        Console.Write($"vban_emitter version {VBan.Version}\n\n");

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

        InstallSignalHandlers();

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

        ret = audio.SetStreamConfig(config.Stream);
        if (ret != 0)
        {
            return ret;
        }

        var buffer = new byte[VBan.ProtocolMaxSize];

        audio.GetStreamConfig(out var streamConfig);
        Packet.InitHeader(buffer, streamConfig, config.StreamName);
        int maxSize = Packet.GetMaxPayloadSize(buffer);

        while (_mainRun)
        {
            int size = audio.Read(buffer, Packet.PayloadOffset, maxSize);
            if (size < 0)
            {
                _mainRun = false;
                break;
            }

            Packet.SetNewContent(buffer, size);
            ret = Packet.Check(config.StreamName, buffer, size + VBan.HeaderSize);
            if (ret != 0)
            {
                Logger.Log(LogLevel.Error, "%s: packet prepared is invalid", nameof(Main));
                break;
            }

            ret = socket.Write(buffer, size + VBan.HeaderSize);
            if (ret < 0)
            {
                _mainRun = false;
                break;
            }
        }

        audio.Release();
        socket.Release();

        return ret;
    }

    private static void InstallSignalHandlers()
    {
        void Handler(PosixSignalContext ctx)
        {
            ctx.Cancel = true;
            _mainRun = false;
        }

        PosixSignalRegistration.Create(PosixSignal.SIGINT, Handler);
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, Handler);
    }

    private static int ParseInt(string? s) => int.TryParse(s, out int v) ? v : 0;

    private static string Truncate(string s, int max) => s.Length > max ? s.Substring(0, max) : s;
}
