/*
 *  This file is part of vban.
 *  Copyright (c) 2017 by Benoît Quiniou <quiniouben@yahoo.fr>
 *
 *  C# port of src/common/stream.{c,h}
 *  Licensed under the GNU General Public License v3 (or later).
 */

namespace Vban;

/// <summary>
/// Carries the important audio parameters of a stream around (the values that
/// come from / go to the VBAN header, before any channel mapping is applied).
/// </summary>
public struct StreamConfig
{
    public uint NbChannels;
    public uint SampleRate;
    public VBanBitResolution BitFmt;
}

public static class StreamFormat
{
    /// <warning>MUST BE ADAPTED WITH VBAN PROTOCOL EVOLUTIONS</warning>
    private static readonly string[] BitFmtNames =
    {
        "8I", "16I", "24I", "32I", "32F", "64F", "12I", "10I",
    };

    private const string HelpMessage =
        "Recognized bit format are 8I, 16I, 24I, 32I, 32F, 64F, 12I, 10I";

    public static VBanBitResolution ParseBitFmt(string argv)
    {
        int index = 0;
        while (index < (int)VBanBitResolution.Max && !string.Equals(argv, BitFmtNames[index], StringComparison.Ordinal))
        {
            ++index;
        }

        return (VBanBitResolution)index;
    }

    public static string PrintBitFmt(VBanBitResolution bitFmt)
    {
        return bitFmt < VBanBitResolution.Max ? BitFmtNames[(int)bitFmt] : "Invalid";
    }

    public static string BitFmtHelp() => HelpMessage;
}
