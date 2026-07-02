/*
 *  This file is part of vban.
 *  Copyright (c) 2015 by Benoît Quiniou <quiniouben@yahoo.fr>
 *
 *  C# port of src/common/socket.{c,h}
 *  Licensed under the GNU General Public License v3 (or later).
 */

using System.Net;
using System.Net.Sockets;

namespace Vban;

public enum SocketDirection
{
    In,
    Out,
}

public struct SocketConfig
{
    public SocketDirection Direction;
    public string IpAddress;
    public int Port;
}

/// <summary>
/// Thin UDP wrapper mirroring src/common/socket.c. On the IN direction it binds
/// to the given port and filters incoming datagrams by source address; on the
/// OUT direction it sends to the configured address (enabling broadcast when the
/// address looks like a broadcast address).
/// </summary>
public sealed class VbanSocket : IDisposable
{
    private SocketConfig _config;
    private Socket? _fd;

    private VbanSocket(SocketConfig config) => _config = config;

    public static int Init(out VbanSocket? handle, in SocketConfig config)
    {
        handle = new VbanSocket(config);
        int ret = handle.Open();
        if (ret != 0)
        {
            handle.Dispose();
            handle = null;
        }

        return ret;
    }

    public int Release()
    {
        int ret = Close();
        return ret;
    }

    public void Dispose() => Close();

    private static bool IsBroadcastAddress(string ip) => ip.EndsWith("255", StringComparison.Ordinal);

    private int Open()
    {
        Logger.Log(LogLevel.Info, "%s: opening socket with port %d", nameof(Open), _config.Port);

        if (_fd is not null)
        {
            int closed = Close();
            if (closed != 0)
            {
                Logger.Log(LogLevel.Error, "%s: socket was open and unable to close it", nameof(Open));
                return closed;
            }
        }

        try
        {
            _fd = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }
        catch (SocketException)
        {
            Logger.Log(LogLevel.Error, "%s: unable to create socket", nameof(Open));
            _fd = null;
            return Errno.EPERM;
        }

        if (_config.Direction == SocketDirection.In)
        {
            try
            {
                _fd.Bind(new IPEndPoint(IPAddress.Any, _config.Port));
            }
            catch (SocketException)
            {
                Logger.Log(LogLevel.Error, "%s: unable to bind socket", nameof(Open));
                Close();
                return Errno.EINVAL;
            }
        }
        else
        {
            if (IsBroadcastAddress(_config.IpAddress))
            {
                Logger.Log(LogLevel.Debug, "%s: broadcast address detected", nameof(Open));
                try
                {
                    _fd.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                }
                catch (SocketException)
                {
                    Logger.Log(LogLevel.Error, "%s: unable to set broadcast option", nameof(Open));
                    Close();
                    return Errno.EINVAL;
                }
            }
        }

        Logger.Log(LogLevel.Info, "%s with port: %d", nameof(Open), _config.Port);
        return 0;
    }

    private int Close()
    {
        if (_fd is null)
        {
            return 0;
        }

        Logger.Log(LogLevel.Info, "%s: closing socket with port %d", nameof(Close), _config.Port);
        try
        {
            _fd.Dispose();
        }
        catch
        {
            // ignore close errors
        }

        _fd = null;
        return 0;
    }

    public int Read(byte[] buffer, int size)
    {
        if (_fd is null)
        {
            Logger.Log(LogLevel.Error, "%s: socket is not open", nameof(Read));
            return Errno.ENODEV;
        }

        Logger.Log(LogLevel.Debug, "%s ip %s", nameof(Read), _config.IpAddress);

        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            int ret;
            try
            {
                ret = _fd.ReceiveFrom(buffer, 0, size, SocketFlags.None, ref remote);
            }
            catch (SocketException)
            {
                Logger.Log(LogLevel.Error, "%s: recvfrom error", nameof(Read));
                return Errno.EPERM;
            }
            catch (ObjectDisposedException)
            {
                // socket closed (e.g. on shutdown): behave like an interrupted read
                return Errno.EINTR;
            }

            string source = ((IPEndPoint)remote).Address.ToString();
            if (!string.Equals(_config.IpAddress, source, StringComparison.Ordinal))
            {
                Logger.Log(LogLevel.Debug, "%s: packet received from wrong ip", nameof(Read));
                continue;
            }

            return ret;
        }
    }

    public int Write(byte[] buffer, int size)
    {
        if (_fd is null)
        {
            Logger.Log(LogLevel.Error, "%s: socket is not open", nameof(Write));
            return Errno.ENODEV;
        }

        var dest = new IPEndPoint(IPAddress.Parse(_config.IpAddress), _config.Port);
        try
        {
            return _fd.SendTo(buffer, 0, size, SocketFlags.None, dest);
        }
        catch (SocketException)
        {
            Logger.Log(LogLevel.Error, "%s: sendto error", nameof(Write));
            return Errno.EPERM;
        }
        catch (ObjectDisposedException)
        {
            return Errno.EINTR;
        }
    }
}
