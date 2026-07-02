/*
 *  This file is part of vban.
 *  Copyright (c) 2015 by Benoît Quiniou <quiniouben@yahoo.fr>
 *
 *  C# port of the configuration/types portion of src/common/audio.{c,h}
 *  Licensed under the GNU General Public License v3 (or later).
 */

using System.Globalization;

namespace Vban.Audio;

public enum AudioDirection
{
    In,
    Out,
}

/// <summary>Configuration structure used at init time.</summary>
public struct AudioConfig
{
    public AudioDirection Direction;
    public string BackendName;
    public string DeviceName;
    public int BufferSize;
}

/// <summary>Channel map configuration.</summary>
public sealed class AudioMapConfig
{
    public readonly byte[] Channels = new byte[VBan.ChannelsMaxNb];
    public int NbChannels;

    /// <summary>
    /// Parse a command line "x,y,z,..." channel list (1-based) into the map.
    /// Returns 0 on success.
    /// </summary>
    public int Parse(string argv)
    {
        if (argv is null)
        {
            Logger.Log(LogLevel.Fatal, "%s: null pointer argument", nameof(Parse));
            return Errno.EINVAL;
        }

        int index = 0;
        foreach (var token in argv.Split(','))
        {
            if (index >= VBan.ChannelsMaxNb)
            {
                break;
            }

            if (!uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint chan))
            {
                break;
            }

            if (chan > VBan.ChannelsMaxNb || chan < 1)
            {
                Logger.Log(LogLevel.Error, "%s: invalid channel id %u, stop parsing", nameof(Parse), chan);
                break;
            }

            Channels[index++] = (byte)(chan - 1);
        }

        NbChannels = index;
        return 0;
    }
}
