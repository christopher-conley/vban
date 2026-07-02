/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 */

using System.Globalization;
using System.Text;

namespace Vban;

/// <summary>
/// Tiny helper that renders the C-style printf format strings used throughout
/// the original sources, so those format strings can be carried over verbatim.
/// Only the conversion specifiers actually used by vban are supported.
/// </summary>
internal static class PrintfFormat
{
    public static string Format(string format, object?[] args)
    {
        var sb = new StringBuilder(format.Length + 16);
        int argIndex = 0;

        for (int i = 0; i < format.Length; ++i)
        {
            char c = format[i];
            if (c != '%')
            {
                sb.Append(c);
                continue;
            }

            // Consume flags / width / precision / length modifiers; we ignore
            // them for layout purposes and only care about the conversion char.
            int j = i + 1;
            while (j < format.Length && "-+ #0123456789.*lhLqjzt".IndexOf(format[j]) >= 0)
            {
                ++j;
            }

            if (j >= format.Length)
            {
                sb.Append('%');
                break;
            }

            char conv = format[j];
            i = j;

            if (conv == '%')
            {
                sb.Append('%');
                continue;
            }

            object? arg = argIndex < args.Length ? args[argIndex++] : null;
            sb.Append(Render(conv, arg));
        }

        return sb.ToString();
    }

    private static string Render(char conv, object? arg)
    {
        switch (conv)
        {
            case 'd':
            case 'i':
            case 'u':
            case 'l':
                return Convert.ToInt64(arg ?? 0L, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture);
            case 'x':
                return Convert.ToInt64(arg ?? 0L, CultureInfo.InvariantCulture)
                    .ToString("x", CultureInfo.InvariantCulture);
            case 'X':
                return Convert.ToInt64(arg ?? 0L, CultureInfo.InvariantCulture)
                    .ToString("X", CultureInfo.InvariantCulture);
            case 'f':
            case 'g':
            case 'e':
                return Convert.ToDouble(arg ?? 0d, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture);
            case 'c':
                return arg?.ToString() ?? string.Empty;
            case 's':
            default:
                return arg?.ToString() ?? string.Empty;
        }
    }
}
