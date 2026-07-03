/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 *
 *  ALSA backend through the managed NAudio.Alsa package, as an alternative to
 *  the direct libasound P/Invoke port in AlsaBackend ("alsa"). No C
 *  counterpart.
 */

using NAudio.Wave.Alsa;

namespace Vban.Audio.Backends;

public sealed class NAudioAlsaBackend : NAudioBackendBase
{
    public const string Name = "naudio-alsa";

    public override int Open(string outputName, AudioDirection direction, int bufferSize, in StreamConfig config)
    {
        Logger.Log(LogLevel.Debug, "%s", nameof(Open));

        if (!OperatingSystem.IsLinux())
        {
            Logger.Log(LogLevel.Fatal, "%s: %s backend is only available on Linux", nameof(Open), Name);
            return Errno.ENODEV;
        }

        int ret = GetWaveFormat(config, out var format, out int frameSize);
        if (ret != 0)
        {
            return ret;
        }

        string device = string.IsNullOrEmpty(outputName) ? "default" : outputName;

        try
        {
            return direction == AudioDirection.Out
                ? OpenPlayback(new AlsaOut(device), format, bufferSize, frameSize)
                : OpenCapture(new AlsaIn(device), format, bufferSize, frameSize);
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Fatal, "%s: unable to open alsa device %s: %s", nameof(Open), device, e.Message);
            return Errno.ENODEV;
        }
    }
}
