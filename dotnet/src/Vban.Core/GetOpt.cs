/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 */

namespace Vban;

/// <summary>
/// Small getopt_long-style parser covering the forms the vban tools rely on:
/// "-i VALUE", "-iVALUE", "--ipaddress=VALUE", "--ipaddress VALUE", flags with no
/// argument, and optional-argument options (value only if attached). Remaining
/// non-option arguments are collected as positionals.
/// </summary>
public sealed class GetOpt
{
    public enum Arg
    {
        None,
        Required,
        Optional,
    }

    private readonly Dictionary<char, Arg> _short = new();
    private readonly Dictionary<string, char> _long = new();

    public List<(char Opt, string? Val)> Options { get; } = new();
    public List<string> Positionals { get; } = new();

    public GetOpt Add(char c, Arg arg, string? longName = null)
    {
        _short[c] = arg;
        if (longName is not null)
        {
            _long[longName] = c;
        }

        return this;
    }

    /// <summary>Returns false on an unknown option or a missing required argument.</summary>
    public bool Parse(string[] args)
    {
        for (int i = 0; i < args.Length; ++i)
        {
            string a = args[i];

            if (a.Length >= 2 && a[0] == '-' && a[1] == '-')
            {
                string name = a.Substring(2);
                string? val = null;
                int eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    val = name.Substring(eq + 1);
                    name = name.Substring(0, eq);
                }

                if (!_long.TryGetValue(name, out char c))
                {
                    return false;
                }

                Arg arg = _short[c];
                if (arg == Arg.Required && val is null)
                {
                    if (i + 1 >= args.Length)
                    {
                        return false;
                    }

                    val = args[++i];
                }

                Options.Add((c, val));
            }
            else if (a.Length >= 1 && a[0] == '-' && a != "-")
            {
                int p = 1;
                while (p < a.Length)
                {
                    char c = a[p];
                    if (!_short.TryGetValue(c, out Arg arg))
                    {
                        return false;
                    }

                    ++p;
                    if (arg == Arg.None)
                    {
                        Options.Add((c, null));
                        continue;
                    }

                    string? val = p < a.Length ? a.Substring(p) : null;
                    if (val is not null)
                    {
                        Options.Add((c, val));
                        break;
                    }

                    if (arg == Arg.Required)
                    {
                        if (i + 1 >= args.Length)
                        {
                            return false;
                        }

                        val = args[++i];
                    }

                    Options.Add((c, val));
                    break;
                }
            }
            else
            {
                Positionals.Add(a);
            }
        }

        return true;
    }
}
