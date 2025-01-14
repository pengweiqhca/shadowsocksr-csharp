﻿using Shadowsocks.Model;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shadowsocks.Controller;

public class ProtocolException : Exception
{
    public ProtocolException(string info)
        : base(info)
    {

    }
}

internal class ProxyAuthHandler
{
    private Configuration _config;
    private ServerTransferTotal _transfer;
    private IPRangeSet _IPRange;

    private byte[] _firstPacket;
    private int _firstPacketLength;

    private Socket _connection;
    private Socket _connectionUDP;
    private string local_sendback_protocol;

    protected const int RECV_SIZE = 16384;
    protected byte[] _connetionRecvBuffer = new byte[RECV_SIZE * 2];

    public byte command;
    protected byte[] _remoteHeaderSendBuffer;

    protected HttpPraser httpProxyState;

    public ProxyAuthHandler(Configuration config, ServerTransferTotal transfer, IPRangeSet IPRange, byte[] firstPacket, int length, Socket socket)
    {
        var local_port = ((IPEndPoint)socket.LocalEndPoint).Port;

        _config = config;
        _transfer = transfer;
        _IPRange = IPRange;
        _firstPacket = firstPacket;
        _firstPacketLength = length;
        _connection = socket;
        socket.NoDelay = true;

        if (_config.GetPortMapCache().ContainsKey(local_port) && _config.GetPortMapCache()[local_port].type == PortMapType.Forward)
        {
            Connect();
        }
        else
        {
            HandshakeReceive();
        }
    }

    private void CloseSocket(ref Socket sock)
    {
        lock (this)
        {
            if (sock != null)
            {
                var s = sock;
                sock = null;
                try
                {
                    s.Shutdown(SocketShutdown.Both);
                }
                catch
                {
                }
                try
                {
                    s.Close();
                }
                catch
                {
                }
            }
        }
    }

    private void Close()
    {
        CloseSocket(ref _connection);
        CloseSocket(ref _connectionUDP);

        _config = null;
    }

    private bool AuthConnection(Socket connection, string authUser, string authPass)
    {
        if ((_config.authUser ?? "").Length == 0)
        {
            return true;
        }
        if (_config.authUser == authUser && (_config.authPass ?? "") == authPass)
        {
            return true;
        }
        if (Util.Utils.isMatchSubNet(((IPEndPoint)_connection.RemoteEndPoint).Address, "127.0.0.0/8"))
        {
            return true;
        }
        return false;
    }

    private void HandshakeReceive()
    {
        try
        {
            var bytesRead = _firstPacketLength;

            if (bytesRead > 1)
            {
                if ((!string.IsNullOrEmpty(_config.authUser) || Util.Utils.isMatchSubNet(((IPEndPoint)_connection.RemoteEndPoint).Address, "127.0.0.0/8"))
                    && _firstPacket[0] == 4 && _firstPacketLength >= 9)
                {
                    RspSocks4aHandshakeReceive();
                }
                else if (_firstPacket[0] == 5 && _firstPacketLength >= 3)
                {
                    RspSocks5HandshakeReceive();
                }
                else
                {
                    RspHttpHandshakeReceive();
                }
            }
            else
            {
                Close();
            }
        }
        catch (Exception e)
        {
            Logging.LogUsefulException(e);
            Close();
        }
    }

