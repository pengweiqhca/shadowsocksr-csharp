using Shadowsocks.Model;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Timers;

namespace Shadowsocks.Controller
{
    public class Listener
    {
        public interface Service
        {
            bool Handle(byte[] firstPacket, int length, Socket socket);
        }

        Configuration _config;
        bool _shareOverLAN;
        string _authUser;
        string _authPass;
        Socket _socket;
        Socket _socket_v6;
        bool _stop;
        readonly IList<Service> _services;
        protected System.Timers.Timer timer;
        protected object timerLock = new();

        public Listener(IList<Service> services)
        {
            _services = services;
            _stop = false;
        }

        public IList<Service> GetServices() => _services;

        private bool CheckIfPortInUse(int port)
        {
            try
            {
                var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
                var ipEndPoints = ipProperties.GetActiveTcpListeners();

                foreach (var endPoint in ipEndPoints)
                {
                    if (endPoint.Port == port)
                    {
                        return true;
                    }
                }
            }
            catch
            {

            }
            return false;
        }

        public bool isConfigChange(Configuration config)
        {
            try
            {
                if (_shareOverLAN != config.shareOverLan
                    || _authUser != config.authUser
                    || _authPass != config.authPass
                    || _socket == null
                    || ((IPEndPoint)_socket.LocalEndPoint).Port != config.localPort)
                {
                    return true;
                }
            }
            catch (Exception)
            { }
            return false;
        }

        public void Start(Configuration config, int port)
        {
            _config = config;
            _shareOverLAN = config.shareOverLan;
            _authUser = config.authUser;
            _authPass = config.authPass;
            _stop = false;

            var localPort = port == 0 ? _config.localPort : port;
            if (CheckIfPortInUse(localPort))
                throw new Exception(I18N.GetString("Port already in use"));

            try
            {
                // Create a TCP/IP socket.
                var ipv6 = true;
                //bool ipv6 = false;
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                if (ipv6)
                {
                    try
                    {
                        _socket_v6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                        //_socket_v6.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, false);
                        _socket_v6.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    }
                    catch
                    {
                        _socket_v6 = null;
                    }
                }
                IPEndPoint localEndPoint = null;
                IPEndPoint localEndPointV6 = null;
                localEndPoint = new IPEndPoint(IPAddress.Any, localPort);
                localEndPointV6 = new IPEndPoint(IPAddress.IPv6Any, localPort);

                // Bind the socket to the local endpoint and listen for incoming connections.
                if (_socket_v6 != null)
                {
                    _socket_v6.Bind(localEndPointV6);
                    _socket_v6.Listen(1024);
                }
                //try
                {
                    //throw new SocketException();
                    _socket.Bind(localEndPoint);
                    _socket.Listen(1024);
                }
                //catch (SocketException e)
                //{
                //    if (_socket_v6 == null)
                //    {
                //        throw e;
                //    }
                //    else
                //    {
                //        _socket.Close();
                //        _socket = _socket_v6;
                //        _socket_v6 = null;
                //    }
                //}

                // Start an asynchronous socket to listen for connections.
                Console.WriteLine($"ShadowsocksR started on port {localPort}");
                _socket.BeginAccept(
                    AcceptCallback,
                    _socket);
                _socket_v6?.BeginAccept(
                    AcceptCallback,
                    _socket_v6);
            }
            catch (SocketException e)
            {
                Logging.LogUsefulException(e);
                if (_socket != null)
                {
                    _socket.Close();
                    _socket = null;
                }
                if (_socket_v6 != null)
                {
                    _socket_v6.Close();
                    _socket_v6 = null;
                }
                throw;
            }
        }

        public void Stop()
        {
            ResetTimeout(0, null);
            _stop = true;
            if (_socket != null)
            {
                _socket.Close();
                _socket = null;
            }
            if (_socket_v6 != null)
            {
                _socket_v6.Close();
                _socket_v6 = null;
            }
        }

        private void ResetTimeout(double time, Socket socket)
        {
            if (time <= 0 && timer == null)
                return;

            lock (timerLock)
            {
                if (time <= 0)
                {
                    if (timer != null)
                    {
                        timer.Enabled = false;
                        timer.Elapsed -= (sender, e) => timer_Elapsed(sender, e, socket);
                        timer.Dispose();
                        timer = null;
                    }
                }
                else
                {
                    if (timer == null)
                    {
                        timer = new System.Timers.Timer(time * 1000.0);
                        timer.Elapsed += (sender, e) => timer_Elapsed(sender, e, socket);
                        timer.Start();
                    }
                    else
                    {
                        timer.Interval = time * 1000.0;
                        timer.Stop();
                        timer.Start();
                    }
                }
            }
        }

        private void timer_Elapsed(object sender, ElapsedEventArgs eventArgs, Socket socket)
        {
            if (timer == null)
            {
                return;
            }
            var listener = socket;
            try
            {
                listener.BeginAccept(
                    AcceptCallback,
                    listener);
                ResetTimeout(0, listener);
            }
            catch (ObjectDisposedException)
            {
                // do nothing
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                ResetTimeout(5, listener);
            }
        }


        public void AcceptCallback(IAsyncResult ar)
        {
            if (_stop) return;

            var listener = (Socket)ar.AsyncState;
            try
            {
                var conn = listener.EndAccept(ar);

                if (!_shareOverLAN && !Util.Utils.isLocal(conn))
                {
                    conn.Shutdown(SocketShutdown.Both);
                    conn.Close();
                }

                var local_port = ((IPEndPoint)conn.LocalEndPoint).Port;

                if ((_authUser ?? "").Length == 0 && !Util.Utils.isLAN(conn)
                    && !(_config.GetPortMapCache().ContainsKey(local_port)
                    || _config.GetPortMapCache()[local_port].type == PortMapType.Forward))
                {
                    conn.Shutdown(SocketShutdown.Both);
                    conn.Close();
                }
                else
                {
                    var buf = new byte[4096];
                    var state = new object[] {
                        conn,
                        buf
                    };

                    if (!_config.GetPortMapCache().ContainsKey(local_port) || _config.GetPortMapCache()[local_port].type != PortMapType.Forward)
                    {
                        conn.BeginReceive(buf, 0, buf.Length, 0,
                            ReceiveCallback, state);
                    }
                    else
                    {
                        foreach (var service in _services)
                        {
                            if (service.Handle(buf, 0, conn))
                            {
                                return;
                            }
                        }
                        // no service found for this
                        // shouldn't happen
                        conn.Shutdown(SocketShutdown.Both);
                        conn.Close();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                try
                {
                    listener.BeginAccept(
                        AcceptCallback,
                        listener);
                }
                catch (ObjectDisposedException)
                {
                    // do nothing
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    ResetTimeout(5, listener);
                }
            }
        }


        private void ReceiveCallback(IAsyncResult ar)
        {
            var state = (object[])ar.AsyncState;

            var conn = (Socket)state[0];
            var buf = (byte[])state[1];
            try
            {
                var bytesRead = conn.EndReceive(ar);
                foreach (var service in _services)
                {
                    if (service.Handle(buf, bytesRead, conn))
                    {
                        return;
                    }
                }
                // no service found for this
                // shouldn't happen
                conn.Shutdown(SocketShutdown.Both);
                conn.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                conn.Shutdown(SocketShutdown.Both);
                conn.Close();
            }
        }
    }
}
