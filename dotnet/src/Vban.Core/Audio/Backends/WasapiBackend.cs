/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 *
 *  WASAPI backend (shared mode) through NAudio.Wasapi. This is what VB-Audio
 *  tools label "WDM". Windows only; the whole file is compiled solely into
 *  the net10.0-windows build. The device name matches a substring of the
 *  endpoint friendly name; empty means the default device.
 */

#if WINDOWS

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Vban.Audio.Backends;

public sealed class WasapiBackend : NAudioBackendBase
{
    public const string Name = "wasapi";

    // WasapiRecorder does not implement IWaveIn, so it is driven directly
    // rather than through the common OpenCapture plumbing
    private WasapiRecorder? _recorder;

    public override int Open(string outputName, AudioDirection direction, int bufferSize, in StreamConfig config)
    {
        Logger.Log(LogLevel.Debug, "%s", nameof(Open));

        int ret = GetWaveFormat(config, out var format, out int frameSize);
        if (ret != 0)
        {
            return ret;
        }

        int latency = LatencyMs(bufferSize, config);
        DataFlow flow = direction == AudioDirection.Out ? DataFlow.Render : DataFlow.Capture;

        MMDevice? device;
        try
        {
            device = FindDevice(flow, outputName);
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Fatal, "%s: device enumeration failed: %s", nameof(Open), e.Message);
            return Errno.ENODEV;
        }

        if (device is null)
        {
            Logger.Log(LogLevel.Fatal, "%s: no wasapi %s device matching \"%s\"", nameof(Open),
                direction == AudioDirection.Out ? "render" : "capture", outputName);
            return Errno.ENODEV;
        }

        Logger.Log(LogLevel.Info, "%s: using device %s", nameof(Open), device.FriendlyName);

        try
        {
            if (direction == AudioDirection.Out)
            {
                var player = new WasapiPlayerBuilder()
                    .WithDevice(device)
                    .WithSharedMode()
                    .WithEventSync()
                    .WithLatency(latency)
                    .Build();
                return OpenPlayback(player, format, bufferSize, frameSize);
            }

            var recorder = new WasapiRecorderBuilder()
                .WithDevice(device)
                .WithSharedMode()
                .WithEventSync()
                .WithFormat(format)
                .Build();

            Ring = new BlockingRingBuffer(RingCapacity(bufferSize, frameSize));
            Signed8 = format is { Encoding: WaveFormatEncoding.Pcm, BitsPerSample: 8 };
            recorder.DataAvailable += OnRecorderData;
            recorder.StartRecording();
            _recorder = recorder;
            return 0;
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Fatal, "%s: unable to open wasapi device: %s", nameof(Open), e.Message);
            Ring = null;
            return Errno.ENODEV;
        }
    }

    private byte[] _scratch = Array.Empty<byte>();

    // zero-copy callback: the span is only valid for the duration of the call,
    // but WriteOverwrite copies it into the ring buffer immediately
    private void OnRecorderData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (Signed8)
        {
            if (_scratch.Length < buffer.Length)
            {
                _scratch = new byte[buffer.Length];
            }

            buffer.CopyTo(_scratch);
            HandleCaptureData(_scratch, buffer.Length);
            return;
        }

        Ring?.WriteOverwrite(buffer);
    }

    public override int Close()
    {
        Ring?.Close();

        try
        {
            if (_recorder is not null)
            {
                _recorder.DataAvailable -= OnRecorderData;
                _recorder.StopRecording();
                _recorder.Dispose();
            }
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Warning, "%s: error stopping wasapi capture: %s", nameof(Close), e.Message);
        }

        _recorder = null;
        return base.Close();
    }

    private static MMDevice? FindDevice(DataFlow flow, string name)
    {
        using var enumerator = new MMDeviceEnumerator();

        if (string.IsNullOrEmpty(name))
        {
            return enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
        }

        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            if (device.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }

            device.Dispose();
        }

        return null;
    }
}

#endif