    private void RspSocks4aHandshakeReceive()
    {
        var firstPacket = new List<byte>();
        for (var i = 0; i < _firstPacketLength; ++i)
        {
            firstPacket.Add(_firstPacket[i]);
        }
        var dataSockSend = firstPacket.GetRange(0, 4);
        dataSockSend[0] = 0;
        dataSockSend[1] = 90;

        var remoteDNS = _firstPacket[4] == 0 && _firstPacket[5] == 0 && _firstPacket[6] == 0 && _firstPacket[7] == 1;
        if (remoteDNS)
        {
            for (var i = 0; i < 4; ++i)
            {
                dataSockSend.Add(0);
            }
            var addrStartPos = firstPacket.IndexOf(0x0, 8);
            var addr = firstPacket.GetRange(addrStartPos + 1, firstPacket.Count - addrStartPos - 2);
            _remoteHeaderSendBuffer = new byte[2 + addr.Count + 2];
            _remoteHeaderSendBuffer[0] = 3;
            _remoteHeaderSendBuffer[1] = (byte)addr.Count;
            Array.Copy(addr.ToArray(), 0, _remoteHeaderSendBuffer, 2, addr.Count);
            _remoteHeaderSendBuffer[2 + addr.Count] = dataSockSend[2];
            _remoteHeaderSendBuffer[2 + addr.Count + 1] = dataSockSend[3];
        }
        else
        {
            for (var i = 0; i < 4; ++i)
            {
                dataSockSend.Add(_firstPacket[4 + i]);
            }
            _remoteHeaderSendBuffer = new byte[1 + 4 + 2];
            _remoteHeaderSendBuffer[0] = 1;
            Array.Copy(dataSockSend.ToArray(), 4, _remoteHeaderSendBuffer, 1, 4);
            _remoteHeaderSendBuffer[1 + 4] = dataSockSend[2];
            _remoteHeaderSendBuffer[1 + 4 + 1] = dataSockSend[3];
        }
        command = 1; // Set TCP connect command
        _connection.Send(dataSockSend.ToArray());
        Connect();
    }

    private void RspSocks5HandshakeReceive()
    {
        byte[] response = { 5, 0 };
        if (_firstPacket[0] != 5)
        {
            response = new byte[] { 0, 91 };
            Console.WriteLine("socks 4/5 protocol error");
            _connection.Send(response);
            Close();
            return;
        }
        var no_auth = false;
        var auth = false;
        var has_method = false;
        for (var index = 0; index < _firstPacket[1]; ++index)
        {
            if (_firstPacket[2 + index] == 0)
            {
                no_auth = true;
                has_method = true;
            }
            else if (_firstPacket[2 + index] == 2)
            {
                auth = true;
                has_method = true;
            }
        }
        if (!has_method)
        {
            Console.WriteLine("Socks5 no acceptable auth method");
            Close();
            return;
        }
        if (auth || !no_auth)
        {
            response[1] = 2;
            _connection.Send(response);
            HandshakeAuthReceiveCallback();
        }
        else if (no_auth && (string.IsNullOrEmpty(_config.authUser)
                             || Util.Utils.isMatchSubNet(((IPEndPoint)_connection.RemoteEndPoint).Address, "127.0.0.0/8")))
        {
            _connection.Send(response);
            HandshakeReceive2Callback();
        }
        else
        {
            Console.WriteLine("Socks5 Auth failed");
            Close();
        }
    }

    private void HandshakeAuthReceiveCallback()
    {
        try
        {
            var bytesRead = _connection.Receive(_connetionRecvBuffer, 1024, 0); //_connection.EndReceive(ar);

            if (bytesRead >= 3)
            {
                var user_len = _connetionRecvBuffer[1];
                var pass_len = _connetionRecvBuffer[user_len + 2];
                byte[] response = { 1, 0 };
                var user = Encoding.UTF8.GetString(_connetionRecvBuffer, 2, user_len);
                var pass = Encoding.UTF8.GetString(_connetionRecvBuffer, user_len + 3, pass_len);
                if (AuthConnection(_connection, user, pass))
                {
                    _connection.Send(response);
                    HandshakeReceive2Callback();
                }
            }
            else
            {
                Console.WriteLine("failed to recv data in HandshakeAuthReceiveCallback");
                Close();
            }
        }
        catch (Exception e)
        {
            Logging.LogUsefulException(e);
            Close();
        }
    }

