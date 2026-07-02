vban — C# / .NET 10 port
========================

This is a direct C# port of the original C `vban` command-line tools (see the
repository root `README.md` for protocol background and full usage). It mirrors
the original architecture: a shared core library plus three executables. This will
be dotnet-ized to use `ILogger`, proper command line parsing, etc. later.

```
dotnet/
├── Vban.sln
├── Directory.Build.props        # shared TFM (net10.0) and compiler settings
└── src/
    ├── Vban.Core/               # protocol, packet, socket, audio + backends
    │   ├── Vban.cs              # constants / enums / tables   (vban.h)
    │   ├── Packet.cs            # packet inspect/build         (packet.c)
    │   ├── StreamConfig.cs      # stream config + bit formats  (stream.c)
    │   ├── Logger.cs            # printf-style logger          (logger.c)
    │   ├── VbanSocket.cs        # UDP send/receive             (socket.c)
    │   ├── GetOpt.cs            # getopt_long-style arg parser
    │   ├── Audio/
    │   │   ├── Audio.cs         # channel mapping + dispatch   (audio.c)
    │   │   └── Backends/        # file, pipe, alsa, pulseaudio, jack
    │   └── Native/              # P/Invoke bindings (libasound, libpulse, libjack, libc)
    ├── VbanReceptor/            # vban_receptor  (receptor/main.c)
    ├── VbanEmitter/             # vban_emitter   (emitter/main.c)
    └── VbanSendText/            # vban_sendtext  (sendtext/main.c)
```

Build & run
-----------

```sh
cd dotnet
dotnet build                                   # builds all four projects
dotnet run --project src/VbanReceptor -- -i 127.0.0.1 -p 6980 -s Stream1
```

The produced executables are named `vban_receptor`, `vban_emitter` and
`vban_sendtext` (under each project's `bin/<config>/net10.0/`).

Audio backends
--------------

`file` and `pipe` are pure managed and run on any platform. `alsa`,
`pulseaudio` and `jack` use `DllImport` into the same native libraries as the C
version (`libasound.so.2`, `libpulse-simple.so.0`, `libjack.so.0`); they
resolve at runtime and so are effectively Linux-only. The code *compiles* on any
platform — a backend that needs a missing native library simply fails to open
at runtime, exactly like selecting an unavailable backend in the C build.

As in the C sources, `jack` is playback-only and the emitter rejects the `jack`
backend.

Notes on parity
---------------
* Tested and working as an emitter on Linux. The VBAudio `VBAN Receptor` and
  `VBAN Talkie` clients are able to successfully receive audio from this
  dotnet/C# emitter.

* The wire format, packet validation, channel mapping and CLI options match the
  C implementation. A UDP loopback round-trip through the `file` backend
  reproduces the input bytes exactly, and the `-c` channel map produces the same
  de-interleaved output.
* `VBanBitResolutionSize` keeps the original table's quirk: only the first six
  entries are defined (12I/10I are 0), matching the C source.
