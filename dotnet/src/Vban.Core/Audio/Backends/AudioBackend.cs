/*
 *  This file is part of vban.
 *
 *  C# port of src/common/backend/audio_backend.{c,h}
 *  Licensed under the GNU General Public License v3 (or later).
 */

using System.Text;

namespace Vban.Audio.Backends;

/// <summary>
/// Base class for an audio backend. Mirrors the function-pointer "vtable" of the
/// C struct audio_backend_t (open/close/write/read).
/// </summary>
public abstract class AudioBackend
{
    public abstract int Open(string outputName, AudioDirection direction, int bufferSize, in StreamConfig config);

    public abstract int Close();

    public abstract int Write(ReadOnlySpan<byte> data, int size);

    public abstract int Read(Span<byte> data, int size);
}

/// <summary>
/// Registry of available backends, equivalent to the static backend_list table.
/// Backends that require a native library will simply fail to open at runtime on
/// platforms where that library is unavailable.
/// </summary>
public static class AudioBackendRegistry
{
    private readonly record struct Item(string Name, Func<AudioBackend> Factory);

    private static readonly Item[] BackendList =
    {
        new(AlsaBackend.Name, () => new AlsaBackend()),
        new(PulseAudioBackend.Name, () => new PulseAudioBackend()),
        new(JackBackend.Name, () => new JackBackend()),
        new(PipeBackend.Name, () => new PipeBackend()),
        new(FileBackend.Name, () => new FileBackend()),
    };

    public static int GetByName(string name, out AudioBackend? backend)
    {
        backend = null;

        if (string.IsNullOrEmpty(name))
        {
            Logger.Log(LogLevel.Info, "%s: taking default backend %s", nameof(GetByName), BackendList[0].Name);
            backend = BackendList[0].Factory();
            return 0;
        }

        foreach (var item in BackendList)
        {
            if (string.Equals(name, item.Name, StringComparison.Ordinal))
            {
                Logger.Log(LogLevel.Info, "%s: found backend %s", nameof(GetByName), name);
                backend = item.Factory();
                return 0;
            }
        }

        Logger.Log(LogLevel.Error, "%s: no backend found with name %s", nameof(GetByName), name);
        return Errno.EINVAL;
    }

    public static string GetHelp()
    {
        var sb = new StringBuilder("Available audio backends are: ");
        foreach (var item in BackendList)
        {
            sb.Append(item.Name).Append(' ');
        }

        sb.Append(". default is ").Append(BackendList[0].Name).Append('.');
        return sb.ToString();
    }
}
