using System.Net;

namespace Shadowsocks.Model
{
    class HostNode
    {
        public bool include_sub;
        public string addr;
        public Dictionary<string, HostNode> subnode;

        public HostNode()
        {
            include_sub = false;
            addr = "";
            subnode = new Dictionary<string, HostNode>();
        }

        public HostNode(bool sub, string addr)
        {
            include_sub = sub;
            this.addr = addr;
            subnode = null;
        }
    }

    public class HostMap
    {
        readonly Dictionary<string, HostNode> root = new();
        readonly IPSegment ips = new("remoteproxy");

        static HostMap instance = new();
        const string HOST_FILENAME = "user.rule";

        public static HostMap Instance() => instance;

        public void Clear(HostMap newInstance)
        {
            if (newInstance == null)
            {
                instance = new HostMap();
            }
            else
            {
                instance = newInstance;
            }
        }

        public void AddHost(string host, string addr)
        {
            if (IPAddress.TryParse(host, out _))
            {
                var addr_parts = addr.Split(new[] { ' ', '\t', }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (addr_parts.Length >= 2)
                {
                    ips.insert(new IPAddressCmp(host), new IPAddressCmp(addr_parts[0]), addr_parts[1]);
                    return;
                }
            }

            var parts = host.Split('.');
            var node = root;
            var include_sub = false;
            var end = 0;
            if (parts[0].Length == 0)
            {
                end = 1;
                include_sub = true;
            }
            for (var i = parts.Length - 1; i > end; --i)
            {
                if (!node.ContainsKey(parts[i]))
                {
                    node[parts[i]] = new HostNode();
                }
                if (node[parts[i]].subnode == null)
                {
                    node[parts[i]].subnode = new Dictionary<string, HostNode>();
                }
                node = node[parts[i]].subnode;
            }
            node[parts[end]] = new HostNode(include_sub, addr);
        }

        public bool GetHost(string host, out string addr)
        {
            var parts = host.Split('.');
            var node = root;
            addr = null;
            for (var i = parts.Length - 1; i >= 0; --i)
            {
                if (!node.ContainsKey(parts[i]))
                {
                    return false;
                }
                if (node[parts[i]].addr.Length > 0 || node[parts[i]].include_sub)
                {
                    addr = node[parts[i]].addr;
                    return true;
                }
                if (node.TryGetValue("*", out var value))
                {
                    addr = value.addr;
                    return true;
                }
                if (node[parts[i]].subnode == null)
                {
                    return false;
                }
                node = node[parts[i]].subnode;
            }
            return false;
        }

        public bool GetIP(IPAddress ip, out string addr)
        {
            var host = ip.ToString();
            addr = ips.Get(new IPAddressCmp(host)) as string;
            return addr != null;
        }

        public bool LoadHostFile()
        {
            var filename = HOST_FILENAME;
            var absFilePath = Path.Combine(Application.StartupPath, filename);
            if (File.Exists(absFilePath))
            {
                try
                {
                    using var stream = File.OpenText(absFilePath);
                    while (true)
                    {
                        var line = stream.ReadLine();
                        if (line == null)
                            break;
                        if (line.Length > 0 && line.StartsWith("#"))
                            continue;
                        var parts = line.Split(new[] { ' ', '\t', }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2)
                            continue;
                        AddHost(parts[0], parts[1]);
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }
    }
}
