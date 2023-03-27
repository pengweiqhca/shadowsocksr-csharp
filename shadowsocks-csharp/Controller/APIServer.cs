using Shadowsocks.Model;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shadowsocks.Controller;

internal class APIServer : Listener.Service
{
    private readonly ShadowsocksController _controller;
    private readonly Configuration _config;

    public const int RecvSize = 16384;
    private readonly byte[] connetionRecvBuffer = new byte[RecvSize];
    private string connection_request;
    private Socket _local;

    public APIServer(ShadowsocksController controller, Configuration config)
    {
        _controller = controller;
        _config = config;
    }

    public bool Handle(byte[] firstPacket, int length, Socket socket)
    {
        try
        {
            var request = Encoding.UTF8.GetString(firstPacket, 0, length);
            var lines = request.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            bool hostMatch = false, pathMatch = false;
            var req = "";
            foreach (var line in lines)
            {
                var kv = line.Split(new[] { ':' }, 2);
                if (kv.Length == 2)
                {
                    if (kv[0] == "Host")
                    {
                        if (kv[1].Trim() == ((IPEndPoint)socket.LocalEndPoint).ToString())
                        {
                            hostMatch = true;
                        }
                    }
                }
                else if (kv.Length == 1)
                {
                    if (line.IndexOf($"auth={_config.localAuthPassword}") > 0)
                    {
                        if (line.IndexOf(" /api?") > 0)
                        {
                            req = line[(line.IndexOf("api?") + 4)..];
                            if (line.IndexOf("GET ") == 0 || line.IndexOf("POST ") == 0)
                            {
                                pathMatch = true;
                                req = req[..req.IndexOf(" ")];
                            }
                        }
                    }
                }
            }
            if (hostMatch && pathMatch)
            {
                _local = socket;
                if (CheckEnd(request))
                {
                    process(request);
                }
                else
                {
                    connection_request = request;
                    socket.BeginReceive(connetionRecvBuffer, 0, RecvSize, 0,
                        HttpHandshakeRecv, null);
                }
                return true;
            }
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private bool CheckEnd(string request)
    {
        var newline_pos = request.IndexOf("\r\n\r\n");
        if (request.StartsWith("POST "))
        {
            if (newline_pos > 0)
            {
                var head = request[..newline_pos];
                var tail = request[(newline_pos + 4)..];
                var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Content-Length: "))
                    {
                        try
                        {
                            var length = int.Parse(line["Content-Length: ".Length..]);
                            if (length <= tail.Length)
                                return true;
                        }
                        catch (FormatException)
                        {
                            break;
                        }
                    }
                }
                return false;
            }
        }
        else
        {
            if (newline_pos + 4 == request.Length)
            {
                return true;
            }
        }
        return false;
    }

    private void HttpHandshakeRecv(IAsyncResult ar)
    {
        try
        {
            var bytesRead = _local.EndReceive(ar);
            if (bytesRead > 0)
            {
                var request = Encoding.UTF8.GetString(connetionRecvBuffer, 0, bytesRead);
                connection_request += request;
                if (CheckEnd(connection_request))
                {
                    process(connection_request);
                }
                else
                {
                    _local.BeginReceive(connetionRecvBuffer, 0, RecvSize, 0,
                        HttpHandshakeRecv, null);
                }
            }
            else
            {
                Console.WriteLine("APIServer: failed to recv data in HttpHandshakeRecv");
                _local.Shutdown(SocketShutdown.Both);
                _local.Close();
            }
        }
        catch (Exception e)
        {
            Logging.LogUsefulException(e);
            try
            {
                _local.Shutdown(SocketShutdown.Both);
                _local.Close();
            }
            catch
            { }
        }
    }

    protected string process(string request)
    {
        var req = request[..request.IndexOf("\r\n")];
        req = req[(req.IndexOf("api?") + 4)..];
        req = req[..req.IndexOf(" ")];

        var get_params = req.Split('&');
        var params_dict = new Dictionary<string, string>();
        foreach (var p in get_params)
        {
            if (p.IndexOf('=') > 0)
            {
                var index = p.IndexOf('=');
                var key = p[..index];
                var val = p[(index + 1)..];
                params_dict[key] = val;
            }
        }
        if (request.IndexOf("POST ") == 0)
        {
            var post_params = request[(request.IndexOf("\r\n\r\n") + 4)..];
            get_params = post_params.Split('&');
            foreach (var p in get_params)
            {
                if (p.IndexOf('=') > 0)
                {
                    var index = p.IndexOf('=');
                    var key = p[..index];
                    var val = p[(index + 1)..];
                    params_dict[key] = Util.Utils.urlDecode(val);
                }
            }
        }
        if (params_dict.ContainsKey("token") && params_dict.ContainsKey("app")
                                             && _config.token.ContainsKey(params_dict["app"]) && _config.token[params_dict["app"]] == params_dict["token"])
        {
            if (params_dict.ContainsKey("action"))
            {
                if (params_dict["action"] == "statistics")
                {
                    var config = _config;
                    var _ServerSpeedLogList = new ServerSpeedLogShow[config.configs.Count];
                    var servers = new Dictionary<string, object>();
                    for (var i = 0; i < config.configs.Count; ++i)
                    {
                        _ServerSpeedLogList[i] = config.configs[i].ServerSpeedLog().Translate();
                        servers[config.configs[i].id] = _ServerSpeedLogList[i];
                    }
                    var content = SimpleJson.SimpleJson.SerializeObject(servers);

                    var text = $@"HTTP/1.1 200 OK
Server: ShadowsocksR
Content-Type: text/plain
Content-Length: {Encoding.UTF8.GetBytes(content).Length}
Connection: Close

{content}";
                    var response = Encoding.UTF8.GetBytes(text);
                    _local.BeginSend(response, 0, response.Length, 0, SendCallback, _local);
                    return "";
                }
                if (params_dict["action"] == "config")
                {
                    if (params_dict.TryGetValue("config", out var value))
                    {
                        var content = "";
                        var ret_code = "200 OK";
                        if (!_controller.SaveServersConfig(value))
                        {
                            ret_code = "403 Forbid";
                        }
                        var text = $@"HTTP/1.1 {ret_code}
Server: ShadowsocksR
Content-Type: text/plain
Content-Length: {Encoding.UTF8.GetBytes(content).Length}
Connection: Close

{content}";
                        var response = Encoding.UTF8.GetBytes(text);
                        _local.BeginSend(response, 0, response.Length, 0, SendCallback, _local);
                        return "";
                    }
                    else
                    {
                        var token = _config.token;
                        _config.token = new Dictionary<string, string>();
                        var content = SimpleJson.SimpleJson.SerializeObject(_config);
                        _config.token = token;

                        var text = $@"HTTP/1.1 200 OK
Server: ShadowsocksR
Content-Type: text/plain
Content-Length: {Encoding.UTF8.GetBytes(content).Length}
Connection: Close

{content}";
                        var response = Encoding.UTF8.GetBytes(text);
                        _local.BeginSend(response, 0, response.Length, 0, SendCallback, _local);
                        return "";
                    }
                }
            }
        }
        {
            var response = Encoding.UTF8.GetBytes("");
            _local.BeginSend(response, 0, response.Length, 0, SendCallback, _local);
        }
        return "";
    }

    private void SendCallback(IAsyncResult ar)
    {
        var conn = (Socket)ar.AsyncState;
        try
        {
            conn.Shutdown(SocketShutdown.Both);
            conn.Close();
        }
        catch
        { }
    }
}