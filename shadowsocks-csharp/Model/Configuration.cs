using Shadowsocks.Controller;
using Shadowsocks.Encryption;
using System.Text;

namespace Shadowsocks.Model
{
    public class UriVisitTime : IComparable
    {
        public DateTime visitTime;
        public string uri;
        public int index;

        public int CompareTo(object other)
        {
            if (other is not UriVisitTime time)
                throw new InvalidOperationException("CompareTo: Not a UriVisitTime");
            if (Equals(time))
                return 0;
            return visitTime.CompareTo(time.visitTime);
        }

    }

    public enum PortMapType : int
    {
        Forward = 0,
        ForceProxy,
        RuleProxy
    }

    public enum ProxyRuleMode : int
    {
        Disable = 0,
        BypassLan,
        BypassLanAndChina,
        BypassLanAndNotChina,
        UserCustom = 16,
    }

    [Serializable]
    public class PortMapConfig
    {
        public bool enable;
        public PortMapType type;
        public string id;
        public string server_addr;
        public int server_port;
        public string remarks;
    }

    public class PortMapConfigCache
    {
        public PortMapType type;
        public string id;
        public Server server;
        public string server_addr;
        public int server_port;
    }

    [Serializable]
    public class ServerSubscribe
    {
        private static readonly string DEFAULT_FEED_URL = "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/free/freenodeplain.txt";
        //private static string OLD_DEFAULT_FEED_URL = "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/free/freenode.txt";

        public string URL = DEFAULT_FEED_URL;
        public string Group;
        public ulong LastUpdateTime;
    }

    public class GlobalConfiguration
    {
        public static string config_password = "";
    }