    private void HandshakeReceive2Callback()
    {
        try
        {
            // +----+-----+-------+------+----------+----------+
            // |VER | CMD |  RSV  | ATYP | DST.ADDR | DST.PORT |
            // +----+-----+-------+------+----------+----------+
            // | 1  |  1  | X'00' |  1   | Variable |    2     |
            // +----+-----+-------+------+----------+----------+
            var bytesRead = _connection.Receive(_connetionRecvBuffer, 5, 0);

            if (bytesRead >= 5)
            {
                command = _connetionRecvBuffer[1];
                _remoteHeaderSendBuffer = new byte[bytesRead - 3];
                Array.Copy(_connetionRecvBuffer, 3, _remoteHeaderSendBuffer, 0, _remoteHeaderSendBuffer.Length);

                var recv_size = _remoteHeaderSendBuffer[0] switch
                {
                    1 => 4 - 1,
                    4 => 16 - 1,
                    3 => _remoteHeaderSendBuffer[1],
                    _ => 0
                };
                if (recv_size == 0)
                    throw new Exception("Wrong socks5 addr type");
                HandshakeReceive3Callback(recv_size + 2); // recv port
            }
            else
            {
                Console.WriteLine("failed to recv data in HandshakeReceive2Callback");
                Close();
            }
        }
        catch (Exception e)
        {
            Logging.LogUsefulException(e);
            Close();
        }
    }

    private void HandshakeReceive3Callback(int recv_size)
    {
        try
        {
            var bytesRead = _connection.Receive(_connetionRecvBuffer, recv_size, 0);

            if (bytesRead > 0)
            {
                Array.Resize(ref _remoteHeaderSendBuffer, _remoteHeaderSendBuffer.Length + bytesRead);
                Array.Copy(_connetionRecvBuffer, 0, _remoteHeaderSendBuffer, _remoteHeaderSendBuffer.Length - bytesRead, bytesRead);

                if (command == 3)
                {
                    RspSocks5UDPHeader(bytesRead);
                }
                else
                {
                    //RspSocks5TCPHeader();
                    local_sendback_protocol = "socks5";
                    Connect();
                }
            }
            else
            {
                Console.WriteLine("failed to recv data in HandshakeReceive3Callback");
                Close();
            }
        }
        catch (Exception e)
        {
            Logging.LogUsefulException(e);
            Close();
        }
    }

    private void RspSocks5UDPHeader(int bytesRead)
    {
        var ipv6 = _connection.AddressFamily == AddressFamily.InterNetworkV6;
        var udpPort = 0;
        if (bytesRead >= 3 + 6)
        {
            ipv6 = _remoteHeaderSendBuffer[0] == 4;
            if (!ipv6)
                udpPort = _remoteHeaderSendBuffer[5] * 0x100 + _remoteHeaderSendBuffer[6];
            else
                udpPort = _remoteHeaderSendBuffer[17] * 0x100 + _remoteHeaderSendBuffer[18];
        }
        if (!ipv6)
        {
            _remoteHeaderSendBuffer = new byte[1 + 4 + 2];
            _remoteHeaderSendBuffer[0] = 0x8 | 1;
            _remoteHeaderSendBuffer[5] = (byte)(udpPort / 0x100);
            _remoteHeaderSendBuffer[6] = (byte)(udpPort % 0x100);
        }
        else
        {
            _remoteHeaderSendBuffer = new byte[1 + 16 + 2];
            _remoteHeaderSendBuffer[0] = 0x8 | 4;
            _remoteHeaderSendBuffer[17] = (byte)(udpPort / 0x100);
            _remoteHeaderSendBuffer[18] = (byte)(udpPort % 0x100);
        }

        var port = 0;
        var ip = ipv6 ? IPAddress.IPv6Any : IPAddress.Any;
        _connectionUDP = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        for (; port < 65536; ++port)
        {
            try
            {
                _connectionUDP.Bind(new IPEndPoint(ip, port));
                break;
            }
            catch (Exception)
            {
                //
            }
        }
        port = ((IPEndPoint)_connectionUDP.LocalEndPoint).Port;
        if (!ipv6)
        {
            byte[] response = { 5, 0, 0, 1,
                0, 0, 0, 0,
                (byte)(port / 0x100), (byte)(port % 0x100) };
            var ip_bytes = ((IPEndPoint)_connection.LocalEndPoint).Address.GetAddressBytes();
            Array.Copy(ip_bytes, 0, response, 4, 4);
            _connection.Send(response);
            Connect();
        }
        else
        {
            byte[] response = { 5, 0, 0, 4,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                (byte)(port / 0x100), (byte)(port % 0x100) };
            var ip_bytes = ((IPEndPoint)_connection.LocalEndPoint).Address.GetAddressBytes();
            Array.Copy(ip_bytes, 0, response, 4, 16);
            _connection.Send(response);
            Connect();
        }
    }

