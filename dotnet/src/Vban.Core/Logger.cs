/*
 *  This file is part of vban.
 *  Copyright (c) 2015 by Benoît Quiniou <quiniouben@yahoo.fr>
 *
 *  C# port of src/common/logger.{c,h}
 *  Licensed under the GNU General Public License v3 (or later).
 */

namespace Vban;

public enum LogLevel
{
    Fatal,
    Error,
    Warning,
    Info,
    Debug,
}

/// <summary>
/// Minimal printf-style logger mirroring the behaviour of the C implementation:
/// FATAL/ERROR/WARNING go to stderr, INFO/DEBUG go to stdout.
/// </summary>
public static class Logger
{
    private static LogLevel _outputLevel = LogLevel.Error;

    public static void SetOutputLevel(LogLevel level) => _outputLevel = level;

    public static void Log(LogLevel level, string format, params object?[] args)
    {
        if (level > _outputLevel)
        {
            return;
        }

        var output = level <= LogLevel.Warning ? Console.Error : Console.Out;

        string prefix = level switch
        {
            LogLevel.Fatal => "Fatal: ",
            LogLevel.Error => "Error: ",
            LogLevel.Warning => "Warning: ",
            LogLevel.Info => "Info: ",
            LogLevel.Debug => "Debug: ",
            _ => null!,
        };

        if (prefix is null)
        {
            return;
        }

        // The format strings come from the C sources and use printf placeholders
        // (%s, %d, ...). Translate the common ones to .NET composite formatting.
        string message = args.Length == 0 ? format : PrintfFormat.Format(format, args);
        output.Write(prefix);
        output.Write(message);
        output.Write('\n');
    }
}