    [Serializable()]
    class ConfigurationException : Exception
    {
        public ConfigurationException() : base() { }
        public ConfigurationException(string message) : base(message) { }
        public ConfigurationException(string message, Exception inner) : base(message, inner) { }
        protected ConfigurationException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context)
        { }
    }
    [Serializable()]
    class ConfigurationWarning : Exception
    {
        public ConfigurationWarning() : base() { }
        public ConfigurationWarning(string message) : base(message) { }
        public ConfigurationWarning(string message, Exception inner) : base(message, inner) { }
        protected ConfigurationWarning(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context)
        { }
    }

    [Serializable]
    public class Configuration
    {
        public List<Server> configs;
        public int index;
        public bool random;
        public int sysProxyMode;
        public bool shareOverLan;
        public int localPort;
        public string localAuthPassword;

        public string localDnsServer;
        public string dnsServer;
        public int reconnectTimes;
        public string balanceAlgorithm;
        public bool randomInGroup;
        public int TTL;
        public int connectTimeout;

        public int proxyRuleMode;

        public bool proxyEnable;
        public bool pacDirectGoProxy;
        public int proxyType;
        public string proxyHost;
        public int proxyPort;
        public string proxyAuthUser;
        public string proxyAuthPass;
        public string proxyUserAgent;

        public string authUser;
        public string authPass;

        public bool autoBan;
        public bool checkSwitchAutoCloseAll;
        public bool logEnable;
        public bool sameHostForSameTarget;

        public int keepVisitTime;

        public bool isHideTips;

        public bool nodeFeedAutoUpdate;
        public List<ServerSubscribe> serverSubscribes;

        public Dictionary<string, string> token = new();
        public Dictionary<string, PortMapConfig> portMap = new();

        private readonly Dictionary<int, ServerSelectStrategy> serverStrategyMap = new();
        private Dictionary<int, PortMapConfigCache> portMapCache = new();
        private readonly LRUCache<string, UriVisitTime> uricache = new(180);

        private static readonly string CONFIG_FILE = "gui-config.json";
        private static readonly string CONFIG_FILE_BACKUP = "gui-config.json.backup";

        public static void SetPassword(string password)
        {
            GlobalConfiguration.config_password = password;
        }

        public static bool SetPasswordTry(string old_password, string password)
        {
            if (old_password != GlobalConfiguration.config_password)
                return false;
            return true;
        }

        public bool KeepCurrentServer(int localPort, string targetAddr, string id)
        {
            if (sameHostForSameTarget && targetAddr != null)
            {
                lock (serverStrategyMap)
                {
                    if (!serverStrategyMap.ContainsKey(localPort))
                        serverStrategyMap[localPort] = new ServerSelectStrategy();
                    var serverStrategy = serverStrategyMap[localPort];

                    if (uricache.ContainsKey(targetAddr))
                    {
                        var visit = uricache.Get(targetAddr);
                        var index = -1;
                        for (var i = 0; i < configs.Count; ++i)
                        {
                            if (configs[i].id == id)
                            {
                                index = i;
                                break;
                            }
                        }
                        if (index >= 0 && visit.index == index && configs[index].enable)
                        {
                            uricache.Del(targetAddr);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public Server GetCurrentServer(int localPort, ServerSelectStrategy.FilterFunc filter, string targetAddr = null, bool cfgRandom = false, bool usingRandom = false, bool forceRandom = false)
        {
            lock (serverStrategyMap)
            {
                if (!serverStrategyMap.ContainsKey(localPort))
                    serverStrategyMap[localPort] = new ServerSelectStrategy();
                var serverStrategy = serverStrategyMap[localPort];

                uricache.SetTimeout(keepVisitTime);
                uricache.Sweep();
                if (sameHostForSameTarget && !forceRandom && targetAddr != null && uricache.ContainsKey(targetAddr))
                {
                    var visit = uricache.Get(targetAddr);
                    if (visit.index < configs.Count && configs[visit.index].enable && configs[visit.index].ServerSpeedLog().ErrorContinurousTimes == 0)
                    {
                        uricache.Del(targetAddr);
                        return configs[visit.index];
                    }
                }
                if (forceRandom)
                {
                    int index;
                    if (filter == null && randomInGroup)
                    {
                        index = serverStrategy.Select(configs, this.index, balanceAlgorithm, delegate (Server server, Server selServer)
                        {
                            if (selServer != null)
                                return selServer.group == server.group;
                            return false;
                        }, true);
                    }
                    else
                    {
                        index = serverStrategy.Select(configs, this.index, balanceAlgorithm, filter, true);
                    }
                    if (index == -1) return GetErrorServer();
                    return configs[index];
                }
                if (usingRandom && cfgRandom)
                {
                    int index;
                    if (filter == null && randomInGroup)
                    {
                        index = serverStrategy.Select(configs, this.index, balanceAlgorithm, delegate (Server server, Server selServer)
                        {
                            if (selServer != null)
                                return selServer.group == server.group;
                            return false;
                        });
                    }
                    else
                    {
                        index = serverStrategy.Select(configs, this.index, balanceAlgorithm, filter);
                    }
                    if (index == -1) return GetErrorServer();
                    if (targetAddr != null)
                    {
                        var visit = new UriVisitTime
                        {
                            uri = targetAddr,
                            index = index,
                            visitTime = DateTime.Now
                        };
                        uricache.Set(targetAddr, visit);
                    }
                    return configs[index];
                }
                if (index >= 0 && index < configs.Count)
                {
                    var selIndex = index;
                    if (usingRandom)
                    {
                        for (var i = 0; i < configs.Count; ++i)
                        {
                            if (configs[selIndex].isEnable())
                            {
                                break;
                            }
                            selIndex = (selIndex + 1) % configs.Count;
                        }
                    }

                    if (targetAddr != null)
                    {
                        var visit = new UriVisitTime
                        {
                            uri = targetAddr,
                            index = selIndex,
                            visitTime = DateTime.Now
                        };
                        uricache.Set(targetAddr, visit);
                    }
                    return configs[selIndex];
                }
                return GetErrorServer();
            }
        }

        public void FlushPortMapCache()
        {
            portMapCache = new Dictionary<int, PortMapConfigCache>();
            var id2server = new Dictionary<string, Server>();
            var server_group = new Dictionary<string, int>();
            foreach (var s in configs)
            {
                id2server[s.id] = s;
                if (!string.IsNullOrEmpty(s.group))
                {
                    server_group[s.group] = 1;
                }
            }
            foreach (var pair in portMap)
            {
                var key = 0;
                var pm = pair.Value;
                if (!pm.enable)
                    continue;
                if (id2server.ContainsKey(pm.id) || server_group.ContainsKey(pm.id) || pm.id is not { Length: not 0 })
                { }
                else
                    continue;
                try
                {
                    key = int.Parse(pair.Key);
                }
                catch (FormatException)
                {
                    continue;
                }
                portMapCache[key] = new PortMapConfigCache
                {
                    type = pm.type,
                    id = pm.id,
                    server = id2server.TryGetValue(pm.id, out var value) ? value : null,
                    server_addr = pm.server_addr,
                    server_port = pm.server_port
                };
            }
            lock (serverStrategyMap)
            {
                var remove_ports = new List<int>();
                foreach (var pair in serverStrategyMap)
                {
                    if (portMapCache.ContainsKey(pair.Key)) continue;
                    remove_ports.Add(pair.Key);
                }
                foreach (var port in remove_ports)
                {
                    serverStrategyMap.Remove(port);
                }
                if (!portMapCache.ContainsKey(localPort))
                    serverStrategyMap.Remove(localPort);
            }

            uricache.Clear();
        }

        public Dictionary<int, PortMapConfigCache> GetPortMapCache() => portMapCache;

        public static void CheckServer(Server server)
        {
            CheckPort(server.server_port);
            if (server.server_udp_port != 0)
                CheckPort(server.server_udp_port);
            try
            {
                CheckPassword(server.password);
            }
            catch (ConfigurationWarning cw)
            {
                server.password = "";
                MessageBox.Show(cw.Message, cw.Message, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            CheckServer(server.server);
        }

        public Configuration()
        {
            index = 0;
            localPort = 1080;

            reconnectTimes = 2;
            keepVisitTime = 180;
            connectTimeout = 5;
            dnsServer = "";
            localDnsServer = "";

            balanceAlgorithm = "LowException";
            random = true;
            sysProxyMode = (int)ProxyMode.Global;
            proxyRuleMode = (int)ProxyRuleMode.BypassLanAndChina;

            nodeFeedAutoUpdate = true;

            serverSubscribes = new List<ServerSubscribe>()
            {
            };

            configs = new List<Server>()
            {
                GetDefaultServer()
            };
        }

        public void CopyFrom(Configuration config)
        {
            configs = config.configs;
            index = config.index;
            random = config.random;
            sysProxyMode = config.sysProxyMode;
            shareOverLan = config.shareOverLan;
            localPort = config.localPort;
            reconnectTimes = config.reconnectTimes;
            balanceAlgorithm = config.balanceAlgorithm;
            randomInGroup = config.randomInGroup;
            TTL = config.TTL;
            connectTimeout = config.connectTimeout;
            dnsServer = config.dnsServer;
            localDnsServer = config.localDnsServer;
            proxyEnable = config.proxyEnable;
            pacDirectGoProxy = config.pacDirectGoProxy;
            proxyType = config.proxyType;
            proxyHost = config.proxyHost;
            proxyPort = config.proxyPort;
            proxyAuthUser = config.proxyAuthUser;
            proxyAuthPass = config.proxyAuthPass;
            proxyUserAgent = config.proxyUserAgent;
            authUser = config.authUser;
            authPass = config.authPass;
            autoBan = config.autoBan;
            checkSwitchAutoCloseAll = config.checkSwitchAutoCloseAll;
            logEnable = config.logEnable;
            sameHostForSameTarget = config.sameHostForSameTarget;
            keepVisitTime = config.keepVisitTime;
            isHideTips = config.isHideTips;
            nodeFeedAutoUpdate = config.nodeFeedAutoUpdate;
            serverSubscribes = config.serverSubscribes;
        }

        public void FixConfiguration()
        {
            if (localPort == 0)
            {
                localPort = 1080;
            }
            if (keepVisitTime == 0)
            {
                keepVisitTime = 180;
            }
            portMap ??= new Dictionary<string, PortMapConfig>();
            token ??= new Dictionary<string, string>();
            if (connectTimeout == 0)
            {
                connectTimeout = 10;
                reconnectTimes = 2;
                TTL = 180;
                keepVisitTime = 180;
            }
            if (localAuthPassword is not { Length: >= 16 })
            {
                localAuthPassword = randString(20);
            }

            var id = new Dictionary<string, int>();
            if (index < 0 || index >= configs.Count) index = 0;
            foreach (var server in configs)
            {
                if (id.ContainsKey(server.id))
                {
                    var new_id = new byte[16];
                    Util.Utils.RandBytes(new_id, new_id.Length);
                    server.id = BitConverter.ToString(new_id).Replace("-", "");
                }
                else
                {
                    id[server.id] = 0;
                }
            }
        }

        private static string randString(int len)
        {
            var set = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            var ret = "";
            var random = new Random();
            for (var i = 0; i < len; ++i)
            {
                ret += set[random.Next(set.Length)];
            }
            return ret;
        }

        public static Configuration LoadFile(string filename)
        {
            try
            {
                var configContent = File.ReadAllText(filename);
                return Load(configContent);
            }
            catch (Exception e)
            {
                if (e is not FileNotFoundException)
                {
                    Console.WriteLine(e);
                }
                return new Configuration();
            }
        }

        public static Configuration Load() => LoadFile(CONFIG_FILE);

        public static void Save(Configuration config)
        {
            if (config.index >= config.configs.Count)
            {
                config.index = config.configs.Count - 1;
            }
            if (config.index < 0)
            {
                config.index = 0;
            }
            try
            {
                var jsonString = SimpleJson.SimpleJson.SerializeObject(config);
                if (GlobalConfiguration.config_password.Length > 0)
                {
                    var encryptor = EncryptorFactory.GetEncryptor("aes-256-cfb", GlobalConfiguration.config_password, false);
                    var cfg_data = Encoding.UTF8.GetBytes(jsonString);
                    var cfg_encrypt = new byte[cfg_data.Length + 128];
                    var data_len = 0;
                    const int buffer_size = 32768;
                    var input = new byte[buffer_size];
                    var ouput = new byte[buffer_size + 128];
                    for (var start_pos = 0; start_pos < cfg_data.Length; start_pos += buffer_size)
                    {
                        var len = Math.Min(cfg_data.Length - start_pos, buffer_size);
                        Buffer.BlockCopy(cfg_data, start_pos, input, 0, len);
                        encryptor.Encrypt(input, len, ouput, out var out_len);
                        Buffer.BlockCopy(ouput, 0, cfg_encrypt, data_len, out_len);
                        data_len += out_len;
                    }
                    jsonString = Convert.ToBase64String(cfg_encrypt, 0, data_len);
                }
                using (var sw = new StreamWriter(File.Open(CONFIG_FILE, FileMode.Create)))
                {
                    sw.Write(jsonString);
                    sw.Flush();
                }

                if (File.Exists(CONFIG_FILE_BACKUP))
                {
                    var dt = File.GetLastWriteTimeUtc(CONFIG_FILE_BACKUP);
                    var now = DateTime.Now;
                    if ((now - dt).TotalHours > 4)
                    {
                        File.Copy(CONFIG_FILE, CONFIG_FILE_BACKUP, true);
                    }
                }
                else
                {
                    File.Copy(CONFIG_FILE, CONFIG_FILE_BACKUP, true);
                }
            }
            catch (IOException e)
            {
                Console.Error.WriteLine(e);
            }
        }

        public static Configuration Load(string config_str)
        {
            try
            {
                if (GlobalConfiguration.config_password.Length > 0)
                {
                    var cfg_encrypt = Convert.FromBase64String(config_str);
                    var encryptor = EncryptorFactory.GetEncryptor("aes-256-cfb", GlobalConfiguration.config_password, false);
                    var cfg_data = new byte[cfg_encrypt.Length];
                    var data_len = 0;
                    const int buffer_size = 32768;
                    var input = new byte[buffer_size];
                    var ouput = new byte[buffer_size + 128];
                    for (var start_pos = 0; start_pos < cfg_encrypt.Length; start_pos += buffer_size)
                    {
                        var len = Math.Min(cfg_encrypt.Length - start_pos, buffer_size);
                        Buffer.BlockCopy(cfg_encrypt, start_pos, input, 0, len);
                        encryptor.Decrypt(input, len, ouput, out var out_len);
                        Buffer.BlockCopy(ouput, 0, cfg_data, data_len, out_len);
                        data_len += out_len;
                    }
                    config_str = Encoding.UTF8.GetString(cfg_data, 0, data_len);
                }
            }
            catch
            {

            }
            try
            {
                var config = SimpleJson.SimpleJson.DeserializeObject<Configuration>(config_str, new JsonSerializerStrategy());
                config.FixConfiguration();
                return config;
            }
            catch
            {
            }
            return null;
        }

        public static Server GetDefaultServer() => new();

        public bool isDefaultConfig()
        {
            if (configs.Count == 1 && configs[0].server == GetDefaultServer().server)
                return true;
            return false;
        }

        public static Server CopyServer(Server server)
        {
            var s = new Server
            {
                server = server.server,
                server_port = server.server_port,
                method = server.method,
                protocol = server.protocol,
                protocolparam = server.protocolparam ?? "",
                obfs = server.obfs,
                obfsparam = server.obfsparam ?? "",
                password = server.password,
                remarks = server.remarks,
                group = server.group,
                udp_over_tcp = server.udp_over_tcp,
                server_udp_port = server.server_udp_port
            };
            return s;
        }

        public static Server GetErrorServer()
        {
            var server = new Server
            {
                server = "invalid"
            };
            return server;
        }

        public static void CheckPort(int port)
        {
            if (port is <= 0 or > 65535)
            {
                throw new ConfigurationException(I18N.GetString("Port out of range"));
            }
        }

        private static void CheckPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ConfigurationWarning(I18N.GetString("Password are blank"));
                //throw new ConfigurationException(I18N.GetString("Password can not be blank"));
            }
        }

        private static void CheckServer(string server)
        {
            if (string.IsNullOrEmpty(server))
            {
                throw new ConfigurationException(I18N.GetString("Server IP can not be blank"));
            }
        }

        private class JsonSerializerStrategy : SimpleJson.PocoJsonSerializerStrategy
        {
            // convert string to int
            public override object DeserializeObject(object value, Type type)
            {
                if (type == typeof(int) && value.GetType() == typeof(string))
                {
                    return int.Parse(value.ToString());
                }
                return base.DeserializeObject(value, type);
            }
        }
    }

    [Serializable]
    public class ServerTrans
    {
        public long totalUploadBytes;
        public long totalDownloadBytes;

        void AddUpload(long bytes)
        {
            //lock (this)
            {
                totalUploadBytes += bytes;
            }
        }
        void AddDownload(long bytes)
        {
            //lock (this)
            {
                totalDownloadBytes += bytes;
            }
        }
    }

    [Serializable]
    public class ServerTransferTotal
    {
        private static readonly string LOG_FILE = "transfer_log.json";

        public Dictionary<string, object> servers = new();
        private int saveCounter;
        private DateTime saveTime;

        public static ServerTransferTotal Load()
        {
            try
            {
                var config_str = File.ReadAllText(LOG_FILE);
                var config = new ServerTransferTotal();
                try
                {
                    if (GlobalConfiguration.config_password.Length > 0)
                    {
                        var cfg_encrypt = Convert.FromBase64String(config_str);
                        var encryptor = EncryptorFactory.GetEncryptor("aes-256-cfb", GlobalConfiguration.config_password, false);
                        var cfg_data = new byte[cfg_encrypt.Length];
                        encryptor.Decrypt(cfg_encrypt, cfg_encrypt.Length, cfg_data, out var data_len);
                        config_str = Encoding.UTF8.GetString(cfg_data, 0, data_len);
                    }
                }
                catch
                {

                }
                config.servers = SimpleJson.SimpleJson.DeserializeObject<Dictionary<string, object>>(config_str, new JsonSerializerStrategy());
                config.Init();
                return config;
            }
            catch (Exception e)
            {
                if (e is not FileNotFoundException)
                {
                    Console.WriteLine(e);
                }
                return new ServerTransferTotal();
            }
        }

        public void Init()
        {
            saveCounter = 256;
            saveTime = DateTime.Now;
            servers ??= new Dictionary<string, object>();
        }

        public static void Save(ServerTransferTotal config)
        {
            try
            {
                using var sw = new StreamWriter(File.Open(LOG_FILE, FileMode.Create));
                var jsonString = SimpleJson.SimpleJson.SerializeObject(config.servers);
                if (GlobalConfiguration.config_password.Length > 0)
                {
                    var encryptor = EncryptorFactory.GetEncryptor("aes-256-cfb", GlobalConfiguration.config_password, false);
                    var cfg_data = Encoding.UTF8.GetBytes(jsonString);
                    var cfg_encrypt = new byte[cfg_data.Length + 128];
                    encryptor.Encrypt(cfg_data, cfg_data.Length, cfg_encrypt, out var data_len);
                    jsonString = Convert.ToBase64String(cfg_encrypt, 0, data_len);
                }
                sw.Write(jsonString);
                sw.Flush();
            }
            catch (IOException e)
            {
                Console.Error.WriteLine(e);
            }
        }

        public void Clear(string server)
        {
            lock (servers)
            {
                if (servers.ContainsKey(server))
                {
                    ((ServerTrans)servers[server]).totalUploadBytes = 0;
                    ((ServerTrans)servers[server]).totalDownloadBytes = 0;
                }
            }
        }

        public void AddUpload(string server, long size)
        {
            lock (servers)
            {
                if (!servers.ContainsKey(server))
                    servers.Add(server, new ServerTrans());
                ((ServerTrans)servers[server]).totalUploadBytes += size;
            }
            if (--saveCounter <= 0)
            {
                saveCounter = 256;
                if ((DateTime.Now - saveTime).TotalMinutes > 10)
                {
                    lock (servers)
                    {
                        Save(this);
                        saveTime = DateTime.Now;
                    }
                }
            }
        }
        public void AddDownload(string server, long size)
        {
            lock (servers)
            {
                if (!servers.ContainsKey(server))
                    servers.Add(server, new ServerTrans());
                ((ServerTrans)servers[server]).totalDownloadBytes += size;
            }
            if (--saveCounter <= 0)
            {
                saveCounter = 256;
                if ((DateTime.Now - saveTime).TotalMinutes > 10)
                {
                    lock (servers)
                    {
                        Save(this);
                        saveTime = DateTime.Now;
                    }
                }
            }
        }

        private class JsonSerializerStrategy : SimpleJson.PocoJsonSerializerStrategy
        {
            public override object DeserializeObject(object value, Type type)
            {
                if (type == typeof(long) && value.GetType() == typeof(string))
                {
                    return long.Parse(value.ToString());
                }
                if (type == typeof(object))
                {
                    return base.DeserializeObject(value, typeof(ServerTrans));
                }
                return base.DeserializeObject(value, type);
            }
        }

    }
}
