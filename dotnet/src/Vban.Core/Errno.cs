/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 */

namespace Vban;

/// <summary>
/// Negative errno-style return codes. The original C code returns negative
/// errno values on failure and callers only test the sign, so the exact
/// magnitudes are kept mostly for parity / readability.
/// </summary>
public static class Errno
{
    public const int EPERM = -1;
    public const int EINTR = -4;
    public const int ENXIO = -6;
    public const int ENOMEM = -12;
    public const int EINVAL = -22;
    public const int ENODEV = -19;
}
