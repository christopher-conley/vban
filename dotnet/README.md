vban — C# / .NET 10 port
========================

This is a direct C# port of the original C `vban` command-line tools (see the
repository root `README.md` for protocol background and full usage). It mirrors
the original architecture: a shared core library plus three executables. This will
be dotnet-ized to use `ILogger`, proper command line parsing, etc. later.

```
dotnet/
├── Vban.sln
├── Directory.Build.props        # shared TFMs and compiler settings
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
    │   │   └── Backends/        # see "Audio backends" below
    │   └── Native/              # P/Invoke bindings (libasound, libpulse, libjack, libc)
    ├── VbanReceptor/            # vban_receptor  (receptor/main.c)
    ├── VbanEmitter/             # vban_emitter   (emitter/main.c)
    └── VbanSendText/            # vban_sendtext  (sendtext/main.c)
```

Every project dual-targets `net10.0` (Linux and any other platform) and
`net10.0-windows10.0.19041` (adds the Windows audio backends, whose NAudio 3.0
packages only ship Windows assemblies). Both flavors build on any OS thanks to
`EnableWindowsTargeting`.

Build & run
-----------

```sh
cd dotnet
dotnet build                                   # builds all four projects
dotnet run --project src/VbanReceptor -- -i 127.0.0.1 -p 6980 -s Stream1
```

The produced executables are named `vban_receptor`, `vban_emitter` and
`vban_sendtext` (under each project's `bin/<config>/net10.0/` for Linux and
`bin/<config>/net10.0-windows10.0.19041/` for Windows). To ship a standalone
Windows binary from a Linux box:

```sh
dotnet publish src/VbanReceptor -c Release -f net10.0-windows10.0.19041 -r win-x64 --self-contained
```

Audio backends
--------------

| backend       | platform | play | capture | implementation |
|---------------|----------|------|---------|----------------|
| `alsa`        | Linux    | yes  | yes     | P/Invoke `libasound.so.2` (port of the C backend) |
| `pulseaudio`  | Linux    | yes  | yes     | P/Invoke `libpulse-simple.so.0` (port of the C backend) |
| `jack`        | Linux    | yes  | no      | P/Invoke `libjack.so.0` (port of the C backend) |
| `naudio-alsa` | Linux    | yes  | yes     | managed, NAudio.Alsa package |
| `pipewire`    | Linux    | yes  | yes     | drives `pw-play` / `pw-record` (pw-cat) |
| `wasapi`      | Windows  | yes  | yes     | NAudio.Wasapi (what VB-Audio tools label "WDM") |
| `mme`         | Windows  | yes  | yes     | NAudio.WinMM (waveOut/waveIn) |
| `asio`        | Windows  | yes  | yes     | NAudio.Asio (needs an installed ASIO driver) |
| `pipe`        | any      | yes  | yes     | managed, fifo / named pipe |
| `file`        | any      | yes  | yes     | managed, raw PCM file or stdout |

The default backend is `alsa` on the plain build and `wasapi` on the Windows
build. The P/Invoke backends resolve their native libraries at runtime, so the
code *compiles* on any platform — a backend that needs a missing native
library simply fails to open at runtime, exactly like selecting an unavailable
backend in the C build.

As in the C sources, `jack` is playback-only and the emitter rejects the `jack`
backend.

The `NAudio.PipeWirePlay` package was evaluated for the `pipewire` backend but
is compiled against NAudio.Core 2.3, whose array-based `IWaveProvider.Read`
no longer exists in the NAudio.Core 3.0 preview that `NAudio.Alsa` requires
(its pump thread dies with `MissingMethodException`). Since that package is a
`pw-play` process wrapper anyway, `PipeWireBackend` spawns `pw-cat` itself with
the same arguments — which also adds capture support via `pw-record`.

Notes on parity
---------------
* Tested and working as an emitter on Linux. The VBAudio `VBAN Receptor` and
  `VBAN Talkie` clients are able to successfully receive audio from this
  dotnet/C# emitter.
* `naudio-alsa` and `pipewire` are verified end-to-end on Linux in both
  directions (sine round-trip through a null sink and its monitor, correct
  frequency and amplitude). The Windows backends compile but have not yet been
  runtime-tested on Windows hardware.

* The wire format, packet validation, channel mapping and CLI options match the
  C implementation. A UDP loopback round-trip through the `file` backend
  reproduces the input bytes exactly, and the `-c` channel map produces the same
  de-interleaved output.
* `VBanBitResolutionSize` keeps the original table's quirk: only the first six
  entries are defined (12I/10I are 0), matching the C source.