    private void RspSocks5TCPHeader()
    {
        if (_connection.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] response = { 5, 0, 0, 1,
                0, 0, 0, 0,
                0, 0 };
            _connection.Send(response);
        }
        else
        {
            byte[] response = { 5, 0, 0, 4,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0 };
            _connection.Send(response);
        }
    }

    private void RspHttpHandshakeReceive()
    {
        command = 1; // Set TCP connect command
        httpProxyState ??= new HttpPraser();
        if (Util.Utils.isMatchSubNet(((IPEndPoint)_connection.RemoteEndPoint).Address, "127.0.0.0/8"))
        {
            httpProxyState.httpAuthUser = "";
            httpProxyState.httpAuthPass = "";
        }
        else
        {
            httpProxyState.httpAuthUser = _config.authUser;
            httpProxyState.httpAuthPass = _config.authPass;
        }
        for (var i = 1; ; ++i)
        {
            var err = httpProxyState.HandshakeReceive(_firstPacket, _firstPacketLength, ref _remoteHeaderSendBuffer);
            if (err == 1)
            {
                if (HttpHandshakeRecv())
                    break;
            }
            else if (err == 2)
            {
                var dataSend = httpProxyState.Http407();
                var httpData = Encoding.UTF8.GetBytes(dataSend);
                _connection.Send(httpData);
                if (HttpHandshakeRecv())
                    break;
            }
            else if (err is 3 or 4)
            {
                Connect();
                break;
            }
            else if (err == 0)
            {
                local_sendback_protocol = "http";
                Connect();
                break;
            }
            else if (err == 500)
            {
                var dataSend = httpProxyState.Http500();
                var httpData = Encoding.UTF8.GetBytes(dataSend);
                _connection.Send(httpData);
                if (HttpHandshakeRecv())
                    break;
            }
            if (i == 3)
            {
                Close();
                break;
            }
        }
    }

    private bool HttpHandshakeRecv()
    {
        try
        {
            var bytesRead = _connection.Receive(_connetionRecvBuffer, _firstPacket.Length, 0);
            if (bytesRead > 0)
            {
                Array.Copy(_connetionRecvBuffer, _firstPacket, bytesRead);
                _firstPacketLength = bytesRead;
                return false;
            }
            Console.WriteLine("failed to recv data in HttpHandshakeRecv");
            Close();
        }
        catch (Exception e)
        {
            Logging.LogUsefulException(e);
            Close();
        }
        return true;
    }

    private void Connect()
    {
        Server GetCurrentServer(int localPort, ServerSelectStrategy.FilterFunc filter, string targetURI, bool cfgRandom, bool usingRandom, bool forceRandom)
        {
            return _config.GetCurrentServer(localPort, filter, targetURI, cfgRandom, usingRandom, forceRandom);
        }

        void KeepCurrentServer(int localPort, string targetURI, string id)
        {
            _config.KeepCurrentServer(localPort, targetURI, id);
        }

        var local_port = ((IPEndPoint)_connection.LocalEndPoint).Port;
        var handler = new Handler
        {
            getCurrentServer = GetCurrentServer,
            keepCurrentServer = KeepCurrentServer,
            connection = new ProxySocketTunLocal(_connection),
            connectionUDP = _connectionUDP,
            cfg =
            {
                reconnectTimesRemain = _config.reconnectTimes,
                random = _config.random,
                forceRandom = _config.random
            }
        };

        handler.setServerTransferTotal(_transfer);
        if (_config.proxyEnable)
        {
            handler.cfg.proxyType = _config.proxyType;
            handler.cfg.socks5RemoteHost = _config.proxyHost;
            handler.cfg.socks5RemotePort = _config.proxyPort;
            handler.cfg.socks5RemoteUsername = _config.proxyAuthUser;
            handler.cfg.socks5RemotePassword = _config.proxyAuthPass;
            handler.cfg.proxyUserAgent = _config.proxyUserAgent;
        }
        handler.cfg.TTL = _config.TTL;
        handler.cfg.connect_timeout = _config.connectTimeout;
        handler.cfg.autoSwitchOff = _config.autoBan;
        if (!string.IsNullOrEmpty(_config.localDnsServer))
        {
            handler.cfg.local_dns_servers = _config.localDnsServer;
        }
        if (!string.IsNullOrEmpty(_config.dnsServer))
        {
            handler.cfg.dns_servers = _config.dnsServer;
        }
        if (_config.GetPortMapCache().ContainsKey(local_port))
        {
            var cfg = _config.GetPortMapCache()[local_port];
            if (cfg.server == null || cfg.id == cfg.server.id)
            {
                if (cfg.server != null)
                {
                    handler.select_server = delegate (Server server, Server selServer) { return server.id == cfg.server.id; };
                }
                else if (!string.IsNullOrEmpty(cfg.id))
                {
                    handler.select_server = delegate (Server server, Server selServer) { return server.group == cfg.id; };
                }
                if (cfg.type == PortMapType.Forward) // tunnel
                {
                    var addr = Encoding.UTF8.GetBytes(cfg.server_addr);
                    var newFirstPacket = new byte[_firstPacketLength + addr.Length + 4];
                    newFirstPacket[0] = 3;
                    newFirstPacket[1] = (byte)addr.Length;
                    Array.Copy(addr, 0, newFirstPacket, 2, addr.Length);
                    newFirstPacket[addr.Length + 2] = (byte)(cfg.server_port / 256);
                    newFirstPacket[addr.Length + 3] = (byte)(cfg.server_port % 256);
                    Array.Copy(_firstPacket, 0, newFirstPacket, addr.Length + 4, _firstPacketLength);
                    _remoteHeaderSendBuffer = newFirstPacket;
                    handler.Start(_remoteHeaderSendBuffer, _remoteHeaderSendBuffer.Length, null);
                }
                else if (_connectionUDP == null && cfg.type == PortMapType.RuleProxy
                                                && new Socks5Forwarder(_config, _IPRange).Handle(_remoteHeaderSendBuffer, _remoteHeaderSendBuffer.Length, _connection, local_sendback_protocol))
                {
                }
                else
                {
                    handler.Start(_remoteHeaderSendBuffer, _remoteHeaderSendBuffer.Length, "socks5");
                }
                Dispose();
                return;
            }
        }
        else
        {
            if (_connectionUDP == null && new Socks5Forwarder(_config, _IPRange).Handle(_remoteHeaderSendBuffer, _remoteHeaderSendBuffer.Length, _connection, local_sendback_protocol))
            {
            }
            else
            {
                handler.Start(_remoteHeaderSendBuffer, _remoteHeaderSendBuffer.Length, local_sendback_protocol);
            }
            Dispose();
            return;
        }
        Dispose();
        Close();
    }

    private void Dispose()
    {
        _transfer = null;
        _IPRange = null;

        _firstPacket = null;
        _connection = null;
        _connectionUDP = null;

        _connetionRecvBuffer = null;
        _remoteHeaderSendBuffer = null;

        httpProxyState = null;
    }
}