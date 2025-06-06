﻿using Shadowsocks.Model;
using Shadowsocks.Properties;
using Shadowsocks.Util;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shadowsocks.Controller;

internal class PACServer : Listener.Service
{
    public static string PAC_FILE = "pac.txt";

    public static string USER_RULE_FILE = "user-rule.txt";

    public static string USER_ABP_FILE = "abp.txt";

    private FileSystemWatcher PACFileWatcher;
    private Configuration _config;

    public Action PACFileChanged;

    public PACServer() => WatchPacFile();

    public void UpdateConfiguration(Configuration config)
    {
        _config = config;
    }

    public bool Handle(byte[] firstPacket, int length, Socket socket)
    {
        try
        {
            var request = Encoding.UTF8.GetString(firstPacket, 0, length);
            var lines = request.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            bool hostMatch = false, pathMatch = false;
            var socksType = 0;
            string proxy = null;
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
                    else if (kv[0] == "User-Agent")
                    {
                        // we need to drop connections when changing servers
                        /* if (kv[1].IndexOf("Chrome") >= 0)
                        {
                            useSocks = true;
                        } */
                    }
                }
                else if (kv.Length == 1)
                {
                    if (!Utils.isLocal(socket) || line.IndexOf($"auth={_config.localAuthPassword}") > 0)
                    {
                        if (line.IndexOf(" /pac?") > 0 && line.IndexOf("GET") == 0)
                        {
                            var url = line[(line.IndexOf(" ") + 1)..];
                            url = url[..url.IndexOf(" ")];
                            pathMatch = true;
                            var port_pos = url.IndexOf("port=");
                            if (port_pos > 0)
                            {
                                var port = url[(port_pos + 5)..];
                                if (port.Contains('&'))
                                {
                                    port = port[..port.IndexOf("&")];
                                }

                                var ip_pos = url.IndexOf("ip=");
                                if (ip_pos > 0)
                                {
                                    proxy = url[(ip_pos + 3)..];
                                    if (proxy.Contains('&'))
                                    {
                                        proxy = proxy[..proxy.IndexOf("&")];
                                    }
                                    proxy += $":{port};";
                                }
                                else
                                {
                                    proxy = $"127.0.0.1:{port};";
                                }
                            }

                            if (url.IndexOf("type=socks4") > 0 || url.IndexOf("type=s4") > 0)
                            {
                                socksType = 4;
                            }
                            if (url.IndexOf("type=socks5") > 0 || url.IndexOf("type=s5") > 0)
                            {
                                socksType = 5;
                            }
                        }
                    }
                }
            }
            if (hostMatch && pathMatch)
            {
                SendResponse(firstPacket, length, socket, socksType, proxy);
                return true;
            }
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }


    public string TouchPACFile()
    {
        if (File.Exists(PAC_FILE))
        {
            return PAC_FILE;
        }
        FileManager.UncompressFile(PAC_FILE, Resources.proxy_pac_txt);
        return PAC_FILE;
    }

    internal string TouchUserRuleFile()
    {
        if (File.Exists(USER_RULE_FILE))
        {
            return USER_RULE_FILE;
        }
        File.WriteAllText(USER_RULE_FILE, Resources.user_rule);
        return USER_RULE_FILE;
    }

    private string GetPACContent()
    {
        if (File.Exists(PAC_FILE))
        {
            return File.ReadAllText(PAC_FILE, Encoding.UTF8);
        }
        return Utils.UnGzip(Resources.proxy_pac_txt);
    }

    public void SendResponse(byte[] firstPacket, int length, Socket socket, int socksType, string setProxy)
    {
        try
        {
            var pac = GetPACContent();

            var localEndPoint = (IPEndPoint)socket.LocalEndPoint;

            var proxy =
                setProxy == null ? GetPACAddress(firstPacket, length, localEndPoint, socksType) :
                socksType == 5 ? $"SOCKS5 {setProxy}" :
                socksType == 4 ? $"SOCKS {setProxy}" :
                $"PROXY {setProxy}";

            if (_config is { pacDirectGoProxy: true, proxyEnable: true })
            {
                pac = _config.proxyType switch
                {
                    0 => pac.Replace("__DIRECT__", $"SOCKS5 {_config.proxyHost}:{_config.proxyPort};DIRECT;"),
                    1 => pac.Replace("__DIRECT__", $"PROXY {_config.proxyHost}:{_config.proxyPort};DIRECT;"),
                    _ => pac
                };
            }
            else
                pac = pac.Replace("__DIRECT__", "DIRECT;");
            pac = pac.Replace("__PROXY__", $"{proxy}DIRECT;");

            var text = $@"HTTP/1.1 200 OK
Server: ShadowsocksR
Content-Type: application/x-ns-proxy-autoconfig
Content-Length: {Encoding.UTF8.GetBytes(pac).Length}
Connection: Close

{pac}";
            var response = Encoding.UTF8.GetBytes(text);
            socket.BeginSend(response, 0, response.Length, 0, SendCallback, socket);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }
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

    private void WatchPacFile()
    {
        PACFileWatcher?.Dispose();
        PACFileWatcher = new FileSystemWatcher(Directory.GetCurrentDirectory())
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = PAC_FILE,
        };
        PACFileWatcher.Changed += Watcher_Changed;
        PACFileWatcher.Created += Watcher_Changed;
        PACFileWatcher.Deleted += Watcher_Changed;
        PACFileWatcher.Renamed += Watcher_Changed;
        PACFileWatcher.EnableRaisingEvents = true;
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e)
    {
        PACFileChanged?.Invoke();
    }

    private string GetPACAddress(byte[] requestBuf, int length, IPEndPoint localEndPoint, int socksType)
    {
        return socksType switch
        {
            //try
            //{
            //    string requestString = Encoding.UTF8.GetString(requestBuf);
            //    if (requestString.IndexOf("AppleWebKit") >= 0)
            //    {
            //        string address = "" + localEndPoint.Address + ":" + config.GetCurrentServer().local_port;
            //        proxy = "SOCKS5 " + address + "; SOCKS " + address + ";";
            //    }
            //}
            //catch (Exception e)
            //{
            //    Console.WriteLine(e);
            //}
            5 => $"SOCKS5 {localEndPoint.Address}:{_config.localPort};",
            4 => $"SOCKS {localEndPoint.Address}:{_config.localPort};",
            _ => $"PROXY {localEndPoint.Address}:{_config.localPort};"
        };
    }
}