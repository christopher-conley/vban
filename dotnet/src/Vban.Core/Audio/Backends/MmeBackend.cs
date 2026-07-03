/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 *
 *  MME (waveOut/waveIn) backend through NAudio.WinMM. Windows only; compiled
 *  solely into the net10.0-windows build. The device name is either a device
 *  number or a substring of the device product name; empty means the default
 *  device.
 */

#if WINDOWS

using NAudio.Wave;

namespace Vban.Audio.Backends;

public sealed class MmeBackend : NAudioBackendBase
{
    public const string Name = "mme";

    public override int Open(string outputName, AudioDirection direction, int bufferSize, in StreamConfig config)
    {
        Logger.Log(LogLevel.Debug, "%s", nameof(Open));

        int ret = GetWaveFormat(config, out var format, out int frameSize);
        if (ret != 0)
        {
            return ret;
        }

        int latency = LatencyMs(bufferSize, config);

        int device = FindDevice(direction, outputName);
        if (device < -1)
        {
            Logger.Log(LogLevel.Fatal, "%s: no mme %s device matching \"%s\"", nameof(Open),
                direction == AudioDirection.Out ? "output" : "input", outputName);
            return Errno.ENODEV;
        }

        if (direction == AudioDirection.Out)
        {
            var player = new WaveOut { DeviceNumber = device, BufferMilliseconds = latency };
            return OpenPlayback(player, format, bufferSize, frameSize);
        }

        var capture = new WaveIn { DeviceNumber = device, BufferMilliseconds = latency };
        return OpenCapture(capture, format, bufferSize, frameSize);
    }

    /// <returns>a device number, -1 for the default device, -2 when no device matches</returns>
    private static int FindDevice(AudioDirection direction, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return -1;
        }

        if (int.TryParse(name, out int number))
        {
            return number;
        }

        int count = direction == AudioDirection.Out ? WaveOut.DeviceCount : WaveIn.DeviceCount;
        for (int i = 0; i != count; ++i)
        {
            string product = direction == AudioDirection.Out
                ? WaveOut.GetCapabilities(i).ProductName
                : WaveIn.GetCapabilities(i).ProductName;

            if (product.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -2;
    }
}

#endif
