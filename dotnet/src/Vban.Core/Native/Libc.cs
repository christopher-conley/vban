/*
 *  This file is part of vban (C# port).
 *  Licensed under the GNU General Public License v3 (or later).
 */

using System.Runtime.InteropServices;

namespace Vban.Native;

/// <summary>Minimal libc bindings needed by the pipe backend on Unix.</summary>
internal static partial class Libc
{
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int mkfifo(string pathname, uint mode);

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int unlink(string pathname);
}
