﻿using OpenDNS;
using Shadowsocks.Controller;
using Shadowsocks.Model;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Shadowsocks.Util;

public class Utils
{
    private static readonly LRUCache<string, IPAddress> dnsBuffer = new();

    public static LRUCache<string, IPAddress> DnsBuffer
    {
        get => dnsBuffer;
    }

    public static LRUCache<string, IPAddress> LocalDnsBuffer
    {
        get => dnsBuffer;
    }

    private static readonly Process current_process = Process.GetCurrentProcess();

    public static void ReleaseMemory()
    {
        // release any unused pages
        // making the numbers look good in task manager
        // this is totally nonsense in programming
        // but good for those users who care
        // making them happier with their everyday life
        // which is part of user experience
        GC.Collect(GC.MaxGeneration);
        GC.WaitForPendingFinalizers();

        SetProcessWorkingSetSize(current_process.Handle, UIntPtr.MaxValue, UIntPtr.MaxValue);
    }

    public static string UnGzip(byte[] buf)
    {
        var buffer = new byte[1024];
        int n;
        using var sb = new MemoryStream();
        using (var input = new GZipStream(new MemoryStream(buf),
                   CompressionMode.Decompress, false))
        {
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                sb.Write(buffer, 0, n);
            }
        }
        return System.Text.Encoding.UTF8.GetString(sb.ToArray());
    }

    public static void RandBytes(byte[] buf, int length)
    {
        var temp = new byte[length];
        RandomNumberGenerator.Create().GetBytes(temp);
        temp.CopyTo(buf, 0);
    }

    public static uint RandUInt32()
    {
        var temp = new byte[4];
        RandomNumberGenerator.Create().GetBytes(temp);
        return BitConverter.ToUInt32(temp, 0);
    }

    public static void Shuffle<T>(IList<T> list, Random rng)
    {
        var n = list.Count;
        while (n > 1)
        {
            var k = rng.Next(n);
            n--;
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    public static bool BitCompare(byte[] target, int target_offset, byte[] m, int m_offset, int targetLength)
    {
        for (var i = 0; i < targetLength; ++i)
        {
            if (target[target_offset + i] != m[m_offset + i])
                return false;
        }
        return true;
    }

    public static int FindStr(byte[] target, int targetLength, byte[] m)
    {
        if (m.Length > 0 && targetLength >= m.Length)
        {
            for (var i = 0; i <= targetLength - m.Length; ++i)
            {
                if (target[i] == m[0])
                {
                    var j = 1;
                    for (; j < m.Length; ++j)
                    {
                        if (target[i + j] != m[j])
                            break;
                    }
                    if (j >= m.Length)
                    {
                        return i;
                    }
                }
            }
        }
        return -1;
    }

    public static bool isMatchSubNet(IPAddress ip, IPAddress net, int netmask)
    {
        var addr = ip.GetAddressBytes();
        var net_addr = net.GetAddressBytes();
        int i = 8, index = 0;
        for (; i < netmask; i += 8, index += 1)
        {
            if (addr[index] != net_addr[index])
                return false;
        }
        if (addr[index] >> i - netmask != net_addr[index] >> i - netmask)
            return false;
        return true;
    }

    public static bool isMatchSubNet(IPAddress ip, string netmask)
    {
        var mask = netmask.Split('/');
        var netmask_ip = IPAddress.Parse(mask[0]);
        if (ip.AddressFamily == netmask_ip.AddressFamily)
        {
            try
            {
                return isMatchSubNet(ip, netmask_ip, Convert.ToInt16(mask[1]));
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    public static bool isLocal(IPAddress ip)
    {
        var addr = ip.GetAddressBytes();
        if (addr.Length == 4)
        {
            var netmasks = new[]
            {
                "127.0.0.0/8", "169.254.0.0/16",
            };
            foreach (var netmask in netmasks)
            {
                if (isMatchSubNet(ip, netmask))
                    return true;
            }
            return false;
        }
        if (addr.Length == 16)
        {
            var netmasks = new[]
            {
                "::1/128",
            };
            foreach (var netmask in netmasks)
            {
                if (isMatchSubNet(ip, netmask))
                    return true;
            }
            return false;
        }
        return true;
    }

    public static bool isLocal(Socket socket) => isLocal(((IPEndPoint)socket.RemoteEndPoint).Address);

    public static bool isLAN(IPAddress ip)
    {
        var addr = ip.GetAddressBytes();
        if (addr.Length == 4)
        {
            if (ip.Equals(new IPAddress(0)))
                return false;
            var netmasks = new[]
            {
                "0.0.0.0/8", "10.0.0.0/8",
                //"100.64.0.0/10", //部分地区运营商貌似在使用这个，这个可能不安全
                "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
                //"192.0.0.0/24",
                //"192.0.2.0/24",
                "192.168.0.0/16",
                //"198.18.0.0/15",
                //"198.51.100.0/24",
                //"203.0.113.0/24",
            };
            foreach (var netmask in netmasks)
            {
                if (isMatchSubNet(ip, netmask))
                    return true;
            }
            return false;
        }
        if (addr.Length == 16)
        {
            var netmasks = new[]
            {
                "::1/128", "fc00::/7", "fe80::/10",
            };
            foreach (var netmask in netmasks)
            {
                if (isMatchSubNet(ip, netmask))
                    return true;
            }
            return false;
        }
        return true;
    }

    public static bool isLAN(Socket socket) => isLAN(((IPEndPoint)socket.RemoteEndPoint).Address);

    public static string GetTimestamp(DateTime value) => value.ToString("yyyyMMddHHmmssffff");

    public static string urlDecode(string str)
    {
        var ret = "";
        for (var i = 0; i < str.Length; ++i)
        {
            if (str[i] == '%' && i < str.Length - 2)
            {
                var s = str.Substring(i + 1, 2).ToLower();
                var val = 0;
                var c1 = s[0];
                var c2 = s[1];
                val += c1 < 'a' ? c1 - '0' : 10 + (c1 - 'a');
                val *= 16;
                val += c2 < 'a' ? c2 - '0' : 10 + (c2 - 'a');

                ret += (char)val;
                i += 2;
            }
            else if (str[i] == '+')
            {
                ret += ' ';
            }
            else
            {
                ret += str[i];
            }
        }
        return ret;
    }

    public static void SetArrayMinSize<T>(ref T[] array, int size)
    {
        if (size > array.Length)
        {
            Array.Resize(ref array, size);
        }
    }

    public static void SetArrayMinSize2<T>(ref T[] array, int size)
    {
        if (size > array.Length)
        {
            Array.Resize(ref array, size * 2);
        }
    }

    public static IPAddress QueryDns(string host, string dns_servers, bool IPv6_first = false)
    {
        var ret_ipAddress = _QueryDns(host, dns_servers, IPv6_first);

        Logging.Info($"DNS query {host} answer {ret_ipAddress}");

        return ret_ipAddress;
    }

    private static IPAddress _QueryDns(string host, string dns_servers, bool IPv6_first = false)
    {
        IPAddress ret_ipAddress = null;

        if (!string.IsNullOrEmpty(dns_servers))
        {
            var types = IPv6_first
                ? new[]
                {
                    Types.AAAA, Types.A
                }
                : new[]
                {
                    Types.A, Types.AAAA
                };

            var _dns_server = dns_servers.Split(',');
            var dns_server = new List<IPEndPoint>();

            foreach (var server_str in _dns_server)
            {
                var server = server_str.Trim(' ');
                var index = server.IndexOf(':');
                string ip = null;
                string port = null;
                if (index >= 0)
                {
                    if (server.StartsWith("["))
                    {
                        var ipv6_end = server.IndexOf(']', 1);
                        if (ipv6_end >= 0)
                        {
                            ip = server[1..ipv6_end];

                            index = server.IndexOf(':', ipv6_end);
                            if (index == ipv6_end + 1)
                            {
                                port = server[(index + 1)..];
                            }
                        }
                    }
                    else
                    {
                        ip = server[..index];
                        port = server[(index + 1)..];
                    }
                }
                else
                {
                    index = server.IndexOf(' ');
                    if (index >= 0)
                    {
                        ip = server[..index];
                        port = server[(index + 1)..];
                    }
                    else
                    {
                        ip = server;
                    }
                }
                if (ip != null && IPAddress.TryParse(ip, out var ipAddress))
                {
                    var i_port = 53;
                    if (port != null)
                        int.TryParse(port, out i_port);
                    dns_server.Add(new IPEndPoint(ipAddress, i_port));
                    //dns_server.Add(port == null ? ip : ip + " " + port);
                }
            }
            foreach (var type in types)
            {
                var dns = new DnsQuery(host, type)
                {
                    RecursionDesired = true
                };
                foreach (var server in dns_server)
                {
                    dns.Servers.Add(server);
                }
                if (dns.Send())
                {
                    var count = dns.Response.Answers.Count;
                    if (count > 0)
                    {
                        for (var i = 0; i < count; ++i)
                        {
                            if (((ResourceRecord)dns.Response.Answers[i]).Type != type)
                                continue;
                            return ((Address)dns.Response.Answers[i]).IP;
                        }
                    }
                }
            }
        }

        try
        {
            using var cts = new CancellationTokenSource(10000);

            var ipHostEntry = Dns.GetHostEntryAsync(host, cts.Token).GetAwaiter().GetResult();

            foreach (var ad in ipHostEntry.AddressList)
            {
                if (ad.AddressFamily == AddressFamily.InterNetwork) return ad;
            }
            foreach (var ad in ipHostEntry.AddressList)
            {
                return ad;
            }
        }
        catch
        {

        }
        return ret_ipAddress;
    }

    public static string GetExecutablePath() =>
        Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;

    public static string GetStartupPathHash() =>
        $"ShadowsocksR_{Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(Application.StartupPath))[..6])}";

    public static int RunAsAdmin(string Arguments)
    {
        Process process = null;
        var processInfo = new ProcessStartInfo
        {
            Verb = "runas",
            FileName = GetExecutablePath(),
            Arguments = Arguments
        };
        try
        {
            process = Process.Start(processInfo);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1;
        }
        process?.WaitForExit();
        var ret = process.ExitCode;
        process.Close();
        return ret;
    }

    public static int GetDpiMul()
    {
        int dpi;
        using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
        {
            dpi = (int)graphics.DpiX;
        }
        return (dpi * 4 + 48) / 96;
    }

    public enum DeviceCap
    {
        DESKTOPVERTRES = 117,
        DESKTOPHORZRES = 118,
    }

    public static Point GetScreenPhysicalSize()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        var desktop = g.GetHdc();
        var PhysicalScreenWidth = GetDeviceCaps(desktop, (int)DeviceCap.DESKTOPHORZRES);
        var PhysicalScreenHeight = GetDeviceCaps(desktop, (int)DeviceCap.DESKTOPVERTRES);

        return new Point(PhysicalScreenWidth, PhysicalScreenHeight);
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process,
        UIntPtr minimumWorkingSetSize, UIntPtr maximumWorkingSetSize);
}
